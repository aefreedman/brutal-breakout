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
    public void AllWalls(double dx, double dy, double x, double y, double rx, double ry)
    {
        var hit = Arena.Query(new(640, 360), new(dx, dy));
        Near(new(x, y), hit.Position);
        Near(new(rx, ry), hit.ReflectedDirection);
    }

    [Theory]
    [InlineData(60, 60)] [InlineData(1220, 60)]
    [InlineData(60, 660)] [InlineData(1220, 660)]
    public void AllCornersReflectBothAxes(double x, double y)
    {
        var direction = new double2(x - 640, y - 360);
        var hit = Arena.Query(new(640, 360), direction);
        Near(new(x, y), hit.Position);
        Near(-double2.Normalize(direction), hit.ReflectedDirection);
    }

    [Theory]
    [InlineData(60, 360, -1, 0)] [InlineData(1220, 360, 1, 0)]
    [InlineData(640, 60, 0, -1)] [InlineData(640, 660, 0, 1)]
    public void OutwardBoundaryIsImmediate(double x, double y, double dx, double dy)
    {
        var hit = Arena.Query(new(x, y), new(dx, dy));
        Assert.Equal(0, hit.Distance);
        Assert.True(Arena.Query(hit.Position, hit.ReflectedDirection).Distance > 0);
    }

    [Fact]
    public void GrazingBoundaryDoesNotReflectTangentialAxis()
    {
        var hit = Arena.Query(new(60, 60), new(1, 0));
        Near(new(1220, 60), hit.Position);
        Near(new(-1, 0), hit.ReflectedDirection);
    }

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void RejectsNonfiniteQuery(double invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena.Query(new(invalid, 360), new(1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena.Query(new(640, 360), new(1, invalid)));
    }

    [Fact]
    public void RejectsZeroDirectionAndOutsideCenter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena.Query(new(640, 360), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena.Query(new(59, 360), new(1, 0)));
    }

    internal static void Near(double2 expected, double2 actual, double tolerance = 1e-8)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, tolerance);
    }
}
