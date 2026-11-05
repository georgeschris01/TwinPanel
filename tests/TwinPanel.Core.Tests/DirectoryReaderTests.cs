// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Tests;

public class DirectoryReaderTests
{
    [Fact]
    public void Parent_first_then_directories_then_files_sorted()
    {
        using var t = new TempDir();
        t.File("b.txt"); t.File("A.txt"); t.Dir("zeta"); t.Dir("Alpha");

        var names = DirectoryReader.Read(t.Path).Select(e => e.Name).ToList();

        Assert.Equal(["..", "Alpha", "zeta", "A.txt", "b.txt"], names);
    }

    [Fact]
    public void Missing_directory_throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => DirectoryReader.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
    }

    [Fact]
    public void Empty_directory_only_has_parent_link()
    {
        using var t = new TempDir();
        var entries = DirectoryReader.Read(t.Path);
        Assert.Single(entries);
        Assert.True(entries[0].IsParentLink);
    }
}
