using Brutal.Numerics;
using BrutalBreakout.Rendering.Shapes;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Presentation;

public static class BallSandbox
{
    public readonly record struct GeometryEvidence(int ArenaIndices, int BallIndices, int AimIndices);

    public static ShapeFrame Build(ShapeDrawList draw, BallSimulation simulation, int width, int height,
        out GeometryEvidence evidence)
    {
        var v = new ArenaViewport(width, height, width, height);
        draw.BeginFrame(width, height, new float3x2(v.Scale, 0, 0, v.Scale, v.X, v.Y));
        var wall = LinearColor.FromSrgb(.25f, .38f, .48f);
        var gold = LinearColor.FromSrgb(1, .75f, .25f);
        var cyan = LinearColor.FromSrgb(.2f, .9f, .85f);
        var preview = LinearColor.FromSrgb(.9f, .94f, 1);
        draw.FillRect(new(32, 32), new(1216, 16), wall);
        draw.FillRect(new(32, 672), new(1216, 16), wall);
        draw.FillRect(new(32, 48), new(16, 624), wall);
        draw.FillRect(new(1232, 48), new(16, 624), wall);
        var state = simulation.State;
        float2 Point(double2 p) => new((float)p.X, (float)p.Y);
        var center = Point(state.Position);
        var ball = state.CatchEligible ? cyan : gold;
        draw.FillDisc(center, (float)Arena.Radius, ball);
        // Gold: aiming or catch-locked flight. Cyan: a wall has unlocked catching.
        draw.StrokeCircle(center, state.Phase == BallPhase.Aiming ? 21 : 18, 3, ball);
        if (state.Phase == BallPhase.Aiming)
        {
            var hit = simulation.Preview();
            if (hit.HasHit)
            {
                draw.StrokePolyline([center, Point(hit.Position)], 2, preview);
                draw.StrokeCircle(Point(hit.Position), (float)Arena.Radius, 2, preview);
                // Query-only separation from native cast slop; never moves the ball or claims an exact second impact.
                const double secondaryQuerySkin = 1;
                var next = simulation.Query(hit.Position + hit.Normal * secondaryQuerySkin, hit.ReflectedDirection);
                if (next.HasHit)
                {
                    double reflectedLength = Math.Min(72, next.Distance);
                    draw.StrokePolyline([Point(hit.Position), Point(hit.Position + hit.ReflectedDirection * reflectedLength)], 2, preview);
                }
            }
            else draw.StrokePolyline([center, Point(state.Position + state.AimDirection * 48)], 2, preview);
        }
        var frame = draw.Finish();
        // Count actual submitted indices by the three distinct, opaque category colors.
        // No probe geometry, guessed tessellation counts, or renderer ABI changes.
        int arenaIndices = 0, ballIndices = 0, aimIndices = 0;
        foreach (uint index in frame.Indices.Span)
        {
            var color = frame.Vertices.Span[(int)index].Color;
            if (color.X == wall.R && color.Y == wall.G && color.Z == wall.B) arenaIndices++;
            else if (color.X == preview.R && color.Y == preview.G && color.Z == preview.B) aimIndices++;
            else ballIndices++;
        }
        evidence = new(arenaIndices, ballIndices, aimIndices);
        return frame;
    }
}
