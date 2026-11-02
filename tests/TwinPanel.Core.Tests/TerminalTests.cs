// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;
using TwinPanel.Core.Customization;
using TwinPanel.Core.Remote;
using TwinPanel.Core.Settings;
using TwinPanel.Core.Terminal;

namespace TwinPanel.Core.Tests;

public class TerminalEmulatorTests
{
    private const string Esc = "\u001b";

    private static TerminalEmulator Screen(string text, int columns = 20, int rows = 5, int history = 100)
    {
        var e = new TerminalEmulator(columns, rows, history);
        e.Feed(Encoding.UTF8.GetBytes(text));
        return e;
    }

    private static TerminalCell Cell(TerminalEmulator e, int x, int y) => e.ScreenLine(y).Cells[x];

    [Fact]
    public void Text_and_new_lines()
    {
        var e = Screen("un\r\ndeux");
        Assert.Equal("un\ndeux", e.ScreenText());
        Assert.Equal((4, 1), (e.CursorX, e.CursorY));
    }

    [Fact]
    public void Line_wraps_at_the_right_edge_only_when_the_next_character_arrives()
    {
        var e = Screen("abcde", columns: 5, rows: 2);
        Assert.Equal((4, 0), (e.CursorX, e.CursorY));
        e.Feed("f"u8);
        Assert.Equal("abcde\nf", e.ScreenText());
        Assert.True(e.ScreenLine(0).Wrapped);
    }

    [Fact]
    public void Lines_leave_the_screen_into_the_history()
    {
        var e = Screen("1\r\n2\r\n3\r\n4", rows: 3);
        Assert.Equal("2\n3\n4", e.ScreenText());
        Assert.Equal(1, e.HistoryCount);
        Assert.Equal("1", e.LineAt(0).Cells[0].Text);
    }

    [Fact]
    public void History_has_a_limit()
    {
        var e = new TerminalEmulator(10, 2, historyLimit: 100);
        for (int i = 0; i < 1000; i++) e.Feed("x\r\n"u8);
        Assert.True(e.HistoryCount <= 100);
        Assert.True(e.HistoryDropped > 0);
    }

    [Fact]
    public void Text_cut_in_two_pieces_is_understood()
    {
        var bytes = Encoding.UTF8.GetBytes($"é{Esc}[31mR");
        var e = new TerminalEmulator();
        foreach (var b in bytes) e.Feed([b]);
        Assert.Equal("éR", e.ScreenText());
        Assert.Equal(1, Cell(e, 1, 0).Foreground);
    }

