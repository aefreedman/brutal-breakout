using BrutalBreakout.Configuration;
using BrutalBreakout.Presentation;
using Xunit;

public class RenderFrameBudgetTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(60, 0, 1d / 60)]
    [InlineData(60, .010, 1d / 60 - .010)]
    [InlineData(60, .020, 0)]
    [InlineData(60, 1d / 60, 0)]
    public void BudgetIncludesAllWorkAndVSync(int cap, double work, double expected) =>
        Assert.Equal(expected, RenderFrameBudget.RemainingSeconds(0, work, cap), 10);

    [Fact]
    public void WaitChunksPumpEventsAndOvershootHasNoDebtOrBurst()
    {
        double now = .002; var waits = new List<double>();
        var pacer = new RenderFrameBudget(() => now, delay => { waits.Add(delay); now += delay + .003; }, () => false);
        var settings = new WindowSettings(1, 1, true, 60);
        pacer.Wait(0, settings);
        Assert.All(waits, delay => Assert.InRange(delay, double.Epsilon, .010));
        Assert.True(now >= 1d / 60);
        waits.Clear();
        double start = now;
        pacer.Wait(start, settings); // Fresh full period, not reduced by previous overshoot.
        Assert.NotEmpty(waits); Assert.Equal(.010, waits[0]);
    }

    [Fact]
    public void UnlimitedLateCloseAndLiveChanges()
    {
        double now = 0; int waits = 0; bool closed = false;
        var pacer = new RenderFrameBudget(() => now, delay => { waits++; now += delay; closed = true; }, () => closed);
        pacer.Wait(0, new(1, 1, true, 0)); Assert.Equal(0, waits);
        now = .1;
        pacer.Wait(0, new(1, 1, true, 60)); Assert.Equal(0, waits);
        pacer.Wait(now, new(1, 1, false, 1)); Assert.Equal(1, waits); // Close interrupts <=10ms chunk.
        pacer.Wait(now, new(1, 1, true, 144)); Assert.Equal(1, waits);
        closed = false;
        pacer.Wait(now, new(1, 1, true, 144)); Assert.Equal(2, waits);
    }
}
