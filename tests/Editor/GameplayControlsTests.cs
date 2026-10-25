using BrutalBreakout.Editor;
using BrutalBreakout.Presentation;
using Xunit;

public sealed class GameplayControlsTests
{
    private static GameplayControls Create() => new(new GameplayWindowSettings(16, 16, 360, 420));
    [Fact]
    public void RequestsAreQueuedUntilDrainAndNeverMutatePublishedState()
    {
        var controls = Create();
        controls.Synchronize(GameplayMode.Shmup, 432, new BrutalBreakout.Simulation.ShmupArenaSettings(1280, 720, 432));
        controls.RequestWiden(600);
        Assert.Equal(432, controls.Width);
        var request = controls.Drain();
        Assert.Null(request.Mode); Assert.Equal(600, request.Width);
        Assert.Null(controls.Drain().Width);
        controls.RequestWiden(700);
        controls.RequestMode(GameplayMode.Breakout);
        request = controls.Drain();
        Assert.Equal(GameplayMode.Breakout, request.Mode); Assert.Null(request.Width);
        Assert.Equal(GameplayMode.Shmup, controls.Mode);
    }
    [Fact]
    public void SelectedModeIsNoOpAndPausedWideningIsRejected()
    {
        var controls = Create();
        controls.Synchronize(GameplayMode.Shmup, 432, new BrutalBreakout.Simulation.ShmupArenaSettings(1280, 720, 432), true);
        controls.RequestMode(GameplayMode.Shmup);
        Assert.Null(controls.Drain().Mode);
        Assert.Throws<InvalidOperationException>(() => controls.RequestWiden(600));
        Assert.Null(controls.Drain().Width);
    }

    [Theory]
    [InlineData(431)] [InlineData(1281)] [InlineData(double.NaN)]
    public void WideningRejectsNarrowingAndInvalidValues(double width)
    {
        var controls = Create();
        controls.Synchronize(GameplayMode.Shmup, 432, new BrutalBreakout.Simulation.ShmupArenaSettings(1280, 720, 432));
        Assert.Throws<ArgumentOutOfRangeException>(() => controls.RequestWiden(width));
    }
    [Fact]
    public void RestartAndPauseAreQueuedOnceWithoutChangingPublishedState()
    {
        var controls = Create();
        controls.Synchronize(GameplayMode.Shmup, 432, new BrutalBreakout.Simulation.ShmupArenaSettings(1280, 720, 432), true);
        controls.RequestRestart();
        controls.RequestPauseToggle();
        Assert.True(controls.Paused);
        var request = controls.Drain();
        Assert.True(request.Restart); Assert.True(request.TogglePause);
        Assert.False(controls.Drain().Restart); Assert.False(controls.Drain().TogglePause);
        Assert.True(controls.Paused);
    }
    [Fact]
    public void BreakoutHasNoWidthControl()
    {
        var controls = Create();
        controls.Synchronize(GameplayMode.Breakout, 0, new BrutalBreakout.Simulation.ShmupArenaSettings(1280, 720, 432));
        Assert.Throws<ArgumentOutOfRangeException>(() => controls.RequestWiden(700));
    }
}