    [Fact]
    public void Broken_utf8_gives_a_replacement_character_and_no_crash()
    {
        var e = new TerminalEmulator();
        e.Feed([0xC3, (byte)'a', 0xFF, 0xE2, 0x82]);
        e.Feed("b"u8);
        Assert.Contains("a", e.ScreenText(), StringComparison.Ordinal);
        Assert.Contains("\uFFFD", e.ScreenText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Colors_16_256_and_exact()
    {
        var e = Screen($"{Esc}[31mA{Esc}[1;92mB{Esc}[38;5;200mC{Esc}[38;2;1;2;3mD{Esc}[48:2::4:5:6mE{Esc}[0mF");
        Assert.Equal(1, Cell(e, 0, 0).Foreground);
        Assert.Equal(10, Cell(e, 1, 0).Foreground);
        Assert.True(Cell(e, 1, 0).Style.HasFlag(CellStyle.Bold));
        Assert.Equal(200, Cell(e, 2, 0).Foreground);
        Assert.Equal(TerminalColor.FromRgb(1, 2, 3), Cell(e, 3, 0).Foreground);
        Assert.Equal(TerminalColor.FromRgb(4, 5, 6), Cell(e, 4, 0).Background);
        Assert.Equal(TerminalColor.Default, Cell(e, 5, 0).Foreground);
        Assert.Equal(CellStyle.None, Cell(e, 5, 0).Style);
    }

    [Fact]
    public void Palette_extended_colors()
    {
        Assert.Equal(((byte)0, (byte)0, (byte)0), TerminalColor.Extended(16));
        Assert.Equal(((byte)255, (byte)255, (byte)255), TerminalColor.Extended(231));
        Assert.Equal(((byte)8, (byte)8, (byte)8), TerminalColor.Extended(232));
    }

    [Fact]
    public void Contrast_tells_when_a_text_is_unreadable()
    {
        (byte, byte, byte) white = (255, 255, 255), black = (0, 0, 0), lightGray = (212, 215, 220);
        Assert.Equal(21, TerminalColor.Contrast(black, white), 3);
        Assert.Equal(1, TerminalColor.Contrast(white, white), 3);
        Assert.True(TerminalColor.Contrast(lightGray, white) < 3);
    }

    [Fact]
    public void Cursor_moves_and_stays_inside()
    {
        var e = Screen($"{Esc}[3;4H");
        Assert.Equal((3, 2), (e.CursorX, e.CursorY));
        e.Feed(Encoding.UTF8.GetBytes($"{Esc}[99A{Esc}[99D"));
        Assert.Equal((0, 0), (e.CursorX, e.CursorY));
        e.Feed(Encoding.UTF8.GetBytes($"{Esc}[99B{Esc}[99C"));
        Assert.Equal((19, 4), (e.CursorX, e.CursorY));
    }

    [Theory]
    [InlineData(0, "ab")]
    [InlineData(2, "")]
    public void Erase_line_and_screen(int mode, string expected)
    {
        var e = Screen($"abcd{Esc}[1;3H{Esc}[{mode}K");
        Assert.Equal(expected, e.ScreenText());
    }

    [Fact]
    public void Erase_uses_the_current_background()
    {
        var e = Screen($"{Esc}[44m{Esc}[2K");
        Assert.Equal(4, Cell(e, 10, 0).Background);
    }

    [Fact]
    public void Insert_and_delete_characters_and_lines()
    {
        var e = Screen($"abcd{Esc}[1;2H{Esc}[2@");
        Assert.Equal("a  bcd", e.ScreenText());
        e.Feed(Encoding.UTF8.GetBytes($"{Esc}[2P"));
        Assert.Equal("abcd", e.ScreenText());
        e = Screen($"1\r\n2\r\n3{Esc}[2;1H{Esc}[L");
        Assert.Equal("1\n\n2\n3", e.ScreenText());
        e.Feed(Encoding.UTF8.GetBytes($"{Esc}[M"));
        Assert.Equal("1\n2\n3", e.ScreenText());
    }

    [Fact]
    public void Scroll_region_keeps_the_lines_outside()
    {
        var e = Screen($"haut{Esc}[2;3r{Esc}[3;1Ha\r\nb\r\nc{Esc}[r", rows: 4);
        Assert.StartsWith("haut", e.ScreenText(), StringComparison.Ordinal);
        Assert.Equal(0, e.HistoryCount);
    }

    [Fact]
    public void Alternate_screen_comes_back_as_it_was_and_leaves_no_history()
    {
        var e = Screen($"avant{Esc}[?1049h");
        Assert.True(e.IsAlternateScreen);
        Assert.Equal(string.Empty, e.ScreenText());
        e.Feed(Encoding.UTF8.GetBytes($"vim\r\n\r\n\r\n\r\n\r\n\r\n{Esc}[?1049l"));
        Assert.False(e.IsAlternateScreen);
        Assert.Equal("avant", e.ScreenText());
        Assert.Equal((5, 0), (e.CursorX, e.CursorY));
        Assert.Equal(0, e.HistoryCount);
    }

    [Fact]
    public void Wide_characters_take_two_cells_and_accents_join_their_letter()
    {
        var e = Screen("日本a");
        Assert.Equal(5, e.CursorX);
        Assert.Equal(2, Cell(e, 0, 0).Width);
        Assert.Equal(0, Cell(e, 1, 0).Width);
        Assert.Equal("日本a", e.ScreenText());
        e = Screen("e\u0301x");
        Assert.Equal("e\u0301", Cell(e, 0, 0).Text);
        Assert.Equal(2, e.CursorX);
    }

    [Fact]
    public void Writing_over_half_a_wide_character_clears_the_other_half()
    {
        var e = Screen($"日{Esc}[1;2Hx");
        Assert.Null(Cell(e, 0, 0).Text);
        Assert.Equal("x", Cell(e, 1, 0).Text);
    }

    [Fact]
    public void Box_drawing_charset()
    {
        var e = Screen($"{Esc}(0lqk{Esc}(Bq");
        Assert.Equal("┌─┐q", e.ScreenText());
    }

    [Fact]
    public void Answers_the_questions_of_the_program()
    {
        var e = new TerminalEmulator(20, 5);
        var replies = new List<string>();
        e.Reply += replies.Add;
        e.Feed(Encoding.UTF8.GetBytes($"{Esc}[2;3H{Esc}[6n{Esc}[c{Esc}[5n"));
        Assert.Equal([$"{Esc}[2;3R", $"{Esc}[?1;2c", $"{Esc}[0n"], replies);
    }

    [Fact]
    public void Modes_asked_by_programs()
    {
        var e = Screen($"{Esc}[?1h{Esc}[?2004h{Esc}[?25l{Esc}[?1002h{Esc}[?1006h{Esc}[5 q");
        Assert.True(e.ApplicationCursorKeys);
        Assert.True(e.BracketedPaste);
        Assert.False(e.CursorVisible);
        Assert.Equal(MouseTracking.Drag, e.Mouse);
        Assert.True(e.MouseSgr);
        Assert.Equal(2, e.CursorShape);
        e.Feed(Encoding.UTF8.GetBytes($"{Esc}c"));
        Assert.False(e.BracketedPaste);
        Assert.Equal(MouseTracking.None, e.Mouse);
    }

    [Theory]
    [InlineData("\u0007")]
    [InlineData("\u001b\\")]
    public void Title_with_accents(string end)
    {
        var e = new TerminalEmulator();
        e.Feed(Encoding.UTF8.GetBytes($"{Esc}]0;été{end}suite"));
        Assert.Equal("été", e.Title);
        Assert.Equal("suite", e.ScreenText());
    }

    [Fact]
    public void Clipboard_sequences_and_unknown_commands_show_nothing()
    {
        var e = Screen($"{Esc}]52;c;bWFs{(char)7}{Esc}P1$tx{Esc}\\{Esc}[?9999h{Esc}[999zok");
        Assert.Equal("ok", e.ScreenText());
    }

    [Fact]
    public void A_huge_sequence_does_not_grow_without_limit()
    {
        var e = new TerminalEmulator();
        e.Feed(Encoding.UTF8.GetBytes(Esc + "[" + new string('1', 100_000) + "mok"));
        Assert.Contains("ok", e.ScreenText(), StringComparison.Ordinal);
        e.Feed(Encoding.UTF8.GetBytes(Esc + "]0;" + new string('x', 100_000) + "\u0007"));
        Assert.True(e.Title.Length <= 4096);
    }

    [Fact]
    public void Random_stream_never_breaks_the_screen()
    {
        var random = new Random(1234);
        var e = new TerminalEmulator(30, 10, 50);
        var bytes = new byte[256];
        for (int i = 0; i < 5000; i++)
        {
            random.NextBytes(bytes);
            // Beaucoup d'ESC et de CSI, pour tomber sur des suites valides.
            for (int j = 0; j < bytes.Length; j += 7) bytes[j] = (byte)(j % 3 == 0 ? 0x1B : '[');
            e.Feed(bytes);
            if (i % 500 == 0) e.Resize(random.Next(2, 60), random.Next(1, 30));
            Assert.InRange(e.CursorX, 0, e.Columns - 1);
            Assert.InRange(e.CursorY, 0, e.Rows - 1);
        }
    }

    [Fact]
    public void Resize_keeps_the_cursor_line_and_the_text()
    {
        var e = Screen("1\r\n2\r\n3\r\n4\r\n5", columns: 10, rows: 5);
        e.Resize(10, 3);
        Assert.Equal("3\n4\n5", e.ScreenText());
        Assert.Equal(2, e.CursorY);
        Assert.Equal(2, e.HistoryCount);
        e.Resize(10, 5);
        Assert.Equal("1\n2\n3\n4\n5", e.ScreenText());
        Assert.Equal(4, e.CursorY);

        e = Screen("abcdefgh", columns: 10, rows: 2);
        e.Resize(4, 2);
        e.Resize(10, 2);
        Assert.Equal("abcdefgh", e.ScreenText());
    }

    [Fact]
    public void Selection_joins_wrapped_lines()
    {
        var e = Screen("abcdef\r\nsuite", columns: 4, rows: 4);
        var nl = Environment.NewLine;
        Assert.Equal($"abcdef{nl}su", e.TextBetween(0, 0, 2, 2));
        Assert.Equal("cd", e.TextBetween(0, 2, 0, 4));
    }
}

public class TerminalKeysTests
{
    [Theory]
    [InlineData(TerminalKey.Up, TerminalModifiers.None, false, "\u001b[A")]
    [InlineData(TerminalKey.Up, TerminalModifiers.None, true, "\u001bOA")]
    [InlineData(TerminalKey.Left, TerminalModifiers.Control, false, "\u001b[1;5D")]
    [InlineData(TerminalKey.Home, TerminalModifiers.Shift, false, "\u001b[1;2H")]
    [InlineData(TerminalKey.Delete, TerminalModifiers.None, false, "\u001b[3~")]
    [InlineData(TerminalKey.F1, TerminalModifiers.None, false, "\u001bOP")]
    [InlineData(TerminalKey.F5, TerminalModifiers.None, false, "\u001b[15~")]
    [InlineData(TerminalKey.F12, TerminalModifiers.Control, false, "\u001b[24;5~")]
    [InlineData(TerminalKey.Enter, TerminalModifiers.None, false, "\r")]
    [InlineData(TerminalKey.Tab, TerminalModifiers.Shift, false, "\u001b[Z")]
    [InlineData(TerminalKey.Backspace, TerminalModifiers.None, false, "\u007f")]
    public void Special_keys(TerminalKey key, TerminalModifiers modifiers, bool application, string expected) =>
        Assert.Equal(expected, TerminalKeys.Encode(key, modifiers, application));

    [Theory]
    [InlineData('c', "\u0003")]
    [InlineData('Z', "\u001a")]
    [InlineData(' ', "\0")]
    [InlineData('[', "\u001b")]
    [InlineData('é', null)]
    public void Control_characters(char c, string? expected) => Assert.Equal(expected, TerminalKeys.ControlCharacter(c));

    [Fact]
    public void Paste_cannot_escape_from_the_brackets()
    {
        Assert.Equal("a\rb", TerminalKeys.Paste("a\r\nb\n", false));
        Assert.Equal("\u001b[200~a[201~b\u001b[201~", TerminalKeys.Paste("a\u001b[201~b", true));
    }

    [Fact]
    public void A_nested_end_mark_cannot_rebuild_itself()
    {
        var sent = TerminalKeys.Paste("sage\u001b[20\u001b[201~1~\rpiege\r", true);
        Assert.Equal("\u001b[200~sage[20[201~1~\rpiege\r\u001b[201~", sent);
        Assert.Equal(sent.Length - 6, sent.LastIndexOf("\u001b[201~", StringComparison.Ordinal));
        Assert.Equal(1, sent.Split("\u001b[201~").Length - 1);
    }

    [Theory]
    [InlineData("ls\n", "ls")]
    [InlineData("a\u000fb", "ab")]
    [InlineData("a\u0015b", "ab")]
    [InlineData("a\u007fb", "ab")]
    [InlineData("a\u009b1~b", "a1~b")]
    [InlineData("a\tb", "a\tb")]
    public void Pasted_text_cannot_act_on_the_line(string text, string expected) => Assert.Equal(expected, TerminalKeys.Paste(text, false));

    [Theory]
    [InlineData("ls", false)]
    [InlineData("ls\r\n", false)]
    [InlineData("ls\nrm x", true)]
    public void Multiline_paste_is_detected(string text, bool expected) => Assert.Equal(expected, TerminalKeys.IsMultiline(text));

    [Fact]
    public void Mouse_reports()
    {
        const MouseTracking press = MouseTracking.Press;
        Assert.Equal("\u001b[<0;3;2M", TerminalKeys.Mouse(TerminalMouseButton.Left, TerminalMouseAction.Press, 2, 1, TerminalModifiers.None, press, true));
        Assert.Equal("\u001b[<0;3;2m", TerminalKeys.Mouse(TerminalMouseButton.Left, TerminalMouseAction.Release, 2, 1, TerminalModifiers.None, press, true));
        Assert.Equal("\u001b[<64;1;1M", TerminalKeys.Mouse(TerminalMouseButton.WheelUp, TerminalMouseAction.Press, 0, 0, TerminalModifiers.None, press, true));
        Assert.Equal("\u001b[M !\"", TerminalKeys.Mouse(TerminalMouseButton.Left, TerminalMouseAction.Press, 0, 1, TerminalModifiers.None, press, false));
        Assert.Null(TerminalKeys.Mouse(TerminalMouseButton.Left, TerminalMouseAction.Press, 0, 0, TerminalModifiers.None, MouseTracking.None, true));
        Assert.Null(TerminalKeys.Mouse(TerminalMouseButton.None, TerminalMouseAction.Move, 0, 0, TerminalModifiers.None, press, true));
        Assert.Null(TerminalKeys.Mouse(TerminalMouseButton.Left, TerminalMouseAction.Press, 300, 0, TerminalModifiers.None, press, false));
    }
}

public class TerminalCommandsTests
{
    [Fact]
    public void Common_commands_appear_everywhere_and_server_commands_only_there()
    {
        Guid server = Guid.NewGuid(), other = Guid.NewGuid();
        var all = new[]
        {
            new SavedTerminalCommand { Name = "commune" },
            new SavedTerminalCommand { Name = "ici", ServerId = server },
            new SavedTerminalCommand { Name = "ailleurs", ServerId = other },
        };
        Assert.Equal(["commune"], TerminalCommands.Visible(all, null).Select(c => c.Name));
        Assert.Equal(["commune", "ici"], TerminalCommands.Visible(all, server).Select(c => c.Name));
    }

    [Theory]
    [InlineData("ls -la", false, "ls -la\r")]
    [InlineData("ls -la\r\n", false, "ls -la\r")]
    [InlineData("cd /\nls", false, "cd /\rls\r")]
    [InlineData("ls", true, "\u001b[200~ls\u001b[201~\r")]
    public void A_command_is_sent_as_written_then_started(string command, bool bracketed, string expected) =>
        Assert.Equal(expected, TerminalCommands.Input(command, bracketed));

    [Fact]
    public void Saved_commands_survive_the_settings_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "twinpanel-" + Guid.NewGuid() + ".json");
        try
        {
            var id = Guid.NewGuid();
            var store = new SettingsStore(path);
            var settings = new AppSettings();
            settings.Terminal.SavedCommands.Add(new SavedTerminalCommand { Name = "n", Command = "c", AskFirst = true, ServerId = id, ServerName = "s" });
            settings.Terminal.CommandsPanelOpen = true;
            store.Save(settings);
            var loaded = new SettingsStore(path).Load().Terminal;
            var command = Assert.Single(loaded.SavedCommands);
            Assert.Equal(("n", "c", true, id, "s"), (command.Name, command.Command, command.AskFirst, command.ServerId!.Value, command.ServerName));
            Assert.True(loaded.CommandsPanelOpen);
        }
        finally { File.Delete(path); }
    }
}

