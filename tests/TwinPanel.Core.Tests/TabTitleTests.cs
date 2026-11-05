// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Navigation;
using TwinPanel.Core.Settings;
using Xunit;

namespace TwinPanel.Core.Tests;

public class TabTitleTests
{
    [Theory]
    [InlineData(@"C:\Users\utilisateur\Documents", "C: Documents")]
    [InlineData(@"D:\Photos\", "D: Photos")]
    [InlineData(@"C:\", "C:")]
    [InlineData("/home/utilisateur/Images", "Images")]
    public void FromPath_gives_the_folder_name(string path, string expected) =>
        Assert.Equal(expected, TabTitle.FromPath(path));

    [Fact]
    public void Unlocked_tab_follows_the_displayed_folder() =>
        Assert.Equal("Images", TabTitle.Display("", TabLock.None, "", "Images"));

    [Theory]
    [InlineData(TabLock.Locked)]
    [InlineData(TabLock.LockedAllowChange)]
    public void Locked_tab_keeps_the_name_of_its_locked_folder(TabLock mode) =>
        Assert.Equal("C: Documents", TabTitle.Display("", mode, @"C:\Users\utilisateur\Documents", "Images"));

    [Fact]
    public void Custom_name_wins_even_when_locked() =>
        Assert.Equal("Travail", TabTitle.Display("Travail", TabLock.LockedAllowChange, @"C:\Users\utilisateur\Documents", "Images"));
}
