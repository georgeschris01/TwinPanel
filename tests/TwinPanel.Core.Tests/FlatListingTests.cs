// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Navigation;
using TwinPanel.Core.Remote;
using TwinPanel.Core.Search;

namespace TwinPanel.Core.Tests;

public class ThumbnailTests
{
    private static FileEntry F(string name, long size = 1000) => new(name, "/x/" + name, false, size, new DateTime(2026, 1, 1), FileAttributes.Normal);

    [Fact]
    public void Pc_gives_images_and_videos_server_gives_images_only()
    {
        Assert.True(Media.ThumbnailRules.CanHaveThumbnail(F("a.jpg"), Media.ThumbnailSourceKind.Local, false));
        Assert.True(Media.ThumbnailRules.CanHaveThumbnail(F("a.mp4"), Media.ThumbnailSourceKind.Local, false));
        Assert.False(Media.ThumbnailRules.CanHaveThumbnail(F("a.txt"), Media.ThumbnailSourceKind.Local, false));
        Assert.True(Media.ThumbnailRules.CanHaveThumbnail(F("a.jpg"), Media.ThumbnailSourceKind.Remote, false));
        Assert.False(Media.ThumbnailRules.CanHaveThumbnail(F("a.mp4"), Media.ThumbnailSourceKind.Remote, false));
    }

    [Fact]
    public void Server_limit_is_20_mo_included_and_archives_have_none()
    {
        const long limit = 20L * 1024 * 1024;
        Assert.True(Media.ThumbnailRules.CanHaveThumbnail(F("a.jpg", limit), Media.ThumbnailSourceKind.Remote, false));
        Assert.False(Media.ThumbnailRules.CanHaveThumbnail(F("a.jpg", limit + 1), Media.ThumbnailSourceKind.Remote, false));
        Assert.False(Media.ThumbnailRules.CanHaveThumbnail(F("a.jpg"), Media.ThumbnailSourceKind.Local, insideArchive: true));
        Assert.False(Media.ThumbnailRules.CanHaveThumbnail(new FileEntry("d", "/d", true, 0, default, FileAttributes.Directory), Media.ThumbnailSourceKind.Local, false));
    }

    [Fact]
    public void Server_thumbnails_can_be_switched_off()
    {
        Assert.False(Media.ThumbnailRules.CanHaveThumbnail(F("a.jpg"), Media.ThumbnailSourceKind.Remote, false, maxRemoteBytes: -1));
        Assert.False(Media.ThumbnailRules.CanHaveThumbnail(F("vide.jpg", 0), Media.ThumbnailSourceKind.Remote, false, maxRemoteBytes: -1));
        Assert.True(Media.ThumbnailRules.CanHaveThumbnail(F("a.jpg"), Media.ThumbnailSourceKind.Local, false, maxRemoteBytes: -1));
    }

    [Fact]
    public void Modified_file_gets_a_new_key()
    {
        var before = F("a.jpg");
        Assert.NotEqual(Media.ThumbnailRules.Key(before, 160), Media.ThumbnailRules.Key(before with { Size = 2000 }, 160));
        Assert.NotEqual(Media.ThumbnailRules.Key(before, 160), Media.ThumbnailRules.Key(before with { LastWriteTime = new DateTime(2026, 1, 2) }, 160));
        Assert.NotEqual(Media.ThumbnailRules.Key(before, 160), Media.ThumbnailRules.Key(before, 256));
    }

    [Fact]
    public void Cache_drops_the_least_recently_used_beyond_its_limit()
    {
        var cache = new Media.ThumbnailCache<string>(maxBytes: 100);
        Assert.Empty(cache.Add("a", "A", 40));
        Assert.Empty(cache.Add("b", "B", 40));
        Assert.Equal("A", cache.Get("a")); // « a » redevient la plus récente
        Assert.Equal(["B"], cache.Add("c", "C", 40));
        Assert.Null(cache.Get("b"));
        Assert.Equal(80, cache.UsedBytes);
    }

    [Fact]
    public void Cache_replaces_a_key_and_keeps_the_newest_even_if_too_big()
    {
        var cache = new Media.ThumbnailCache<string>(maxBytes: 100);
        cache.Add("a", "A1", 40);
        Assert.Equal(["A1"], cache.Add("a", "A2", 50));
        Assert.Equal(50, cache.UsedBytes);
        Assert.Equal(["A2"], cache.Add("b", "B", 500));
        Assert.Equal("B", cache.Get("b"));
        Assert.Equal(1, cache.Count);
    }
}

public class FlatListingTests
{
    private static async Task<List<FileEntry>> ListLocal(string root, bool showHidden)
    {
        var list = new List<FileEntry>();
        await foreach (var e in FileSearch.SearchAsync(FlatListing.Criteria(root, showHidden))) list.Add(e);
        return list;
    }

