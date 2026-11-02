// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Navigation;
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Tests;

public class TabSetTests
{
    [Fact]
    public void Flat_view_is_kept_in_a_saved_tab()
    {
        var tab = new SavedTab(@"C:\a", TabLock.None, "", Flat: true);
        Assert.Equal(tab, SavedTab.Decode(tab.Encode()));
    }

    [Fact]
    public void Thumbnail_view_is_kept_in_a_saved_tab()
    {
        var only = new SavedTab(@"C:\a", TabLock.None, "", Thumbnails: true);
        Assert.Equal(only, SavedTab.Decode(only.Encode()));
        var both = new SavedTab(@"C:\a", TabLock.Locked, "Photos", Flat: true, Thumbnails: true);
        Assert.Equal(both, SavedTab.Decode(both.Encode()));
    }

    [Fact]
    public void Tab_saved_with_flat_view_before_thumbnails_is_still_read()
    {
        Assert.Equal(new SavedTab(@"C:\a", TabLock.None, "", Flat: true), SavedTab.Decode("C:\\a\u001F0\u001F\u001F1"));
    }

    [Fact]
    public void Older_saved_tabs_are_still_read()
    {
        Assert.Equal(new SavedTab(@"C:\a"), SavedTab.Decode(@"C:\a"));
        var locked = new SavedTab(@"C:\a", TabLock.Locked, "Travail");
        Assert.Equal(locked, SavedTab.Decode($"C:\\a\u001F1\u001FTravail"));
        Assert.Equal(@"C:\a", new SavedTab(@"C:\a").Encode()); // rien de plus pour un onglet ordinaire
    }

    [Fact]
    public void Tab_sets_survive_saving_without_any_host_or_secret()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(Path.Combine(dir.Path, "settings.json"));
        var id = Guid.NewGuid();
        var settings = new AppSettings();
        settings.TabSets.Add(new TabSet
        {
            Name = "Serveur",
            Left = new PanelState { Tabs = [new SavedTab(SavedRemoteTab.Encode(id, "/var/www"), Flat: true).Encode()], ActiveTab = 0 },
            Right = new PanelState { Tabs = [@"C:\a", @"C:\b"], ActiveTab = 1 },
        });
        store.Save(settings);

        var set = Assert.Single(store.Load().TabSets);
        Assert.Equal("Serveur", set.Name);
        Assert.Equal(1, set.Right.ActiveTab);
        var left = SavedTab.Decode(set.Left.Tabs[0]);
        Assert.True(left.Flat);
        Assert.True(SavedRemoteTab.TryDecode(left.Path, out var read, out var path));
        Assert.Equal(id, read);
        Assert.Equal("/var/www", path);
    }

    [Fact]
    public void Settings_copy_keeps_tab_sets()
    {
        var settings = new AppSettings();
        settings.TabSets.Add(new TabSet { Name = "A" });
        Assert.Equal("A", Assert.Single(SettingsStore.Clone(settings).TabSets).Name);
    }
}

public class RecentFoldersTests
{
    private static RecentFolders Make(TempDir dir, int capacity = RecentFolders.DefaultCapacity) =>
        new(Path.Combine(dir.Path, "sous-dossier", "history.json"), capacity);

    [Fact]
    public void Most_recent_comes_first_without_duplicates()
    {
        using var dir = new TempDir();
        var r = Make(dir);
        r.Visit(@"C:\a");
        r.Visit(@"C:\b");
        r.Visit(@"c:\A"); // même dossier sur le PC : remonte en tête
        Assert.Equal([@"c:\A", @"C:\b"], r.Items);
    }

    [Fact]
    public void Server_paths_are_case_sensitive()
    {
        using var dir = new TempDir();
        var r = Make(dir);
        var id = Guid.NewGuid();
        r.Visit(SavedRemoteTab.Encode(id, "/var/Www"));
        r.Visit(SavedRemoteTab.Encode(id, "/var/www"));
        Assert.Equal(2, r.Items.Count);
    }

    [Fact]
    public void Oldest_are_dropped_beyond_capacity()
    {
        using var dir = new TempDir();
        var r = Make(dir, capacity: 3);
        foreach (var p in new[] { "a", "b", "c", "d" }) r.Visit(p);
        Assert.Equal(["d", "c", "b"], r.Items);
    }

    [Fact]
    public void Lowering_the_capacity_forgets_the_oldest()
    {
        using var dir = new TempDir();
        var r = Make(dir);
        foreach (var p in new[] { "a", "b", "c", "d" }) r.Visit(p);
        r.Capacity = 2;
        Assert.Equal(["d", "c"], r.Items);
        r.Visit("e");
        Assert.Equal(["e", "d"], r.Items);
    }

    [Fact]
    public void Capacity_set_before_loading_keeps_a_long_history()
    {
        using var dir = new TempDir();
        var r = Make(dir, capacity: 50);
        foreach (var i in Enumerable.Range(0, 50)) r.Visit($"d{i}");
        r.Save();

        var again = Make(dir);
        again.Capacity = 50;
        again.Load();
        Assert.Equal(50, again.Items.Count);
    }

    [Fact]
    public void Save_then_load_keeps_the_order()
    {
        using var dir = new TempDir();
        var r = Make(dir);
        foreach (var p in new[] { "a", "b", "c" }) r.Visit(p);
        r.Save();

        var again = Make(dir);
        again.Load();
        Assert.Equal(["c", "b", "a"], again.Items);
    }

    [Fact]
    public void Missing_or_damaged_file_gives_an_empty_list()
    {
        using var dir = new TempDir();
        var r = Make(dir);
        r.Load();
        Assert.Empty(r.Items);

        Directory.CreateDirectory(Path.GetDirectoryName(r.FilePath)!);
        File.WriteAllText(r.FilePath, "{ ceci n'est pas une liste");
        r.Visit("a");
        r.Load();
        Assert.Empty(r.Items);
    }
}