public class TerminalBacklogTests
{
    /// <summary>Connexion simulée : un énorme texte déjà arrivé et en attente de lecture.</summary>
    private static (List<byte> Delivered, long Left) Run(long size, long limit, long tail)
    {
        long left = size;
        var delivered = new List<byte>();
        TerminalBacklog.Pump(
            (buffer, offset, count) =>
            {
                int n = (int)Math.Min(count, left);
                for (int i = 0; i < n; i++) buffer[offset + i] = (byte)'\n';
                left -= n;
                return n;
            },
            () => left,
            (data, length) => delivered.AddRange(data.AsSpan(0, length)),
            limit, tail);
        return (delivered, left);
    }

    [Fact]
    public void Everything_is_delivered_while_the_screen_follows()
    {
        var (delivered, left) = Run(100_000, limit: 1_000_000, tail: 10_000);
        Assert.Equal(100_000, delivered.Count);
        Assert.Equal(0, left);
    }

    [Fact]
    public void A_huge_backlog_is_skipped_and_its_end_is_kept()
    {
        var (delivered, left) = Run(500_000_000, limit: 4_000_000, tail: 100_000);
        Assert.Equal(0, left);
        Assert.InRange(delivered.Count, 100, 300_000);
        Assert.Contains((byte)0x18, delivered);
    }

