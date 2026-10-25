using BrutalBreakout.Configuration;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Presentation;
using Xunit;

public sealed class HostPauseTests
{
    private static InputSnapshot Sample => new() { Focused = true, PadConnected = true, Cursor = new(double.NaN, double.NaN) };
    private static GameplaySessionHost Host(GameplayMode mode) => new(TestSettings.Baseline, TestSettings.Gameplay,
        ShmupConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "shmup.json")).Settings,
        TestSettings.Input, new(2, 64, 2), mode,
        BallTrailConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "trails.json")).Settings);
    private static int Advance(GameplaySessionHost host, InputSnapshot sample, double seconds) => host.Shmup is { } shmup
        ? shmup.Advance(sample, seconds, new(1280, 720, 1280, 720, shmup.Settings.Arena))
        : host.Breakout!.Advance(sample, seconds, new(1280, 720, 1280, 720, host.Breakout.Simulation.Gameplay.Arena));
    [Theory]
    [InlineData(GameplayMode.Breakout)] [InlineData(GameplayMode.Shmup)]
    public void PauseFreezesRunDropsDebtAndRequiresRelease(GameplayMode mode)
    {
        using var host = Host(mode);
        host.UpdateControls(Sample);
        Advance(host, Sample, 0);
        var held = Sample with { KeyboardPrimary = true, PadRightTrigger = 1 };
        Advance(host, held, 0); // pending action without a tick
        host.UpdateControls(held with { PadPause = true });
        Assert.True(host.Paused);
        var breakout = host.Breakout?.Simulation.State;
        var snapshot = host.Shmup?.Simulation.Snapshot;
        int trails = host.Shmup?.Trails?.PointCount ?? 0;
        for (int i = 0; i < 5; i++) Assert.Equal(0, Advance(host, held, 100));
        Assert.Equal(breakout, host.Breakout?.Simulation.State);
        Assert.Same(snapshot, host.Shmup?.Simulation.Snapshot);
        Assert.Equal(trails, host.Shmup?.Trails?.PointCount ?? 0);
        // Loss/regain of focus never resumes manual pause.
        host.UpdateControls(default); host.UpdateControls(held with { PadPause = true });
        Assert.True(host.Paused);
        host.UpdateControls(held);
        host.UpdateControls(held with { PadPause = true });
        Assert.False(host.Paused);
        Assert.Equal(0, Advance(host, held, 100));
        Assert.Equal(1, Advance(host, held, TestSettings.Baseline.StepSeconds));
        if (host.Shmup is { } shmup) Assert.Empty(shmup.Simulation.Snapshot.Balls.ToArray());
        else Assert.Equal(breakout!.Value.Phase, host.Breakout!.Simulation.State.Phase);
        Advance(host, Sample, TestSettings.Baseline.StepSeconds);
        Advance(host, held, TestSettings.Baseline.StepSeconds);
        if (host.Shmup is { } fired) Assert.NotEmpty(fired.Simulation.Snapshot.Balls.ToArray());
    }
    [Theory]
    [InlineData(GameplayMode.Breakout)] [InlineData(GameplayMode.Shmup)]
    public void QueuedRestartWinsAndModeSwitchRetainsPauseAndSettings(GameplayMode mode)
    {
        using var host = Host(mode);
        Advance(host, Sample, 0);
        Advance(host, Sample with { KeyboardPrimary = true, PadRightTrigger = 1 }, 0);
        host.TogglePause();
        var oldBreakout = host.Breakout;
        var oldShmup = host.Shmup;
        host.RequestRestart();
        Assert.Same(oldBreakout, host.Breakout); Assert.Same(oldShmup, host.Shmup);
        host.UpdateControls(Sample);
        Assert.True(host.Paused);
        Assert.Equal(0, Advance(host, Sample, 100));
        if (host.Shmup is { } fresh)
        {
            Assert.NotSame(oldShmup, fresh);
            Assert.Same(oldShmup!.Settings, fresh.Settings);
            Assert.Equal(0, fresh.Simulation.Snapshot.Tick);
            Assert.Empty(fresh.Simulation.Snapshot.Balls.ToArray());
            Assert.Equal(0, fresh.Trails!.PointCount);
        }
        else
        {
            Assert.NotSame(oldBreakout, host.Breakout);
            Assert.Same(oldBreakout!.Simulation.Gameplay, host.Breakout!.Simulation.Gameplay);
            Assert.Equal(0, host.Breakout.TotalTicks);
        }
        host.Switch(mode == GameplayMode.Shmup ? GameplayMode.Breakout : GameplayMode.Shmup);
        Assert.True(host.Paused);
        Assert.Equal(0, Advance(host, Sample, 100));
    }
}
