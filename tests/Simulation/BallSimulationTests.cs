using Brutal.Numerics;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class BallSimulationTests
{
    [Fact]
    public void InitialStateAndLaunch()
    {
        var sim = new BallSimulation();
        Assert.Equal(BallPhase.Aiming, sim.State.Phase);
        ArenaTests.Near(new(640, 612), sim.State.Position);
        ArenaTests.Near(default, sim.State.Velocity);
        ArenaTests.Near(new(0, -1), sim.State.AimDirection);
        sim.Step(new(PrimaryPressed: true));
        Assert.Equal(BallPhase.Flying, sim.State.Phase);
        Assert.False(sim.State.CatchEligible);
        ArenaTests.Near(new(640, 607), sim.State.Position);
        ArenaTests.Near(new(0, -600), sim.State.Velocity);
    }

    [Fact]
    public void CatchOnlyAfterContactPreservesPositionAndRequiresSeparatePress()
    {
        var sim = new BallSimulation();
        sim.Step(new(PrimaryPressed: true));
        sim.Step(new(PrimaryPressed: true));
        Assert.Equal(BallPhase.Flying, sim.State.Phase);
        while (!sim.State.CatchEligible) sim.Step();
        var position = sim.State.Position;
        sim.Step(new(PrimaryPressed: true));
        Assert.Equal(BallPhase.Aiming, sim.State.Phase);
        ArenaTests.Near(position, sim.State.Position);
        ArenaTests.Near(default, sim.State.Velocity);
        sim.Step();
        ArenaTests.Near(position, sim.State.Position);
        sim.Step(new(new double2(1, 0), PrimaryPressed: true));
        Assert.Equal(BallPhase.Flying, sim.State.Phase);
        Assert.False(sim.State.CatchEligible);
        ArenaTests.Near(position + new double2(5, 0), sim.State.Position);
    }

    [Fact]
    public void RestartWinsOverAimAndPrimary()
    {
        var sim = new BallSimulation();
        sim.Step(new(PrimaryPressed: true));
        sim.Step(new(new double2(1, 0), true, true));
        var initial = new BallSimulation().State;
        Assert.Equal(initial, sim.State);
    }

    [Theory]
    [InlineData(3, 4)] [InlineData(double.MaxValue, double.MaxValue)]
    [InlineData(double.Epsilon, double.Epsilon)]
    public void NormalizesFiniteDirectionsRobustly(double x, double y)
    {
        var sim = new BallSimulation();
        sim.Step(new(new double2(x, y), true));
        Assert.InRange(double2.Distance(default, sim.State.Velocity), 599.99999999, 600.00000001);
        ArenaTests.Near(double2.Normalize(new double2(x / Math.Max(x, y), y / Math.Max(x, y))), sim.State.AimDirection);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(double.NaN, 1)]
    [InlineData(1, double.PositiveInfinity)] [InlineData(double.NegativeInfinity, 1)]
    public void ZeroAndNonfiniteAimAreExplicitlyIgnored(double x, double y)
    {
        var sim = new BallSimulation();
        sim.Step(new(new double2(x, y), true));
        ArenaTests.Near(new(0, -600), sim.State.Velocity);
    }

    [Theory]
    [InlineData(1, 0)] [InlineData(-1, 0)] [InlineData(0, 1)] [InlineData(0, -1)]
    [InlineData(580, -552)] [InlineData(-580, -552)]
    [InlineData(580, 48)] [InlineData(-580, 48)]
    [InlineData(1, 0.000001)]
    public void PreviewMatchesActualFirstImpactAndRemainingTravel(double x, double y)
    {
        var sim = new BallSimulation();
        sim.Step(new(new double2(x, y)));
        var preview = sim.Preview();
        sim.Step(new(PrimaryPressed: true));
        int ticks = 1;
        while (!sim.State.CatchEligible && ticks < 1000) { sim.Step(); ticks++; }
        Assert.True(sim.State.CatchEligible);
        double remainder = ticks * 5 - preview.Distance;
        Assert.InRange(remainder, -1e-8, 5.00000001);
        ArenaTests.Near(preview.Position + preview.ReflectedDirection * remainder, sim.State.Position);
        ArenaTests.Near(preview.ReflectedDirection * Arena.Speed, sim.State.Velocity);
    }

    [Fact]
    public void LongRunStaysInsideAtConstantSpeedWithoutAllocations()
    {
        var sim = new BallSimulation();
        sim.Step(new(new double2(1, -0.731), true));
        for (int i = 0; i < 100; i++) sim.Step(); // Warm up.
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) sim.Step();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        for (int i = 0; i < 10000; i++)
        {
            sim.Step();
            Assert.InRange(sim.State.Position.X, Arena.MinX, Arena.MaxX);
            Assert.InRange(sim.State.Position.Y, Arena.MinY, Arena.MaxY);
            Assert.InRange(double2.Distance(default, sim.State.Velocity), 599.99999999, 600.00000001);
        }
    }
}
