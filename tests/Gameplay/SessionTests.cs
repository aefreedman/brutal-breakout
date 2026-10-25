using Brutal.Numerics;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Presentation;
using CrushKit.Brutal.Draw2D;
using BrutalBreakout.Simulation;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public class SessionTests
{
    private const double CoordinateRoundoff = .001;
    private static readonly ArenaViewport View = new(1280, 720, 1280, 720, TestSettings.Gameplay.Arena);
    private static InputSnapshot Sample(bool press = false, bool restart = false) =>
        new() { Focused = true, KeyboardPrimary = press, KeyboardRestart = restart };

    [Fact] public void PendingEdgeSurvivesNoTickAndIsConsumedOnlyOnce()
    {
        using var s = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
        s.Advance(Sample(), 0, View);
        Assert.Equal(0, s.Advance(Sample(true), .001, View));
        Assert.Equal(BallPhase.Aiming, s.Simulation.State.Phase);
        Assert.Equal(8, s.Advance(Sample(), 1, View));
        Assert.Equal(BallPhase.Flying, s.Simulation.State.Phase);
        Assert.InRange(Math.Abs(572 - s.Simulation.State.Position.Y), 0, CoordinateRoundoff);
    }

    [Fact] public void CatchHoldsAndSeparateEdgeRelaunches()
    {
        using var s = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
        s.Advance(Sample(), 0, View);
        s.Advance(Sample(true), 1.0 / 60, View);
        for (int i = 0; i < 200 && !s.Simulation.State.CatchEligible; i++)
            s.Advance(Sample(), 1.0 / 60, View);
        Assert.True(s.Simulation.State.CatchEligible);
        var p = s.Simulation.State.Position;
        s.Advance(Sample(true), 1.0 / 60, View);
        Assert.Equal(BallPhase.Aiming, s.Simulation.State.Phase);
        for (int i = 0; i < 10; i++) s.Advance(Sample(true), 1.0 / 60, View);
        Assert.Equal(p, s.Simulation.State.Position);
        s.Advance(Sample(), 1.0 / 60, View);
        s.Advance(Sample(true), 1.0 / 60, View);
        Assert.Equal(BallPhase.Flying, s.Simulation.State.Phase);
    }

    [Fact] public void RestartCancelsPendingPrimaryAndAim()
    {
        using var s = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
        s.Advance(Sample(), 0, View);
        s.Advance(Sample(true), 0, View);
        s.Advance(Sample(true, true), 0, View);
        s.Advance(Sample(), 1.0 / 60, View);
        Assert.Equal(BallPhase.Aiming, s.Simulation.State.Phase);
        Assert.Equal(new double2(640, 612), s.Simulation.State.Position);
    }

    [Fact] public void FocusLossDropsPendingAndResumeDropsElapsedAndHeldPress()
    {
        using var s = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
        s.Advance(Sample(), 0, View);
        s.Advance(Sample(true), .001, View);
        s.Advance(default, 100, View);
        Assert.Equal(0, s.Advance(Sample(true), 100, View));
        s.Advance(Sample(true), 1.0 / 60, View);
        Assert.Equal(BallPhase.Aiming, s.Simulation.State.Phase);
        s.Advance(Sample(), 1.0 / 60, View);
        s.Advance(Sample(true), 1.0 / 60, View);
        Assert.Equal(BallPhase.Flying, s.Simulation.State.Phase);
    }

    [Fact] public void DirectionYIsConvertedAndStationaryMouseDoesNotOverride()
    {
        using var s = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
        var a = Sample() with { KeyboardDirection = new(1, 1), Cursor = new(100, 100) };
        s.Advance(a, 0, View);
        s.Advance(a, 1.0 / 60, View);
        Assert.True(s.Simulation.State.AimDirection.X > 0);
        Assert.True(s.Simulation.State.AimDirection.Y < 0);
        var aim = s.Simulation.State.AimDirection;
        s.Advance(a with { KeyboardDirection = default }, 1.0 / 60, View);
        Assert.Equal(aim, s.Simulation.State.AimDirection);
    }

    [Fact] public void ZeroFramebufferDoesNotPausePhysicsOrChangeAim()
    {
        using var s = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
        s.Advance(Sample(), 0, View);
        s.Advance(Sample(true), 1.0 / 60, View);
        var p = s.Simulation.State.Position;
        s.Advance(Sample() with { Cursor = new(100, 100) }, 1.0 / 60, new(1280, 720, 0, 0, TestSettings.Gameplay.Arena));
        Assert.InRange(Math.Abs(p.Y - 10 - s.Simulation.State.Position.Y), 0, CoordinateRoundoff);
        Assert.Equal(new double2(0, -1), s.Simulation.State.AimDirection);
    }

    [Fact] public void PointerAimsFromBallCenterNotWorldOrigin()
    {
        using var s = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
        s.Advance(Sample(), 0, View);
        s.Advance(Sample() with { Cursor = new(740, 612) }, 1.0 / 60, View);
        Assert.Equal(new double2(1, 0), s.Simulation.State.AimDirection);
        s.Advance(Sample() with { Cursor = new(800, 10) }, 1.0 / 60, new(1000, 1000, 2000, 2000, TestSettings.Gameplay.Arena));
        Assert.Equal(new double2(1, 0), s.Simulation.State.AimDirection);
    }

    [Fact] public void FlyingResumeDoesNotJump()
    {
        using var s = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
        s.Advance(Sample(), 0, View);
        s.Advance(Sample(true), 1.0 / 60, View);
        var p = s.Simulation.State.Position;
        s.Advance(Sample() with { Minimized = true }, 1, View);
        Assert.Equal(0, s.Advance(Sample(true), 10, View));
        Assert.Equal(p, s.Simulation.State.Position);
        s.Advance(Sample(true), 1.0 / 60, View);
        Assert.InRange(Math.Abs(p.Y - 10 - s.Simulation.State.Position.Y), 0, CoordinateRoundoff);
    }

    [Theory]
    [InlineData(1280, 720, 2560, 1440, 640, 360, 640, 360)]
    [InlineData(1000, 1000, 2000, 2000, 500, 500, 640, 360)]
    [InlineData(960, 540, 960, 540, 480, 270, 640, 360)]
    public void DpiAndUniformFit(int w, int h, int fw, int fh, float x, float y, double wx, double wy)
    {
        Assert.True(new ArenaViewport(w, h, fw, fh, TestSettings.Gameplay.Arena).TryWorld(new(x, y), out var p));
        Assert.Equal(wx, p.X, 5); Assert.Equal(wy, p.Y, 5);
    }

    [Theory]
    [InlineData(1000, 1000, 2000, 2000, 500, 10)]
    [InlineData(1280, 720, 0, 0, 640, 360)]
    [InlineData(0, 0, 1280, 720, 640, 360)]
    [InlineData(1280, 720, 1280, 720, float.NaN, 360)]
    public void InvalidOrLetterboxPointerIgnored(int w, int h, int fw, int fh, float x, float y)
        => Assert.False(new ArenaViewport(w, h, fw, fh, TestSettings.Gameplay.Arena).TryWorld(new(x, y), out _));

    [Theory]
    [InlineData(1280, 720)] [InlineData(960, 540)]
    public void GeometryIncludesArenaBallAndPhaseCue(int w, int h)
    {
        using var draw = new ShapeDrawList();
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        var frame = BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, 0, w, h, out var aiming);
        Assert.True(frame.Indices.Length > 0);
        Assert.True(aiming.ArenaIndices > 0 && aiming.BallIndices > 0 && aiming.AimIndices > 0);
        sim.Step(new(PrimaryPressed: true));
        BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, 0, w, h, out var flying);
        Assert.Equal(0, flying.AimIndices);
        for (int i = 0; i < 400 && !sim.State.CatchEligible; i++) sim.Step();
        Assert.True(sim.State.CatchEligible);
        Assert.True(BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, 0, w, h, out _).Indices.Length > 0);
    }
}
