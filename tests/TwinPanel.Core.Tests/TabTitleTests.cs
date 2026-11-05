// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Navigation;
using TwinPanel.Core.Settings;
using Xunit;

namespace TwinPanel.Core.Tests;

public class TabTitleTests
{
    /// <summary>Disques du PC : seulement sous Windows (sous Linux, « \ » n'est pas un séparateur de dossiers).</summary>
    [WindowsTheory]
    [InlineData(@"C:\Users\utilisateur\Documents", "C: Documents")]
    [InlineData(@"D:\Photos\", "D: Photos")]
    [InlineData(@"C:\", "C:")]
    public void FromPath_puts_the_drive_letter_before_the_folder_name(string path, string expected) =>
        Assert.Equal(expected, TabTitle.FromPath(path));

    [Theory]
    [InlineData("/home/utilisateur/Images", "Images")]
    [InlineData("/home/utilisateur/Images/", "Images")]
    public void FromPath_gives_the_folder_name(string path, string expected) =>
        Assert.Equal(expected, TabTitle.FromPath(path));

    [Fact]
    public void Unlocked_tab_follows_the_displayed_folder() =>
        Assert.Equal("Images", TabTitle.Display("", TabLock.None, "", "Images"));

    [Theory]
    [InlineData(TabLock.Locked)]
    [InlineData(TabLock.LockedAllowChange)]
    public void Locked_tab_keeps_the_name_of_its_locked_folder(TabLock mode) =>
        Assert.Equal("Documents", TabTitle.Display("", mode, "/home/utilisateur/Documents", "Images"));

    [Fact]
    public void Custom_name_wins_even_when_locked() =>
        Assert.Equal("Travail", TabTitle.Display("Travail", TabLock.LockedAllowChange, "/home/utilisateur/Documents", "Images"));
}
