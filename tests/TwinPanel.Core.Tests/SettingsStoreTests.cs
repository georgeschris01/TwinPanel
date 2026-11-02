// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Navigation;
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Missing_file_gives_defaults()
    {
        using var t = new TempDir();
        var s = new SettingsStore(Path.Combine(t.Path, "none.json")).Load();
        Assert.Equal("System", s.Theme);
        Assert.Empty(s.Favorites);
    }

    [Fact]
    public void Round_trip()
    {
        using var t = new TempDir();
        var store = new SettingsStore(Path.Combine(t.Path, "sub", "settings.json"));
        var s = new AppSettings
        {
            Theme = "Dark",
            ShowHidden = true,
            Left = new PanelState { Tabs = ["C:\\", "D:\\"], ActiveTab = 1, SortColumn = SortColumn.Size, SortDescending = true },
        };
        s.Favorites.Add(new FavoriteFolder { Name = "Projet", Path = "D:\\Projets" });

        store.Save(s);
        var loaded = store.Load();

        Assert.Equal("Dark", loaded.Theme);
        Assert.True(loaded.ShowHidden);
        Assert.Equal(["C:\\", "D:\\"], loaded.Left.Tabs);
        Assert.Equal(1, loaded.Left.ActiveTab);
        Assert.Equal(SortColumn.Size, loaded.Left.SortColumn);
        Assert.Equal("Projet", Assert.Single(loaded.Favorites).Name);
    }

    [Fact]
    public void Corrupted_file_gives_defaults_and_keeps_a_backup()
    {
        using var t = new TempDir();
        var path = t.File("settings.json", "{ ceci n'est pas du json");
        var loaded = new SettingsStore(path).Load();

        Assert.Equal("System", loaded.Theme);
        Assert.True(File.Exists(path + ".corrompu"));
    }
}
