using Brutal.Numerics;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class ArenaTests
{
    [Theory]
    [InlineData(1, 0, 1220, 360, -1, 0)]
    [InlineData(-1, 0, 60, 360, 1, 0)]
    [InlineData(0, 1, 640, 660, 0, -1)]
    [InlineData(0, -1, 640, 60, 0, 1)]
    public void CircleCastAllWallsExcludesOwnBall(double dx, double dy, double x, double y, double rx, double ry)
    {
        using var sim = new BallSimulation(TestSettings.Baseline);
        var hit = sim.Query(new(640, 360), new(dx, dy));
        Assert.True(hit.HasHit);
        Near(new(x, y), hit.Position, .51);
        Near(new(rx, ry), hit.ReflectedDirection, .001);
        Assert.InRange(hit.Distance, 299, 581); // circle center, not a ray to the wall face
        Assert.False(sim.Query(hit.Position, hit.ReflectedDirection).HasHit);
        var next = sim.Query(hit.Position + hit.Normal, hit.ReflectedDirection);
        Assert.True(next.HasHit);
        Assert.True(next.Distance > 590);
    }

    [Theory]
    [InlineData(60, 60)] [InlineData(1220, 60)]
    [InlineData(60, 660)] [InlineData(1220, 660)]
    public void CornerCastDoesNotInventCombinedNormal(double x, double y)
    {
        using var sim = new BallSimulation(TestSettings.Baseline);
        var hit = sim.Query(new(640, 360), new(x - 640, y - 360));
        if (hit.HasHit)
        {
            Near(new(x, y), hit.Position, 1);
            Assert.InRange(double2.Distance(default, hit.ReflectedDirection), .999, 1.001);
        } // one native normal is acceptable; no synthetic both-axis reflection
    }

    [Theory]
    [InlineData(48, 360, -1, 0)] [InlineData(1232, 360, 1, 0)]
    [InlineData(640, 48, 0, -1)] [InlineData(640, 672, 0, 1)]
    public void InitialOverlapIsNotAReflection(double x, double y, double dx, double dy)
    {
        using var sim = new BallSimulation(TestSettings.Baseline);
        Assert.False(sim.Query(new(x, y), new(dx, dy)).HasHit);
    }

    [Fact]
    public void NoHitAndDefaultAreExplicit()
    {
        using var sim = new BallSimulation(TestSettings.Baseline);
        Assert.False(default(WallContact).HasHit);
        Assert.False(sim.Query(new(10, 10), new(-1, 0)).HasHit);
    }

    [Fact]
    public void GrazingCastRemainsFinite()
    {
        using var sim = new BallSimulation(TestSettings.Baseline);
        var hit = sim.Query(new(61, 61), new(1, 0));
        Assert.True(hit.HasHit);
        Near(new(1220, 61), hit.Position, .51);
        Near(new(-1, 0), hit.ReflectedDirection, .001);
    }

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void RejectsNonfiniteQuery(double invalid)
    {
        using var sim = new BallSimulation(TestSettings.Baseline);
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Query(new(invalid, 360), new(1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Query(new(640, 360), new(1, invalid)));
    }

    [Theory]
    [InlineData(double.MaxValue, double.MaxValue)]
    [InlineData(double.Epsilon, double.Epsilon)]
    public void NativeCastNormalizesExtremeFiniteDirections(double x, double y)
    {
        using var sim = new BallSimulation(TestSettings.Baseline);
        var hit = sim.Query(new(640, 360), new(x, y));
        Assert.True(hit.HasHit);
        Assert.InRange(hit.Distance, 400, 430);
        Assert.InRange(double2.Distance(default, hit.ReflectedDirection), .999, 1.001);
    }

    [Fact]
    public void RejectsInvalidArgumentsSeparatelyFromOrdinaryOverlap()
    {
        using var sim = new BallSimulation(TestSettings.Baseline);
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Query(new(640, 360), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Query(new(-1, 360), new(1, 0)));
    }

    internal static void Near(double2 expected, double2 actual, double tolerance = .01)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, tolerance);
    }
}
