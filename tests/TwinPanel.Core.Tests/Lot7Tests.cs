// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Customization;
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Tests;

public class CommandArgumentsTests
{
    private static readonly CommandContext Ctx = new(@"C:\Photos", "vacances.jpg", @"C:\Photos\vacances.jpg", @"D:\Tri",
        [@"C:\Photos\a b.jpg", @"C:\Photos\c.jpg"]);

    [Theory]
    [InlineData("\"%F\"", "\"C:\\Photos\\vacances.jpg\"")]
    [InlineData("%p|%n|%t", "C:\\Photos|vacances.jpg|D:\\Tri")]
    [InlineData("100%%", "100%")]
    [InlineData("%X reste", "%X reste")]
    [InlineData("fin %", "fin %")]
    public void Placeholders(string template, string expected) => Assert.Equal(expected, CommandArguments.Expand(template, Ctx));

    [Fact]
    public void Marked_items_are_each_quoted_or_current_item_if_none()
    {
        Assert.Equal("\"C:\\Photos\\a b.jpg\" \"C:\\Photos\\c.jpg\"", CommandArguments.Expand("%S", Ctx));
        Assert.Equal("\"C:\\Photos\\vacances.jpg\"", CommandArguments.Expand("%S", Ctx with { Marked = [] }));
        Assert.Equal(string.Empty, CommandArguments.Expand("%S", Ctx with { Marked = [], CurrentPath = null }));
    }

    [Fact]
    public void Start_info_uses_current_folder_when_start_in_is_empty()
    {
        using var t = new TempDir();
        var ctx = Ctx with { CurrentFolder = t.Path };
        var psi = CommandArguments.BuildStartInfo(new CustomCommand { Target = "notepad.exe", Arguments = "\"%F\"" }, ctx);
        Assert.Equal("notepad.exe", psi.FileName);
        Assert.Equal(t.Path, psi.WorkingDirectory);
        Assert.Equal("\"C:\\Photos\\vacances.jpg\"", psi.Arguments);
        Assert.True(psi.UseShellExecute);
    }
}

public class ShortcutTests
{
    [Theory]
    [InlineData("shift+ctrl+o", "Ctrl+Shift+O")]
    [InlineData("Ctrl+F5", "Ctrl+F5")]
    [InlineData("alt + f7", "Alt+F7")]
    [InlineData("maj+F4", "Shift+F4")]
    [InlineData("", "")]
    [InlineData("Ctrl+", "")]
    public void Normalize(string input, string expected) => Assert.Equal(expected, Shortcuts.Normalize(input));

    [Fact]
    public void Conflicts_are_found_whatever_the_writing()
    {
        var map = new Dictionary<string, string> { ["Copy"] = "F5", ["Pack"] = "f5", ["View"] = "F3", ["None"] = "" };
        var conflicts = Shortcuts.FindConflicts(map);
        var c = Assert.Single(conflicts);
        Assert.Equal(new[] { "Copy", "Pack" }, c.Value);
    }
}

public class AppearanceTests
{
    [Fact]
    public void Mask_rules_win_over_categories()
    {
        var a = new AppearanceSettings
        {
            ColorRules = [new ColorRule { Mask = "*.log;*.tmp", Color = "#888888" }, new ColorRule { Mask = "*.x", Color = "pas une couleur" }],
            CategoryColors = { [FileCategory.Image] = "#FF0000", [FileCategory.Video] = "rouge" },
        };
        var rules = new FileColorRules(a);
        var log = new FileEntry("serveur.log", @"C:\serveur.log", false, 1, DateTime.Now, FileAttributes.Normal);
        var x = new FileEntry("a.x", @"C:\a.x", false, 1, DateTime.Now, FileAttributes.Normal);
        Assert.Equal("#888888", rules.RuleColor(log));
        Assert.Null(rules.RuleColor(x));
        Assert.Equal("#FF0000", rules.CategoryColor(FileCategory.Image));
        Assert.Null(rules.CategoryColor(FileCategory.Video));
    }

    [Fact]
    public void Editor_rules_match_extensions()
    {
        var rule = new EditorRule { Extensions = "py; *.JS ;.ts" };
        Assert.True(rule.Matches("script.py"));
        Assert.True(rule.Matches("app.js"));
        Assert.True(rule.Matches("x.TS"));
        Assert.False(rule.Matches("notes.txt"));
        Assert.False(rule.Matches("Makefile"));
    }
}

public class TerminalTests
{
    private static bool None(string _) => false;

    [Fact]
    public void Cmd_and_powershell_open_in_folder()
    {
        var (exe, args) = TerminalCommand.Build(new TerminalSettings { Kind = TerminalKind.Cmd }, new(@"D:\clone total commander", null, null, 0), None);
        Assert.Equal("cmd.exe", exe);
        Assert.Equal(new[] { "/K", "cd", "/d", @"D:\clone total commander" }, args);

        var (ps, psArgs) = TerminalCommand.Build(new TerminalSettings { Kind = TerminalKind.PowerShell }, new(@"C:\l'été", null, null, 0), None);
        Assert.Equal("powershell.exe", ps);
        Assert.Equal("Set-Location -LiteralPath 'C:\\l''été'", psArgs[^1]);
    }

    [Fact]
    public void Ssh_target_for_remote_panel()
    {
        var (exe, args) = TerminalCommand.Build(new TerminalSettings { Kind = TerminalKind.WindowsTerminal }, new(@"C:\Users\x", "root", "192.168.0.10", 2222), None);
        Assert.Equal("wt.exe", exe);
        Assert.Equal(new[] { "-d", @"C:\Users\x", "ssh", "-p", "2222", "root@192.168.0.10" }, args);
        var (_, cmdArgs) = TerminalCommand.Build(new TerminalSettings { Kind = TerminalKind.Cmd }, new(null, "chris", "nas", 22), None);
        Assert.Equal(new[] { "/K", "ssh", "chris@nas" }, cmdArgs);
    }
}

