// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Navigation;
using TwinPanel.Core.Settings;
using Xunit;

namespace TwinPanel.Core.Tests;

public class TabPositionsTests
{
    private static PanelState Open(int active, params string[] tabs) =>
        new() { Tabs = [.. tabs], ActiveTab = active, SortColumn = SortColumn.Size, SortDescending = true };

    [Fact]
    public void FermetureAvantLaReconnexion_GardeLesOngletsDeServeurAleurPlace()
    {
        // Enregistré : [serveur A, C:\, serveur B, D:\], actif = D:\ (3). Au démarrage, seuls C:\ et D:\ sont ouverts.
        var open = Open(1, @"C:\", @"D:\");
        var result = TabPositions.WithPending(open, [(0, "remote:a|/media"), (2, "remote:b|/home")]);

        Assert.Equal(["remote:a|/media", @"C:\", "remote:b|/home", @"D:\"], result.Tabs);
        Assert.Equal(3, result.ActiveTab);
        Assert.Equal(SortColumn.Size, result.SortColumn);
        Assert.True(result.SortDescending);
    }

    [Fact]
    public void OngletDejaRouvert_PasEnDouble()
    {
        var open = Open(0, "remote:a|/media", @"C:\");
        var result = TabPositions.WithPending(open, [(0, "remote:a|/media")]);

        Assert.Same(open, result);
    }

    [Fact]
    public void AucunOngletEnAttente_RienNeChange()
    {
        var open = Open(0, @"C:\");
        Assert.Same(open, TabPositions.WithPending(open, []));
    }

    [Fact]
    public void AucunOngletDuPcOuvert_LesOngletsDeServeurRestent()
    {
        var result = TabPositions.WithPending(Open(0), [(1, "remote:b|/home"), (0, "remote:a|/media")]);

        Assert.Equal(["remote:a|/media", "remote:b|/home"], result.Tabs);
    }
}
