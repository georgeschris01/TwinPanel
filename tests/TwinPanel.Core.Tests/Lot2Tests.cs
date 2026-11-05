// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Operations;
using TwinPanel.Core.Selection;

namespace TwinPanel.Core.Tests;

public class ConflictPolicyTests
{
    [Fact]
    public async Task OverwriteOlder_only_replaces_older_files()
    {
        using var t = new TempDir();
        var newer = t.File("src/a.txt", "new");
        var older = t.File("src/b.txt", "new");
        t.File("dest/a.txt", "old");
        t.File("dest/b.txt", "old");
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(Path.Combine(t.Path, "dest", "a.txt"), DateTime.UtcNow.AddDays(-1));
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(Path.Combine(t.Path, "dest", "b.txt"), DateTime.UtcNow);

        await FileOperations.CopyAsync([newer, older], Path.Combine(t.Path, "dest"), ConflictPolicy.OverwriteOlder);

        Assert.Equal("new", File.ReadAllText(Path.Combine(t.Path, "dest", "a.txt")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(t.Path, "dest", "b.txt")));
    }

    [Fact]
    public async Task RenameNew_keeps_both()
    {
        using var t = new TempDir();
        var f = t.File("src/a.txt", "new");
        t.File("dest/a.txt", "old");

        await FileOperations.CopyAsync([f], Path.Combine(t.Path, "dest"), ConflictPolicy.RenameNew);

        Assert.Equal("old", File.ReadAllText(Path.Combine(t.Path, "dest", "a.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(t.Path, "dest", "a (2).txt")));
    }

    [Fact]
    public async Task Copy_preserves_date_and_leaves_no_temp_file()
    {
        using var t = new TempDir();
        var f = t.File("src/a.txt", "data");
        var when = new DateTime(2020, 5, 17, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(f, when);
        var dest = t.Dir("dest");

        await FileOperations.CopyAsync([f], dest, ConflictPolicy.Overwrite);

        Assert.Equal(when, File.GetLastWriteTimeUtc(Path.Combine(dest, "a.txt")));
        Assert.Single(Directory.GetFiles(dest));
    }

    [Fact]
    public async Task Cancelled_copy_keeps_existing_target_intact()
    {
        using var t = new TempDir();
        var big = t.File("src/big.bin", new string('x', 3 * 1024 * 1024));
        t.File("dest/big.bin", "original");
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.BytesDone > 0) cts.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FileOperations.CopyAsync([big], Path.Combine(t.Path, "dest"), ConflictPolicy.Overwrite, progress, cts.Token));

        Assert.Equal("original", File.ReadAllText(Path.Combine(t.Path, "dest", "big.bin")));
        Assert.Single(Directory.GetFiles(Path.Combine(t.Path, "dest")));
    }

    [Fact]
    public async Task Progress_reaches_total()
    {
        using var t = new TempDir();
        var a = t.File("src/a.txt", "12345");
        var b = t.File("src/d/b.txt", "123");
        TransferProgress? last = null;
        var progress = new SyncProgress(p => last = p);

        await FileOperations.CopyAsync([a, Path.Combine(t.Path, "src", "d")], t.Dir("dest"), ConflictPolicy.Overwrite, progress);

        Assert.NotNull(last);
        Assert.Equal(8, last!.BytesTotal);
        Assert.Equal(8, last.BytesDone);
        Assert.Equal(2, last.FilesDone);
    }

    [Fact]
    public async Task Move_with_skip_keeps_skipped_source()
    {
        using var t = new TempDir();
        t.File("src/d/keep.txt", "src");
        t.File("src/d/go.txt", "src");
        t.File("dest/d/keep.txt", "dest");

        await FileOperations.MoveAsync([Path.Combine(t.Path, "src", "d")], Path.Combine(t.Path, "dest"), ConflictPolicy.Skip);

        Assert.True(File.Exists(Path.Combine(t.Path, "src", "d", "keep.txt")));
        Assert.False(File.Exists(Path.Combine(t.Path, "src", "d", "go.txt")));
        Assert.Equal("dest", File.ReadAllText(Path.Combine(t.Path, "dest", "d", "keep.txt")));
    }

    /// <summary>IProgress synchrone (Progress&lt;T&gt; passe par le contexte et arrive trop tard pour les tests).</summary>
    private sealed class SyncProgress(Action<TransferProgress> action) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => action(value);
    }
}

public class RenameAndCreateTests
{
    [Fact]
    public void Rename_file()
    {
        using var t = new TempDir();
        var f = t.File("a.txt");
        var result = FileOperations.Rename(f, "b.txt");
        Assert.True(File.Exists(result));
        Assert.False(File.Exists(f));
    }

    [Fact]
    public void Rename_case_only_on_folder()
    {
        using var t = new TempDir();
        var d = t.Dir("photos");
        var result = FileOperations.Rename(d, "Photos");
        Assert.Equal("Photos", Path.GetFileName(result));
        Assert.True(Directory.Exists(result));
    }

    [Fact]
    public void Rename_refuses_existing_name()
    {
        using var t = new TempDir();
        var a = t.File("a.txt");
        t.File("b.txt");
        Assert.Throws<FileOperationException>(() => FileOperations.Rename(a, "b.txt"));
    }

