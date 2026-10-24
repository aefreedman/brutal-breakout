using Brutal.Numerics;
using BrutalBreakout.Rendering.Shapes;
using Xunit;

namespace Shapes.Tests;

public class DecorativeDashTests
{
    private static readonly LinearColor Half = new(1, 1, 1, .5f);
    private static float2 V(float x, float y) => new(x, y);

    [Theory]
    [InlineData(DashShape.Basic)] [InlineData(DashShape.Angled)] [InlineData(DashShape.Chevron)]
    public void StyleRetainsSampler(DashShape shape)
    {
        StrokePattern[] patterns = [StrokePattern.Dashes(3, 2, -1e300, true),
            StrokePattern.RelativeDashes(3, 2, -.25, true), StrokePattern.FixedDashes(5, .3, .75)];
        foreach (var p in patterns) foreach (double modifier in new[] { -1.0, 0, 1 })
        {
            var a = p.Sample(35, 2, true); var b = p.WithShape(shape, modifier).Sample(35, 2, true);
            Assert.Equal(a.Intervals, b.Intervals); Assert.Equal(a.Solid, b.Solid);
        }
    }

    [Fact]
    public void InvalidStylesReject()
    {
        var p = StrokePattern.Dashes(3, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => p.WithShape((DashShape)99));
        foreach (double m in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.01, 1.01 })
            foreach (var s in Enum.GetValues<DashShape>())
                Assert.Throws<ArgumentOutOfRangeException>(() => p.WithShape(s, m));
        foreach (var pDot in new[] { StrokePattern.Dots(3), StrokePattern.RelativeDots(3), StrokePattern.FixedDots(3) })
            foreach (var s in new[] { DashShape.Angled, DashShape.Chevron })
                Assert.Throws<ArgumentOutOfRangeException>(() => pDot.WithShape(s, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => default(StrokePattern).WithShape(DashShape.Basic));
    }

    [Theory]
    [InlineData(DashShape.Angled, LineCap.Square)] [InlineData(DashShape.Angled, LineCap.Round)]
    [InlineData(DashShape.Chevron, LineCap.Square)] [InlineData(DashShape.Chevron, LineCap.Round)]
    public void CapsRejectBeforeNoOutput(DashShape shape, LineCap cap)
    {
        using var d = new ShapeDrawList(); d.BeginFrame(100, 100);
        var p = StrokePattern.Dashes(3, 2).WithShape(shape, 0);
        Assert.Throws<ArgumentException>(() => d.StrokePatternedSegment(V(1, 1), V(1, 1), 0, p, new(0, 0, 0, 0), cap));
        Assert.Throws<ArgumentException>(() => d.StrokePatternedCircle(V(1, 1), 0, 0, p, Half, cap));
        Assert.Throws<ArgumentException>(() => d.StrokePatternedPolyline([], 0, p, Half, cap: cap));
        Assert.Empty(d.Finish().Indices.ToArray());
    }
}
