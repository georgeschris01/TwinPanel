// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Buffers.Binary;
using System.Text;
using TwinPanel.Core.Trash;
using Bin = TwinPanel.Core.Trash.RecycleBin;

namespace TwinPanel.Core.Tests;

/// <summary>Corbeille lue directement : fiches « $I », restauration, suppression définitive, copie sous le vrai nom.</summary>
public sealed class RecycleBinTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tp-trash-" + Guid.NewGuid().ToString("N"));
    private readonly string _bin;
    private static readonly DateTime Deleted = new(2026, 11, 14, 10, 30, 0, DateTimeKind.Utc);

    public RecycleBinTests()
    {
        _bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(_bin);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static byte[] InfoV2(string originalPath, long size, DateTime deletedUtc)
    {
        var path = Encoding.Unicode.GetBytes(originalPath + "\0");
        var data = new byte[28 + path.Length];
        BinaryPrimitives.WriteInt64LittleEndian(data, 2);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), deletedUtc.ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), originalPath.Length + 1);
        path.CopyTo(data, 28);
        return data;
    }

    private static byte[] InfoV1(string originalPath, long size, DateTime deletedUtc)
    {
        var data = new byte[24 + 520];
        BinaryPrimitives.WriteInt64LittleEndian(data, 1);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), deletedUtc.ToFileTimeUtc());
        Encoding.Unicode.GetBytes(originalPath).CopyTo(data, 24);
        return data;
    }

    /// <summary>Met dans la fausse corbeille un fichier supprimé (son contenu et sa fiche).</summary>
    private RecycleItem PutFile(string id, string originalPath, string content)
    {
        File.WriteAllText(Path.Combine(_bin, "$R" + id), content);
        File.WriteAllBytes(Path.Combine(_bin, "$I" + id), InfoV2(originalPath, content.Length, Deleted));
        return Bin.ListFolders([_bin]).Single(i => i.DataPath.EndsWith("$R" + id, StringComparison.Ordinal));
    }

    [Fact]
    public void A_windows_10_info_file_gives_path_size_and_date()
    {
        var info = Bin.ParseInfo(InfoV2(@"C:\Users\utilisateur\Documents\lettre.txt", 1234, Deleted));
        Assert.NotNull(info);
        Assert.Equal(@"C:\Users\utilisateur\Documents\lettre.txt", info.Value.OriginalPath);
        Assert.Equal(1234, info.Value.Size);
        Assert.Equal(Deleted, info.Value.DeletedUtc);
    }

    [Fact]
    public void An_old_windows_info_file_is_read_too()
    {
        var info = Bin.ParseInfo(InfoV1(@"D:\photos\vacances.jpg", 99, Deleted));
        Assert.NotNull(info);
        Assert.Equal(@"D:\photos\vacances.jpg", info.Value.OriginalPath);
        Assert.Equal(99, info.Value.Size);
    }

    [Fact]
    public void A_damaged_or_unknown_info_file_is_ignored()
    {
        Assert.Null(Bin.ParseInfo(new byte[10]));
        var unknown = InfoV2(@"C:\a.txt", 1, Deleted);
        BinaryPrimitives.WriteInt64LittleEndian(unknown, 7);
        Assert.Null(Bin.ParseInfo(unknown));
        var tooLong = InfoV2(@"C:\a.txt", 1, Deleted);
        BinaryPrimitives.WriteInt32LittleEndian(tooLong.AsSpan(24), 5000);
        Assert.Null(Bin.ParseInfo(tooLong));
    }

    [Fact]
    public void Items_are_listed_under_their_real_name_and_original_folder()
    {
        var origin = Path.Combine(_root, "docs", "lettre.txt");
        var item = PutFile("ABC123.txt", origin, "bonjour");
        Assert.Equal("lettre.txt", item.Name);
        Assert.Equal(Path.Combine(_root, "docs"), item.OriginalFolder);
        Assert.False(item.IsDirectory);

        var entry = Bin.ToEntry(item);
        Assert.Equal("lettre.txt", entry.Name);
        Assert.Equal(item.DataPath, entry.FullPath);
        Assert.Equal(item.OriginalFolder, entry.OriginalFolder);
    }

    [Fact]
    public void An_info_file_without_its_item_is_ignored()
    {
        File.WriteAllBytes(Path.Combine(_bin, "$IORPHAN.txt"), InfoV2(@"C:\x.txt", 1, Deleted));
        Assert.Empty(Bin.ListFolders([_bin]));
        Assert.False(Bin.AnyIn([_bin]));
    }

    [Fact]
    public void A_deleted_folder_is_listed_as_a_folder()
    {
        var data = Path.Combine(_bin, "$RDIR001");
        Directory.CreateDirectory(Path.Combine(data, "sous"));
        File.WriteAllBytes(Path.Combine(_bin, "$IDIR001"), InfoV2(Path.Combine(_root, "projet"), 0, Deleted));
        var item = Assert.Single(Bin.ListFolders([_bin]));
        Assert.True(item.IsDirectory);
        Assert.Equal("projet", item.Name);
        Assert.True(Bin.AnyIn([_bin]));
    }

    [Fact]
    public void Restore_puts_the_item_back_and_recreates_its_folder()
    {
        var origin = Path.Combine(_root, "disparu", "lettre.txt");
        var item = PutFile("R1.txt", origin, "contenu");
        Assert.True(Bin.Restore(item));
        Assert.Equal("contenu", File.ReadAllText(origin));
        Assert.False(File.Exists(item.DataPath));
        Assert.False(File.Exists(item.InfoPath));
        Assert.Empty(Bin.ListFolders([_bin]));
    }

    [Fact]
    public void Restore_never_overwrites_an_item_already_in_place()
    {
        var origin = Path.Combine(_root, "lettre.txt");
        File.WriteAllText(origin, "nouveau");
        var item = PutFile("R2.txt", origin, "ancien");
        Assert.False(Bin.Restore(item));
        Assert.Equal("nouveau", File.ReadAllText(origin));
        Assert.True(File.Exists(item.DataPath)); // toujours dans la corbeille
        Assert.True(File.Exists(item.InfoPath));
    }

    [Fact]
    public void Delete_permanently_removes_the_item_and_its_info_even_read_only()
    {
        var item = PutFile("D1.txt", Path.Combine(_root, "a.txt"), "x");
        File.SetAttributes(item.DataPath, FileAttributes.ReadOnly);
        Bin.DeletePermanently(item);
        Assert.False(File.Exists(item.DataPath));
        Assert.False(File.Exists(item.InfoPath));

        var folder = Path.Combine(_bin, "$RD2");
        Directory.CreateDirectory(folder);
        var inner = Path.Combine(folder, "f.txt");
        File.WriteAllText(inner, "y");
        File.SetAttributes(inner, FileAttributes.ReadOnly);
        File.WriteAllBytes(Path.Combine(_bin, "$ID2"), InfoV2(Path.Combine(_root, "d"), 0, Deleted));
        Bin.DeletePermanently(Bin.Find(folder)!);
        Assert.False(Directory.Exists(folder));
        Assert.Empty(Bin.ListFolders([_bin]));
    }

    [Fact]
    public void Delete_permanently_of_a_link_leaves_its_target_untouched()
    {
        var target = Path.Combine(_root, "cible");
        Directory.CreateDirectory(target);
        var inner = Path.Combine(target, "garde.txt");
        File.WriteAllText(inner, "z");
        File.SetAttributes(inner, FileAttributes.ReadOnly);
        var link = Path.Combine(_bin, "$RL1");
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // liens interdits sur ce PC
        File.WriteAllBytes(Path.Combine(_bin, "$IL1"), InfoV2(Path.Combine(_root, "lien"), 0, Deleted));

        Bin.DeletePermanently(Bin.Find(link)!);

        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(inner));
        Assert.True(File.GetAttributes(inner).HasFlag(FileAttributes.ReadOnly));
        File.SetAttributes(inner, FileAttributes.Normal);
    }

    [Fact]
    public async Task Copying_out_uses_the_real_name()
    {
        var item = PutFile("C1.txt", Path.Combine(_root, "rapport.txt"), "texte");
        var dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(dest);
        await FileOperations.CopyNamedAsync([(item.DataPath, item.Name)], dest, ConflictPolicy.Overwrite);
        Assert.Equal("texte", File.ReadAllText(Path.Combine(dest, "rapport.txt")));
        Assert.True(File.Exists(item.DataPath)); // la copie laisse l'élément dans la corbeille
    }

    [Fact]
    public void The_panel_path_is_recognized()
    {
        Assert.True(Bin.IsPanelPath("trash:"));
        Assert.True(Bin.IsPanelPath("TRASH:/"));
        Assert.False(Bin.IsPanelPath(@"C:\trash"));
        Assert.False(Bin.IsPanelPath(null));
        Assert.Equal(Path.Combine("X", "$RAB.txt"), Bin.DataPathOf(Path.Combine("X", "$IAB.txt")));
        Assert.Equal(Path.Combine("X", "$IAB.txt"), Bin.InfoPathOf(Path.Combine("X", "$RAB.txt")));
    }

    [Fact]
    public void A_path_inside_a_deleted_windows_folder_leads_back_to_that_folder()
    {
        const string root = @"C:\$Recycle.Bin\S-1-5-21-1\$RAB12CD";
        Assert.Equal(root, Bin.ItemRootOf(root, null));
        Assert.Equal(root, Bin.ItemRootOf(root + @"\photos\2026", null));
        Assert.Null(Bin.ItemRootOf(@"C:\$Recycle.Bin\S-1-5-21-1", null));         // la corbeille brute, pas un élément
        Assert.Null(Bin.ItemRootOf(@"C:\$Recycle.Bin\S-1-5-21-1\$IAB12CD", null)); // une fiche
        Assert.Null(Bin.ItemRootOf(@"C:\Users\utilisateur\Documents", null));
        Assert.Null(Bin.ItemRootOf("trash:", null));
    }

    [Fact]
    public void A_path_inside_a_deleted_desktop_folder_leads_back_to_that_folder()
    {
        var trash = Path.Combine(_root, "Trash");
        var root = Path.Combine(trash, "files", "projet");
        Assert.Equal(root, Bin.ItemRootOf(root, trash));
        Assert.Equal(root, Bin.ItemRootOf(Path.Combine(root, "src", "a"), trash));
        Assert.Null(Bin.ItemRootOf(Path.Combine(trash, "files"), trash));
        Assert.Null(Bin.ItemRootOf(Path.Combine(trash, "info", "projet.trashinfo"), trash));
        Assert.Null(Bin.ItemRootOf(_root, trash));
    }

    [Fact]
    public void A_deleted_folder_shows_its_real_name()
    {
        var trash = PutDesktop("projet.2", Path.Combine(_root, "projet"), null);
        var root = Path.Combine(trash, "files", "projet.2");
        Assert.Equal("projet", Bin.RealNameOf(root));
    }

    // ---- Linux : corbeille des bureaux (freedesktop.org) ----

    /// <summary>Met dans une fausse corbeille de bureau un élément supprimé, avec sa fiche.</summary>
    private string PutDesktop(string trashName, string originalPath, string? content)
    {
        var trash = Path.Combine(_root, "Trash");
        Directory.CreateDirectory(Path.Combine(trash, "files"));
        Directory.CreateDirectory(Path.Combine(trash, "info"));
        var data = Path.Combine(trash, "files", trashName);
        if (content is null) Directory.CreateDirectory(data);
        else File.WriteAllText(data, content);
        File.WriteAllText(Path.Combine(trash, "info", trashName + ".trashinfo"), DesktopTrash.InfoText(originalPath, new DateTime(2026, 11, 14, 15, 4, 5)));
        return trash;
    }

    [Fact]
    public void A_desktop_info_file_gives_the_decoded_path_and_the_date()
    {
        var info = Bin.ParseTrashInfo("[Trash Info]\nPath=/home/utilisateur/Mes%20documents/%C3%A9t%C3%A9.txt\nDeletionDate=2026-11-14T15:04:05\n");
        Assert.NotNull(info);
        Assert.Equal("/home/utilisateur/Mes documents/été.txt", info.Value.OriginalPath);
        Assert.Equal(new DateTime(2026, 11, 14, 15, 4, 5), info.Value.Deleted);
        Assert.Null(Bin.ParseTrashInfo("[Autre]\nPath=/a\n"));
        Assert.Null(Bin.ParseTrashInfo("rien"));
    }

    [Fact]
    public void Desktop_items_are_listed_with_their_real_name_size_and_folder_sizes()
    {
        var origin = Path.Combine(_root, "docs", "lettre.txt");
        var trash = PutDesktop("lettre.2.txt", origin, "bonjour");
        PutDesktop("projet", Path.Combine(_root, "projet"), null);
        var noted = new DateTimeOffset(File.GetLastWriteTimeUtc(Path.Combine(trash, "info", "projet.trashinfo"))).ToUnixTimeMilliseconds();
        File.WriteAllText(Path.Combine(trash, "directorysizes"), $"4096 {noted} projet\n");
        File.WriteAllText(Path.Combine(trash, "info", "orphelin.trashinfo"), DesktopTrash.InfoText("/x", DateTime.Now));

        var items = Bin.ListDesktop(trash).OrderBy(i => i.Name).ToList();
        Assert.Equal(["lettre.txt", "projet"], items.Select(i => i.Name));
        Assert.Equal(7, items[0].Size);
        Assert.Equal(Path.Combine(_root, "docs"), items[0].OriginalFolder);
        Assert.True(items[1].IsDirectory);
        Assert.Equal(4096, items[1].Size);
        Assert.True(Bin.AnyInDesktop(trash));

        // Le panneau retrouve l'élément d'après le chemin qu'il montre (files/…).
        Assert.Equal("lettre.txt", Bin.Find(items[0].DataPath)?.Name);
    }

    [Fact]
    public void A_folder_size_noted_for_an_older_folder_of_the_same_name_is_ignored()
    {
        var trash = PutDesktop("photos", Path.Combine(_root, "photos"), null);
        File.WriteAllText(Path.Combine(trash, "directorysizes"), "10737418240 1600000000 photos\n");
        Assert.Equal(0, Assert.Single(Bin.ListDesktop(trash)).Size);
    }

    [Fact]
    public void Other_disk_trashes_are_recognized_and_keep_paths_relative_to_their_disk()
    {
        var sep = Path.DirectorySeparatorChar;
        var usb = Path.Combine(_root, "usb");
        var personal = Path.Combine(usb, ".Trash-1000");
        var shared = Path.Combine(usb, ".Trash", "1000");
        Assert.True(Bin.IsVolumeTrash(personal));
        Assert.True(Bin.IsVolumeTrash(shared));
        Assert.False(Bin.IsVolumeTrash(Path.Combine(_root, "Trash")));
        Assert.False(Bin.IsVolumeTrash(Path.Combine(usb, ".Trash-abc")));
        Assert.Equal(usb, Bin.TopDirectoryOf(personal));
        Assert.Equal(usb, Bin.TopDirectoryOf(shared));
        Assert.Null(Bin.TopDirectoryOf(Path.Combine(_root, "Trash")));

        // Un dossier supprimé de la clé est reconnu comme tel, même si la corbeille principale est ailleurs.
        var home = Path.Combine(_root, "home", "Trash");
        var root = Path.Combine(personal, "files", "vacances");
        Assert.Equal(root, Bin.ItemRootOf(Path.Combine(root, "juillet"), home));
        Assert.Null(Bin.ItemRootOf(Path.Combine(usb, "files", "x"), home));

        // Chemin d'origine gardé par rapport à la clé : rattaché à elle.
        Directory.CreateDirectory(Path.Combine(personal, "files"));
        Directory.CreateDirectory(Path.Combine(personal, "info"));
        File.WriteAllText(Path.Combine(personal, "files", "a.txt"), "x");
        File.WriteAllText(Path.Combine(personal, "info", "a.txt.trashinfo"), DesktopTrash.InfoText($"docs{sep}a.txt", DateTime.Now));
        var item = Assert.Single(Bin.ListDesktop(personal));
        Assert.Equal(Path.Combine(usb, "docs", "a.txt"), Path.GetFullPath(item.OriginalPath)); // « / » des fiches ramené au séparateur du système
    }

    [Fact]
    public void A_desktop_item_is_restored_and_its_info_removed()
    {
        var origin = Path.Combine(_root, "retour", "note.txt");
        var trash = PutDesktop("note.txt", origin, "texte");
        var item = Assert.Single(Bin.ListDesktop(trash));
        Assert.True(Bin.Restore(item));
        Assert.Equal("texte", File.ReadAllText(origin));
        Assert.Empty(Bin.ListDesktop(trash));
        Assert.False(Bin.AnyInDesktop(trash));
    }
}
