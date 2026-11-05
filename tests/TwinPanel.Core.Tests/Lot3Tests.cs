// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;
using TwinPanel.Core.Compare;
using TwinPanel.Core.Search;

namespace TwinPanel.Core.Tests;

public class FileSearchTests
{
    private static async Task<List<string>> Run(SearchCriteria c)
    {
        var list = new List<string>();
        await foreach (var e in FileSearch.SearchAsync(c)) list.Add(e.Name);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    [Fact]
    public async Task By_mask_recursive()
    {
        using var t = new TempDir();
        t.File("a.jpg"); t.File("b.txt"); t.File("sub/c.jpg"); t.File("sub/deep/d.JPG");
        Assert.Equal(["a.jpg", "c.jpg", "d.JPG"], await Run(new SearchCriteria { Root = t.Path, NameMask = "*.jpg", IncludeFolders = false }));
    }

    [Fact]
    public async Task Depth_zero_stays_in_root()
    {
        using var t = new TempDir();
        t.File("a.jpg"); t.File("sub/c.jpg");
        Assert.Equal(["a.jpg"], await Run(new SearchCriteria { Root = t.Path, NameMask = "*.jpg", MaxDepth = 0 }));
    }

    [Fact]
    public async Task Containing_text_case_insensitive_and_latin1()
    {
        using var t = new TempDir();
        t.File("utf8.txt", "Bonjour Élodie");
        File.WriteAllBytes(Path.Combine(t.Path, "latin1.txt"), Encoding.Latin1.GetBytes("Salut élodie"));
        t.File("other.txt", "rien");
        Assert.Equal(["latin1.txt", "utf8.txt"], await Run(new SearchCriteria { Root = t.Path, ContainsText = "ÉLODIE" }));
    }

    [Fact]
    public async Task Containing_text_across_chunk_boundary()
    {
        using var t = new TempDir();
        var content = new string('x', 1024 * 1024 - 3) + "AIGUILLE" + new string('y', 100);
        t.File("big.txt", content);
        Assert.Equal(["big.txt"], await Run(new SearchCriteria { Root = t.Path, ContainsText = "aiguille" }));
    }

    [Fact]
    public async Task Regex_and_size_filters()
    {
        using var t = new TempDir();
        t.File("a.log", "erreur 404 ici");
        t.File("b.log", "erreur abc");
        t.File("c.log", new string('z', 5000) + "erreur 500");
        var c = new SearchCriteria { Root = t.Path, ContainsText = @"erreur \d{3}", TextIsRegex = true, MaxSize = 1000 };
        Assert.Equal(["a.log"], await Run(c));
    }

    [Fact]
    public async Task Missing_root_throws()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            Run(new SearchCriteria { Root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()) }));
    }
}

public class DuplicateFinderTests
{
    [Fact]
    public async Task Finds_identical_content_only()
    {
        using var t = new TempDir();
        var a = t.File("a.txt", "pareil");
        var b = t.File("sub/b.txt", "pareil");
        t.File("c.txt", "autre!");           // même taille, contenu différent
        t.File("d.txt", "différent et plus long");
        var files = Directory.GetFiles(t.Path, "*", SearchOption.AllDirectories)
            .Select(p => new FileEntry(Path.GetFileName(p), p, false, new FileInfo(p).Length, default, FileAttributes.Normal));

        var groups = await DuplicateFinder.FindAsync(files);

        var g = Assert.Single(groups);
        Assert.Equal(2, g.Files.Count);
        // GetFullPath : sous Windows, « sub/b.txt » et « sub\\b.txt » désignent le même fichier.
        Assert.Contains(g.Files, f => f.FullPath == Path.GetFullPath(a));
        Assert.Contains(g.Files, f => f.FullPath == Path.GetFullPath(b));
    }
}

public class FolderCompareTests
{
    [Fact]
    public async Task Detects_all_states_and_syncs()
    {
        using var t = new TempDir();
        var L = t.Dir("L");
        var R = t.Dir("R");
        t.File("L/same.txt", "x"); t.File("R/same.txt", "x");
        t.File("L/onlyleft.txt", "l");
        t.File("R/onlyright.txt", "r");
        t.File("L/newer.txt", "new"); t.File("R/newer.txt", "old");
        t.File("L/sub/inner.txt", "i");
        var now = DateTime.Now;
        foreach (var f in new[] { "L/same.txt", "R/same.txt" }) File.SetLastWriteTime(Path.Combine(t.Path, f), now);
        File.SetLastWriteTime(Path.Combine(t.Path, "L/newer.txt"), now);
        File.SetLastWriteTime(Path.Combine(t.Path, "R/newer.txt"), now.AddHours(-1));

        var items = await FolderComparer.CompareAsync(L, R, new CompareOptions());
        CompareState StateOf(string rel) => items.Single(i => i.RelativePath == rel).State;

        Assert.Equal(CompareState.Equal, StateOf("same.txt"));
        Assert.Equal(CompareState.LeftOnly, StateOf("onlyleft.txt"));
        Assert.Equal(CompareState.RightOnly, StateOf("onlyright.txt"));
        Assert.Equal(CompareState.LeftNewer, StateOf("newer.txt"));
        Assert.Equal(CompareState.LeftOnly, StateOf("sub"));
        Assert.Equal(CompareState.LeftOnly, StateOf(Path.Combine("sub", "inner.txt")));

        var plan = items.Select(i => (i, i.DefaultAction(bidirectional: true))).ToList();
        await FolderSync.ExecuteAsync(L, R, plan);

        Assert.Equal("new", File.ReadAllText(Path.Combine(R, "newer.txt")));
        Assert.True(File.Exists(Path.Combine(R, "onlyleft.txt")));
        Assert.True(File.Exists(Path.Combine(L, "onlyright.txt")));
        Assert.True(File.Exists(Path.Combine(R, "sub", "inner.txt")));

        var after = await FolderComparer.CompareAsync(L, R, new CompareOptions());
        Assert.All(after, i => Assert.Equal(CompareState.Equal, i.State));
    }

