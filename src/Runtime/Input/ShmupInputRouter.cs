using Brutal.Numerics;
using CrushKit.Brutal.Input;

namespace BrutalBreakout.Platform.Input;

public readonly record struct ShmupInputIntent(float2 Move, float2? Aim, double2? Pointer,
    bool FireHeld, bool RestartPressed, bool Active);

/// <summary>Left stick/WASD move, right stick aims; mouse aims when right stick is neutral.
/// RT/Space/left click fire. Held fire must be released after focus/reset/device changes.
/// Directions use +Y up; the session converts once to logical screen coordinates.</summary>
public sealed class ShmupInputRouter
{
    private InputSettings settings;
    public InputSettings Settings => settings;
    public void ApplyTuning(InputSettings settings) => this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ButtonEdges restart = new();
    private readonly HashSet<string> armed = new();
    private readonly HashSet<string> known = new();
    private double2? previousCursor;
    private bool previousMouse;
    public ShmupInputRouter(InputSettings settings) => this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    public void Reset() { restart.Reset(); armed.Clear(); known.Clear(); previousCursor = null; previousMouse = false; }
    public ShmupInputIntent Route(InputSnapshot sample)
    {
        if (!sample.Focused || sample.Minimized) { Reset(); return default; }
        var sources = new Dictionary<string, bool> { ["keyboard"] = sample.KeyboardPrimary, ["mouse"] = sample.MousePrimary };
        var restarts = new Dictionary<string, bool> { ["keyboard"] = sample.KeyboardRestart };
        if (sample.PadConnected)
        {
            sources[$"pad:{sample.PadId}"] = sample.PadRightTrigger > .5f;
        }
        armed.RemoveWhere(s => !sources.ContainsKey(s));
        known.RemoveWhere(s => !sources.ContainsKey(s));
        bool fire = false;
        foreach (var (source, down) in sources)
        {
            if (!known.Contains(source)) armed.Remove(source);
            if (!down) armed.Add(source);
            fire |= down && armed.Contains(source);
            known.Add(source);
        }
        float2 move = default;
        if (!InputMath.TryDirection(sample.KeyboardDirection, 0, out move) && sample.PadConnected
            && InputMath.TryDirection(sample.PadStick, Settings.StickDeadzone, out move))
            move *= Math.Min(1, MathF.Sqrt(sample.PadStick.X * sample.PadStick.X + sample.PadStick.Y * sample.PadStick.Y));
        float2? aim = sample.PadConnected && InputMath.TryDirection(sample.PadRightStick, Settings.StickDeadzone, out var right) ? right : null;
        bool valid = double.IsFinite(sample.Cursor.X) && double.IsFinite(sample.Cursor.Y);
        bool moved = valid && previousCursor is { } old && (old.X != sample.Cursor.X || old.Y != sample.Cursor.Y);
        double2? pointer = valid && (moved || (sample.MousePrimary && !previousMouse && known.Contains("mouse"))) ? sample.Cursor : null;
        previousCursor = valid ? sample.Cursor : null;
        previousMouse = sample.MousePrimary;
        return new(move, aim, pointer, fire, restart.Update(true, restarts), true);
    }
}
