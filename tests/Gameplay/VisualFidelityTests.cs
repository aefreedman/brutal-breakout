using Brutal.Numerics;
using BrutalBreakout.Presentation;
using CrushKit.Brutal.Draw2D;
using BrutalBreakout.Simulation;
using Xunit;

public class VisualFidelityTests
{
    private static ShapeVertex[] Category(ShapeFrame frame, int start, int count) =>
        frame.Indices.Slice(start, count).ToArray().Select(i => frame.Vertices.Span[(int)i]).ToArray();

    [Theory]
    [InlineData(1280, 720)] [InlineData(960, 540)] [InlineData(2000, 1800)] [InlineData(720, 1280)]
    public void HeldRingIsScreenRelativeRoundedGappedAndAnimatesWithoutSimulation(int w, int h)
    {
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        using var draw = new ShapeDrawList();
        var state = sim.State;
        var frame = BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, 0, w, h, out var e);
        int start = e.BackgroundIndices + e.ArenaIndices + e.BrickIndices + e.CatchLineIndices + e.AimIndices + e.BallIndices;
        var ring = Category(frame, start, e.HeldRingIndices);
        Assert.NotEmpty(ring);
        var viewport = new ArenaViewport(w / 2, h / 2, w, h, sim.Gameplay.Arena);
        float cx = (float)state.Position.X * viewport.Scale + viewport.X;
        float cy = (float)state.Position.Y * viewport.Scale + viewport.Y;
        double r = Math.Min(w, h) * .2, halfWidth = Math.Min(w, h) * .01;
        var distances = ring.Select(v => Math.Sqrt(Math.Pow(v.Position.X - cx, 2) + Math.Pow(v.Position.Y - cy, 2))).ToArray();
        Assert.InRange(distances.Min(), r - halfWidth - .6, r - halfWidth + .6);
        Assert.InRange(distances.Max(), r + halfWidth - .6, r + halfWidth + .6);
        var color = TestSettings.Colors.HeldRing;
        Assert.All(ring, v => Assert.Equal(new float4(color.R, color.G, color.B, 1), v.Color));
        // A gap and a dash at the centerline; rounded caps add vertices beyond the butt endpoints.
        bool Covered(double angle)
        {
            var p = new float2(cx + (float)(r * Math.Cos(angle)), cy + (float)(r * Math.Sin(angle)));
            static double Cross(float2 a, float2 b, float2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            for (int i = 0; i < ring.Length; i += 3)
            {
                double a = Cross(ring[i].Position, ring[i + 1].Position, p), b = Cross(ring[i + 1].Position, ring[i + 2].Position, p), c = Cross(ring[i + 2].Position, ring[i].Position, p);
                if ((a >= 0 && b >= 0 && c >= 0) || (a <= 0 && b <= 0 && c <= 0)) return true;
            }
            return false;
        }
        Assert.True(Covered(Math.Tau / 16 * .25));
        Assert.False(Covered(Math.Tau / 16 * .75));
        Assert.True(Covered(-.25 * halfWidth / r)); // round cap protrudes before phase anchor
        var animated = BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, .125, w, h, out var animatedEvidence);
        var moved = Category(animated, start, animatedEvidence.HeldRingIndices);
        Assert.False(ring.Select(v => v.Position).SequenceEqual(moved.Select(v => v.Position)));
        Assert.Equal(state.Position, sim.State.Position);
        Assert.Equal(state.Phase, sim.State.Phase);
        Assert.Equal(state.Velocity, sim.State.Velocity);
        Assert.Equal(6, e.BackgroundIndices);
        Assert.Equal(new float2(0), frame.Vertices.Span[0].Position);
    }

    [Theory]
    [InlineData(1280, 720)] [InlineData(960, 540)] [InlineData(2000, 1800)] [InlineData(720, 1280)]
    public void RealHeldRingProducerSurvivesPreviouslyCrashingAnimationTimeAndResize(int w, int h)
    {
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        using var draw = new ShapeDrawList();
        var before = sim.State;
        var failedTimes = new[] { .058823029411764705, .10588065702741996, .04706206663840547 };
        foreach (double time in failedTimes.SelectMany(t => new[] { Math.BitDecrement(t), t, Math.BitIncrement(t), t + .5, t + 65536 }))
        {
            var frame = BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, time, w, h, out var e);
            Assert.True(e.HeldRingIndices > 0);
            Assert.Equal(frame.Indices.Length, e.ArenaIndices + e.BallIndices + e.AimIndices + e.BrickIndices +
                e.CatchLineIndices + e.HudIndices + e.BackgroundIndices + e.HeldRingIndices);
            Assert.All(frame.Indices.ToArray(), id => Assert.InRange(id, 0u, (uint)frame.Vertices.Length - 1));
            Assert.Equal(before.Position, sim.State.Position);
            Assert.Equal(before.Phase, sim.State.Phase);
            Assert.Equal(before.Velocity, sim.State.Velocity);
        }
    }

    [Fact]
    public void CaughtBallRendersCriticalAndOrdinaryPhasesWithoutMovingSimulation()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        using var draw = new ShapeDrawList();
        sim.Step(new(PrimaryPressed: true));
        for (int i = 0; i < 2000 && !sim.State.CatchEligible; i++) sim.Step();
        Assert.True(sim.State.CatchEligible);
        sim.Step(new(PrimaryPressed: true));
        Assert.Equal(BallPhase.Aiming, sim.State.Phase);
        var held = sim.State;
        Assert.NotEqual(sim.Gameplay.Ball.Spawn, held.Position);
        foreach (double time in new[] { .10588065702741996, .04706206663840547, 10.1234567, 65536.10588065703 })
        {
            var frame = BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, time, 1280, 720, out var e);
            Assert.True(e.HeldRingIndices > 0);
            Assert.Equal(held.Position, sim.State.Position); Assert.Equal(held.Velocity, sim.State.Velocity);
            Assert.Equal(held.Phase, sim.State.Phase);
        }
    }

    [Fact]
    public void HeldRingFailureRetainsInvariantReproductionContextAndInnerCause()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        using var draw = new ShapeDrawList();
        var s = TestSettings.Style;
        var unsupported = new PresentationStyle(10000, s.HeldRingWidthScreenFraction, s.HeldRingDashCount,
            s.HeldRingGapFraction, s.HeldRingCyclesPerSecond, s.AimWidthRadiusRatio, s.ImpactRingWidthRadiusRatio,
            s.ReflectedLengthRadiusRatio, s.FallbackAimLengthRadiusRatio, s.BrickCornerFraction,
            s.CatchLineWidthScreenFraction, s.CatchLineDashLengthMultiples, s.CatchLineGapMultiples, s.Hud);
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            var error = Assert.Throws<ArgumentException>(() =>
                BallSandbox.Build(draw, sim, TestSettings.Colors, unsupported, .10588065702741996, 1280, 720, out _));
            Assert.Contains("renderSeconds=0.10588065702741996", error.Message);
            Assert.Contains("phase=0.21176131405483992", error.Message);
            Assert.Contains("framebuffer=1280x720; center=640,612; scale=1", error.Message);
            Assert.Contains("width=14.4; dashes=16; gap=0.5; cyclesPerSecond=2", error.Message);
            Assert.Contains("cap=Round; space=Framebuffer", error.Message);
            Assert.IsType<ArgumentException>(error.InnerException);
            Assert.Contains("Curve precision limit", error.InnerException!.Message);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void FlyingBallIsOnlyConfiguredDiscWithNoHaloOrReadyRecolor()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        sim.Step(new(PrimaryPressed: true));
        for (int i = 0; i < 500 && !sim.State.CatchEligible; i++) sim.Step();
        Assert.True(sim.State.CatchEligible);
        using var draw = new ShapeDrawList();
        var frame = BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, 1, 1280, 720, out var e);
        Assert.Equal(0, e.HeldRingIndices); Assert.Equal(0, e.AimIndices);
        var ball = Category(frame, e.BackgroundIndices + e.ArenaIndices + e.BrickIndices + e.CatchLineIndices, e.BallIndices);
        var color = TestSettings.Colors.Ball;
        Assert.All(ball, v =>
        {
            Assert.Equal(new float4(color.R, color.G, color.B, 1), v.Color);
            Assert.InRange(Math.Sqrt(Math.Pow(v.Position.X - sim.State.Position.X, 2) + Math.Pow(v.Position.Y - sim.State.Position.Y, 2)), 0, sim.Gameplay.Ball.Radius + .001);
        });
        using var disc = new ShapeDrawList(); disc.BeginFrame(1280, 720);
        disc.FillDisc(new((float)sim.State.Position.X, (float)sim.State.Position.Y), (float)sim.Gameplay.Ball.Radius, color);
        Assert.Equal(disc.Finish().Indices.Length, e.BallIndices);
    }

    [Fact]
    public void NativeAimUsesSurfaceClearancesRadiusRatiosAndShortReflectedLeg()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        var hit = sim.Preview(); Assert.True(hit.HasHit);
        using var draw = new ShapeDrawList();
        var frame = BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, 0, 1280, 720, out var e);
        var aim = Category(frame, e.BackgroundIndices + e.ArenaIndices + e.BrickIndices + e.CatchLineIndices, e.AimIndices);
        float r = (float)sim.Gameplay.Ball.Radius;
        // Initial aim is straight up. First six indices are the butt shaft.
        var shaft = aim.Take(6).ToArray();
        Assert.Equal((float)sim.State.Position.Y - r, shaft.Max(v => v.Position.Y), 3);
        Assert.Equal((float)hit.Position.Y + r, shaft.Min(v => v.Position.Y), 3);
        Assert.Equal(r / 4, shaft.Max(v => v.Position.X) - shaft.Min(v => v.Position.X), 3);
        // Final six indices are the fixed reflected leg, clear of the contact center.
        var reflection = aim.TakeLast(6).ToArray();
        Assert.Equal((float)hit.Position.Y + r, reflection.Min(v => v.Position.Y), 3);
        Assert.Equal(2 * r, reflection.Max(v => v.Position.Y) - reflection.Min(v => v.Position.Y), 3);
        var ring = aim.Skip(6).SkipLast(6).ToArray();
        var radii = ring.Select(v => Math.Sqrt(Math.Pow(v.Position.X - hit.Position.X, 2) + Math.Pow(v.Position.Y - hit.Position.Y, 2))).ToArray();
        Assert.InRange(radii.Min(), r * .95 - .1, r * .95 + .1);
        Assert.InRange(radii.Max(), r * 1.05 - .1, r * 1.05 + .1);
    }

    [Theory]
    [InlineData(1280, 720)] [InlineData(960, 540)] [InlineData(1000, 1800)]
    public void CatchLineIsScreenRelativeRoundedDashesAndBricksAreRoundedFills(int w, int h)
    {
        var game = new GameplaySettings(TestSettings.Gameplay.Arena, TestSettings.Gameplay.Ball,
            new(3, 1.0 / 3, 12, [new(450, 200, 100, 30)]));
        using var sim = new BallSimulation(TestSettings.Baseline, game, new(2, 64, 2));
        using var draw = new ShapeDrawList();
        var frame = BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, 0, w, h, out var e);
        var line = Category(frame, e.BackgroundIndices + e.ArenaIndices + e.BrickIndices, e.CatchLineIndices);
        float width = Math.Min(w, h) * .01f;
        Assert.Equal(width, line.Max(v => v.Position.Y) - line.Min(v => v.Position.Y), 3);
        var color = TestSettings.Colors.CatchLine;
        Assert.All(line, v => Assert.Equal(new float4(color.R, color.G, color.B, 1), v.Color));
        var viewport = new ArenaViewport(w / 2, h / 2, w, h, game.Arena);
        using var expected = new ShapeDrawList();
        expected.BeginFrame(w, h, new(viewport.Scale, 0, 0, viewport.Scale, viewport.X, viewport.Y));
        expected.FillRoundedRect(new(450, 200), new(100, 30), 3, TestSettings.Colors.BrickPrimary);
        Assert.Equal(expected.Finish().Indices.Length, e.BrickIndices); // no added border/keyline
        // First dash includes a round cap extending half a width beyond the source endpoint.
        Assert.InRange(line.Min(v => v.Position.X), (float)game.Arena.Left * viewport.Scale + viewport.X - width / 2 - .3f, (float)game.Arena.Left * viewport.Scale + viewport.X - width / 2 + .3f);
    }

    [Fact]
    public void OpenBottomNoHitOnlyDrawsBoundedPlainDirectionSegment()
    {
        var game = new GameplaySettings(TestSettings.Gameplay.Arena, new(12, 600, 640, 612, 0, 1), new(3, 1.0 / 3, 12, [new(450, 200, 100, 30)]));
        using var sim = new BallSimulation(TestSettings.Baseline, game, new(2, 64, 2));
        Assert.False(sim.Preview().HasHit);
        using var draw = new ShapeDrawList();
        var frame = BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, 0, 1280, 720, out var e);
        Assert.Equal(6, e.AimIndices); // shaft only, no fabricated ring/reflection/arrowhead
        var aim = Category(frame, e.BackgroundIndices + e.ArenaIndices + e.BrickIndices + e.CatchLineIndices, e.AimIndices);
        Assert.Equal(624, aim.Min(v => v.Position.Y)); // outgoing ball surface
        Assert.Equal(672, aim.Max(v => v.Position.Y)); // four-radius bounded fallback
    }

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(-1)]
    public void AnimationTimeMustBeExplicitFiniteAndNonnegative(double seconds)
    {
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        using var draw = new ShapeDrawList();
        Assert.Throws<ArgumentOutOfRangeException>(() => BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, seconds, 1280, 720, out _));
    }

    [Fact]
    public void LongFiniteAnimationTimeDoesNotOverflow()
    {
        Assert.InRange(TestSettings.Style.RingOffset(double.MaxValue), 0, 1);
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        using var draw = new ShapeDrawList();
        Assert.True(BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, double.MaxValue, 1280, 720, out _).Indices.Length > 0);
    }
}
