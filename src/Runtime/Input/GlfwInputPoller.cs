using Brutal.GlfwApi;
using CrushKit.Brutal.Input;

namespace BrutalBreakout.Platform.Input;

/// <summary>Breakout bindings and axis orientation. Device sampling/selection belongs to CrushKit.</summary>
public sealed class GlfwInputPoller
{
    private readonly CrushKit.Brutal.Input.GlfwInputPoller poller;
    public GlfwInputPoller(GlfwWindow window) => poller = new(window,
        [GlfwKey.Right, GlfwKey.D, GlfwKey.Left, GlfwKey.A, GlfwKey.Up, GlfwKey.W,
         GlfwKey.Down, GlfwKey.S, GlfwKey.Space, GlfwKey.R, GlfwKey.F1], [GlfwMouseButton.Left]);
    public InputSnapshot Poll() => Map(poller.Poll());

    public static float NormalizeTrigger(float signedValue) => float.IsFinite(signedValue)
        ? Math.Clamp((signedValue + 1) / 2, 0, 1) : 0;

    public static InputSnapshot Map(DeviceSnapshot sample)
    {
        if (!sample.Focused || sample.Minimized) return new() { Focused = sample.Focused, Minimized = sample.Minimized };
        bool Key(GlfwKey key) => sample.Down(key);
        var pad = sample.Gamepad;
        return new()
        {
            Focused = true,
            KeyboardDirection = new((Key(GlfwKey.Right) || Key(GlfwKey.D) ? 1 : 0) - (Key(GlfwKey.Left) || Key(GlfwKey.A) ? 1 : 0),
                (Key(GlfwKey.Up) || Key(GlfwKey.W) ? 1 : 0) - (Key(GlfwKey.Down) || Key(GlfwKey.S) ? 1 : 0)),
            KeyboardPrimary = Key(GlfwKey.Space), KeyboardRestart = Key(GlfwKey.R), KeyboardDebugToggle = Key(GlfwKey.F1),
            MousePrimary = sample.Down(GlfwMouseButton.Left), Cursor = sample.Cursor,
            PadConnected = pad is not null, PadId = sample.PadId ?? -1,
            PadStick = pad is null ? default : new(pad.Axis(GlfwGamepadAxis.LeftX), -pad.Axis(GlfwGamepadAxis.LeftY)),
            PadRightStick = pad is null ? default : new(pad.Axis(GlfwGamepadAxis.RightX), -pad.Axis(GlfwGamepadAxis.RightY)),
            PadRightTrigger = pad is null ? 0 : NormalizeTrigger(pad.Axis(GlfwGamepadAxis.RightTrigger)),
            PadPrimary = pad?.Down(GlfwGamepadButton.A) ?? false,
            PadRestart = pad?.Down(GlfwGamepadButton.Start) ?? false
        };
    }
}
