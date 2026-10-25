using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalBreakout.Platform.Input;
using CrushKit.Brutal.Input;
using Xunit;

public sealed class BindingsTests
{
    private sealed class Source : IInputSource
    {
        public bool Focused => true;
        public bool Minimized => false;
        public double2 Cursor => new(10, 20);
        public bool Down(GlfwKey key) => key is GlfwKey.W or GlfwKey.D or GlfwKey.Space or GlfwKey.R or GlfwKey.F1;
        public bool Down(GlfwMouseButton button) => button == GlfwMouseButton.Left;
        public bool TryGamepad(int id, out GamepadSnapshot? gamepad)
        {
            gamepad = id == 2 ? new(new float[] { .25f, -.5f, -.75f, .5f, -1, 1 }, [GlfwGamepadButton.A, GlfwGamepadButton.Start]) : null;
            return gamepad is not null;
        }
    }
    [Fact]
    public void BreakoutOwnsBindingsAndYUpConversion()
    {
        var devices = new InputPoller(new Source(), [GlfwKey.W, GlfwKey.D, GlfwKey.Space, GlfwKey.R, GlfwKey.F1], [GlfwMouseButton.Left]);
        var sample = BrutalBreakout.Platform.Input.GlfwInputPoller.Map(devices.Poll());
        Assert.Equal(new float2(1, 1), sample.KeyboardDirection);
        Assert.True(sample.KeyboardPrimary); Assert.True(sample.KeyboardRestart); Assert.True(sample.KeyboardDebugToggle);
        Assert.True(sample.MousePrimary); Assert.True(sample.PadPrimary); Assert.True(sample.PadRestart);
        Assert.Equal(2, sample.PadId); Assert.Equal(new float2(.25f, .5f), sample.PadStick);
        Assert.Equal(new float2(-.75f, -.5f), sample.PadRightStick);
        Assert.Equal(1, sample.PadRightTrigger);
        Assert.Equal(new double2(10, 20), sample.Cursor);
    }
}
