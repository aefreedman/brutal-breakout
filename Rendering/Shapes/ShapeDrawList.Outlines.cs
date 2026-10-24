using Brutal.Numerics;

namespace BrutalBreakout.Rendering.Shapes;

public sealed partial class ShapeDrawList
{
    private readonly List<P> outlineBoundary = [], outlineOuter = [], outlineInner = [], outlineClip = [];

    private void StartOutline(float width, LinearColor color, StrokeAlignment alignment, StrokeSpace space)
    {
        Start(color);
        Nonnegative(width);
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        if (!Enum.IsDefined(space)) throw new ArgumentOutOfRangeException(nameof(space));
        outlineBoundary.Clear(); outlineOuter.Clear(); outlineInner.Clear();
    }

    public void StrokeRect(float2 origin, float2 size, float width, LinearColor color,
        StrokeAlignment alignment = StrokeAlignment.Center, StrokeSpace space = StrokeSpace.Local)
    {
        StartOutline(width, color, alignment, space);
        var o = Input(origin); var s = Size(size);
        if (NoOutput(color) || width == 0 || s.X == 0 || s.Y == 0) return;
        Rect(o, s); outlineBoundary.AddRange(polygon); polygon.Clear();
        StrokeBoundary(width, color, alignment, space);
    }

    public void StrokeTriangle(float2 a, float2 b, float2 c, float width, LinearColor color,
        StrokeAlignment alignment = StrokeAlignment.Center, StrokeSpace space = StrokeSpace.Local)
    {
        StartOutline(width, color, alignment, space);
        var p = Input(a); var q = Input(b); var r = Input(c);
        if (NoOutput(color) || width == 0 || Cross(q - p, r - p) == 0) return;
        outlineBoundary.Add(p); outlineBoundary.Add(q); outlineBoundary.Add(r);
        StrokeBoundary(width, color, alignment, space);
    }

    /// <summary>Simple convex closed contours only, at most 1024 supplied points. Uses miter corners;
    /// duplicate and forward-collinear points are removed. Nonconvex/crossing contours reject visibly.
    /// Erosion can remove edges or consume the entire hole, leaving a solid outer silhouette.</summary>
    public void StrokeClosedPolyline(ReadOnlySpan<float2> points, float width, LinearColor color,
        StrokeAlignment alignment = StrokeAlignment.Center, StrokeSpace space = StrokeSpace.Local)
    {
        StartOutline(width, color, alignment, space);
        if (points.Length > 1024) throw new ArgumentOutOfRangeException(nameof(points));
        foreach (var point in points) Input(point);
        if (NoOutput(color) || width == 0) return;
        foreach (var point in points) outlineBoundary.Add(Input(point));
        CleanOutline(outlineBoundary);
        if (outlineBoundary.Count < 3) return;
        StrokeBoundary(width, color, alignment, space);
    }

    // These contour/offset operations intentionally stay independent of public shape entry points,
    // so a subsequent patterned-path sampler can reuse the selected measurement-space boundary.
    private void PrepareBoundary(StrokeSpace space)
    {
        CleanOutline(outlineBoundary);
        ValidateSimpleOutline(outlineBoundary, "Closed stroke requires a simple convex contour.");
        double area = 0;
        for (int i = 1; i + 1 < outlineBoundary.Count; i++)
            area += Cross(outlineBoundary[i] - outlineBoundary[0], outlineBoundary[i + 1] - outlineBoundary[0]);
        if (area == 0) throw new ArgumentException("Closed stroke requires a nondegenerate convex contour.");
        if (area < 0) outlineBoundary.Reverse();
        for (int i = 0; i < outlineBoundary.Count; i++)
        {
            var a = outlineBoundary[i]; var b = outlineBoundary[(i + 1) % outlineBoundary.Count];
            var c = outlineBoundary[(i + 2) % outlineBoundary.Count];
            if (Cross(b - a, c - b) <= 0) throw new ArgumentException("Closed stroke requires a convex contour.");
        }
        if (space == StrokeSpace.Framebuffer)
        {
            for (int i = 0; i < outlineBoundary.Count; i++) outlineBoundary[i] = Pixel(outlineBoundary[i]);
            if (Cross(outlineBoundary[1] - outlineBoundary[0], outlineBoundary[2] - outlineBoundary[1]) < 0)
                outlineBoundary.Reverse();
        }
    }

    private static (double outside, double inside) Distances(float width, StrokeAlignment alignment) =>
        alignment switch { StrokeAlignment.Inside => (0, width), StrokeAlignment.Outside => (width, 0), _ => (width / 2.0, width / 2.0) };

