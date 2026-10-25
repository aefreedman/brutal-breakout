using Brutal.GlfwApi;
using Brutal.Numerics;

namespace BrutalBreakout.Platform.Input;

/// <summary>Thin synchronous GLFW sampler. Construct and Poll on the GLFW window/event thread, after
/// the host pumps events. Does not initialize GLFW, pump events, own the window, or change input modes.
/// Uses GLFW's standardized mappings; retains the selected pad until unavailable, then scans IDs 0..15.</summary>
public sealed class GlfwInputPoller
{
    private readonly GlfwWindow window;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private int selectedPad = -1;

    public GlfwInputPoller(GlfwWindow window) => this.window = window;

    public unsafe InputSnapshot Poll()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("GLFW input must be polled on its window thread.");

        bool focused = window.IsFocused;
        bool minimized = window.IsIconified;
        if (!focused || minimized)
            return new() { Focused = focused, Minimized = minimized };

        bool Key(GlfwKey key) => window.GetKey(key) == GlfwKeyAction.Press;
        float x = (Key(GlfwKey.Right) || Key(GlfwKey.D) ? 1 : 0)
            - (Key(GlfwKey.Left) || Key(GlfwKey.A) ? 1 : 0);
        float y = (Key(GlfwKey.Up) || Key(GlfwKey.W) ? 1 : 0)
            - (Key(GlfwKey.Down) || Key(GlfwKey.S) ? 1 : 0);
        bool connected = TryPad(out GlfwGamepadState pad);
        return new()
        {
            Focused = true,
            KeyboardDirection = new float2(x, y),
            KeyboardPrimary = Key(GlfwKey.Space),
            KeyboardRestart = Key(GlfwKey.R),
            KeyboardDebugToggle = Key(GlfwKey.F1),
            MousePrimary = window.GetMouseButton(GlfwMouseButton.Left) == GlfwButtonAction.Press,
            Cursor = window.CursorPos,
            PadConnected = connected,
            PadId = selectedPad,
            PadStick = connected ? new(pad.Axes[(int)GlfwGamepadAxis.LeftX], -pad.Axes[(int)GlfwGamepadAxis.LeftY]) : default,
            PadPrimary = connected && pad.Buttons[(int)GlfwGamepadButton.A] == (byte)GlfwButtonAction.Press,
            PadRestart = connected && pad.Buttons[(int)GlfwGamepadButton.Start] == (byte)GlfwButtonAction.Press
        };
    }

    private bool TryPad(out GlfwGamepadState state)
    {
        int old = selectedPad;
        if (old >= 0 && Glfw.JoystickIsGamepad(old) && Glfw.GetGamepadState(old, out state)) return true;
        selectedPad = -1;
        for (int id = 0; id < 16; id++)
        {
            if (id == old || !Glfw.JoystickIsGamepad(id) || !Glfw.GetGamepadState(id, out state)) continue;
            selectedPad = id;
            return true;
        }
        state = default;
        return false;
    }
}