    [Fact]
    public void CreateFile_creates_empty_file()
    {
        using var t = new TempDir();
        var p = FileOperations.CreateFile(t.Path, "notes.txt");
        Assert.Equal(0, new FileInfo(p).Length);
    }
}

public class MaskMatcherTests
{
    [Theory]
    [InlineData("*.jpg", "photo.JPG", true)]
    [InlineData("*.jpg;*.png", "image.png", true)]
    [InlineData("*.jpg *.png", "doc.txt", false)]
    [InlineData("*.*", "README", true)]
    [InlineData("*.* | *.tmp", "cache.tmp", false)]
    [InlineData("*.* | *.tmp", "cache.txt", true)]
    [InlineData("img_??.jpg", "img_01.jpg", true)]
    [InlineData("img_??.jpg", "img_001.jpg", false)]
    public void Matches(string mask, string name, bool expected) =>
        Assert.Equal(expected, new MaskMatcher(mask).IsMatch(name));
}

public class FolderSizeTests
{
    [Fact]
    public async Task Counts_everything_recursively()
    {
        using var t = new TempDir();
        t.File("a.txt", "12345");
        t.File("sub/b.txt", "123");
        t.Dir("sub/empty");

        var r = await FolderSize.CalculateAsync(t.Path);

        Assert.Equal(8, r.Bytes);
        Assert.Equal(2, r.Files);
        Assert.Equal(2, r.Folders);
    }
}

public class MultiRenameTests
{
    private static FileEntry Entry(string path) => new(Path.GetFileName(path), path, Directory.Exists(path),
        File.Exists(path) ? new FileInfo(path).Length : 0, new DateTime(2026, 3, 9, 14, 5, 7), FileAttributes.Normal);

    [Fact]
    public void Counter_and_name_slice()
    {
        using var t = new TempDir();
        var items = new[] { Entry(t.File("vacances.jpg")), Entry(t.File("plage.jpg")) };
        var o = new RenameOptions { NameMask = "[N1-3]_[C]", CounterDigits = 3 };

        var names = MultiRename.Preview(items, o).Select(p => p.NewName);

        Assert.Equal(["vac_001.jpg", "pla_002.jpg"], names);
    }

    [Fact]
    public void Date_placeholders_and_extension_mask()
    {
        using var t = new TempDir();
        var items = new[] { Entry(t.File("IMG.JPEG")) };
        var o = new RenameOptions { NameMask = "[Y]-[M]-[D]_[h][m][s]", ExtensionMask = "jpg" };
        Assert.Equal("2026-03-09_140507.jpg", MultiRename.Preview(items, o)[0].NewName);
    }

    [Fact]
    public void Search_replace_and_case()
    {
        using var t = new TempDir();
        var items = new[] { Entry(t.File("Mon Fichier Test.TXT")) };
        var o = new RenameOptions { Search = " ", Replace = "_", Case = CaseMode.Lower };
        Assert.Equal("mon_fichier_test.txt", MultiRename.Preview(items, o)[0].NewName);
    }

    [Fact]
    public void Regex_replace()
    {
        using var t = new TempDir();
        var items = new[] { Entry(t.File("episode 12.mkv")) };
        var o = new RenameOptions { Search = @"episode (\d+)", Replace = "E$1", UseRegex = true };
        Assert.Equal("E12.mkv", MultiRename.Preview(items, o)[0].NewName);
    }

    [Fact]
    public void Invalid_regex_is_reported_not_thrown()
    {
        using var t = new TempDir();
        var items = new[] { Entry(t.File("a.txt")) };
        var o = new RenameOptions { Search = "(", UseRegex = true };
        Assert.NotNull(MultiRename.Preview(items, o)[0].Error);
    }

    [Fact]
    public void Duplicates_are_reported()
    {
        using var t = new TempDir();
        var items = new[] { Entry(t.File("a.txt")), Entry(t.File("b.txt")) };
        var previews = MultiRename.Preview(items, new RenameOptions { NameMask = "same" });
        Assert.All(previews, p => Assert.NotNull(p.Error));
        Assert.Throws<FileOperationException>(() => MultiRename.Execute(previews));
    }

    [Fact]
    public void Collision_with_untouched_file_is_reported()
    {
        using var t = new TempDir();
        t.File("taken.txt");
        var items = new[] { Entry(t.File("a.txt")) };
        Assert.NotNull(MultiRename.Preview(items, new RenameOptions { NameMask = "taken" })[0].Error);
    }

    [Fact]
    public void Swap_two_names_then_undo()
    {
        using var t = new TempDir();
        var a = t.File("a.txt", "A");
        var b = t.File("b.txt", "B");
        var items = new[] { Entry(a), Entry(b) };
        // a → b et b → a grâce à un rechercher/remplacer par expression régulière
        var o = new RenameOptions { Search = "^([ab])", Replace = "x$1", UseRegex = true };
        var previews = MultiRename.Preview(items, o)
            .Select(p => p with { NewName = p.Entry.Name == "a.txt" ? "b.txt" : "a.txt", Error = null }).ToList();

        var log = MultiRename.Execute(previews);
        Assert.Equal("B", File.ReadAllText(a));
        Assert.Equal("A", File.ReadAllText(b));

        MultiRename.Undo(log);
        Assert.Equal("A", File.ReadAllText(a));
        Assert.Equal("B", File.ReadAllText(b));
    }

    [Fact]
    public void Folders_have_no_extension()
    {
        using var t = new TempDir();
        var items = new[] { Entry(t.Dir("v1.2")) };
        Assert.Equal("v1.2_old", MultiRename.Preview(items, new RenameOptions { NameMask = "[N]_old" })[0].NewName);
    }
}
