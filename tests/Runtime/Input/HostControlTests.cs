using BrutalBreakout.Platform.Input;
using Xunit;

public sealed class HostControlTests
{
    private static InputSnapshot Sample => new() { Focused = true, PadConnected = true, PadId = 2 };
    [Fact]
    public void ControlsAreIndependentAndHeldButtonsNeverRepeat()
    {
        var router = new HostControlRouter();
        Assert.Equal(default, router.Route(Sample with { PadPause = true, PadPanelToggle = true }));
        router.Route(Sample);
        Assert.Equal(new HostControlIntent(true, true), router.Route(Sample with { PadPause = true, PadPanelToggle = true }));
        Assert.Equal(default, router.Route(Sample with { PadPause = true, PadPanelToggle = true }));
        router.Route(Sample);
        Assert.Equal(new HostControlIntent(true, false), router.Route(Sample with { KeyboardDebugToggle = true }));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void FocusReconnectReplacementAndMinimizeBaselineHeldControls(int transition)
    {
        var router = new HostControlRouter();
        router.Route(Sample);
        var held = Sample with { PadPause = true, PadPanelToggle = true };
        router.Route(held);
        if (transition == 0) router.Route(Sample with { Focused = false });
        if (transition == 1) router.Route(Sample with { PadConnected = false });
        if (transition == 2) held = held with { PadId = 3 };
        if (transition == 3) router.Route(Sample with { Minimized = true });
        Assert.Equal(default, router.Route(held));
        Assert.Equal(default, router.Route(held));
        router.Route(held with { PadPause = false, PadPanelToggle = false });
        Assert.Equal(new HostControlIntent(true, true), router.Route(held));
    }
    [Fact]
    public void StartNeverRestartsEitherGameplayRouter()
    {
        var breakout = new InputRouter(new(.2));
        var shmup = new ShmupInputRouter(new(.2));
        breakout.Route(Sample); shmup.Route(Sample);
        Assert.False(breakout.Route(Sample with { PadPause = true }).RestartPressed);
        Assert.False(shmup.Route(Sample with { PadPause = true }).RestartPressed);
    }
}
