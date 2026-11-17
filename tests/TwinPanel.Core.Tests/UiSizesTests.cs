// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Tests;

/// <summary>Taille de l'interface, taille des icônes des fichiers et fichiers cachés estompés (Options → Affichage).</summary>
public class UiSizesTests
{
    [Fact]
    public void Defaults_are_normal_size_and_dimmed_hidden_files()
    {
        var a = new AppearanceSettings();
        Assert.Equal(100, a.UiScale);
        Assert.Equal(16, a.FileIconSize);
        Assert.True(a.DimHiddenFiles);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(10, 60)]
    [InlineData(62, 60)]
    [InlineData(70, 65)]
    [InlineData(500, 200)]
    [InlineData(120, 115)]
    [InlineData(160, 150)]
    public void Scale_is_brought_back_to_an_allowed_choice(int saved, int expected) =>
        Assert.Equal(expected, UiSizes.Scale(saved));

    [Theory]
    [InlineData(16, 16)]
    [InlineData(0, 16)]
    [InlineData(23, 24)]
    [InlineData(100, 32)]
    public void Icon_size_is_brought_back_to_an_allowed_choice(double saved, double expected) =>
        Assert.Equal(expected, UiSizes.IconSize(saved));

    [Fact]
    public void Row_height_grows_with_the_icons_and_the_text()
    {
        Assert.Equal(28, UiSizes.RowHeight(28, 14, 16));   // réglage d'origine : rien ne change
        Assert.Equal(40, UiSizes.RowHeight(28, 14, 32));   // très grandes icônes : la ligne suit
        Assert.Equal(35, UiSizes.RowHeight(22, 20, 16));   // texte agrandi (Ctrl + molette)
        Assert.Equal(72, UiSizes.RowHeight(28, 80, 32));   // jamais plus de 72
    }

    [Fact]
    public void Settings_survive_a_save()
    {
        using var t = new TempDir();
        var store = new SettingsStore(Path.Combine(t.Path, "s.json"));
        var s = new AppSettings();
        s.Appearance.UiScale = 150;
        s.Appearance.FileIconSize = 24;
        s.Appearance.DimHiddenFiles = false;
        store.Save(s);
        var back = store.Load().Appearance;
        Assert.Equal(150, back.UiScale);
        Assert.Equal(24, back.FileIconSize);
        Assert.False(back.DimHiddenFiles);
    }

    [Fact]
    public void Hidden_and_system_items_look_hidden_but_not_the_parent_link()
    {
        static FileEntry E(string name, FileAttributes a, bool parent = false) =>
            new(name, "/x/" + name, false, 0, default, a, parent);
        Assert.True(E("cache.txt", FileAttributes.Hidden).LooksHidden);
        Assert.True(E("pagefile.sys", FileAttributes.System).LooksHidden);
        Assert.False(E("visible.txt", FileAttributes.Normal).LooksHidden);
        Assert.False(E("..", FileAttributes.Hidden, parent: true).LooksHidden);
    }

    [Fact]
    public void Dot_files_read_from_disk_look_hidden_on_linux()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var t = new TempDir();
        File.WriteAllText(Path.Combine(t.Path, ".bashrc"), "x");
        File.WriteAllText(Path.Combine(t.Path, "notes.txt"), "x");
        var list = DirectoryReader.Read(t.Path, showHidden: true);
        Assert.True(list.Single(e => e.Name == ".bashrc").LooksHidden);
        Assert.False(list.Single(e => e.Name == "notes.txt").LooksHidden);
    }
}
