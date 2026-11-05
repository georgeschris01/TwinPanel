// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Tests;

public class DesktopTrashTests : IDisposable
{
    private static readonly DateTime When = new(2026, 11, 2, 12, 5, 9);
    private readonly string _root = Directory.CreateTempSubdirectory("twinpanel-corbeille-").FullName;

    private string Trash => Path.Combine(_root, "Trash");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string NewFile(string folder, string name, string text = "contenu")
    {
        var directory = Path.Combine(_root, folder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Info_text_follows_the_standard()
    {
        var text = DesktopTrash.InfoText("/home/moi/mon été/a b#c.txt", When);
        Assert.Equal("[Trash Info]\nPath=/home/moi/mon%20%C3%A9t%C3%A9/a%20b%23c.txt\nDeletionDate=2026-11-02T12:05:09\n", text);
    }

    [Fact]
    public void A_control_character_in_a_name_cannot_add_a_line_to_the_info()
    {
        var text = DesktopTrash.InfoText("/home/moi/x\nPath=/etc/passwd", When);
        Assert.Equal(3, text.TrimEnd('\n').Split('\n').Length);
    }

    [Fact]
    public void A_file_moves_with_its_info()
    {
        var file = NewFile("a", "note.txt");
        DesktopTrash.MoveTo(file, Trash, null, When);
        Assert.False(File.Exists(file));
        Assert.Equal("contenu", File.ReadAllText(Path.Combine(Trash, "files", "note.txt")));
        Assert.StartsWith("[Trash Info]\nPath=", File.ReadAllText(Path.Combine(Trash, "info", "note.txt.trashinfo")));
    }

    [Fact]
    public void A_folder_keeps_its_content()
    {
        var file = NewFile(Path.Combine("a", "dossier"), "dedans.txt");
        var folder = Path.GetDirectoryName(file)!;
        DesktopTrash.MoveTo(folder, Trash, null, When);
        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(Path.Combine(Trash, "files", "dossier", "dedans.txt")));
    }

    [Fact]
    public void The_same_name_twice_keeps_both()
    {
        DesktopTrash.MoveTo(NewFile("a", "note.txt", "un"), Trash, null, When);
        DesktopTrash.MoveTo(NewFile("b", "note.txt", "deux"), Trash, null, When);
        Assert.Equal("un", File.ReadAllText(Path.Combine(Trash, "files", "note.txt")));
        Assert.Equal("deux", File.ReadAllText(Path.Combine(Trash, "files", "note.2.txt")));
        Assert.True(File.Exists(Path.Combine(Trash, "info", "note.2.txt.trashinfo")));
    }

    [Fact]
    public void Failure_keeps_nothing_and_says_so()
    {
        var missing = Path.Combine(_root, "absent.txt");
        var error = Assert.Throws<FileOperationException>(() => DesktopTrash.MoveTo(missing, Trash, null, When));
        Assert.Contains("absent.txt", error.Message);
        Assert.Empty(Directory.GetFiles(Path.Combine(Trash, "info")));
    }

    [Fact]
    public void A_file_stays_in_place_if_the_trash_cannot_be_created()
    {
        var file = NewFile("a", "note.txt");
        var blocked = NewFile("b", "pas-un-dossier");
        Assert.Throws<FileOperationException>(() => DesktopTrash.MoveTo(file, blocked, null, When));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void On_another_disk_the_path_is_relative_to_it()
    {
        var file = NewFile("disque", "note.txt");
        DesktopTrash.MoveTo(file, Trash, Path.Combine(_root, "disque"), When);
        Assert.Contains("Path=note.txt\n", File.ReadAllText(Path.Combine(Trash, "info", "note.txt.trashinfo")));
    }

    [Fact]
    public void The_longest_mount_wins()
    {
        var disk = Path.Combine(_root, "disque");
        string[] mounts = [_root, disk];
        Assert.Equal(disk + Path.DirectorySeparatorChar, DesktopTrash.MountOf(Path.Combine(disk, "x"), mounts));
        Assert.Equal(_root + Path.DirectorySeparatorChar, DesktopTrash.MountOf(disk + "2", mounts));
    }
}