    [Fact]
    public void Lines_are_reused_without_old_text_coming_back()
    {
        var e = new TerminalEmulator(10, 2, historyLimit: 20);
        for (int i = 0; i < 200; i++) e.Feed("abcdefghij\r\n"u8);
        e.Feed("\u001b[2Jx"u8);
        e.Feed("\r\n\r\n\r\n"u8);
        for (int i = 0; i < e.Rows; i++) Assert.DoesNotContain("abc", new string(e.ScreenLine(i).Cells.Select(c => (c.Text ?? " ")[0]).ToArray()), StringComparison.Ordinal);
    }
}

public class TerminalSessionTests
{
    private sealed class FakeConnection : ITerminalConnection
    {
        public event Action<byte[], int>? DataReceived;
        public event Action<string?>? Closed;
        public List<byte[]> Written { get; } = [];
        public (int, int)? Size { get; private set; }
        public bool Disposed { get; private set; }
        public void Start() { }
        public void Write(ReadOnlySpan<byte> data) => Written.Add(data.ToArray());
        public void Resize(int columns, int rows) => Size = (columns, rows);
        public void Dispose() => Disposed = true;
        public void Receive(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            DataReceived?.Invoke(bytes, bytes.Length);
        }
        public void Close(string? reason) => Closed?.Invoke(reason);
    }

