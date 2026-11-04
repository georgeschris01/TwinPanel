// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Operations;

namespace TwinPanel.Core.Tests;

public class FileClipboardTests
{
    [Fact]
    public void Drop_effect_round_trips_copy_and_move()
    {
        Assert.True(FileClipboard.IsMove(FileClipboard.DropEffectBytes(move: true)));
        Assert.False(FileClipboard.IsMove(FileClipboard.DropEffectBytes(move: false)));
        Assert.Equal(new byte[] { 2, 0, 0, 0 }, FileClipboard.DropEffectBytes(move: true));
    }

    [Fact]
    public void Explorer_copy_with_link_is_not_a_move_and_missing_effect_means_copy()
    {
        Assert.False(FileClipboard.IsMove([5, 0, 0, 0])); // copier + lien, comme l'Explorateur
        Assert.False(FileClipboard.IsMove([3, 0, 0, 0]));
        Assert.False(FileClipboard.IsMove(null));
        Assert.False(FileClipboard.IsMove([2]));
    }

    [Theory]
    [InlineData(@"C:\a\b.txt", @"C:\a")]
    [InlineData(@"C:\a\dossier\", @"C:\a")]
    [InlineData("sftp://serveur/home/x.txt", "sftp://serveur/home")]
    [InlineData("sftp://serveur/x.txt", "sftp://serveur")]
    [InlineData("/home/u/x", "/home/u")]
    public void Parent_of_local_and_server_paths(string path, string parent) =>
        Assert.Equal(parent, FileClipboard.ParentOf(path));

    [Fact]
    public void Paste_into_the_same_folder_is_detected()
    {
        Assert.True(FileClipboard.AnyAlreadyIn(["sftp://serveur/home/x.txt"], "sftp://serveur/home/"));
        Assert.False(FileClipboard.AnyAlreadyIn(["sftp://serveur/home/x.txt"], "sftp://serveur/tmp"));
        Assert.True(FileClipboard.AnyAlreadyIn(["sftp://serveur/x.txt"], "sftp://serveur/"));
    }

    [Fact]
    public void Local_entry_reads_files_and_folders_and_ignores_missing_ones()
    {
        using var t = new TempDir();
        var file = t.File("a.txt", "abc");
        var dir = t.Dir("d");
        Assert.Equal(3, FileClipboard.LocalEntry(file)!.Size);
        Assert.True(FileClipboard.LocalEntry(dir)!.IsDirectory);
        Assert.Null(FileClipboard.LocalEntry(Path.Combine(t.Path, "absent")));
    }
}
