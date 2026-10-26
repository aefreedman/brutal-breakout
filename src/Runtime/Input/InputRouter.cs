using Brutal.Numerics;
using CrushKit.Brutal.Input;

namespace BrutalBreakout.Platform.Input;

/// <summary>Pure synchronous snapshot mapping; no GLFW calls. Aim priority: active keyboard, active stick,
/// then moved/clicked mouse. Stationary mouse never replaces aim. Stick uses the configured radial deadzone.
/// Actions use aggregate edges with per-source baselines: overlapping sources cannot double-fire.
/// First sample, focus regain, and a newly selected pad baseline held buttons without firing them.</summary>
public sealed class InputRouter
{
    private InputSettings settings;
    public InputSettings Settings => settings;
    public void ApplyTuning(InputSettings settings) => this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public InputRouter(InputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        this.settings = settings;
    }
    private bool initialized;
    private readonly ButtonEdges primaryEdges = new(), restartEdges = new();
    private InputSnapshot previous;
    private bool cursorValid;
    private double2 cursor;

    /// <summary>Call on focus/minimize transitions even if no samples are taken while inactive.</summary>
    public void Reset()
    {
        initialized = false;
        primaryEdges.Reset(); restartEdges.Reset();
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

        Dictionary<string, bool> primarySources = new() { ["keyboard"] = sample.KeyboardPrimary, ["mouse"] = sample.MousePrimary };
        Dictionary<string, bool> restartSources = new() { ["keyboard"] = sample.KeyboardRestart };
        if (sample.PadConnected)
        {
            primarySources[$"pad:{sample.PadId}"] = sample.PadPrimary;
        }
        bool primary = primaryEdges.Update(true, primarySources);
        bool restart = restartEdges.Update(true, restartSources);
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
        if (InputMath.TryDirection(sample.KeyboardDirection, 0, out aim)
            || (sample.PadConnected && InputMath.TryDirection(sample.PadStick, Settings.StickDeadzone, out aim)))
            kind = AimKind.Direction;
        else if (valid && (moved || mousePress))
        {
            kind = AimKind.Pointer;
            aim = new((float)sample.Cursor.X, (float)sample.Cursor.Y);
        }
        return new(kind, aim, primary, restart, true, sample.PadConnected ? sample.PadId : null);
    }

}
