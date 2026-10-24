using Brutal.Numerics;
using BrutalBreakout.Rendering.Shapes;
using Xunit;

namespace Shapes.Tests;

public class DecorativeDashTests
{
    private static readonly LinearColor Half = new(1, 1, 1, .5f);
    private static float2 V(float x, float y) => new(x, y);

    private static double Cross(float2 a, float2 b, float2 c) => ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);
    private static double Area(ShapeFrame f)
    {
        double area = 0; var vs = f.Vertices.Span; var ids = f.Indices.Span;
        Assert.Equal(0, ids.Length % 3);
        foreach (var v in vs) { Assert.True(float.IsFinite(v.Position.X)); Assert.True(float.IsFinite(v.Position.Y)); }
        for (int i = 0; i < ids.Length; i += 3)
        {
            Assert.InRange(ids[i], 0u, (uint)vs.Length - 1); Assert.InRange(ids[i + 1], 0u, (uint)vs.Length - 1); Assert.InRange(ids[i + 2], 0u, (uint)vs.Length - 1);
            double a = Math.Abs(Cross(vs[(int)ids[i]].Position, vs[(int)ids[i + 1]].Position, vs[(int)ids[i + 2]].Position)) / 2;
            Assert.True(a > 0); area += a;
        }
        return area;
    }
    // Convex triangle clipping gives exact positive-area overlap, permitting shared tile boundaries.
    private static void Tiled(ShapeFrame frame)
    {
        var vs = frame.Vertices.ToArray(); var ids = frame.Indices.ToArray();
        for (int i = 0; i < ids.Length; i += 3) for (int j = i + 3; j < ids.Length; j += 3)
        {
            var poly = new List<(double x, double y)>(); for (int k = 0; k < 3; k++) { var p = vs[ids[i + k]].Position; poly.Add((p.X, p.Y)); }
            var t = Enumerable.Range(0, 3).Select(k => vs[ids[j + k]].Position).ToArray(); double sign = Math.Sign(Cross(t[0], t[1], t[2]));
            for (int edge = 0; edge < 3 && poly.Count > 0; edge++)
            {
                var a = t[edge]; var b = t[(edge + 1) % 3];
                double Side((double x, double y) p) => sign * ((b.X - a.X) * (p.y - a.Y) - (b.Y - a.Y) * (p.x - a.X));
                var output = new List<(double x, double y)>(); var previous = poly[^1]; double ps = Side(previous);
                foreach (var current in poly) { double cs = Side(current); if ((cs >= 0) != (ps >= 0)) { double u = ps / (ps - cs); output.Add((previous.x + u * (current.x - previous.x), previous.y + u * (current.y - previous.y))); } if (cs >= 0) output.Add(current); previous = current; ps = cs; }
                poly = output;
            }
            double area = 0; for (int k = 0; k < poly.Count; k++) { var a = poly[k]; var b = poly[(k + 1) % poly.Count]; area += a.x * b.y - a.y * b.x; }
            Assert.True(Math.Abs(area) < 0.0001, $"Overlapping triangles {i / 3}/{j / 3}: {area / 2}");
        }
    }
    [Theory]
    [InlineData(-1.0)] [InlineData(1.0)]
    public void AngledAnalyticGlyph(double modifier)
    {
        using var d = new ShapeDrawList(); d.BeginFrame(100, 100);
        d.StrokePatternedSegment(V(10, 20), V(30, 20), 4,
            StrokePattern.Dashes(4, 16, .5).WithShape(DashShape.Angled, modifier), Half);
        var f = d.Finish(); Tiled(f); Assert.Equal(16, Area(f), 6);
        foreach (var p in new[] { V((float)(20 - 2 * modifier), 18), V((float)(24 - 2 * modifier), 18),
            V((float)(20 + 2 * modifier), 22), V((float)(24 + 2 * modifier), 22) })
            Assert.Contains(f.Vertices.ToArray(), v => v.Position == p);
        Assert.All(f.Vertices.ToArray(), v => Assert.Equal(.5f, v.Color.W));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AngledAcrossCornerAndSeam(bool reverse)
    {
        float2[] open = [V(10, 10), V(30, 10), V(30, 30)];
        float2[] loop = [V(10, 10), V(30, 10), V(30, 30), V(10, 30)];
        if (reverse) { Array.Reverse(open); Array.Reverse(loop); }
        foreach (bool closed in new[] { false, true })
        {
            using var d = new ShapeDrawList(); d.BeginFrame(100, 100);
            d.StrokePatternedPolyline(closed ? loop : open, 2,
                StrokePattern.Dashes(24, 16, .75, true).WithShape(DashShape.Angled), Half, closed);
            var f = d.Finish(); Assert.True(Area(f) > 0); Tiled(f);
        }
    }

    [Theory]
    [InlineData(-1.0)] [InlineData(1.0)]
    public void AngledShortGlyphsAndOpenEndpointClipping(double modifier)
    {
        using var d = new ShapeDrawList(); d.BeginFrame(100, 100);
        d.StrokePatternedSegment(V(10, 20), V(18, 20), 4,
            StrokePattern.Dashes(.5, 1.5).WithShape(DashShape.Angled, modifier), Half);
        var f = d.Finish(); Tiled(f); Assert.InRange(Area(f), 7.99999, 8.00001);
        Assert.All(f.Vertices.ToArray(), v => Assert.InRange(v.Position.X, 10, 18));
        Assert.Contains(f.Vertices.ToArray(), v => v.Position.X == 10);
        Assert.Contains(f.Vertices.ToArray(), v => v.Position.X == 18);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AngledBasicFallbackAtCorner(bool zeroGap)
    {
        ShapeVertex[]? vertices = null; uint[]? indices = null;
        foreach (bool decorative in new[] { false, true })
        {
            using var d = new ShapeDrawList(); d.BeginFrame(100, 100);
            var p = StrokePattern.Dashes(24, zeroGap ? 0 : 8);
            if (decorative) p = p.WithShape(DashShape.Angled, zeroGap ? 1 : 0);
            d.StrokePatternedPolyline([V(10, 10), V(30, 10), V(30, 30)], 2, p, Half);
            var f = d.Finish();
            if (!decorative) { vertices = f.Vertices.ToArray(); indices = f.Indices.ToArray(); }
            else { Assert.Equal(vertices, f.Vertices.ToArray()); Assert.Equal(indices, f.Indices.ToArray()); }
        }
    }

    [Fact]
    public void AngledSampledCircle()
    {
        using var d = new ShapeDrawList(); d.BeginFrame(200, 200);
        d.StrokePatternedCircle(V(80, 80), 40, 2, StrokePattern.FixedDashes(8, .5, .9).WithShape(DashShape.Angled), Half);
        var f = d.Finish(); Assert.InRange(Area(f), 240, 260); Tiled(f);
    }

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
