// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core;

namespace TwinPanel.Core.Tests;

public class TransferGateTests
{
    [Fact]
    public void OneAtATimeByDefault_InArrivalOrder()
    {
        var gate = new TransferGate<string>();
        Assert.True(gate.Enter("a"));
        Assert.False(gate.Enter("b"));
        Assert.False(gate.Enter("c"));

        Assert.Equal(["b"], gate.Leave("a"));
        Assert.Equal(["c"], gate.Leave("b"));
        Assert.Empty(gate.Leave("c"));
    }

    [Fact]
    public void RaisingTheLimit_StartsWaitingOnesAtOnce()
    {
        var gate = new TransferGate<string>();
        gate.Enter("a");
        gate.Enter("b");
        gate.Enter("c");

        Assert.Equal(["b", "c"], gate.SetLimit(3));
    }

    [Fact]
    public void LoweringTheLimit_StopsNothing_ButHoldsTheNextOnes()
    {
        var gate = new TransferGate<string>();
        gate.SetLimit(2);
        gate.Enter("a");
        gate.Enter("b");
        gate.Enter("c");

        Assert.Empty(gate.SetLimit(1));
        Assert.Empty(gate.Leave("a")); // b tourne encore : la nouvelle limite est atteinte
        Assert.Equal(["c"], gate.Leave("b"));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(4, 4)]
    [InlineData(50, 8)]
    public void LimitStaysInRange(int asked, int kept)
    {
        var gate = new TransferGate<string>();
        gate.SetLimit(asked);
        Assert.Equal(kept, gate.Limit);
    }

    [Fact]
    public void ForgetRemovesAWaitingOne_NotARunningOne()
    {
        var gate = new TransferGate<string>();
        gate.Enter("a");
        gate.Enter("b");
        gate.Enter("c");

        Assert.True(gate.Forget("b"));
        Assert.False(gate.Forget("a"));
        Assert.Equal(["c"], gate.Leave("a"));
    }

    [Fact]
    public void SameArchive_NeverTwiceAtOnce_EvenWithRoom()
    {
        var gate = new TransferGate<string>();
        gate.SetLimit(4);
        Assert.True(gate.Enter("edit 1", @"D:\a.zip"));
        Assert.False(gate.Enter("edit 2", @"d:\A.ZIP"));
        Assert.True(gate.Enter("other archive", @"D:\b.zip"));
        Assert.True(gate.Enter("copy"));

        Assert.Equal(["edit 2"], gate.Leave("edit 1"));
    }

    [Fact]
    public void JobHeldByItsKey_DoesNotBlockTheOnesBehindIt()
    {
        var gate = new TransferGate<string>();
        gate.SetLimit(2);
        gate.Enter("edit 1", "a.zip");
        Assert.False(gate.Enter("edit 2", "a.zip"));
        Assert.True(gate.Enter("copy"));
    }

    [Fact]
    public void Lines_ThreeAtMost_MostImportantFirst_KeepingArrivalOrder()
    {
        var jobs = new[] { ("waiting", 2), ("running 1", 1), ("failed", 0), ("running 2", 1), ("done", 3) };

        var shown = ProgressLines.Pick(jobs, j => j.Item2).Select(j => j.Item1);

        Assert.Equal(["failed", "running 1", "running 2"], shown);
    }
}