    [Fact]
    public void Data_reaches_the_screen_and_answers_go_back()
    {
        var c = new FakeConnection();
        using var s = new TerminalSession(c, 20, 5);
        int changes = 0;
        s.Changed += () => changes++;
        c.Receive("salut\u001b[6n");
        Assert.Equal("salut", s.Screen.ScreenText());
        Assert.Equal(1, changes);
        Assert.Equal("\u001b[1;6R", Encoding.UTF8.GetString(Assert.Single(c.Written)));
    }

    [Fact]
    public void Resize_is_passed_on_once()
    {
        var c = new FakeConnection();
        using var s = new TerminalSession(c, 20, 5);
        s.Resize(20, 5);
        Assert.Null(c.Size);
        s.Resize(30, 6);
        Assert.Equal((30, 6), c.Size);
    }

    [Fact]
    public void Nothing_is_sent_after_the_end_and_the_end_is_told_once()
    {
        var c = new FakeConnection();
        using var s = new TerminalSession(c, 20, 5);
        var reasons = new List<string?>();
        s.Closed += reasons.Add;
        c.Close("coupé");
        c.Close(null);
        s.Send("x");
        Assert.Equal(["coupé"], reasons);
        Assert.Empty(c.Written);
    }

    [Fact]
    public void Messages_of_TwinPanel_do_not_go_to_the_program()
    {
        var c = new FakeConnection();
        using var s = new TerminalSession(c, 30, 5);
        s.ShowMessage("Terminal fermé");
        Assert.Contains("Terminal fermé", s.Screen.ScreenText(), StringComparison.Ordinal);
        Assert.Empty(c.Written);
    }

