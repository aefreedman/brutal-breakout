using Brutal.Numerics;
using BrutalBreakout.Rendering.Shapes;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Presentation;

/// <summary>Unity-reference presentation and geometric feedback; no dependency on window or native APIs.</summary>
public static class BallSandbox
{
    public readonly record struct GeometryEvidence(int ArenaIndices, int BallIndices, int AimIndices,
        int BrickIndices, int CatchLineIndices, int HudIndices, int BackgroundIndices, int HeldRingIndices)
    {
        public float HudBarOuterWidth { get; init; }
        public float HudBarOuterHeight { get; init; }
        public float HudBarCenterY { get; init; }
    }

    public static ShapeFrame Build(ShapeDrawList draw, BallSimulation simulation, ColorScheme colors,
        PresentationStyle style, double renderSeconds, int width, int height, out GeometryEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(colors);
        ArgumentNullException.ThrowIfNull(style);
        double offset = style.RingOffset(renderSeconds);
        var a = simulation.Gameplay.Arena;
        var radius = (float)simulation.Gameplay.Ball.Radius;
        var v = new ArenaViewport(width, height, width, height, a);
        draw.BeginFrame(width, height);
        draw.FillVerticalGradientRect(new(0), new(width, height), colors.Background, colors.BackgroundBottom);
        int backgroundEnd = draw.IndexCount;
        draw.SetTransform(new float3x2(v.Scale, 0, 0, v.Scale, v.X, v.Y));
        float shortExtent = Math.Min(width, height);
        float t = (float)a.WallThickness;
        float left = (float)a.Left, right = (float)a.Right, top = (float)a.Top, bottom = (float)a.Bottom;
        var rules = simulation.Gameplay.Breakout;
        draw.FillRect(new(left - t, top - t), new(right - left + 2 * t, t), colors.Walls);
        if (rules is null) draw.FillRect(new(left - t, bottom), new(right - left + 2 * t, t), colors.Walls);
        draw.FillRect(new(left - t, top), new(t, bottom - top), colors.Walls);
        draw.FillRect(new(right, top), new(t, bottom - top), colors.Walls);
        int arenaEnd = draw.IndexCount;

        foreach (var brick in simulation.Bricks)
        {
            if (!brick.Alive) continue;
            var b = brick.Bounds;
            var fill = brick.Id % 3 == 0 ? colors.BrickPrimary : brick.Id % 3 == 1 ? colors.BrickSecondary : colors.BrickAccent;
            var origin = new float2((float)b.X, (float)b.Y);
            var size = new float2((float)b.Width, (float)b.Height);
            draw.FillRoundedRect(origin, size, (float)(Math.Min(size.X, size.Y) * style.BrickCornerFraction), fill);
        }
        int bricksEnd = draw.IndexCount;
        if (rules is not null)
            draw.StrokePatternedSegment(new(left, (float)simulation.CatchLineY), new(right, (float)simulation.CatchLineY),
                (float)(shortExtent * style.CatchLineWidthScreenFraction),
                StrokePattern.RelativeDashes(style.CatchLineDashLengthMultiples, style.CatchLineGapMultiples),
                colors.CatchLine, LineCap.Round, space: StrokeSpace.Framebuffer);
        int lineEnd = draw.IndexCount;

        var state = simulation.State;
        float2 Point(double2 p) => new((float)p.X, (float)p.Y);
        var center = Point(state.Position);
        bool playing = simulation.RoundPhase == RoundPhase.Playing;
        // Aim is behind both the held disc and its selection ring.
        if (playing && state.Phase == BallPhase.Aiming)
        {
            var hit = simulation.Preview();
            float aimWidth = (float)(radius * style.AimWidthRadiusRatio);
            var start = state.Position + state.AimDirection * radius;
            if (hit.HasHit)
            {
                if (hit.Distance > 2 * radius)
                    draw.StrokeSegment(Point(start), Point(hit.Position - state.AimDirection * radius), aimWidth, LineCap.Butt, colors.Aim);
                draw.StrokeCircle(Point(hit.Position), radius, (float)(radius * style.ImpactRingWidthRadiusRatio), colors.Aim);
                // Query-only separation from native cast slop; never moves the ball or creates a hit.
                const double secondaryQuerySkin = 1;
                var next = simulation.Query(hit.Position + hit.Normal * secondaryQuerySkin, hit.ReflectedDirection);
                double endDistance = radius * (1 + style.ReflectedLengthRadiusRatio);
                if (next.HasHit) endDistance = Math.Min(endDistance, next.Distance);
                if (endDistance > radius)
                    draw.StrokeSegment(Point(hit.Position + hit.ReflectedDirection * radius),
                        Point(hit.Position + hit.ReflectedDirection * endDistance), aimWidth, LineCap.Butt, colors.Aim);
            }
            else
                draw.StrokeSegment(Point(start), Point(start + state.AimDirection * (radius * style.FallbackAimLengthRadiusRatio)),
                    aimWidth, LineCap.Butt, colors.Aim);
        }
        int aimEnd = draw.IndexCount;
        if (playing) draw.FillDisc(center, radius, colors.Ball);
        int ballEnd = draw.IndexCount;
        if (playing && state.Phase == BallPhase.Aiming)
            draw.StrokePatternedCircle(center, (float)(shortExtent * style.HeldRingRadiusScreenFraction / v.Scale),
                (float)(shortExtent * style.HeldRingWidthScreenFraction),
                StrokePattern.FixedDashes(style.HeldRingDashCount, style.HeldRingGapFraction, offset),
                colors.HeldRing, LineCap.Round, space: StrokeSpace.Framebuffer);
        int heldEnd = draw.IndexCount;
        int barOutlineEnd = heldEnd;
        if (rules is not null)
        {
            // Compact remaining-ball and brick progress bars, independent of large configured counts.
            var hud = style.Hud;
            float bar = (float)Math.Min(hud.BarMaxWidth, (right - left) * hud.BarMaxArenaWidthFraction);
            float y = (float)Math.Max(hud.BarMinCenterY, (top - t) * hud.BarTopMarginFraction);
            float barHeight = (float)hud.BarHeight, outline = (float)hud.BarOutlineWidth;
            draw.StrokeRect(new(left, y - barHeight / 2), new(bar, barHeight), outline, colors.Hud);
            barOutlineEnd = draw.IndexCount;
            draw.FillRect(new(left, y - barHeight / 2), new(bar * simulation.BallsRemaining / rules.StartingBalls, barHeight), colors.Hud);
            draw.StrokeRect(new(right - bar, y - barHeight / 2), new(bar, barHeight), outline, colors.Hud);
            draw.FillRect(new(right - bar, y - barHeight / 2), new(bar * simulation.RemainingBricks / rules.Bricks.Count, barHeight), colors.Hud);
            if (!playing)
            {
                var status = simulation.RoundPhase == RoundPhase.Won ? colors.Win : colors.Loss;
                float x = (left + right) / 2, cy = (float)simulation.CatchLineY;
                float size = (float)Math.Min(hud.TerminalMaxSize, Math.Min(right - left, bottom - top) * hud.TerminalMaxArenaSizeFraction);
                if (simulation.RoundPhase == RoundPhase.Won)
                    draw.StrokePolyline([new(x - size, cy), new(x - size / 4, cy + size / 2), new(x + size, cy - size)], (float)hud.TerminalMarkerWidth, status);
                else
                {
                    draw.StrokePolyline([new(x - size, cy - size), new(x + size, cy + size)], (float)hud.TerminalMarkerWidth, status);
                    draw.StrokePolyline([new(x - size, cy + size), new(x + size, cy - size)], (float)hud.TerminalMarkerWidth, status);
                }
            }
        }
        int end = draw.IndexCount;
        evidence = new(arenaEnd - backgroundEnd, ballEnd - aimEnd, aimEnd - lineEnd, bricksEnd - arenaEnd,
            lineEnd - bricksEnd, end - heldEnd, backgroundEnd, heldEnd - ballEnd);
        var frame = draw.Finish();
        if (barOutlineEnd > heldEnd)
        {
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            foreach (uint index in frame.Indices.Span.Slice(heldEnd, barOutlineEnd - heldEnd))
            {
                var p = frame.Vertices.Span[(int)index].Position;
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
            evidence = evidence with { HudBarOuterWidth = maxX - minX, HudBarOuterHeight = maxY - minY, HudBarCenterY = (minY + maxY) / 2 };
        }
        return frame;
    }
}