    [Theory]
    [InlineData(@"C:\a", @"C:\a\f.txt", "")]
    [InlineData(@"C:\a", @"C:\a\b\c\f.txt", @"b\c")]
    [InlineData(@"C:\", @"C:\b\f.txt", "b")]
    [InlineData(@"C:\", @"C:\f.txt", "")]
    [InlineData("sftp://srv/home", "sftp://srv/home/x/f.txt", "x")]
    [InlineData("sftp://srv/", "sftp://srv/x/y/f.txt", "x/y")]
    public void Relative_folder(string root, string path, string expected) =>
        Assert.Equal(expected, FlatListing.RelativeFolder(root, path));

    [Fact]
    public void Duplicate_names_ignore_case()
    {
        Assert.Equal(["a.txt"], FlatListing.DuplicateNames(["a.txt", "b.txt", "A.TXT"]));
        Assert.Empty(FlatListing.DuplicateNames(["a.txt", "b.txt"]));
    }

    [Fact]
    public void Split_gives_groups_with_unique_names()
    {
        var groups = FlatListing.SplitUniqueNames(["a", "b", "A", "a"], n => n);
        Assert.Equal(3, groups.Count);
        Assert.Equal(["a", "b"], groups[0]);
        Assert.Equal(["A"], groups[1]);
        Assert.Equal(["a"], groups[2]);
    }

    [Fact]
    public async Task Local_lists_files_of_all_subfolders_without_folders()
    {
        using var dir = new TempDir();
        dir.File("racine.txt");
        dir.File(@"a\un.txt");
        dir.File(@"a\b\deux.txt");
        var names = (await ListLocal(dir.Path, showHidden: false)).Select(e => e.Name).Order();
        Assert.Equal(["deux.txt", "racine.txt", "un.txt"], names);
    }

    [Fact]
    public async Task Local_hidden_folders_are_not_entered_unless_asked()
    {
        using var dir = new TempDir();
        dir.File("visible.txt");
        dir.File(@"cache\dedans.txt");
        File.SetAttributes(dir.Dir("cache"), FileAttributes.Hidden | FileAttributes.Directory);
        File.SetAttributes(dir.File("masque.txt"), FileAttributes.Hidden);

        Assert.Equal(["visible.txt"], (await ListLocal(dir.Path, false)).Select(e => e.Name));
        Assert.Equal(3, (await ListLocal(dir.Path, true)).Count);
    }

    [Fact]
    public async Task Server_skips_dot_folders_unless_asked()
    {
        var fs = new MemoryFileSystem();
        foreach (var dir in new[] { "/site", "/site/images", "/site/.git" }) await fs.CreateDirectoryAsync(dir, default);
        fs.AddFile("/site/index.html", "x");
        fs.AddFile("/site/images/logo.png", "x");
        fs.AddFile("/site/.git/config", "x");
        var root = new PanelRemotePath("sftp", "srv", "/site");

        async Task<List<FileEntry>> List(bool hidden)
        {
            var list = new List<FileEntry>();
            await foreach (var e in RemoteSearch.SearchAsync(fs, root, FlatListing.Criteria("/site", hidden))) list.Add(e);
            return list;
        }

        var visible = await List(false);
        Assert.Equal(["index.html", "logo.png"], visible.Select(e => e.Name).Order());
        Assert.Contains(visible, e => e.FullPath == "sftp://srv/site/images/logo.png");
        Assert.Equal(3, (await List(true)).Count);
    }

    [Fact]
    public void Sort_by_folder_then_name()
    {
        FileEntry F(string path) => new(Path.GetFileName(path.Replace('\\', '/')), path, false, 0, default, FileAttributes.Normal);
        var sorted = EntrySorter.Sort([F("/r/b/a.txt"), F("/r/a/z.txt"), F("/r/a/c.txt")], SortColumn.Folder, false);
        Assert.Equal(["/r/a/c.txt", "/r/a/z.txt", "/r/b/a.txt"], sorted.Select(e => e.FullPath));
    }

    [Fact]
    public async Task Same_names_are_all_kept_when_moved_into_one_folder_of_the_server()
    {
        // Le cas dangereux de la vue à plat : deux « x.txt » de sous-dossiers différents vers un seul dossier.
        var fs = new MemoryFileSystem();
        fs.AddFile("/src/a/x.txt", "un");
        fs.AddFile("/src/b/x.txt", "deux");
        await fs.CreateDirectoryAsync("/dest", default);
        var items = new[] { new RemoteItem("x.txt", "/src/a/x.txt", false, 2, default), new RemoteItem("x.txt", "/src/b/x.txt", false, 4, default) };

        await RemoteTransfer.RenameIntoAsync(fs, items, "/dest", ConflictPolicy.RenameNew, null, [], default);

        var names = (await fs.ListAsync("/dest", default)).Select(i => i.Name).ToList();
        Assert.Equal(2, names.Count);
        Assert.Contains("x.txt", names);
    }
}
