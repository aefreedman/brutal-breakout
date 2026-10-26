using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using Xunit;

public sealed class ConfigurationReconfigureTests
{
    private static string PathFor(string section) => Path.Combine(AppContext.BaseDirectory, "Config", section + ".json");
    private static ShmupSettings Shmup(double speed = 576)
    {
        var json = JsonNode.Parse(File.ReadAllText(PathFor("shmup")))!;
        json["capacity"]!["maxBalls"] = 16; json["capacity"]!["maxBricks"] = 32; json["ball"]!["speed"] = speed;
        return ShmupConfiguration.Load(PathFor("shmup"), json.ToJsonString()).Settings;
    }
    [Theory]
    [InlineData(GameplayMode.Breakout)] [InlineData(GameplayMode.Shmup)]
    public void ReplacementUsesExactSettingsRetainsModePauseAndRawStartLatchAndClearsQueuedRestart(GameplayMode mode)
    {
        var physics = PhysicsConfiguration.Load(PathFor("physics")).Settings;
        var gameplay = GameplayConfiguration.Load(PathFor("gameplay")).Settings;
        var input = InputConfiguration.Load(PathFor("input")).Settings;
        var execution = new ExecutionSettings(1, 32, 1);
        var trails = BallTrailConfiguration.Load(PathFor("trails")).Settings;
        using var host = new GameplaySessionHost(physics, gameplay, Shmup(), input, execution, mode, trails);
        var held = new InputSnapshot { Focused = true, PadConnected = true, PadPause = true };
        host.UpdateControls(held with { PadPause = false });
        host.UpdateControls(held); Assert.True(host.Paused);
        var previousShmup = host.Shmup; var previousBreakout = host.Breakout;
        host.RequestRestart(); // reconfigure supersedes pending commands
        var replacement = Shmup(600.5);
        host.Reconfigure(physics, gameplay, replacement, input, execution, trails);
        Assert.Equal(mode, host.Mode); Assert.True(host.Paused);
        var freshShmup = host.Shmup; var freshBreakout = host.Breakout;
        host.UpdateControls(held);
        Assert.True(host.Paused); Assert.Same(freshShmup, host.Shmup); Assert.Same(freshBreakout, host.Breakout);
        if (mode == GameplayMode.Shmup)
        {
            Assert.True(host.Shmup!.Paused); Assert.NotSame(previousShmup, host.Shmup);
            Assert.Same(replacement, host.Shmup.Settings); Assert.Same(replacement, host.Shmup.Simulation.Settings);
            Assert.Same(physics, host.Shmup.Simulation.Physics); Assert.Same(execution, host.Shmup.Simulation.Execution);
            Assert.Equal(0, host.Shmup.Simulation.Snapshot.Tick); Assert.Empty(host.Shmup.DrainEvents().ToArray());
            Assert.Throws<ObjectDisposedException>(() => previousShmup!.Simulation.Step());
            host.TogglePause();
            var view = new ShmupViewport(1280, 720, 1280, 720, replacement.Arena);
            Assert.Equal(0, host.Shmup.Advance(new() { Focused = true }, 100, view)); // no inherited debt
            Assert.Empty(host.Shmup.Simulation.Snapshot.Balls.ToArray());
        }
        else
        {
            Assert.True(host.Breakout!.Paused); Assert.NotSame(previousBreakout, host.Breakout);
            Assert.Same(gameplay, host.Breakout.Simulation.Gameplay); Assert.Same(physics, host.Breakout.Settings);
            Assert.Empty(host.Breakout.DrainEvents().ToArray());
            Assert.Throws<ObjectDisposedException>(() => previousBreakout!.Advance(default, 0, new(1280, 720, 1280, 720, gameplay.Arena)));
        }
        host.UpdateControls(held with { PadPause = false }); host.UpdateControls(held);
        Assert.Equal(mode == GameplayMode.Shmup, host.Paused); // release/repress toggles exactly once
    }

    [Theory]
    [InlineData(GameplayMode.Breakout)] [InlineData(GameplayMode.Shmup)]
    public void FailedPreparationKeepsPreviousRunAndSettingsUsable(GameplayMode mode)
    {
        var physics = PhysicsConfiguration.Load(PathFor("physics")).Settings;
        var gameplay = GameplayConfiguration.Load(PathFor("gameplay")).Settings;
        var input = InputConfiguration.Load(PathFor("input")).Settings;
        var settings = Shmup(); var execution = new ExecutionSettings(1, 32, 1);
        using var host = new GameplaySessionHost(physics, gameplay, settings, input, execution, mode);
        host.TogglePause();
        var previousShmup = host.Shmup; var previousBreakout = host.Breakout;
        Exception? wrongThread = null;
        var thread = new Thread(() => wrongThread = Record.Exception(() => host.Reconfigure(physics, gameplay, settings, input, execution, null)));
        thread.Start(); thread.Join();
        Assert.IsType<InvalidOperationException>(wrongThread);
        Assert.Throws<ArgumentNullException>(() => host.Reconfigure(physics, gameplay, Shmup(600), input, null!, null));
        Assert.Same(previousShmup, host.Shmup); Assert.Same(previousBreakout, host.Breakout);
        Assert.Equal(mode, host.Mode); Assert.True(host.Paused);
        host.TogglePause(); host.Switch(mode);
        Assert.False(host.Paused);
        if (mode == GameplayMode.Shmup) Assert.Same(settings, host.Shmup!.Settings);
        else Assert.Same(gameplay, host.Breakout!.Simulation.Gameplay);
    }
}
