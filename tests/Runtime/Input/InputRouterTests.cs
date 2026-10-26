using Brutal.Numerics;
using BrutalBreakout.Platform.Input;
using Xunit;

namespace BrutalBreakout.Input.Tests;

public sealed class InputRouterTests
{
    private static InputSnapshot Idle => new() { Focused = true, Cursor = new(20, 30) };
    private static InputSnapshot Pad => Idle with { PadConnected = true, PadId = 2 };

    [Fact]
    public void LiveDeadzoneRetainsButtonBaselinesAndDoesNotReplayHeldPrimary()
    {
        var router = new InputRouter(new(.1));
        router.Route(Pad);
        Assert.True(router.Route(Pad with { PadPrimary = true }).PrimaryPressed);
        var next = new InputSettings(.6);
        router.ApplyTuning(next);
        Assert.Same(next, router.Settings);
        var routed = router.Route(Pad with { PadPrimary = true, PadStick = new(.4f, 0) });
        Assert.False(routed.PrimaryPressed); Assert.Equal(AimKind.None, routed.AimKind);
        router.Route(Pad);
        Assert.True(router.Route(Pad with { PadPrimary = true }).PrimaryPressed);
    }

    [Fact]
    public void ConfiguredDeadzoneChangesPriorityWithoutChangingNormalization()
    {
        var low = new InputRouter(new InputSettings(0.1));
        var high = new InputRouter(new InputSettings(0.7));
        low.Route(Pad);
        high.Route(Pad);
        var sample = Pad with { PadStick = new(0.3f, 0.4f), Cursor = new(100, 200) };
        var active = low.Route(sample);
        Assert.Equal(AimKind.Direction, active.AimKind);
        Assert.Equal(new float2(0.6f, 0.8f), active.Aim);
        Assert.InRange(active.Aim.Length(), 0.99999f, 1.00001f);
        var pointer = high.Route(sample);
        Assert.Equal(AimKind.Pointer, pointer.AimKind);
        Assert.Equal(new float2(100, 200), pointer.Aim);
        Assert.Equal(AimKind.None, high.Route(sample).AimKind);

        var stronger = high.Route(sample with { PadStick = new(0.6f, 0.8f) });
        Assert.Equal(AimKind.Direction, stronger.AimKind);
        Assert.Equal(active.Aim, stronger.Aim);
        Assert.InRange(stronger.Aim.Length(), 0.99999f, 1.00001f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.375)]
    [InlineData(0.9999999403953552)]
    public void ConfiguredThresholdIsInclusiveAndNextFloatIsActive(double deadzone)
    {
        var settings = new InputSettings(deadzone);
        var router = new InputRouter(settings);
        float threshold = settings.StickDeadzone;
        Assert.Equal(AimKind.None, router.Route(Pad with { PadStick = new(threshold, 0) }).AimKind);
        var above = router.Route(Pad with { PadStick = new(float.BitIncrement(threshold), 0) });
        Assert.Equal(AimKind.Direction, above.AimKind);
        Assert.Equal(new float2(1, 0), above.Aim);
        if (threshold > 0)
            Assert.Equal(AimKind.None, router.Route(Pad with { PadStick = new(float.BitDecrement(threshold), 0) }).AimKind);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(0.19f, 0)] [InlineData(0.12f, 0.12f)] [InlineData(0.2f, 0)]
    public void RadialDeadzone(float x, float y)
        => Assert.Equal(AimKind.None, new InputRouter(new InputSettings(0.2)).Route(Pad with { PadStick = new(x, y) }).AimKind);

    [Theory]
    [InlineData(1, 1)] [InlineData(0.15f, 0.15f)] [InlineData(float.MaxValue, float.MaxValue)]
    public void ActiveStickNormalized(float x, float y)
    {
        var intent = new InputRouter(new InputSettings(0.2)).Route(Pad with { PadStick = new(x, y), PadPrimary = true });
        Assert.Equal(AimKind.Direction, intent.AimKind);
        Assert.InRange(intent.Aim.Length(), 0.99999f, 1.00001f);
        Assert.False(intent.PrimaryPressed);
    }

    [Theory]
    [InlineData(float.NaN, 1)] [InlineData(1, float.PositiveInfinity)] [InlineData(float.NegativeInfinity, 0)]
    public void NonfiniteStickIgnored(float x, float y)
        => Assert.Equal(AimKind.None, new InputRouter(new InputSettings(0.2)).Route(Pad with { PadStick = new(x, y) }).AimKind);

    [Fact]
    public void KeyboardBeatsStickAndMovingMouse()
    {
        var r = new InputRouter(new InputSettings(0.2)); r.Route(Pad);
        var i = r.Route(Pad with { KeyboardDirection = new(-1, 1), PadStick = new(1, 0), Cursor = new(100, 200) });
        Assert.Equal(AimKind.Direction, i.AimKind);
        Assert.InRange(i.Aim.X, -0.708f, -0.706f);
        Assert.InRange(i.Aim.Y, 0.706f, 0.708f);
    }

    [Fact]
    public void StickBeatsMovementAndStationaryMouseNeverOverrides()
    {
        var r = new InputRouter(new InputSettings(0.2)); r.Route(Pad);
        var s = Pad with { PadStick = new(1, 0), Cursor = new(100, 200) };
        Assert.Equal(AimKind.Direction, r.Route(s).AimKind);
        Assert.Equal(AimKind.Direction, r.Route(s).AimKind);
        Assert.Equal(AimKind.None, r.Route(s with { PadStick = default }).AimKind);
    }

