using Brutal.Numerics;

namespace BrutalBreakout.Platform.Input;

/// <summary>Pure synchronous snapshot mapping; no GLFW calls. Aim priority: active keyboard, active stick,
/// then moved/clicked mouse. Stationary mouse never replaces aim. Stick uses the configured radial deadzone.
/// Actions use aggregate edges with per-source baselines: overlapping sources cannot double-fire.
/// First sample, focus regain, and a newly selected pad baseline held buttons without firing them.</summary>
public sealed class InputRouter
{
    public InputSettings Settings { get; }

    public InputRouter(InputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
    }
    private bool initialized;
    private InputSnapshot previous;
    private bool cursorValid;
    private double2 cursor;

    /// <summary>Call on focus/minimize transitions even if no samples are taken while inactive.</summary>
    public void Reset()
    {
        initialized = false;
        previous = default;
        cursorValid = false;
        cursor = default;
    }

    public InputIntent Route(InputSnapshot sample)
    {
        if (!sample.Focused || sample.Minimized)
        {
            Reset();
            return new(AimKind.None, default, false, false, false, null);
        }

        bool samePad = initialized && sample.PadConnected && previous.PadConnected && sample.PadId == previous.PadId;
        bool primary = initialized && Edge(sample.KeyboardPrimary, sample.MousePrimary,
            sample.PadConnected && sample.PadPrimary, previous.KeyboardPrimary, previous.MousePrimary,
            samePad ? previous.PadPrimary : sample.PadConnected && sample.PadPrimary);
        bool restart = initialized && Edge(sample.KeyboardRestart, false,
            sample.PadConnected && sample.PadRestart, previous.KeyboardRestart, false,
            samePad ? previous.PadRestart : sample.PadConnected && sample.PadRestart);
        bool mousePress = initialized && sample.MousePrimary && !previous.MousePrimary;
        bool valid = double.IsFinite(sample.Cursor.X) && double.IsFinite(sample.Cursor.Y)
            && Math.Abs(sample.Cursor.X) <= float.MaxValue && Math.Abs(sample.Cursor.Y) <= float.MaxValue;
        bool moved = valid && cursorValid && (sample.Cursor.X != cursor.X || sample.Cursor.Y != cursor.Y);
        cursorValid = valid;
        cursor = sample.Cursor;
        previous = sample;
        initialized = true;

        AimKind kind = AimKind.None;
        float2 aim = default;
        if (Direction(sample.KeyboardDirection, 0, out aim)
            || (sample.PadConnected && Direction(sample.PadStick, Settings.StickDeadzone, out aim)))
            kind = AimKind.Direction;
        else if (valid && (moved || mousePress))
        {
            kind = AimKind.Pointer;
            aim = new((float)sample.Cursor.X, (float)sample.Cursor.Y);
        }
        return new(kind, aim, primary, restart, true, sample.PadConnected ? sample.PadId : null);
    }

    private static bool Edge(bool key, bool mouse, bool pad, bool oldKey, bool oldMouse, bool oldPad)
        => (key || mouse || pad) && !(oldKey || oldMouse || oldPad);

    private static bool Direction(float2 value, float deadzone, out float2 direction)
    {
        direction = default;
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)) return false;
        // Double intermediates avoid overflow/underflow for any finite snapshot axis.
        double length = Math.Sqrt((double)value.X * value.X + (double)value.Y * value.Y);
        if (length <= deadzone) return false;
        direction = new((float)(value.X / length), (float)(value.Y / length));
        return true;
    }
}
