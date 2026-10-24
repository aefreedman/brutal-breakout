using Brutal.Numerics;
using BrutalBreakout.Rendering.Shapes;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Presentation;

/// <summary>Flat Unity-reference palette and geometric feedback; no dependency on window or native APIs.</summary>
public static class BallSandbox
{
    public readonly record struct GeometryEvidence(int ArenaIndices, int BallIndices, int AimIndices,
        int BrickIndices, int CatchLineIndices, int HudIndices);

    public static ShapeFrame Build(ShapeDrawList draw, BallSimulation simulation, ColorScheme colors,
        int width, int height, out GeometryEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var a = simulation.Gameplay.Arena;
        var radius = (float)simulation.Gameplay.Ball.Radius;
        var v = new ArenaViewport(width, height, width, height, a);
        draw.BeginFrame(width, height, new float3x2(v.Scale, 0, 0, v.Scale, v.X, v.Y));
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
            draw.FillRect(origin, size, fill);
            // Inset keyline: keep all visible brick geometry inside its native rectangle.
            float stroke = MathF.Min(2, MathF.Min(size.X, size.Y) / 4);
            draw.StrokeRect(origin + new float2(stroke / 2), size - new float2(stroke), stroke, colors.Walls);
        }
        int bricksEnd = draw.IndexCount;
        if (rules is not null)
            draw.StrokePolyline([new(left, (float)simulation.CatchLineY), new(right, (float)simulation.CatchLineY)], 2, colors.CatchLine);
        int lineEnd = draw.IndexCount;

        var state = simulation.State;
        float2 Point(double2 p) => new((float)p.X, (float)p.Y);
        var center = Point(state.Position);
        bool playing = simulation.RoundPhase == RoundPhase.Playing;
        if (playing)
        {
            var ball = state.CatchEligible ? colors.CatchReady : colors.Ball;
            draw.FillDisc(center, radius, ball);
            draw.StrokeCircle(center, radius + (state.Phase == BallPhase.Aiming ? 9 : 6), 3, ball);
        }
        int ballEnd = draw.IndexCount;
        if (playing && state.Phase == BallPhase.Aiming)
        {
            var hit = simulation.Preview();
            if (hit.HasHit)
            {
                draw.StrokePolyline([center, Point(hit.Position)], 2, colors.Aim);
                draw.StrokeCircle(Point(hit.Position), radius, 2, colors.Aim);
                // Query-only separation from native cast slop; never moves the ball.
                const double secondaryQuerySkin = 1;
                var next = simulation.Query(hit.Position + hit.Normal * secondaryQuerySkin, hit.ReflectedDirection);
                if (next.HasHit)
                {
                    double reflectedLength = Math.Min(72, next.Distance);
                    draw.StrokePolyline([Point(hit.Position), Point(hit.Position + hit.ReflectedDirection * reflectedLength)], 2, colors.Aim);
                }
            }
            else draw.StrokePolyline([center, Point(state.Position + state.AimDirection * 48)], 2, colors.Aim);
        }
        int aimEnd = draw.IndexCount;
        if (rules is not null)
        {
            // Compact remaining-ball and brick progress bars, independent of large configured counts.
            float bar = MathF.Min(120, (right - left) / 4), y = MathF.Max(4, (top - t) / 2);
            draw.StrokeRect(new(left, y - 3), new(bar, 6), 1, colors.Hud);
            draw.FillRect(new(left, y - 3), new(bar * simulation.BallsRemaining / rules.StartingBalls, 6), colors.Hud);
            draw.StrokeRect(new(right - bar, y - 3), new(bar, 6), 1, colors.Hud);
            draw.FillRect(new(right - bar, y - 3), new(bar * simulation.RemainingBricks / rules.Bricks.Count, 6), colors.Hud);
            if (!playing)
            {
                var status = simulation.RoundPhase == RoundPhase.Won ? colors.Win : colors.Loss;
                float x = (left + right) / 2, cy = (float)simulation.CatchLineY;
                float size = MathF.Min(28, MathF.Min(right - left, bottom - top) / 8);
                if (simulation.RoundPhase == RoundPhase.Won)
                    draw.StrokePolyline([new(x - size, cy), new(x - size / 4, cy + size / 2), new(x + size, cy - size)], 4, status);
                else
                {
                    draw.StrokePolyline([new(x - size, cy - size), new(x + size, cy + size)], 4, status);
                    draw.StrokePolyline([new(x - size, cy + size), new(x + size, cy - size)], 4, status);
                }
            }
        }
        int end = draw.IndexCount;
        evidence = new(arenaEnd, ballEnd - lineEnd, aimEnd - ballEnd, bricksEnd - arenaEnd,
            lineEnd - bricksEnd, end - aimEnd);
        return draw.Finish();
    }
}
