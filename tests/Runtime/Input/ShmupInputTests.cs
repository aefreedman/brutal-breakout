using Brutal.Numerics;
using BrutalBreakout.Platform.Input;
using Xunit;

public sealed class ShmupInputTests
{
    private static InputSnapshot Active => new() { Focused = true, Cursor = new(double.NaN) };
    [Fact]
    public void IndependentSticksAndNeutralAim()
    {
        var router = new ShmupInputRouter(new(.2));
        var result = router.Route(Active with { PadConnected = true, PadStick = new(1, 0), PadRightStick = new(0, 1) });
        Assert.Equal(new float2(1, 0), result.Move);
        Assert.Equal(new float2(0, 1), result.Aim);
        result = router.Route(Active with { PadConnected = true, PadRightStick = new(.1f, 0) });
        Assert.Null(result.Aim);
    }
    [Fact]
    public void HeldFireIsBaselinedAcrossResetAndReconnect()
    {
        var router = new ShmupInputRouter(new(.2));
        var held = Active with { PadConnected = true, PadRightTrigger = 1 };
        Assert.False(router.Route(held).FireHeld);
        router.Route(held with { PadRightTrigger = 0 });
        Assert.True(router.Route(held).FireHeld);
        Assert.True(router.Route(held).FireHeld);
        router.Route(Active with { Focused = false });
        Assert.False(router.Route(held).FireHeld);
        router.Route(Active);
        Assert.False(router.Route(held).FireHeld);
        router.Route(held with { PadRightTrigger = 0 });
        Assert.True(router.Route(held).FireHeld);
        Assert.False(router.Route(held with { PadId = 2 }).FireHeld);
    }
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, .5f)]
    [InlineData(1, 1)]
    [InlineData(float.NaN, 0)]
    public void SignedTriggerNormalization(float raw, float expected) =>
        Assert.Equal(expected, GlfwInputPoller.NormalizeTrigger(raw));

    [Fact]
    public void KeyboardMouseFallbackAndRestartEdges()
    {
        var router = new ShmupInputRouter(new(.2));
        router.Route(Active);
        var input = Active with { KeyboardDirection = new(1, 1), MousePrimary = true, KeyboardRestart = true, Cursor = new(20, 30) };
        var result = router.Route(input);
        Assert.True(result.FireHeld);
        Assert.True(result.RestartPressed);
        Assert.Equal(new double2(20, 30), result.Pointer);
        Assert.InRange(result.Move.X, .70f, .71f);
        Assert.False(router.Route(input).RestartPressed);
    }
}
