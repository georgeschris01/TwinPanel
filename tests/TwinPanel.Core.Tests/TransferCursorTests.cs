// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Navigation;
using Xunit;

namespace TwinPanel.Core.Tests;

public class TransferCursorTests
{
    [Fact]
    public void Same_remote_folder_ignores_the_last_slash() =>
        Assert.True(TransferCursor.SameFolder("sftp://serveur/home/utilisateur/", "sftp://serveur/home/utilisateur"));

    [Fact]
    public void Another_server_is_another_folder() =>
        Assert.False(TransferCursor.SameFolder("sftp://serveur/home", "sftp://autre/home"));

    [Fact]
    public void A_server_folder_is_never_a_pc_folder() =>
        Assert.False(TransferCursor.SameFolder("sftp://serveur/home", "/home"));

    [Fact]
    public void Same_pc_folder_ignores_the_last_separator() =>
        Assert.True(TransferCursor.SameFolder(Path.Combine(Path.GetTempPath(), "dossier") + Path.DirectorySeparatorChar,
                                              Path.Combine(Path.GetTempPath(), "dossier")));

    private static readonly string[] Before = ["..", "a", "b", "c", "d", "e"];

    private static string? After(int cursor, params string[] gone) =>
        TransferCursor.NextRemaining(Before, cursor, k => !gone.Contains(k));

    [Fact]
    public void One_item_moved_the_next_line_takes_its_place() =>
        Assert.Equal("c", After(2, "b"));

    [Fact]
    public void Several_items_moved_the_cursor_goes_to_the_first_line_left_after_them() =>
        Assert.Equal("d", After(1, "a", "b", "c"));

    [Fact]
    public void Items_moved_above_the_cursor_do_not_push_it_down() =>
        Assert.Equal("d", After(3, "a", "b", "c"));

    [Fact]
    public void The_last_lines_moved_the_cursor_stays_at_the_bottom() =>
        Assert.Equal("c", After(4, "d", "e"));

    [Fact]
    public void Nothing_left_gives_no_cursor() =>
        Assert.Null(TransferCursor.NextRemaining(["a", "b"], 0, _ => false));

    [Fact]
    public void First_arrived_follows_the_list_order() =>
        Assert.Equal("b.txt", TransferCursor.FirstArrived(["..", "a", "b.txt", "c.txt"], ["c.txt", "b.txt"], remote: true));

    [Fact]
    public void Nothing_arrived_gives_no_cursor() =>
        Assert.Null(TransferCursor.FirstArrived(["a", "b"], ["z"], remote: false));
}
