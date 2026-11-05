// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Remote;
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Tests;

/// <summary>Onglets ouverts qui passent d'une machine à une autre, par une sauvegarde ou une synchronisation.</summary>
public class TabTransferTests
{
    private static readonly ConnectionProfile Nas = new() { Name = "nas", Host = "192.168.0.10", Port = 22, Protocol = RemoteProtocol.Sftp };

    private static string Remote(Guid id, string path, TabLock lockMode = TabLock.None, string name = "") =>
        new SavedTab(SavedRemoteTab.Encode(id, path), lockMode, name).Encode();

    private static TransferredTabs FromWindows() => TabTransfer.Capture(
        new PanelState { Tabs = [new SavedTab(@"D:\musique", TabLock.Locked, "Musique").Encode(), Remote(Nas.Id, "/srv", TabLock.Locked, "Serveur")], ActiveTab = 1 },
        new PanelState { Tabs = [@"C:\Users\utilisateur", @"\\nas\partage"] },
        id => id == Nas.Id ? Nas : null);

    [Fact]
    public void Server_tabs_keep_their_lock_and_name_and_carry_their_server()
    {
        var tabs = FromWindows();
        Assert.Equal(4, tabs.Count);
        var server = Assert.Single(tabs.Servers);
        Assert.Equal(("nas", "192.168.0.10", 22), (server.Name, server.Host, server.Port));

        var (set, skipped) = TabTransfer.ForThisMachine(tabs, [Nas], onWindows: true);
        Assert.Equal(0, skipped);
        var back = SavedTab.Decode(set.Left.Tabs[1]);
        Assert.Equal((TabLock.Locked, "Serveur"), (back.Lock, back.Name));
        Assert.Equal(1, set.Left.ActiveTab);
    }

    [Fact]
    public void On_linux_only_the_server_tabs_of_windows_remain()
    {
        var (set, skipped) = TabTransfer.ForThisMachine(FromWindows(), [Nas], onWindows: false);
        Assert.Equal(3, skipped);
        var only = SavedTab.Decode(Assert.Single(set.Left.Tabs));
        Assert.True(SavedRemoteTab.TryDecode(only.Path, out _, out var path));
        Assert.Equal("/srv", path);
        Assert.Equal(0, set.Left.ActiveTab); // l'onglet actif a suivi malgré l'onglet écarté devant lui
        Assert.Empty(set.Right.Tabs);
    }

    [Fact]
    public void Linux_folders_do_not_open_on_windows()
    {
        var tabs = TabTransfer.Capture(new PanelState { Tabs = ["/home/utilisateur", "/srv/films"] }, new PanelState(), _ => null);
        var (set, skipped) = TabTransfer.ForThisMachine(tabs, [], onWindows: true);
        Assert.Equal(2, skipped);
        Assert.Empty(set.Left.Tabs);
    }

    [Fact]
    public void A_server_with_another_id_here_is_found_by_its_address()
    {
        var here = new ConnectionProfile { Name = "nas", Host = "192.168.0.10", Port = 22, Protocol = RemoteProtocol.Sftp };
        var (set, skipped) = TabTransfer.ForThisMachine(FromWindows(), [here], onWindows: false);
        Assert.Equal(3, skipped);
        Assert.True(SavedRemoteTab.TryDecode(SavedTab.Decode(set.Left.Tabs[0]).Path, out var id, out _));
        Assert.Equal(here.Id, id);
    }

    [Fact]
    public void A_server_missing_here_is_skipped()
    {
        var other = new ConnectionProfile { Name = "autre", Host = "192.168.0.20" };
        var (set, skipped) = TabTransfer.ForThisMachine(FromWindows(), [other], onWindows: true);
        Assert.Equal(1, skipped);
        Assert.Equal(3, set.Left.Tabs.Count + set.Right.Tabs.Count);
    }

    [Fact]
    public void Two_servers_with_the_same_address_need_the_same_name()
    {
        var a = new ConnectionProfile { Name = "a", Host = "192.168.0.10" };
        var b = new ConnectionProfile { Name = "b", Host = "192.168.0.10" };
        var server = new TabServer(Guid.NewGuid(), "nas", "192.168.0.10", 22, RemoteProtocol.Sftp);
        Assert.Null(TabTransfer.Match(server, [a, b]));
        Assert.Same(a, TabTransfer.Match(server, [a]));
    }

    [Theory]
    [InlineData(@"D:\musique", true, true)]
    [InlineData(@"\\nas\partage", true, true)]
    [InlineData("/home/utilisateur", true, false)]
    [InlineData("/home/utilisateur", false, true)]
    [InlineData(@"D:\musique", false, false)]
    [InlineData("relatif", true, false)]
    public void A_path_fits_its_own_system(string path, bool onWindows, bool fits) =>
        Assert.Equal(fits, TabTransfer.FitsSystem(path, onWindows));

    [Fact]
    public void A_backup_carries_the_tabs_apart_and_an_old_backup_is_still_read()
    {
        using var t = new TempDir();
        var file = Path.Combine(t.Path, "sauvegarde.json");
        var s = new AppSettings { Left = new PanelState { Tabs = [@"D:\musique"] } };

        SettingsStore.ExportBundle(s, null, file, FromWindows());
        var back = SettingsStore.ReadBundle(file);
        Assert.Equal(4, back.OpenTabs!.Count);
        Assert.Empty(back.Settings!.Left.Tabs); // jamais deux fois : une vieille version les rouvrirait au démarrage

        SettingsStore.ExportBundle(s, null, file);
        Assert.Null(SettingsStore.ReadBundle(file).OpenTabs);

        // Sauvegarde de version 1 : les onglets étaient dans les réglages.
        File.WriteAllText(file, "{\"Format\":\"TwinPanel-reglages\",\"Version\":1,\"Settings\":{\"Left\":{\"Tabs\":[\"D:\\\\musique\"]}}}");
        Assert.Equal(1, SettingsStore.ReadBundle(file).OpenTabs!.Count);
    }

    [Fact]
    public void A_backup_without_settings_keeps_only_what_was_chosen()
    {
        using var t = new TempDir();
        var file = Path.Combine(t.Path, "sauvegarde.json");
        SettingsStore.ExportBundle(new AppSettings { Theme = "Dark" }, null, file, FromWindows(), withSettings: false);
        var back = SettingsStore.ReadBundle(file);
        Assert.False(back.HasSettings);
        Assert.NotEqual("Dark", back.Settings!.Theme);
        Assert.Equal(4, back.OpenTabs!.Count);
    }
}
