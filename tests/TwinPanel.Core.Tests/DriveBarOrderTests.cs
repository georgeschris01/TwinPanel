// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Customization;
using Xunit;

namespace TwinPanel.Core.Tests;

public class DriveBarOrderTests
{
    [Fact]
    public void Arrange_KeepsOriginalOrder_WhenNothingSaved()
    {
        Assert.Equal(["a", "b", "c"], DriveBarOrder.Arrange(["a", "b", "c"], []));
    }

    [Fact]
    public void Arrange_FollowsSavedOrder_NewKeysAtTheEnd()
    {
        var r = DriveBarOrder.Arrange(["Z:\\", "Y:\\", "X:\\", "W:\\"], ["x:\\", "absent", "Z:\\"]);
        Assert.Equal(["X:\\", "Z:\\", "Y:\\", "W:\\"], r);
    }

    [Fact]
    public void Move_Forward_And_Backward()
    {
        Assert.Equal(["b", "c", "a"], DriveBarOrder.Move(["a", "b", "c"], "a", 3));
        Assert.Equal(["b", "a", "c"], DriveBarOrder.Move(["a", "b", "c"], "a", 2));
        Assert.Equal(["c", "a", "b"], DriveBarOrder.Move(["a", "b", "c"], "c", 0));
    }

    [Fact]
    public void Move_ReturnsNull_WhenNothingChanges()
    {
        Assert.Null(DriveBarOrder.Move(["a", "b", "c"], "b", 1));
        Assert.Null(DriveBarOrder.Move(["a", "b", "c"], "b", 2));
        Assert.Null(DriveBarOrder.Move(["a", "b", "c"], "inconnu", 0));
    }

    [Fact]
    public void Save_KeepsTheOtherGroup()
    {
        var saved = DriveBarOrder.Save(["Z:\\", "id1", "Y:\\", "id2"], ["id2", "id1"]);
        Assert.Equal(["id2", "id1", "Z:\\", "Y:\\"], saved);
        // L'ordre de l'autre groupe reste le même après relecture.
        Assert.Equal(["Z:\\", "Y:\\"], DriveBarOrder.Arrange(["Y:\\", "Z:\\"], saved));
    }
}
