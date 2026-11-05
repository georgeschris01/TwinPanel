// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Navigation;
using Xunit;

namespace TwinPanel.Core.Tests;

public class NavigationCursorTests
{
    [Theory]
    [InlineData(@"C:\A", @"C:\A\B", "B")]
    [InlineData(@"C:\A", @"C:\A\B\C", "B")]
    [InlineData(@"C:\", @"C:\Users", "Users")]
    [InlineData("sftp://serveur/home", "sftp://serveur/home/utilisateur/photos", "utilisateur")]
    [InlineData("sftp://serveur/", "sftp://serveur/etc", "etc")]
    [InlineData(@"C:\A", @"C:\AB", null)]        // pas un sous-dossier, malgré le début commun
    [InlineData(@"C:\A\B", @"C:\A", null)]       // on était plus haut
    [InlineData(@"D:\Photos", @"C:\Photos\2026", null)]
    public void Child_toward_the_folder_we_came_from(string parent, string current, string? expected) =>
        Assert.Equal(expected, NavigationHistory.ChildToward(parent, current));

    [Fact]
    public void Back_returns_to_the_item_left_under_the_cursor()
    {
        var h = new NavigationHistory();
        h.RememberCursor(@"C:\A", "facture.pdf");
        Assert.Equal("facture.pdf", h.CursorFor(@"C:\A", @"C:\Autre"));
    }

    [Fact]
    public void Without_a_remembered_item_back_points_at_the_folder_we_came_out_of()
    {
        var h = new NavigationHistory();
        Assert.Equal("B", h.CursorFor(@"C:\A", @"C:\A\B"));
    }

    [Fact]
    public void The_parent_line_is_not_remembered()
    {
        var h = new NavigationHistory();
        h.RememberCursor(@"C:\A", "..");
        Assert.Null(h.CursorFor(@"C:\A", @"C:\Autre"));
    }
}
