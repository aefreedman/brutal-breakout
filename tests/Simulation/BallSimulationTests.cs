using Brutal.Numerics;
using BrutalBreakout.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Simulation.Tests;

public class BallSimulationTests(ITestOutputHelper output)
{
    [Fact]
    public void InitialStateAndLaunch()
    {
        using var sim = new BallSimulation();
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
        using var sim = new BallSimulation();
        sim.Step(new(PrimaryPressed: true));
        sim.Step(new(PrimaryPressed: true));
        Assert.Equal(BallPhase.Flying, sim.State.Phase);
        for (int i = 0; i < 300 && !sim.State.CatchEligible; i++) sim.Step();
        Assert.True(sim.State.CatchEligible);
        var position = sim.State.Position;
        sim.Step(new(PrimaryPressed: true));
        Assert.Equal(BallPhase.Aiming, sim.State.Phase);
        ArenaTests.Near(position, sim.State.Position);
        ArenaTests.Near(default, sim.State.Velocity);
        sim.Step();
        ArenaTests.Near(position, sim.State.Position);
        sim.Step(new(new double2(1, 0), PrimaryPressed: true));
        Assert.Equal(BallPhase.Flying, sim.State.Phase);
        // A tangential relaunch at contact can generate a genuine new begin event.
        ArenaTests.Near(position + new double2(5, 0), sim.State.Position);
    }

    [Fact]
    public void RestartWinsOverAimAndPrimary()
    {
        using var sim = new BallSimulation();
        sim.Step(new(PrimaryPressed: true));
        sim.Step(new(new double2(1, 0), true, true));
        using var reference = new BallSimulation();
        var initial = reference.State;
        Assert.Equal(initial, sim.State);
    }

    [Theory]
    [InlineData(3, 4)] [InlineData(double.MaxValue, double.MaxValue)]
    [InlineData(double.Epsilon, double.Epsilon)]
    public void NormalizesFiniteDirectionsRobustly(double x, double y)
    {
        using var sim = new BallSimulation();
        sim.Step(new(new double2(x, y), true));
        Assert.InRange(double2.Distance(default, sim.State.Velocity), 599.99, 600.01);
        ArenaTests.Near(double2.Normalize(new double2(x / Math.Max(x, y), y / Math.Max(x, y))), sim.State.AimDirection);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(double.NaN, 1)]
    [InlineData(1, double.PositiveInfinity)] [InlineData(double.NegativeInfinity, 1)]
    public void ZeroAndNonfiniteAimAreExplicitlyIgnored(double x, double y)
    {
        using var sim = new BallSimulation();
        sim.Step(new(new double2(x, y), true));
        ArenaTests.Near(new(0, -600), sim.State.Velocity);
    }

    [Theory]
    [InlineData(1, 0)] [InlineData(-1, 0)] [InlineData(0, 1)] [InlineData(0, -1)]
    [InlineData(1, -0.5)] [InlineData(-1, -0.5)] [InlineData(1, 0.000001)]
    public void PreviewMatchesFirstNativeImpactWithinOneLogicalUnit(double x, double y)
    {
        using var sim = new BallSimulation();
        sim.Step(new(new double2(x, y)));
        var preview = sim.Preview();
        Assert.True(preview.HasHit);
        double closest = double.PositiveInfinity;
        // Native contacts are soft/speculative, so measure the closest contact-plane
        // approach, not the removed solver's exact TOI plus remaining-distance formula.
        var normal = ArenaDirectionNormal(sim.State.AimDirection, preview.ReflectedDirection);
        for (int ticks = 0; ticks < 1000; ticks++)
        {
            sim.Step(new(PrimaryPressed: ticks == 0));
            var delta = sim.State.Position - preview.Position;
            closest = Math.Min(closest, Math.Abs(delta.X * normal.X + delta.Y * normal.Y));
            if (sim.State.CatchEligible && sim.State.Velocity.X * normal.X + sim.State.Velocity.Y * normal.Y > 0) break;
        }
        Assert.True(sim.State.CatchEligible);
        output.WriteLine($"Shot ({x},{y}): closest preview-plane approach {closest:F9} logical units");
        Assert.InRange(closest, 0, 1);
        ArenaTests.Near(preview.ReflectedDirection * Arena.Speed, sim.State.Velocity, .1);
    }

    private static double2 ArenaDirectionNormal(double2 incoming, double2 reflected) =>
        double2.Normalize(reflected - incoming);

    [Theory]
    [InlineData(1, 0)] [InlineData(-1, 0)] [InlineData(0, 1)] [InlineData(0, -1)]
    [InlineData(1, -0.731)] [InlineData(580, -552)] [InlineData(-580, -552)]
    [InlineData(580, 48)] [InlineData(-580, 48)] [InlineData(1, 0.000001)]
    public void LongRunNativeSolverDoesNotEscapeStallOrLoseEnergy(double x, double y)
    {
        using var sim = new BallSimulation();
        sim.Step(new(new double2(x, y), true));
        const double nativeContactAllowance = 2, coordinateRoundoffAllowance = .001;
        double penetration = 0, drift = 0;
        for (int i = 0; i < 10000; i++)
        {
            sim.Step();
            penetration = Math.Max(penetration, Math.Max(Math.Max(Arena.MinX - sim.State.Position.X, sim.State.Position.X - Arena.MaxX),
                Math.Max(Arena.MinY - sim.State.Position.Y, sim.State.Position.Y - Arena.MaxY)));
            drift = Math.Max(drift, Math.Abs(double2.Distance(default, sim.State.Velocity) - Arena.Speed));
            Assert.InRange(sim.State.Position.X, Arena.MinX - nativeContactAllowance - coordinateRoundoffAllowance, Arena.MaxX + nativeContactAllowance + coordinateRoundoffAllowance);
            Assert.InRange(sim.State.Position.Y, Arena.MinY - nativeContactAllowance - coordinateRoundoffAllowance, Arena.MaxY + nativeContactAllowance + coordinateRoundoffAllowance);
            Assert.InRange(double2.Distance(default, sim.State.Velocity), 599, 601);
        }
        output.WriteLine($"Shot ({x},{y}), 10000 ticks: worst penetration {penetration:F9}, speed drift {drift:F9}");
    }
}
