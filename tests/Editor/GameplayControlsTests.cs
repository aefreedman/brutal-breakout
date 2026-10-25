using BrutalBreakout.Editor;
using BrutalBreakout.Presentation;
using Xunit;

public sealed class GameplayControlsTests
{
    [Fact]
    public void RequestsAreQueuedUntilDrainAndNeverMutatePublishedState()
    {
        var controls = new GameplayControls();
        controls.Synchronize(GameplayMode.Shmup, 432, 1280);
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
    [Theory]
    [InlineData(431)] [InlineData(1281)] [InlineData(double.NaN)]
    public void WideningRejectsNarrowingAndInvalidValues(double width)
    {
        var controls = new GameplayControls();
        controls.Synchronize(GameplayMode.Shmup, 432, 1280);
        Assert.Throws<ArgumentOutOfRangeException>(() => controls.RequestWiden(width));
    }
    [Fact]
    public void BreakoutHasNoWidthControl()
    {
        var controls = new GameplayControls();
        controls.Synchronize(GameplayMode.Breakout, 0, 1280);
        Assert.Throws<ArgumentOutOfRangeException>(() => controls.RequestWiden(700));
    }
}
