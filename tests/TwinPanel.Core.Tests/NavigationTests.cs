// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Navigation;

namespace TwinPanel.Core.Tests;

public class EntrySorterTests
{
    private static FileEntry F(string name, long size, int day) =>
        new(name, "/x/" + name, false, size, new DateTime(2026, 1, day), FileAttributes.Normal);
    private static FileEntry D(string name, int day) =>
        new(name, "/x/" + name, true, 0, new DateTime(2026, 1, day), FileAttributes.Directory);
    private static readonly FileEntry Up = new("..", "/", true, 0, default, FileAttributes.Directory, IsParentLink: true);

    private static readonly FileEntry[] Sample =
        [F("b.txt", 300, 3), D("zeta", 1), F("a.zip", 100, 5), Up, D("alpha", 9), F("c.jpg", 200, 1)];

    [Fact]
    public void By_name_parent_then_dirs_then_files()
    {
        var names = EntrySorter.Sort(Sample, SortColumn.Name, false).Select(e => e.Name);
        Assert.Equal(["..", "alpha", "zeta", "a.zip", "b.txt", "c.jpg"], names);
    }

    [Fact]
    public void By_name_descending_keeps_parent_and_dirs_first()
    {
        var names = EntrySorter.Sort(Sample, SortColumn.Name, true).Select(e => e.Name);
        Assert.Equal(["..", "zeta", "alpha", "c.jpg", "b.txt", "a.zip"], names);
    }

    [Fact]
    public void By_size_sorts_files_only()
    {
        var names = EntrySorter.Sort(Sample, SortColumn.Size, true).Select(e => e.Name);
        Assert.Equal(["..", "alpha", "zeta", "b.txt", "c.jpg", "a.zip"], names);
    }

    [Fact]
    public void By_extension()
    {
        var names = EntrySorter.Sort(Sample, SortColumn.Extension, false).Select(e => e.Name);
        Assert.Equal(["..", "alpha", "zeta", "c.jpg", "b.txt", "a.zip"], names);
    }

    [Fact]
    public void By_date_applies_to_dirs_too()
    {
        var names = EntrySorter.Sort(Sample, SortColumn.Date, false).Select(e => e.Name);
        Assert.Equal(["..", "zeta", "alpha", "c.jpg", "b.txt", "a.zip"], names);
    }
}

public class NavigationHistoryTests
{
    [Fact]
    public void Back_and_forward()
    {
        var h = new NavigationHistory();
        h.Push("A"); h.Push("B"); h.Push("C");
        Assert.Equal("B", h.Back());
        Assert.Equal("A", h.Back());
        Assert.False(h.CanGoBack);
        Assert.Equal("B", h.Forward());
        Assert.True(h.CanGoForward);
    }

    [Fact]
    public void Push_after_back_drops_forward_branch()
    {
        var h = new NavigationHistory();
        h.Push("A"); h.Push("B"); h.Push("C");
        h.Back();
        h.Push("D");
        Assert.False(h.CanGoForward);
        Assert.Equal(["A", "B", "D"], h.Items);
    }

    [Fact]
    public void Same_path_twice_is_recorded_once()
    {
        var h = new NavigationHistory();
        h.Push("A"); h.Push("a");
        Assert.Single(h.Items);
    }

    [Fact]
    public void Capacity_is_respected()
    {
        var h = new NavigationHistory(capacity: 3);
        foreach (var p in new[] { "1", "2", "3", "4" }) h.Push(p);
        Assert.Equal(["2", "3", "4"], h.Items);
    }

    [Fact]
    public void Undo_restores_position_after_failed_back()
    {
        var h = new NavigationHistory();
        h.Push("A"); h.Push("B");
        h.Back();
        h.Undo(wasBack: true);
        Assert.Equal("B", h.Current);
    }
}

public class BreadcrumbTests
{
    [Fact]
    public void Splits_into_cumulative_segments()
    {
        using var t = new TempDir();
        var deep = t.Dir("one/two");
        var segs = Breadcrumb.Split(deep);

        Assert.Equal("two", segs[^1].Name);
        Assert.Equal(deep, segs[^1].FullPath);
        Assert.Equal("one", segs[^2].Name);
        Assert.Equal(Path.GetPathRoot(deep), segs[0].FullPath);
    }
}

public class FileCategoryTests
{
    [Fact]
    public void Dot_file_has_no_extension()
    {
        var e = new FileEntry(".keep", "/x/.keep", false, 1, default, FileAttributes.Normal);
        Assert.Equal(string.Empty, e.Extension);
        Assert.Equal(".keep", e.DisplayName);
    }

    [Theory]
    [InlineData("photo.JPG", FileCategory.Image)]
    [InlineData("film.mkv", FileCategory.Video)]
    [InlineData("data.7z", FileCategory.Archive)]
    [InlineData("noext", FileCategory.File)]
    [InlineData(".gitignore", FileCategory.File)]
    public void Detects_category_from_extension(string name, FileCategory expected)
    {
        var e = new FileEntry(name, "/" + name, false, 1, default, FileAttributes.Normal);
        Assert.Equal(expected, e.Category);
    }
}