    private void StrokeBoundary(float width, LinearColor color, StrokeAlignment alignment, StrokeSpace space)
    {
        PrepareBoundary(space);
        var (outside, inside) = Distances(width, alignment);
        OffsetOuter(outside);
        ErodeInner(inside);
        EmitOutline(color, space);
    }

    private void OffsetOuter(double distance)
    {
        int n = outlineBoundary.Count;
        for (int i = 0; i < n; i++)
        {
            var p = outlineBoundary[i];
            if (distance == 0) { outlineOuter.Add(p); continue; }
            var a = Unit(p - outlineBoundary[(i + n - 1) % n]);
            var b = Unit(outlineBoundary[(i + 1) % n] - p);
            // Intersect shifted support lines. Cross formulation avoids 1+dot cancellation at acute tips.
            var na = new P(a.Y, -a.X); var nb = new P(b.Y, -b.X);
            double turn = Cross(a, b);
            var q = p + na * distance + a * (Cross((nb - na) * distance, b) / turn);
            if (!double.IsFinite(q.X) || !double.IsFinite(q.Y)) throw new ArgumentException("Invalid stroke offset.");
            outlineOuter.Add(q);
        }
    }

    private void ErodeInner(double distance)
    {
        outlineInner.AddRange(outlineBoundary);
        if (distance == 0) return;
        // Intersection of inward shifted halfplanes, rather than intersecting only adjacent lines:
        // narrow edges may vanish before the hole is fully consumed.
        for (int edge = 0; edge < outlineBoundary.Count && outlineInner.Count > 0; edge++)
        {
            var a = outlineBoundary[edge]; var d = Unit(outlineBoundary[(edge + 1) % outlineBoundary.Count] - a);
            double Side(P p) => Cross(d, p - a) - distance;
            outlineClip.Clear();
            var previousPoint = outlineInner[^1]; double ps = Side(previousPoint);
            foreach (var current in outlineInner)
            {
                double cs = Side(current);
                if ((cs > 0) != (ps > 0)) outlineClip.Add(previousPoint + (current - previousPoint) * (ps / (ps - cs)));
                if (cs > 0) outlineClip.Add(current);
                previousPoint = current; ps = cs;
            }
            outlineInner.Clear(); outlineInner.AddRange(outlineClip);
        }
        CleanOutline(outlineInner);
        if (outlineInner.Count < 3) outlineInner.Clear();
    }

    private void ValidatePixelLoop(List<P> loop, StrokeSpace space)
    {
        outlineClip.Clear();
        foreach (var p in loop)
        {
            var q = space == StrokeSpace.Framebuffer ? p : Pixel(p);
            if (!double.IsFinite(q.X) || !double.IsFinite(q.Y) || Math.Abs(q.X) > 1048576 || Math.Abs(q.Y) > 1048576)
                throw new ArgumentException("Framebuffer coordinate envelope exceeded.");
            outlineClip.Add(new((float)q.X, (float)q.Y));
        }
        CleanOutline(outlineClip);
        if (outlineClip.Count < 3) throw new ArgumentException("Framebuffer FLOAT32 precision collapses stroke boundary.");
        ValidateSimpleOutline(outlineClip, "Framebuffer FLOAT32 precision breaks stroke boundary.");
    }

    private void EmitOutline(LinearColor color, StrokeSpace space)
    {
        CleanOutline(outlineOuter); CleanOutline(outlineInner);
        ValidatePixelLoop(outlineOuter, space);
        if (outlineInner.Count == 0)
        {
            polygon.AddRange(outlineOuter);
            for (int i = 1; i + 1 < polygon.Count; i++) Tri(0, i, i + 1);
        }
        else
        {
            ValidatePixelLoop(outlineInner, space);
            // Bridge the rightmost vertices. Its open segment lies strictly to the right of the
            // convex hole, and within the convex outer contour. Duplicate endpoints represent a
            // topological cut, not overlapping geometry. All tiles use only boundary vertices.
            int oi = Rightmost(outlineOuter), ii = Rightmost(outlineInner);
            for (int i = 0; i < outlineOuter.Count; i++) polygon.Add(outlineOuter[(oi + i) % outlineOuter.Count]);
            polygon.Add(outlineOuter[oi]);
            for (int i = 0; i < outlineInner.Count; i++) polygon.Add(outlineInner[(ii - i + outlineInner.Count) % outlineInner.Count]);
            polygon.Add(outlineInner[ii]);
            EarTriangulate(hole: true);
        }
        Emit(color, framebuffer: space == StrokeSpace.Framebuffer);
    }

    private static int Rightmost(List<P> loop)
    {
        int best = 0;
        for (int i = 1; i < loop.Count; i++)
            if (loop[i].X > loop[best].X || (loop[i].X == loop[best].X && loop[i].Y < loop[best].Y)) best = i;
        return best;
    }
}
