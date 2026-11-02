// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Tests;

/// <summary>Plus de place sur le disque, et onglets rouverts dans leur ordre.</summary>
public class DiskSpaceTests
{
    private static readonly IOException WindowsFull = new("Espace insuffisant", unchecked((int)0x80070070));

    [Fact]
    public void Full_disk_errors_are_recognized()
    {
        Assert.True(DiskSpace.IsFull(WindowsFull));
        Assert.True(DiskSpace.IsFull(new IOException("x", unchecked((int)0x80070027))));
        Assert.True(DiskSpace.IsFull(new IOException("x", 28)));
        Assert.True(DiskSpace.IsFull(new IOException("sftp: No space left on device")));
        Assert.True(DiskSpace.IsFull(new FileOperationException("x", WindowsFull)));
    }

    [Fact]
    public void Other_errors_are_not_taken_for_a_full_disk()
    {
        Assert.False(DiskSpace.IsFull(null));
        Assert.False(DiskSpace.IsFull(new IOException("Accès refusé", unchecked((int)0x80070005))));
        Assert.False(DiskSpace.IsFull(new InvalidOperationException("x")));
    }

    [Fact]
    public void The_message_says_how_much_is_missing()
    {
        var message = DiskSpace.ShortfallMessage(@"D:\films", 5_000, 1_000, canResume: true);
        Assert.Contains(FileEntry.FormatSize(4_000), message, StringComparison.Ordinal);
        Assert.Contains("Reprendre", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Reprendre", DiskSpace.ShortfallMessage(@"D:\films", 5_000, 1_000, canResume: false), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_room_an_operation_stops_or_pauses()
    {
        using var t = new TempDir();
        const long tooMuch = long.MaxValue / 2;
        Assert.Throws<FileOperationException>(() => DiskSpace.CheckBeforeStart(t.Path, tooMuch, null, CancellationToken.None));

        var pause = new PauseController();
        string? told = null;
        pause.PauseRequested += reason => { told = reason; pause.Resume(); };
        DiskSpace.CheckBeforeStart(t.Path, tooMuch, pause, CancellationToken.None);
        Assert.Contains("Pas assez de place", told, StringComparison.Ordinal);

        DiskSpace.CheckBeforeStart(t.Path, 1, null, CancellationToken.None); // assez de place : rien
    }

    [Fact]
    public async Task A_copy_waits_on_a_full_disk_then_continues_with_the_same_file()
    {
        using var t = new TempDir();
        var first = t.File("src/a.txt", "AAAA");
        var second = t.File("src/b.txt", "BB");
        var dest = t.Dir("dest");
        var failing = second;
        int faults = 0;
        FileOperations.CopyFault = source =>
        {
            if (source != failing || faults++ > 0) return null;
            return WindowsFull;
        };
        try
        {
            var pause = new PauseController();
            string? told = null;
            bool partLeft = true;
            pause.PauseRequested += reason =>
            {
                told = reason;
                partLeft = File.Exists(Path.Combine(dest, "b.txt.twinpanel-part"));
                pause.Resume();
            };

            await FileOperations.CopyAsync([first, second], dest, ConflictPolicy.Overwrite, pause: pause);

            Assert.Contains("b.txt", told, StringComparison.Ordinal);
            Assert.False(partLeft);
            Assert.Equal("BB", File.ReadAllText(Path.Combine(dest, "b.txt")));
            Assert.Equal("AAAA", File.ReadAllText(Path.Combine(dest, "a.txt")));
        }
        finally { FileOperations.CopyFault = null; }
    }

    [Fact]
    public async Task Without_pause_a_full_disk_stops_with_a_clear_message()
    {
        using var t = new TempDir();
        var file = t.File("src/c.txt", "CC");
        var dest = t.Dir("dest");
        FileOperations.CopyFault = source => source == file ? WindowsFull : null;
        try
        {
            var error = await Assert.ThrowsAsync<FileOperationException>(() => FileOperations.CopyAsync([file], dest, ConflictPolicy.Overwrite));
            Assert.Contains("Plus de place", error.Message, StringComparison.Ordinal);
            Assert.True(DiskSpace.IsFull(error));
        }
        finally { FileOperations.CopyFault = null; }
    }

    [Theory]
    [InlineData(ConflictPolicy.Overwrite, 4)]    // le plus gros fichier remplacé doit tenir en entier à côté de l'ancien
    [InlineData(ConflictPolicy.Skip, 2)]
    [InlineData(ConflictPolicy.RenameNew, 6)]
    public void Needed_room_counts_what_is_already_there(ConflictPolicy policy, long expected)
    {
        using var t = new TempDir();
        var kept = t.File("src/a.txt", "AAAA");
        var fresh = t.File("src/b.txt", "BB");
        t.File("dest/a.txt", "ZZZZ");
        Assert.Equal(expected, FileOperations.NeededBytes([kept, fresh], Path.Combine(t.Path, "dest"), policy, move: false, CancellationToken.None));
    }

    [Fact]
    public void A_move_on_the_same_disk_needs_no_room()
    {
        using var t = new TempDir();
        var file = t.File("src/a.txt", "AAAA");
        Assert.Equal(0, FileOperations.NeededBytes([file], t.Dir("dest"), ConflictPolicy.Overwrite, move: true, CancellationToken.None));
    }

    [Fact]
    public void Server_tabs_go_back_between_the_local_ones()
    {
        // SSSSSLLSS, onglet actif : le sixième.
        var tabs = new List<string> { "L5", "L6" };
        int active = TabPositions.Merge(tabs, 0, [(0, "S0"), (1, "S1"), (2, "S2"), (3, "S3"), (4, "S4"), (7, "S7"), (8, "S8")]);

        Assert.Equal(["S0", "S1", "S2", "S3", "S4", "L5", "L6", "S7", "S8"], tabs);
        Assert.Equal(5, active);
    }

    [Fact]
    public void A_server_that_did_not_open_leaves_the_others_in_order()
    {
        var tabs = new List<string> { "L1" };
        TabPositions.Merge(tabs, 0, [(5, "S5"), (3, "S3")]);
        Assert.Equal(["L1", "S3", "S5"], tabs);
    }

    [Fact]
    public void A_tab_after_the_active_one_does_not_move_it()
    {
        var tabs = new List<string> { "L0", "L1" };
        Assert.Equal(1, TabPositions.Merge(tabs, 1, [(2, "S2")]));
    }
}
