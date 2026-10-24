using Brutal.Numerics;
using BrutalBreakout.Rendering.Shapes;
using Xunit;

namespace Shapes.Tests;

public class PatternTests
{
    private static readonly LinearColor Half = new(1, 1, 1, .5f);
    private static float2 V(float x, float y) => new(x, y);
    [Theory]
    [InlineData(-2.25, .75)] [InlineData(10.75, .75)] [InlineData(1e300, 0)] [InlineData(-1e300, 0)]
    public void PhaseIsPeriodic(double phase, double normalized)
    {
        var a = StrokePattern.Dashes(3, 2, phase).Sample(20, 1, false);
        var b = StrokePattern.Dashes(3, 2, normalized).Sample(20, 1, false);
        Assert.Equal(b.Intervals, a.Intervals);
        Assert.Equal(StrokePattern.Dots(5, normalized).Sample(20, 1, false).Sites,
            StrokePattern.Dots(5, phase).Sample(20, 1, false).Sites);
    }
    [Fact]
    public void RelativeAndFixedSemantics()
    {
        Assert.Equal(StrokePattern.Dashes(6, 4).Sample(30, 1, false).Intervals,
            StrokePattern.RelativeDashes(3, 2).Sample(30, 2, false).Intervals);
        Assert.Equal(new double[] { 0, 5, 10, 15 }, StrokePattern.FixedDots(4).Sample(20, 1, false).Sites);
        Assert.Equal(new double[] { 0, 5, 10, 15, 20 }, StrokePattern.Dots(5).Sample(20, 1, false).Sites);
        Assert.Equal(new double[] { 0, 10 }, StrokePattern.RelativeDots(5).Sample(15, 2, false).Sites);
        Assert.Equal(5, StrokePattern.FixedDashes(4, .5, .75).Sample(20, 1, false).Intervals.Count);
    }
    [Fact]
    public void ClosedFitAndSeam()
    {
        var s = StrokePattern.Dashes(3, 2, .8, true).Sample(22, 1, true).Intervals;
        Assert.Equal(4, s.Count); Assert.Equal(22.0 + 2.2, s[^1].End, 10);
        Assert.Equal(13.2, s.Sum(x => x.End - x.Start), 10);
        Assert.Equal(3, StrokePattern.Dots(4, fitClosed: true).Sample(10, 1, true).Sites.Count); // midpoint up
        Assert.Equal(4, StrokePattern.Dots(5).Sample(20, 1, true).Sites.Count);
    }
    [Fact]
    public void InvalidAndExplosiveStylesReject()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StrokePattern.Dashes(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrokePattern.Dashes(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrokePattern.Dots(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrokePattern.Dots(1, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrokePattern.FixedDots(4097));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrokePattern.FixedDashes(2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => default(StrokePattern).Sample(1, 1, false));
        Assert.Throws<ArgumentException>(() => StrokePattern.Dots(1e-300).Sample(100, 1, false));
        Assert.Empty(StrokePattern.Dots(1e-300).Sample(0, 1, false).Sites);
        Assert.Single(StrokePattern.Dashes(1e-300, 0).Sample(100, 1, false).Intervals);
        Assert.Equal(4096, StrokePattern.FixedDots(4096).Sample(100, 1, true).Sites.Count);
    }
}
