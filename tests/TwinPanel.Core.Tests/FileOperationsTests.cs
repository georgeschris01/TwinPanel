// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Tests;

public class FileOperationsTests
{
    [Fact]
    public async Task Copy_directory_recursively()
    {
        using var t = new TempDir();
        t.File("src/folder/sub/deep.txt", "hello");
        var dest = t.Dir("dest");

        await FileOperations.CopyAsync([Path.Combine(t.Path, "src", "folder")], dest, ConflictPolicy.Overwrite);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(dest, "folder", "sub", "deep.txt")));
        Assert.True(File.Exists(Path.Combine(t.Path, "src", "folder", "sub", "deep.txt"))); // source intacte
    }

    [Fact]
    public async Task Copy_into_itself_is_refused()
    {
        using var t = new TempDir();
        var src = t.Dir("a");
        var inner = t.Dir("a/b");

        await Assert.ThrowsAsync<FileOperationException>(() => FileOperations.CopyAsync([src], inner, ConflictPolicy.Overwrite));
    }

    [Fact]
    public async Task Copy_to_same_folder_is_refused()
    {
        using var t = new TempDir();
        var f = t.File("x.txt");
        await Assert.ThrowsAsync<FileOperationException>(() => FileOperations.CopyAsync([f], t.Path, ConflictPolicy.Overwrite));
    }

    [Fact]
    public async Task Skip_policy_keeps_existing_file()
    {
        using var t = new TempDir();
        var f = t.File("src/x.txt", "new");
        t.File("dest/x.txt", "old");

        await FileOperations.CopyAsync([f], Path.Combine(t.Path, "dest"), ConflictPolicy.Skip);

        Assert.Equal("old", File.ReadAllText(Path.Combine(t.Path, "dest", "x.txt")));
    }

    [Fact]
    public async Task Overwrite_policy_replaces_existing_file()
    {
        using var t = new TempDir();
        var f = t.File("src/x.txt", "new");
        t.File("dest/x.txt", "old");

        await FileOperations.CopyAsync([f], Path.Combine(t.Path, "dest"), ConflictPolicy.Overwrite);

        Assert.Equal("new", File.ReadAllText(Path.Combine(t.Path, "dest", "x.txt")));
    }

    [Fact]
    public async Task Move_removes_source()
    {
        using var t = new TempDir();
        var f = t.File("src/x.txt");
        var dest = t.Dir("dest");

        await FileOperations.MoveAsync([f], dest, ConflictPolicy.Overwrite);

        Assert.False(File.Exists(f));
        Assert.True(File.Exists(Path.Combine(dest, "x.txt")));
    }

    [Fact]
    public async Task Move_merges_into_existing_directory()
    {
        using var t = new TempDir();
        t.File("src/d/new.txt");
        t.File("dest/d/old.txt");

        await FileOperations.MoveAsync([Path.Combine(t.Path, "src", "d")], Path.Combine(t.Path, "dest"), ConflictPolicy.Overwrite);

        Assert.True(File.Exists(Path.Combine(t.Path, "dest", "d", "new.txt")));
        Assert.True(File.Exists(Path.Combine(t.Path, "dest", "d", "old.txt")));
        Assert.False(Directory.Exists(Path.Combine(t.Path, "src", "d")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData("a/b")]
    public void CreateDirectory_rejects_invalid_names(string name)
    {
        using var t = new TempDir();
        Assert.Throws<FileOperationException>(() => FileOperations.CreateDirectory(t.Path, name));
    }

    [Fact]
    public void CreateDirectory_rejects_existing()
    {
        using var t = new TempDir();
        t.Dir("exists");
        Assert.Throws<FileOperationException>(() => FileOperations.CreateDirectory(t.Path, "exists"));
    }

    [Fact]
    public async Task Delete_permanently()
    {
        using var t = new TempDir();
        var f = t.File("x.txt");
        var d = t.File("d/y.txt");

        await FileOperations.DeleteAsync([f, Path.GetDirectoryName(d)!], useRecycleBin: false);

        Assert.False(File.Exists(f));
        Assert.False(Directory.Exists(Path.GetDirectoryName(d)));
    }

    [Fact]
    public void FindConflicts_lists_existing_destinations()
    {
        using var t = new TempDir();
        var a = t.File("src/a.txt");
        var b = t.File("src/b.txt");
        t.File("dest/a.txt");

        var conflicts = FileOperations.FindConflicts([a, b], Path.Combine(t.Path, "dest"));

        Assert.Single(conflicts);
        Assert.EndsWith("a.txt", conflicts[0]);
    }
}
