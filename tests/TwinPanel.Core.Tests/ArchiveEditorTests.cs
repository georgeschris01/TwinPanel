// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Common;
using SharpCompress.Writers.SevenZip;
using TwinPanel.Core.Archives;

namespace TwinPanel.Core.Tests;

/// <summary>Modifier une archive : le résultat doit être exact, et l'original intact au moindre problème.</summary>
public class ArchiveEditorTests
{
    private static readonly DateTime OldDate = new(2020, 5, 6, 7, 8, 10);

    private sealed class SyncProgress(Action<TransferProgress> report) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => report(value);
    }

    /// <summary>photos/a.txt, photos/2024/b.txt, notes.txt et le dossier vide/.</summary>
    private static async Task<string> Sample(TempDir t, string name)
    {
        t.File("src/photos/a.txt", "AAAA");
        t.File("src/photos/2024/b.txt", "BB");
        t.File("src/notes.txt", "texte");
        t.Dir("src/vide");
        var src = Path.Combine(t.Path, "src");
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories)) File.SetLastWriteTime(file, OldDate);
        var archive = Path.Combine(t.Path, name);
        if (ArchiveEditor.FormatOf(name) == ArchiveFormat.SevenZip)
        {
            using var output = File.Create(archive);
            using var writer = new SevenZipWriter(output, new SevenZipWriterOptions(CompressionType.LZMA));
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                using var input = File.OpenRead(file);
                writer.Write(Path.GetRelativePath(src, file).Replace('\\', '/'), input, OldDate);
            }
            writer.WriteDirectory("vide", OldDate);
        }
        else
        {
            await ArchiveService.CreateAsync([Path.Combine(src, "photos"), Path.Combine(src, "notes.txt"), Path.Combine(src, "vide")], archive,
                ArchiveService.KindFromName(name)!.Value);
        }
        return archive;
    }

    /// <summary>Tout le contenu, relu réellement : « chemin → texte » ; un dossier donne « / ».</summary>
    private static async Task<Dictionary<string, string>> Content(TempDir t, string archive)
    {
        var dest = Path.Combine(t.Path, "lu-" + Guid.NewGuid().ToString("N")[..6]);
        await ArchiveService.ExtractAsync(archive, [""], "", dest, ConflictPolicy.Overwrite);
        var found = new Dictionary<string, string>();
        foreach (var item in ArchiveIndex.Load(archive).All)
            found[item.Key] = item.IsDirectory ? "/" : File.ReadAllText(Path.Combine(dest, item.Key.Replace('/', '\\')));
        return found;
    }

    [Theory]
    [InlineData("a.zip")]
    [InlineData("a.7z")]
    [InlineData("a.tar")]
    [InlineData("a.tar.gz")]
    [InlineData("a.tgz")]
    public async Task Adding_keeps_everything_else(string name)
    {
        using var t = new TempDir();
        var archive = await Sample(t, name);
        var added = t.File("nouveau.txt", "neuf");

        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Add = [new ArchiveAddition(added, "photos/2024")] });

        var c = await Content(t, archive);
        Assert.Equal("neuf", c["photos/2024/nouveau.txt"]);
        Assert.Equal("BB", c["photos/2024/b.txt"]);
        Assert.Equal("AAAA", c["photos/a.txt"]);
        Assert.Equal("texte", c["notes.txt"]);
        Assert.Equal("/", c["vide"]);
        Assert.True(File.Exists(added));
        Assert.Single(Directory.GetFiles(t.Path, "*" + Path.GetExtension(name))); // aucun fichier de travail ne reste
    }

    [Theory]
    [InlineData("a.zip")]
    [InlineData("a.7z")]
    [InlineData("a.tar")]
    [InlineData("a.tgz")]
    public async Task A_kept_file_keeps_its_date(string name)
    {
        using var t = new TempDir();
        var archive = await Sample(t, name);

        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Delete = ["notes.txt"] });

        var kept = ArchiveIndex.Load(archive).All.Single(i => i.Key == "photos/a.txt");
        Assert.True((kept.Modified - OldDate).Duration() <= TimeSpan.FromSeconds(2), $"date lue : {kept.Modified}");
    }

    [Theory]
    [InlineData("a.zip")]
    [InlineData("a.7z")]
    [InlineData("a.tar")]
    [InlineData("a.tgz")]
    public async Task Deleting_a_folder_takes_its_content(string name)
    {
        using var t = new TempDir();
        var archive = await Sample(t, name);

        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Delete = ["photos"] });

        var c = await Content(t, archive);
        Assert.DoesNotContain(c.Keys, k => k.StartsWith("photos", StringComparison.Ordinal));
        Assert.Equal("texte", c["notes.txt"]);
    }

    [Theory]
    [InlineData("a.zip")]
    [InlineData("a.7z")]
    [InlineData("a.tar")]
    [InlineData("a.tgz")]
    public async Task Renaming_a_folder_moves_its_content(string name)
    {
        using var t = new TempDir();
        var archive = await Sample(t, name);

        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Rename = new Dictionary<string, string> { ["photos/2024"] = "photos/2025", ["notes.txt"] = "Notes.md" } });

        var c = await Content(t, archive);
        Assert.Equal("BB", c["photos/2025/b.txt"]);
        Assert.Equal("texte", c["Notes.md"]);
        Assert.DoesNotContain("photos/2024/b.txt", c.Keys);
        Assert.DoesNotContain("photos/2024", c.Keys);
    }

    [Theory]
    [InlineData("a.zip")]
    [InlineData("a.7z")]
    [InlineData("a.tar")]
    [InlineData("a.tgz")]
    public async Task A_new_folder_and_an_emptied_folder_both_exist_afterwards(string name)
    {
        using var t = new TempDir();
        var archive = await Sample(t, name);

        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { NewFolders = ["photos/neuf"], Delete = ["photos/2024/b.txt"] });

        var c = await Content(t, archive);
        Assert.Equal("/", c["photos/neuf"]);
        Assert.Equal("/", c["photos/2024"]); // le panneau qui le montrait doit pouvoir y rester
    }

    [Theory]
    [InlineData("a.zip")]
    [InlineData("a.7z")]
    [InlineData("a.tar")]
    [InlineData("a.tgz")]
    public async Task Removing_everything_leaves_a_valid_archive(string name)
    {
        using var t = new TempDir();
        var archive = await Sample(t, name);

        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Delete = ["photos", "notes.txt", "vide"] });

        Assert.Empty(ArchiveIndex.Load(archive).All);
    }

    [Fact]
    public async Task Adding_a_folder_keeps_its_tree_and_its_empty_folders()
    {
        using var t = new TempDir();
        var archive = await Sample(t, "a.zip");
        t.File("album/sous/x.txt", "X");
        t.Dir("album/rien");

        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Add = [new ArchiveAddition(Path.Combine(t.Path, "album"), "")] });

        var c = await Content(t, archive);
        Assert.Equal("X", c["album/sous/x.txt"]);
        Assert.Equal("/", c["album/rien"]);
    }

    [Theory]
    [InlineData(ConflictPolicy.Overwrite, "neuf", null)]
    [InlineData(ConflictPolicy.Skip, "texte", null)]
    [InlineData(ConflictPolicy.RenameNew, "texte", "neuf")]
    public async Task Same_name_follows_the_chosen_rule(ConflictPolicy policy, string notes, string? renamed)
    {
        using var t = new TempDir();
        var archive = await Sample(t, "a.zip");
        var added = t.File("arrivee/notes.txt", "neuf");

        var result = await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Add = [new ArchiveAddition(added, "")], Policy = policy });

        var c = await Content(t, archive);
        Assert.Equal(notes, c["notes.txt"]);
        Assert.Equal(renamed, c.GetValueOrDefault("notes (2).txt"));
        Assert.Equal(policy == ConflictPolicy.Skip ? 1 : 0, result.SkippedFiles);
    }

    [Fact]
    public async Task A_moved_source_leaves_the_pc_only_if_it_really_went_in()
    {
        using var t = new TempDir();
        var archive = await Sample(t, "a.zip");
        var taken = t.File("depart/notes.txt", "autre"); // nom déjà pris : ignoré
        var free = t.File("depart/libre.txt", "ok");

        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits
        {
            Add = [new ArchiveAddition(taken, ""), new ArchiveAddition(free, "")],
            Policy = ConflictPolicy.Skip,
            MoveSources = true,
        });

        Assert.True(File.Exists(taken));
        Assert.False(File.Exists(free));
        Assert.Equal("ok", (await Content(t, archive))["libre.txt"]);
    }

    [Fact]
    public async Task Moving_a_folder_out_of_the_archive_removes_it_there()
    {
        using var t = new TempDir();
        var archive = await Sample(t, "a.zip");
        var dest = Path.Combine(t.Path, "sortie");

        await ArchiveEditor.MoveOutAsync(archive, ["photos"], "", dest, ConflictPolicy.Overwrite);

        Assert.Equal("AAAA", File.ReadAllText(Path.Combine(dest, "photos", "a.txt")));
        Assert.DoesNotContain(ArchiveIndex.Load(archive).All, i => i.Key.StartsWith("photos", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ConflictPolicy.Skip)]
    [InlineData(ConflictPolicy.RenameNew)]
    public async Task A_file_not_taken_out_stays_in_the_archive(ConflictPolicy policy)
    {
        using var t = new TempDir();
        var archive = await Sample(t, "a.zip");
        var dest = Path.Combine(t.Path, "sortie");
        t.File("sortie/notes.txt", "déjà là");

        await ArchiveEditor.MoveOutAsync(archive, ["notes.txt"], "", dest, policy);

        Assert.Contains(ArchiveIndex.Load(archive).All, i => i.Key == "notes.txt");
        Assert.Equal("déjà là", File.ReadAllText(Path.Combine(dest, "notes.txt")));
    }

    [Fact]
    public async Task Tar_keeps_links_rights_and_owners()
    {
        using var t = new TempDir();
        var archive = Path.Combine(t.Path, "a.tar");
        using (var writer = new TarWriter(File.Create(archive), TarEntryFormat.Gnu))
        {
            writer.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile, "script.sh")
            {
                DataStream = new MemoryStream("#!/bin/sh"u8.ToArray()),
                Mode = UnixFileMode.UserRead | UnixFileMode.UserExecute,
                Uid = 1000,
                Gid = 2000,
                UserName = "utilisateur",
            });
            writer.WriteEntry(new GnuTarEntry(TarEntryType.SymbolicLink, "lien.sh") { LinkName = "script.sh" });
        }

        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { NewFolders = ["neuf"] });

        using var reader = new TarReader(File.OpenRead(archive));
        var entries = new List<TarEntry>();
        while (reader.GetNextEntry(copyData: true) is { } entry) entries.Add(entry);
        var script = entries.Single(e => e.Name == "script.sh");
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserExecute, script.Mode);
        Assert.Equal((1000, 2000, "utilisateur"), (script.Uid, script.Gid, ((GnuTarEntry)script).UserName));
        Assert.Equal("script.sh", entries.Single(e => e.Name == "lien.sh").LinkName);
        Assert.Equal(TarEntryType.SymbolicLink, entries.Single(e => e.Name == "lien.sh").EntryType);
    }

    [Fact]
    public async Task Zip_keeps_unix_rights_and_comments()
    {
        using var t = new TempDir();
        var archive = Path.Combine(t.Path, "a.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            zip.Comment = "Commentaire général";
            var entry = zip.CreateEntry("x.sh");
            entry.ExternalAttributes = unchecked((int)0x81ED0000);
            entry.Comment = "Note";
            using var stream = entry.Open();
            stream.Write("#!"u8);
        }

        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { NewFolders = ["neuf"] });

        using var back = ZipFile.OpenRead(archive);
        Assert.Equal("Commentaire général", back.Comment);
        var kept = back.GetEntry("x.sh")!;
        Assert.Equal(unchecked((int)0x81ED0000), kept.ExternalAttributes);
        Assert.Equal("Note", kept.Comment);
    }

    [Theory]
    [InlineData("a.rar")]
    [InlineData("a.gz")]
    [InlineData("a.tar.bz2")]
    [InlineData("a.xz")]
    public void Other_formats_stay_read_only(string name) => Assert.False(ArchiveEditor.CanEdit(name));

    [Fact]
    public async Task A_read_only_format_is_refused_with_a_clear_message()
    {
        using var t = new TempDir();
        var archive = t.File("a.gz", "x");

        var error = await Assert.ThrowsAsync<FileOperationException>(() => ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { NewFolders = ["n"] }));

        Assert.Contains("lecture seule", error.Message);
    }

    [Fact]
    public async Task Cancelling_leaves_the_original_exact_and_nothing_beside_it()
    {
        using var t = new TempDir();
        var archive = await Sample(t, "a.zip");
        var before = File.ReadAllBytes(archive);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Delete = ["notes.txt"] },
            new SyncProgress(_ => cts.Cancel()), cts.Token));

        Assert.Equal(before, File.ReadAllBytes(archive));
        Assert.Single(Directory.GetFiles(t.Path, "*.zip"));
    }

    [Fact]
    public async Task A_source_that_vanished_stops_before_anything_is_replaced()
    {
        using var t = new TempDir();
        var archive = await Sample(t, "a.zip");
        var before = File.ReadAllBytes(archive);

        await Assert.ThrowsAsync<FileOperationException>(() => ArchiveEditor.ApplyAsync(archive,
            new ArchiveEdits { Add = [new ArchiveAddition(Path.Combine(t.Path, "absent.txt"), "")] }));

        Assert.Equal(before, File.ReadAllBytes(archive));
    }

    [Fact]
    public async Task An_archive_changed_meanwhile_is_not_replaced()
    {
        using var t = new TempDir();
        var archive = await Sample(t, "a.zip");
        bool changed = false;

        var progress = new SyncProgress(_ =>
        {
            if (changed) return;
            changed = true;
            File.SetLastWriteTime(archive, DateTime.Now.AddMinutes(1));
        });
        var before = File.ReadAllBytes(archive);
        var error = await Assert.ThrowsAsync<FileOperationException>(() => ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Delete = ["notes.txt"] }, progress));

        Assert.Contains("a changé", error.Message);
        Assert.Equal(before, File.ReadAllBytes(archive));
    }

    [Theory]
    [InlineData("photos/a.txt", "photos/2024")]
    [InlineData("notes.txt", "notes.txt")]
    public async Task An_impossible_rename_changes_nothing(string from, string to)
    {
        using var t = new TempDir();
        var archive = await Sample(t, "a.zip");
        var before = File.ReadAllBytes(archive);

        await Assert.ThrowsAsync<FileOperationException>(() => ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Rename = new Dictionary<string, string> { [from] = to } }));

        Assert.Equal(before, File.ReadAllBytes(archive));
    }

    [Fact]
    public async Task Verification_sees_a_wrong_content_a_missing_file_and_an_extra_file()
    {
        using var t = new TempDir();
        var archive = await Sample(t, "a.zip");
        Dictionary<string, Queue<byte[]>> Good() => new()
        {
            ["photos/a.txt"] = new([SHA256.HashData("AAAA"u8)]),
            ["photos/2024/b.txt"] = new([SHA256.HashData("BB"u8)]),
            ["notes.txt"] = new([SHA256.HashData("texte"u8)]),
        };

        ArchiveEditor.Verify(archive, Good());

        var wrong = Good();
        wrong["notes.txt"] = new([SHA256.HashData("autre"u8)]);
        Assert.Throws<FileOperationException>(() => ArchiveEditor.Verify(archive, wrong));

        var missing = Good();
        missing["absent.txt"] = new([SHA256.HashData(Encoding.UTF8.GetBytes("x"))]);
        Assert.Throws<FileOperationException>(() => ArchiveEditor.Verify(archive, missing));

        var extra = Good();
        extra.Remove("notes.txt");
        Assert.Throws<FileOperationException>(() => ArchiveEditor.Verify(archive, extra));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("fin.")]
    [InlineData(" espace")]
    [InlineData("deux:points")]
    public void Bad_names_are_refused(string name) => Assert.NotNull(ArchiveEditor.CheckName(name));

    [Fact]
    public void Good_names_are_accepted() => Assert.Null(ArchiveEditor.CheckName("Vacances été 2026.jpg"));
}
