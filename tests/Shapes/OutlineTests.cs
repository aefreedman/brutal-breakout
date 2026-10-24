using Brutal.Numerics;
using BrutalBreakout.Rendering.Shapes;
using Xunit;

namespace Shapes.Tests;

public class OutlineTests
{
    private static readonly LinearColor White = new(1, 1, 1);
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
    [InlineData(StrokeAlignment.Center, -1, 12, 80)]
    [InlineData(StrokeAlignment.Inside, 0, 10, 64)]
    [InlineData(StrokeAlignment.Outside, -2, 14, 96)]
    public void RectangleAlignmentIsHollowAndTiled(StrokeAlignment alignment, float min, float extent, double area)
    {
        using var d = new ShapeDrawList(); d.BeginFrame(100, 100);
        d.StrokeRect(V(0, 0), V(10, 10), 2, new(1, 1, 1, .5f), alignment);
        var f = d.Finish(); Assert.Equal(area, Area(f), 5); Tiled(f);
        Assert.Equal(min, f.Vertices.ToArray().Min(v => v.Position.X));
        Assert.Equal(min + extent, f.Vertices.ToArray().Max(v => v.Position.X));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvexCleanupWindingAndConsumedHole(bool reverse)
    {
        float2[] p = [V(0, 0), V(5, 0), V(10, 0), V(10, 0), V(10, 10), V(0, 10), V(0, 0)];
        if (reverse) Array.Reverse(p);
        using var d = new ShapeDrawList(); d.BeginFrame(100, 100);
        d.StrokeClosedPolyline(p, 10, White, StrokeAlignment.Inside);
        var f = d.Finish(); Assert.Equal(100, Area(f), 5); Tiled(f);
    }
    [Fact]
    public void AcuteTriangleAndDisappearingInsetEdgesTile()
    {
        using var d = new ShapeDrawList(); d.BeginFrame(1000, 1000);
        d.StrokeTriangle(V(0, 0), V(100, 0), V(1, 2), 1, White);
        var f = d.Finish(); Assert.True(Area(f) > 0); Tiled(f);
        d.BeginFrame(1000, 1000);
        d.StrokeClosedPolyline([V(0, 0), V(10, 0), V(10, 1), V(9, 10), V(0, 10)], 2, White, StrokeAlignment.Inside);
        f = d.Finish(); Assert.True(Area(f) > 0); Tiled(f);
    }
    [Fact]
    public void InvalidContoursRejectAndBudgetFailureIsAtomic()
    {
        using var d = new ShapeDrawList(4, 6); d.BeginFrame(100, 100); d.FillRect(V(0, 0), V(1, 1), White);
        Assert.Throws<ArgumentException>(() => d.StrokeClosedPolyline([V(0, 0), V(10, 0), V(2, 2), V(10, 10), V(0, 10)], 1, White));
        Assert.Throws<ArgumentException>(() => d.StrokeClosedPolyline([V(0, 0), V(10, 10), V(0, 10), V(10, 0)], 1, White));
        Assert.Throws<InvalidOperationException>(() => d.StrokeRect(V(0, 0), V(10, 10), 1, White));
        var f = d.Finish(); Assert.Equal(4, f.Vertices.Length); Assert.Equal(1, Area(f));
    }
}