public class SettingsBundleTests
{
    [Fact]
    public void Export_then_read_back_with_and_without_connections()
    {
        using var t = new TempDir();
        var s = new AppSettings { Theme = "Dark" };
        s.Shortcuts["Copy"] = "Ctrl+F5";
        s.ButtonBar.Add(new CustomCommand { Label = "Bloc-notes", Target = "notepad.exe", Arguments = "\"%F\"" });
        s.Appearance.CategoryColors[FileCategory.Image] = "#00FF00";
        var file = Path.Combine(t.Path, "sauvegarde.json");

        SettingsStore.ExportBundle(s, "{\"Profiles\":[]}", file);
        var back = SettingsStore.ReadBundle(file);
        Assert.Equal("Dark", back.Settings!.Theme);
        Assert.Equal("Ctrl+F5", back.Settings.Shortcuts["Copy"]);
        Assert.Equal("notepad.exe", Assert.Single(back.Settings.ButtonBar).Target);
        Assert.Equal("#00FF00", back.Settings.Appearance.CategoryColors[FileCategory.Image]);
        Assert.NotNull(back.Connections);

        SettingsStore.ExportBundle(s, null, file);
        Assert.Null(SettingsStore.ReadBundle(file).Connections);
    }

    [Fact]
    public void Other_files_are_refused()
    {
        using var t = new TempDir();
        Assert.Throws<InvalidDataException>(() => SettingsStore.ReadBundle(t.File("x.json", "{\"Theme\":\"Dark\"}")));
        Assert.Throws<InvalidDataException>(() => SettingsStore.ReadBundle(t.File("y.json", "pas du json")));
    }

    [Fact]
    public void Clone_is_independent()
    {
        var s = new AppSettings();
        s.ButtonBar.Add(new CustomCommand { Label = "A" });
        var c = SettingsStore.Clone(s);
        c.ButtonBar[0].Label = "B";
        c.Shortcuts["X"] = "F1";
        Assert.Equal("A", s.ButtonBar[0].Label);
        Assert.Empty(s.Shortcuts);
    }

    [Fact]
    public void Old_settings_files_still_load_with_defaults()
    {
        using var t = new TempDir();
        var path = t.File("settings.json", "{\"Theme\":\"Light\",\"ShowHidden\":true}");
        var s = new SettingsStore(path).Load();
        Assert.Equal("Light", s.Theme);
        Assert.True(s.Behavior.ConfirmDelete);
        Assert.True(s.Startup.RestoreTabs);
        Assert.Equal(28, s.Appearance.RowHeight);
    }
}

/// <summary>Ces tests changent des réglages globaux d'affichage : ils tournent seuls, après les autres.</summary>
[CollectionDefinition("Formats globaux", DisableParallelization = true)]
public class GlobalFormatsCollection;

[Collection("Formats globaux")]
public class DisplayFormatTests
{
    [Fact]
    public void Size_and_date_formats_follow_settings()
    {
        try
        {
            FileEntry.SizeDisplay = SizeDisplay.Kilobytes;
            Assert.Equal((2).ToString("N0") + " Ko", FileEntry.FormatListSize(1025));
            FileEntry.DateFormat = "yyyy-MM-dd";
            Assert.Equal("2024-03-05", FileEntry.FormatDate(new DateTime(2024, 3, 5, 10, 0, 0)));
            FileEntry.DateFormat = "%"; // format invalide : repli sur le format par défaut
            // « / » = séparateur de dates de Windows (« - » sur certains PC) : on compare au format par défaut.
            var d = new DateTime(2024, 3, 5, 10, 0, 0);
            Assert.Equal(d.ToString("dd/MM/yyyy HH:mm"), FileEntry.FormatDate(d));
        }
        finally
        {
            FileEntry.SizeDisplay = SizeDisplay.Auto;
            FileEntry.DateFormat = "dd/MM/yyyy HH:mm";
        }
    }

}

public class SavedRemoteTabTests
{
    [Fact]
    public void Encode_decode_round_trip()
    {
        var id = Guid.NewGuid();
        var text = SavedRemoteTab.Encode(id, "/home/utilisateur/mes docs");
        Assert.True(SavedRemoteTab.TryDecode(text, out var back, out var path));
        Assert.Equal(id, back);
        Assert.Equal("/home/utilisateur/mes docs", path);
        Assert.False(SavedRemoteTab.TryDecode(@"C:\Users", out _, out _));
        Assert.False(SavedRemoteTab.TryDecode("remote:pas-un-guid|/x", out _, out _));
    }
}

public class SavedTabTests
{
    [Fact]
    public void Plain_path_stays_plain_and_old_settings_still_read()
    {
        var t = new TwinPanel.Core.Settings.SavedTab(@"D:\Photos");
        Assert.Equal(@"D:\Photos", t.Encode());
        Assert.Equal(t, TwinPanel.Core.Settings.SavedTab.Decode(@"D:\Photos"));
    }

    [Fact]
    public void Lock_and_name_round_trip()
    {
        var t = new TwinPanel.Core.Settings.SavedTab("remote:abc|/var/www", TwinPanel.Core.Settings.TabLock.LockedAllowChange, "Site web");
        var back = TwinPanel.Core.Settings.SavedTab.Decode(t.Encode());
        Assert.Equal(t, back);
    }
}
