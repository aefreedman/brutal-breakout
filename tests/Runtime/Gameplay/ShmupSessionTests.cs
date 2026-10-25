using Brutal.Numerics;
using BrutalBreakout.Configuration;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;
using Xunit;

public sealed class ShmupSessionTests
{
    private static readonly PhysicsSettings Physics = TestSettings.Baseline;
    private static ShmupSettings Settings() => ShmupConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "shmup.json")).Settings;
    private static InputSnapshot Sample(bool fire = false, bool restart = false) => new()
    {
        Focused = true, PadConnected = true, PadRightTrigger = fire ? 1 : 0,
        PadRightStick = new(0, 1), KeyboardRestart = restart, Cursor = new(double.NaN, double.NaN)
    };
    private static ShmupViewport View(ShmupSettings settings) => new(1280, 720, 1280, 720, settings.Arena);
    private static ShmupSession Session(ShmupSettings settings) => new(Physics, settings, TestSettings.Input, new(2, 64, 2));

    [Fact]
    public void ReleasedFireOnZeroTickFrameDoesNotBecomeLatchedShot()
    {
        var settings = Settings(); using var session = Session(settings); var view = View(settings);
        session.Advance(Sample(), 0, view);
        session.Advance(Sample(true), 0, view);
        session.Advance(Sample(), 0, view);
        session.Advance(Sample(), Physics.StepSeconds, view);
        Assert.Empty(session.Simulation.Snapshot.Balls.ToArray());
    }
    [Fact]
    public void FocusRegainRequiresFireReleaseAndDropsPausedTime()
    {
        var settings = Settings(); using var session = Session(settings); var view = View(settings);
        session.Advance(Sample(), 0, view);
        session.Advance(Sample(true), Physics.StepSeconds, view);
        Assert.NotEmpty(session.Simulation.Snapshot.Balls.ToArray());
        session.Advance(default, 100, view);
        long tick = session.Simulation.Snapshot.Tick;
        Assert.Equal(0, session.Advance(Sample(true), 100, view));
        Assert.Equal(tick, session.Simulation.Snapshot.Tick);
        session.Simulation.Restart();
        session.Advance(Sample(true), Physics.StepSeconds, view);
        Assert.Empty(session.Simulation.Snapshot.Balls.ToArray());
        session.Advance(Sample(), Physics.StepSeconds, view);
        session.Advance(Sample(true), Physics.StepSeconds, view);
        Assert.NotEmpty(session.Simulation.Snapshot.Balls.ToArray());
    }
    [Fact]
    public void RestartClearsPendingFireAndRequiresReleaseAfterCatchup()
    {
        var settings = Settings(); using var session = Session(settings); var view = View(settings);
        session.Advance(Sample(), 0, view);
        session.Advance(Sample(true), 0, view);
        session.Advance(Sample(true, true), 0, view);
        session.Advance(Sample(true), 1, view);
        Assert.Empty(session.Simulation.Snapshot.Balls.ToArray());
        session.Advance(Sample(true), 1, view);
        Assert.Empty(session.Simulation.Snapshot.Balls.ToArray());
        session.Advance(Sample(), Physics.StepSeconds, view);
        session.Advance(Sample(true), Physics.StepSeconds, view);
        Assert.NotEmpty(session.Simulation.Snapshot.Balls.ToArray());
    }
    [Fact]
    public void SwitchingDisposesPreviousWorldAndCreatesFreshRunWithSameSettings()
    {
        var settings = Settings();
        using var host = new GameplaySessionHost(Physics, TestSettings.Gameplay, settings, TestSettings.Input, new(2, 64, 2), GameplayMode.Shmup);
        var old = host.Shmup!;
        Assert.Same(settings, old.Settings); Assert.Same(settings, old.Simulation.Settings);
        old.Simulation.Widen(settings.Arena.Width);
        old.Advance(Sample(), 0, View(settings));
        old.Advance(Sample(true), Physics.StepSeconds, View(settings));
        var retained = old.Simulation.Snapshot;
        var retainedBalls = retained.Balls.ToArray();
        host.Switch(GameplayMode.Breakout);
        Assert.Null(host.Shmup); Assert.NotNull(host.Breakout);
        Assert.Throws<ObjectDisposedException>(() => old.Advance(Sample(), 0, View(settings)));
        host.Switch(GameplayMode.Shmup);
        Assert.Null(host.Breakout);
        var fresh = host.Shmup!.Simulation.Snapshot;
        Assert.Equal(settings.Arena.InitialPlayAreaWidth, fresh.Width);
        Assert.Equal(settings.Avatar.Health, fresh.PlayerHealth);
        Assert.Equal(0, fresh.Tick);
        Assert.Empty(fresh.Balls.ToArray()); Assert.Empty(fresh.Bricks.ToArray());
        Assert.Equal(retainedBalls, retained.Balls.ToArray());
        Assert.Same(settings, host.Shmup.Simulation.Settings);
    }
    [Fact]
    public void IndependentMovementAndAimWithNeutralRetention()
    {
        var settings = Settings(); using var session = Session(settings); var view = View(settings);
        session.Advance(Sample(), 0, view);
        var start = session.Simulation.Snapshot.PlayerPosition;
        session.Advance(Sample() with { PadStick = new(1, 0), PadRightStick = new(0, 1) }, Physics.StepSeconds, view);
        var state = session.Simulation.Snapshot;
        Assert.True(state.PlayerPosition.X > start.X);
        Assert.Equal(new double2(0, -1), state.AimDirection);
        session.Advance(Sample() with { PadRightStick = default, Cursor = new(0, 0) }, Physics.StepSeconds, view);
        Assert.Equal(new double2(0, -1), session.Simulation.Snapshot.AimDirection);
    }
    [Fact]
    public void ViewportUsesOwnCanvasAndMapsDpiAndLetterbox()
    {
        var settings = Settings();
        var view = new ShmupViewport(640, 480, 1280, 960, settings.Arena);
        Assert.True(view.TryWorld(new(320, 240), out var center));
        Assert.Equal(new double2(640, 360), center);
        Assert.False(view.TryWorld(new(320, 0), out _));
        Assert.False(view.TryWorld(new(double.NaN, 0), out _));
        using var session = Session(settings);
        Assert.Throws<ArgumentException>(() => session.Advance(Sample(), 0,
            new(1280, 720, 1280, 720, new(1280, 720, 432))));
    }
    [Fact]
    public void WidenedGeometryAndHudBuildWithoutGraphicsAndRetainedSnapshotStaysStable()
    {
        var settings = Settings(); using var session = Session(settings); using var draw = new ShapeDrawList();
        var retained = session.Simulation.Snapshot;
        var first = ShmupPresentation.Build(draw, retained, settings, TestSettings.Colors, TestSettings.Style, 1280, 720);
        Assert.NotEmpty(first.Indices.ToArray());
        session.Simulation.Widen(settings.Arena.Width);
        var widened = session.Simulation.Snapshot;
        Assert.Equal(settings.Arena.InitialPlayAreaWidth, retained.Width);
        Assert.Equal(settings.Arena.Width, widened.Width);
        var second = ShmupPresentation.Build(draw, widened, settings, TestSettings.Colors, TestSettings.Style, 960, 540);
        Assert.NotEmpty(second.Indices.ToArray());
        var overlay = ShmupPresentation.Overlay(widened, TestSettings.Colors.Hud);
        Assert.Contains("HP: 100", overlay.Labels[0].Text);
        Assert.Contains("RT: fire", overlay.Labels[2].Text);
    }
}
