// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.IO.Compression;
using TwinPanel.Core.Archives;

namespace TwinPanel.Core.Tests;

public class ArchiveTests
{
    /// <summary>Petite arborescence : photos/a.jpg, photos/2024/b.jpg, notes.txt, vide/</summary>
    private static (TempDir Dir, string Src) Sample()
    {
        var t = new TempDir();
        t.File("src/photos/a.jpg", "AAAA");
        t.File("src/photos/2024/b.jpg", "BB");
        t.File("src/notes.txt", "texte");
        t.Dir("src/vide");
        return (t, Path.Combine(t.Path, "src"));
    }

    [Theory]
    [InlineData("test.zip", ArchiveKind.Zip)]
    [InlineData("test.tar", ArchiveKind.Tar)]
    [InlineData("test.tar.gz", ArchiveKind.TarGz)]
    public async Task Create_list_and_extract_roundtrip(string name, ArchiveKind kind)
    {
        var (t, src) = Sample();
        using var _ = t;
        var archive = Path.Combine(t.Path, name);
        await ArchiveService.CreateAsync([Path.Combine(src, "photos"), Path.Combine(src, "notes.txt"), Path.Combine(src, "vide")], archive, kind);

        // Affichage comme un dossier
        var source = new ArchiveFileSource();
        Assert.True(source.CanHandle(archive));
        var root = await source.ListAsync(archive, false, default);
        Assert.Contains(root, e => e.IsParentLink);
        Assert.Contains(root, e => e.Name == "photos" && e.IsDirectory);
        Assert.Contains(root, e => e.Name == "vide" && e.IsDirectory);
        Assert.Contains(root, e => e.Name == "notes.txt" && e.Size == 5);

        var inPhotos = await source.ListAsync(ArchivePath.Combine(archive, "photos"), false, default);
        Assert.Contains(inPhotos, e => e.Name == "a.jpg");
        Assert.Contains(inPhotos, e => e.Name == "2024" && e.IsDirectory);
        Assert.Equal(archive, source.GetParent(ArchivePath.Combine(archive, "photos")));
        Assert.Equal(t.Path, source.GetParent(archive));

        // Extraction du sous-dossier « 2024 » depuis « photos » : arrive directement dans la destination
        var dest = Path.Combine(t.Path, "out");
        await ArchiveService.ExtractAsync(archive, ["photos/2024"], "photos", dest, ConflictPolicy.Overwrite);
        Assert.Equal("BB", File.ReadAllText(Path.Combine(dest, "2024", "b.jpg")));
        Assert.False(File.Exists(Path.Combine(dest, "a.jpg")));

        // Tout extraire
        var all = Path.Combine(t.Path, "all");
        await ArchiveService.ExtractAsync(archive, [""], "", all, ConflictPolicy.Overwrite);
        Assert.Equal("AAAA", File.ReadAllText(Path.Combine(all, "photos", "a.jpg")));
        Assert.Equal("texte", File.ReadAllText(Path.Combine(all, "notes.txt")));
        Assert.True(Directory.Exists(Path.Combine(all, "vide")));

        await ArchiveService.TestAsync(archive); // ne doit pas lever d'erreur
    }

    [Fact]
    public async Task Zip_slip_entry_is_refused_and_nothing_escapes()
    {
        using var t = new TempDir();
        var archive = Path.Combine(t.Path, "piege.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry("ok.txt").Open())) w.Write("ok");
            using (var w = new StreamWriter(zip.CreateEntry("../../evade.txt").Open())) w.Write("danger");
        }
        var dest = t.Dir("dest");

        await Assert.ThrowsAsync<FileOperationException>(() =>
            ArchiveService.ExtractAsync(archive, [""], "", dest, ConflictPolicy.Overwrite));

        Assert.False(File.Exists(Path.Combine(t.Path, "evade.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(t.Path)!, "evade.txt")));
    }

    [Fact]
    public async Task Skip_policy_keeps_existing_file()
    {
        var (t, src) = Sample();
        using var _ = t;
        var archive = Path.Combine(t.Path, "a.zip");
        await ArchiveService.CreateAsync([Path.Combine(src, "notes.txt")], archive, ArchiveKind.Zip);
        var dest = t.Dir("dest");
        File.WriteAllText(Path.Combine(dest, "notes.txt"), "à garder");

        await ArchiveService.ExtractAsync(archive, ["notes.txt"], "", dest, ConflictPolicy.Skip);
        Assert.Equal("à garder", File.ReadAllText(Path.Combine(dest, "notes.txt")));

        await ArchiveService.ExtractAsync(archive, ["notes.txt"], "", dest, ConflictPolicy.RenameNew);
        Assert.Equal("texte", File.ReadAllText(Path.Combine(dest, "notes (2).txt")));
    }

    [Fact]
    public async Task Damaged_archive_gives_a_clear_error()
    {
        var (t, src) = Sample();
        using var _ = t;
        var archive = Path.Combine(t.Path, "abime.zip");
        File.WriteAllBytes(archive, [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6, 7, 8, 9]);

        var ex = await Assert.ThrowsAnyAsync<IOException>(() => new ArchiveFileSource().ListAsync(archive, false, default));
        Assert.Contains("abime.zip", ex.Message);
    }

    [Fact]
    public void Split_paths()
    {
        using var t = new TempDir();
        var zip = t.File("d/archive.zip", "x");
        Assert.True(ArchivePath.TrySplit(Path.Combine(zip, "a", "b.txt"), out var a, out var inner));
        Assert.Equal(Path.GetFullPath(zip), a); // « d/archive.zip » et « d\\archive.zip » : même fichier
        Assert.Equal("a/b.txt", inner);
        Assert.False(ArchivePath.TrySplit(Path.Combine(t.Path, "d"), out _, out _));
        Assert.False(ArchivePath.TrySplit(Path.Combine(t.Path, "absent.zip", "x"), out _, out _));
    }

    [Fact]
    public async Task Archive_created_inside_its_own_source_folder_does_not_include_itself()
    {
        var (t, src) = Sample();
        using var _ = t;
        var archive = Path.Combine(src, "photos", "moi.zip");
        await ArchiveService.CreateAsync([Path.Combine(src, "photos")], archive, ArchiveKind.Zip);
        var index = ArchiveIndex.Load(archive);
        Assert.DoesNotContain(index.All, i => i.Key.EndsWith("moi.zip", StringComparison.Ordinal) || i.Key.EndsWith(".twinpanel-part", StringComparison.Ordinal));
    }
}