    [Fact]
    public void MouseMovementAndClickGiveLogicalPointer()
    {
        var r = new InputRouter(new InputSettings(0.2)); Assert.Equal(AimKind.None, r.Route(Idle).AimKind);
        var moved = Idle with { Cursor = new(12.5, 42.25) };
        Assert.Equal(new float2(12.5f, 42.25f), r.Route(moved).Aim);
        Assert.Equal(AimKind.None, r.Route(moved).AimKind);
        var click = r.Route(moved with { MousePrimary = true });
        Assert.Equal(AimKind.Pointer, click.AimKind); Assert.True(click.PrimaryPressed);
        Assert.Equal(AimKind.None, r.Route(moved with { MousePrimary = true }).AimKind);
    }

    [Theory]
    [InlineData(double.NaN, 1)] [InlineData(1, double.PositiveInfinity)] [InlineData(double.MaxValue, 0)]
    public void InvalidCursorDoesNotAim(double x, double y)
    {
        var r = new InputRouter(new InputSettings(0.2)); r.Route(Idle);
        var i = r.Route(Idle with { Cursor = new(x, y), MousePrimary = true });
        Assert.Equal(AimKind.None, i.AimKind); Assert.True(i.PrimaryPressed);
        Assert.Equal(AimKind.None, r.Route(Idle).AimKind);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void PrimaryFirstHeldReleaseRepress(int source)
    {
        var r = new InputRouter(new InputSettings(0.2));
        var held = Pad with { KeyboardPrimary = source == 0, MousePrimary = source == 1, PadPrimary = source == 2 };
        Assert.False(r.Route(held).PrimaryPressed);
        Assert.False(r.Route(held).PrimaryPressed);
        Assert.False(r.Route(Pad).PrimaryPressed);
        Assert.True(r.Route(held).PrimaryPressed);
        Assert.False(r.Route(held).PrimaryPressed);
    }

    [Fact]
    public void OverlappingSourcesDoNotDoubleFire()
    {
        var r = new InputRouter(new InputSettings(0.2)); r.Route(Pad);
        Assert.True(r.Route(Pad with { KeyboardPrimary = true }).PrimaryPressed);
        Assert.False(r.Route(Pad with { KeyboardPrimary = true, PadPrimary = true }).PrimaryPressed);
        Assert.False(r.Route(Pad with { PadPrimary = true }).PrimaryPressed);
        r.Route(Pad);
        Assert.True(r.Route(Pad with { PadPrimary = true, MousePrimary = true }).PrimaryPressed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FocusOrMinimizeSuppressesAndRegainBaselinesHeld(bool minimize)
    {
        var r = new InputRouter(new InputSettings(0.2)); r.Route(Pad);
        var held = Pad with { KeyboardPrimary = true, MousePrimary = true, PadPrimary = true,
            KeyboardRestart = true, PadPause = true, PadStick = new(1, 0) };
        var inactive = r.Route(held with { Focused = minimize, Minimized = minimize });
        Assert.False(inactive.Active); Assert.False(inactive.PrimaryPressed);
        Assert.False(inactive.RestartPressed); Assert.Equal(AimKind.None, inactive.AimKind);
        var regain = r.Route(held);
        Assert.True(regain.Active); Assert.False(regain.PrimaryPressed); Assert.False(regain.RestartPressed);
        Assert.Equal(AimKind.Direction, regain.AimKind);
        r.Route(Pad);
        Assert.True(r.Route(held).PrimaryPressed);
    }

    [Fact]
    public void ResetBaselinesHeldAndCursor()
    {
        var r = new InputRouter(new InputSettings(0.2)); r.Route(Idle); r.Reset();
        var i = r.Route(Idle with { KeyboardPrimary = true, Cursor = new(300, 400) });
        Assert.False(i.PrimaryPressed); Assert.Equal(AimKind.None, i.AimKind);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PadDisconnectReconnectOrReplacementHeldDoesNotFire(bool replace)
    {
        var r = new InputRouter(new InputSettings(0.2)); r.Route(Pad);
        Assert.True(r.Route(Pad with { PadPrimary = true, PadPause = true }).PrimaryPressed);
        if (!replace) Assert.False(r.Route(Idle).PrimaryPressed);
        var newPad = Pad with { PadId = replace ? 3 : 2, PadPrimary = true, PadPause = true };
        var first = r.Route(newPad);
        Assert.False(first.PrimaryPressed); Assert.False(first.RestartPressed);
        Assert.False(r.Route(newPad).PrimaryPressed);
        r.Route(newPad with { PadPrimary = false, PadPause = false });
        var press = r.Route(newPad); Assert.True(press.PrimaryPressed); Assert.False(press.RestartPressed);
    }

    [Fact]
    public void UnmappedOrMissingPadIgnored()
    {
        var r = new InputRouter(new InputSettings(0.2)); r.Route(Idle);
        var i = r.Route(Idle with { PadPrimary = true, PadPause = true, PadStick = new(1, 0) });
        Assert.Null(i.PadId); Assert.Equal(AimKind.None, i.AimKind);
        Assert.False(i.PrimaryPressed); Assert.False(i.RestartPressed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RestartEdgesAndCoemission(bool pad)
    {
        var r = new InputRouter(new InputSettings(0.2));
        var held = Pad with { KeyboardRestart = !pad, PadPause = pad, KeyboardPrimary = true };
        Assert.False(r.Route(held).RestartPressed);
        r.Route(Pad);
        var i = r.Route(held); Assert.Equal(!pad, i.RestartPressed); Assert.True(i.PrimaryPressed);
        Assert.False(r.Route(held).RestartPressed);
        r.Route(Pad); Assert.Equal(!pad, r.Route(held).RestartPressed);
    }
}

