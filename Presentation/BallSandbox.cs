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
        var viewport = new ArenaViewport(width, height, width, height);
        draw.BeginFrame(width, height, new float3x2(viewport.Scale, 0, 0, viewport.Scale, viewport.X, viewport.Y));
        var wall = LinearColor.FromSrgb(.25f, .38f, .48f);
        var gold = LinearColor.FromSrgb(1, .75f, .25f);
        var cyan = LinearColor.FromSrgb(.2f, .9f, .85f);
        draw.FillRect(new(32, 32), new(1216, 16), wall);
        draw.FillRect(new(32, 672), new(1216, 16), wall);
        draw.FillRect(new(32, 48), new(16, 624), wall);
        draw.FillRect(new(1232, 48), new(16, 624), wall);
        int arenaIndices = draw.Finish().Indices.Length;
        // Finish is observational for these counts; BeginFrame would discard geometry, so build
        // the remaining primitives in a single pass below instead of reopening a finished list.
        return BuildAll(draw, simulation, width, height, viewport, wall, gold, cyan, arenaIndices, out evidence);
    }

    private static ShapeFrame BuildAll(ShapeDrawList draw, BallSimulation simulation, int width, int height,
        ArenaViewport v, LinearColor wall, LinearColor gold, LinearColor cyan, int arenaIndices,
        out GeometryEvidence evidence)
    {
        draw.BeginFrame(width, height, new float3x2(v.Scale, 0, 0, v.Scale, v.X, v.Y));
        draw.FillRect(new(32, 32), new(1216, 16), wall);
        draw.FillRect(new(32, 672), new(1216, 16), wall);
        draw.FillRect(new(32, 48), new(16, 624), wall);
        draw.FillRect(new(1232, 48), new(16, 624), wall);
        var state = simulation.State;
        float2 Point(double2 p) => new((float)p.X, (float)p.Y);
        var center = Point(state.Position);
        draw.FillDisc(center, (float)Arena.Radius, state.CatchEligible ? cyan : gold);
        draw.StrokeCircle(center, 19, 3, state.Phase == BallPhase.Aiming ? gold : state.CatchEligible ? cyan : wall);
        if (state.Phase == BallPhase.Aiming)
        {
            var hit = simulation.Preview();
            draw.StrokePolyline([center, Point(hit.Position)], 2, gold);
            draw.StrokeCircle(Point(hit.Position), (float)Arena.Radius, 2, gold);
            draw.StrokePolyline([Point(hit.Position), Point(hit.Position + hit.ReflectedDirection * 72)], 2, cyan);
        }
        var frame = draw.Finish();
        // Ball mesh is measured separately so smoke evidence cannot mistake arena triangles for a ball.
        using var probe = new ShapeDrawList();
        probe.BeginFrame(width, height);
        probe.FillDisc(center, (float)Arena.Radius * v.Scale, gold);
        int ball = probe.Finish().Indices.Length;
        evidence = new(arenaIndices, ball, state.Phase == BallPhase.Aiming ? frame.Indices.Length - arenaIndices - ball : 0);
        return frame;
    }
}