    [Fact]
    public async Task Content_comparison_catches_same_size_and_date()
    {
        using var t = new TempDir();
        var a = t.File("L/f.bin", "abc");
        var b = t.File("R/f.bin", "abd");
        var when = DateTime.Now;
        File.SetLastWriteTime(a, when);
        File.SetLastWriteTime(b, when);

        var quick = await FolderComparer.CompareAsync(Path.Combine(t.Path, "L"), Path.Combine(t.Path, "R"), new CompareOptions());
        var deep = await FolderComparer.CompareAsync(Path.Combine(t.Path, "L"), Path.Combine(t.Path, "R"), new CompareOptions { CompareContent = true });

        Assert.Equal(CompareState.Equal, quick.Single().State);
        Assert.Equal(CompareState.Different, deep.Single().State);
    }

    [Fact]
    public void One_way_sync_ignores_right_only()
    {
        var item = new CompareItem("x", false, null, new FileEntry("x", "/x", false, 1, default, FileAttributes.Normal), CompareState.RightOnly);
        Assert.Equal(SyncAction.None, item.DefaultAction(bidirectional: false));
        Assert.Equal(SyncAction.CopyToLeft, item.DefaultAction(bidirectional: true));
    }
}

public class TextDiffTests
{
    [Fact]
    public void Identical_texts_are_all_same()
    {
        var d = TextDiff.Diff(["a", "b"], ["a", "b"]);
        Assert.All(d, l => Assert.Equal(DiffKind.Same, l.Kind));
    }

    [Fact]
    public void Insert_delete_change()
    {
        string[] left = ["un", "deux", "trois", "quatre"];
        string[] right = ["un", "DEUX", "trois", "quatre", "cinq"];
        var d = TextDiff.Diff(left, right);

        Assert.Equal(DiffKind.Same, d[0].Kind);
        Assert.Equal(DiffKind.Changed, d[1].Kind);
        Assert.Equal("deux", d[1].Left);
        Assert.Equal("DEUX", d[1].Right);
        Assert.Equal(DiffKind.Added, d[^1].Kind);
        Assert.Equal("cinq", d[^1].Right);
        Assert.Equal(5, d.Count);
    }

    [Fact]
    public void Removed_line_in_the_middle()
    {
        var d = TextDiff.Diff(["a", "b", "c", "x", "d"], ["a", "c", "x", "d"]);
        var removed = Assert.Single(d, l => l.Kind != DiffKind.Same);
        Assert.Equal(DiffKind.Removed, removed.Kind);
        Assert.Equal("b", removed.Left);
        Assert.Equal(2, removed.LeftNo);
    }

    [Fact]
    public void Every_line_is_accounted_for()
    {
        var rnd = new Random(42);
        var a = Enumerable.Range(0, 300).Select(_ => rnd.Next(20).ToString()).ToList();
        var b = Enumerable.Range(0, 280).Select(_ => rnd.Next(20).ToString()).ToList();
        var d = TextDiff.Diff(a, b);
        Assert.Equal(a, d.Where(l => l.Left is not null).Select(l => l.Left!).ToList());
        Assert.Equal(b, d.Where(l => l.Right is not null).Select(l => l.Right!).ToList());
    }
}

public class ChecksumTests
{
    [Fact]
    public async Task Known_sha256_and_md5()
    {
        using var t = new TempDir();
        var f = t.File("abc.txt", "abc");
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", await Checksums.ComputeAsync(f, HashKind.Sha256));
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", await Checksums.ComputeAsync(f, HashKind.Md5));
    }

    [Fact]
    public async Task Create_then_verify_detects_changes()
    {
        using var t = new TempDir();
        var a = t.File("a.txt", "un");
        var b = t.File("sub/b.txt", "deux");
        var c = t.File("c.txt", "trois");
        var sums = Path.Combine(t.Path, "sommes.sha256");

        await Checksums.CreateAsync([a, b, c], HashKind.Sha256, sums);
        Assert.All(await Checksums.VerifyAsync(sums), r => Assert.Equal(VerifyStatus.Ok, r.Status));

        File.WriteAllText(b, "modifié");
        File.Delete(c);
        var results = await Checksums.VerifyAsync(sums);
        Assert.Equal(VerifyStatus.Ok, results.Single(r => r.FileName == "a.txt").Status);
        Assert.Equal(VerifyStatus.Mismatch, results.Single(r => r.FileName == "sub/b.txt").Status);
        Assert.Equal(VerifyStatus.Missing, results.Single(r => r.FileName == "c.txt").Status);
    }
}
