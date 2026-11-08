// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Operations;

namespace TwinPanel.Core.Tests;

public sealed class ClipboardStagingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TwinPanelTests", "staging-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private static FileEntry File(string name, long size = 10) => new(name, "/serveur/" + name, false, size, DateTime.Now, FileAttributes.Normal);
    private static FileEntry Folder(string name) => new(name, "/serveur/" + name, true, 0, DateTime.Now, FileAttributes.Directory);

    [Fact]
    public void Each_copy_gets_its_own_new_folder_inside_the_root()
    {
        var staging = new ClipboardStaging(_root);
        var a = staging.NewFolder();
        var b = staging.NewFolder();
        Assert.NotEqual(a, b);
        Assert.True(Directory.Exists(a) && Directory.Exists(b));
        Assert.Equal(_root, Path.GetDirectoryName(a));
    }

    [Fact]
    public void Cleaning_removes_previous_copies_but_keeps_the_one_asked()
    {
        var staging = new ClipboardStaging(_root);
        var old = staging.NewFolder();
        System.IO.File.WriteAllText(Path.Combine(old, "photo.jpg"), "x");
        var current = staging.NewFolder();
        Assert.Equal(1, staging.CleanUp(keep: current));
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(current));
    }

    [Fact]
    public void At_startup_only_copies_older_than_a_day_are_removed()
    {
        var staging = new ClipboardStaging(_root);
        var old = staging.NewFolder();
        Directory.SetCreationTime(old, DateTime.Now.AddDays(-2));
        var recent = staging.NewFolder();
        Assert.Equal(1, staging.CleanUp(ClipboardStaging.KeepFor));
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(recent));
    }

    [Fact]
    public void Cleaning_without_any_copy_does_nothing()
    {
        Assert.Equal(0, new ClipboardStaging(_root).CleanUp());
    }

    [Fact]
    public void Same_names_from_a_flat_view_go_to_separate_folders()
    {
        var items = new[] { File("photo.jpg"), File("notes.txt"), File("photo.jpg"), File("photo.jpg") };
        var plan = ClipboardStaging.Plan(@"C:\copies", items, e => e.Name);
        Assert.Equal(3, plan.Count);
        Assert.Equal(@"C:\copies", plan[0].Folder);
        Assert.Equal(Path.Combine(@"C:\copies", "2"), plan[1].Folder);
        Assert.Equal(Path.Combine(@"C:\copies", "3"), plan[2].Folder);
        var paths = ClipboardStaging.LocalPaths(plan, e => e.Name);
        Assert.Equal(4, paths.Count);
        Assert.Equal(4, paths.Distinct().Count());
    }

    [Fact]
    public void Different_names_all_stay_in_the_same_folder()
    {
        var plan = ClipboardStaging.Plan(@"C:\copies", new[] { File("a.txt"), Folder("Vacances") }, e => e.Name);
        Assert.Single(plan);
        Assert.Equal(new[] { Path.Combine(@"C:\copies", "a.txt"), Path.Combine(@"C:\copies", "Vacances") }, ClipboardStaging.LocalPaths(plan, e => e.Name));
    }

    [Fact]
    public void Only_a_big_known_size_makes_copy_ask_first()
    {
        Assert.False(ClipboardStaging.ShouldAsk([File("petit.jpg", 3_000_000), Folder("Dossier")]));
        Assert.True(ClipboardStaging.ShouldAsk([File("film.mkv", ClipboardStaging.AskAboveBytes + 1)]));
        Assert.Equal(30, ClipboardStaging.KnownBytes([File("a", 10), File("b", 20), Folder("c")]));
    }
}
