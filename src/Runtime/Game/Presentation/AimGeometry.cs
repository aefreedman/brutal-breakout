using Brutal.Numerics;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;

namespace BrutalBreakout.Presentation;

/// <summary>Shared legacy indicator geometry; collision queries remain in their owning simulation.</summary>
public static class AimGeometry
{
    public static void Draw(ShapeDrawList draw, double2 position, double2 direction, double radius,
        WallContact hit, WallContact next, PresentationStyle style, LinearColor color, bool drawOpenMiss = false)
    {
        float2 Point(double2 p) => new((float)p.X, (float)p.Y);
        float width = (float)(radius * style.AimWidthRadiusRatio);
        var start = position + direction * radius;
        if (hit.HasHit)
        {
            if (hit.Distance > 2 * radius)
                draw.StrokeSegment(Point(start), Point(hit.Position - direction * radius), width, LineCap.Butt, color);
            draw.StrokeCircle(Point(hit.Position), (float)radius, (float)(radius * style.ImpactRingWidthRadiusRatio), color);
            double endDistance = radius * (1 + style.ReflectedLengthRadiusRatio);
            if (next.HasHit || (drawOpenMiss && next.Distance > 0)) endDistance = Math.Min(endDistance, next.Distance);
            if (endDistance > radius)
                draw.StrokeSegment(Point(hit.Position + hit.ReflectedDirection * radius),
                    Point(hit.Position + hit.ReflectedDirection * endDistance), width, LineCap.Butt, color);
        }
        else if (drawOpenMiss && hit.Distance > 0)
        {
            if (hit.Distance > radius) draw.StrokeSegment(Point(start), Point(hit.Position), width, LineCap.Butt, color);
        }
        else
            draw.StrokeSegment(Point(start), Point(start + direction * (radius * style.FallbackAimLengthRadiusRatio)),
                width, LineCap.Butt, color);
    }
}