    [Fact]
    public void Disposing_closes_the_connection()
    {
        var c = new FakeConnection();
        new TerminalSession(c, 20, 5).Dispose();
        Assert.True(c.Disposed);
    }
}

public class TerminalShellsTests
{
    private static string System32 => Environment.GetFolderPath(Environment.SpecialFolder.System);
    private static string Cmd => Path.Combine(System32, "cmd.exe");
    private static string Ps => Path.Combine(System32, "WindowsPowerShell", "v1.0", "powershell.exe");

    [Fact]
    public void Only_installed_programs_are_offered()
    {
        var offered = TerminalShells.Installed(f => f == Cmd);
        Assert.Equal([TerminalShell.Cmd, TerminalShell.Custom], offered);
    }

    [Fact]
    public void A_program_that_disappeared_falls_back_on_one_that_works()
    {
        var settings = new TerminalSettings { IntegratedShell = TerminalShell.Wsl };
        Assert.Contains("powershell.exe -NoLogo", TerminalShells.CommandLine(settings, f => f == Ps), StringComparison.Ordinal);
        Assert.Contains("cmd.exe", TerminalShells.CommandLine(settings, f => f == Cmd), StringComparison.Ordinal);
        settings = new TerminalSettings { IntegratedShell = TerminalShell.Custom, IntegratedShellPath = @"C:\absent.exe" };
        Assert.Contains("cmd.exe", TerminalShells.CommandLine(settings, f => f == Cmd), StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_program_with_spaces_is_quoted()
    {
        const string file = @"C:\Mes Outils\shell.exe";
        var settings = new TerminalSettings { IntegratedShell = TerminalShell.Custom, IntegratedShellPath = $"\"{file}\"" };
        Assert.Equal($"\"{file}\"", TerminalShells.CommandLine(settings, f => f == file));
    }

    [Fact]
    public void Custom_external_terminal_keeps_a_folder_with_spaces_as_one_argument()
    {
        var settings = new TerminalSettings { Kind = TerminalKind.Custom, CustomPath = "t.exe", CustomArguments = "--dir %P --fin" };
        var (_, args) = TerminalCommand.Build(settings, new(@"D:\mon dossier", null, null, 0), _ => false);
        Assert.Equal(["--dir", @"D:\mon dossier", "--fin"], args);
    }

    [Theory]
    [InlineData("/var/www", "cd -- '/var/www' && clear\n")]
    [InlineData("/l'été", "cd -- '/l'\\''été' && clear\n")]
    public void Remote_folder_is_quoted(string folder, string expected) =>
        Assert.Equal(expected, SshTerminalConnection.ChangeDirectoryCommand(folder));

    [Theory]
    [InlineData("/srv/mon dossier/l'été", true)]
    [InlineData("/srv/x\u0015curl site|sh\r", false)]
    [InlineData("/srv/x\u0003y", false)]
    [InlineData("/srv/x\ny", false)]
    [InlineData("/srv/x\u001by", false)]
    [InlineData("/srv/x\u007fy", false)]
    [InlineData("/srv/x\u009by", false)]
    public void Only_a_clean_path_is_typed_in_the_shell(string folder, bool expected) => Assert.Equal(expected, SshTerminalConnection.CanTypePath(folder));
}

public class ConPtyTests
{
    [Fact]
    public void A_real_program_runs_in_the_terminal()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var done = new ManualResetEventSlim();
        using var session = new TerminalSession(new ConPtyConnection("cmd.exe /c echo bonjour-twinpanel", null, 80, 25), 80, 25);
        session.Closed += _ => done.Set();
        session.Start();
        Assert.True(done.Wait(TimeSpan.FromSeconds(20)), "Le programme ne s'est pas terminé.");
        lock (session.Gate) Assert.Contains("bonjour-twinpanel", session.Screen.ScreenText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Keys_reach_a_real_shell_and_the_size_is_real()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var done = new ManualResetEventSlim();
        using var session = new TerminalSession(new ConPtyConnection("cmd.exe /q", null, 80, 25), 80, 25);
        session.Closed += _ => done.Set();
        session.Start();
        session.Send("echo saisie-%COMPUTERNAME:~0,0%ok\r");
        session.Resize(100, 30);
        session.Send("mode con\r");
        session.Send("exit\r");
        Assert.True(done.Wait(TimeSpan.FromSeconds(20)), "Le programme ne s'est pas terminé.");
        string all;
        lock (session.Gate)
            all = session.Screen.TextBetween(0, 0, session.Screen.HistoryCount + session.Screen.Rows, 200);
        Assert.Contains("saisie-ok", all, StringComparison.Ordinal);
        Assert.Contains("100", all, StringComparison.Ordinal);
    }
}
