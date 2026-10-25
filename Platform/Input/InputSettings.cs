using CrushKit.Brutal.Input;

namespace BrutalBreakout.Platform.Input;

/// <summary>Game-owned immutable schema; no implicit defaults.</summary>
public sealed class InputSettings
{
    public float StickDeadzone { get; }
    public InputSettings(double stickDeadzone)
    {
        try { StickDeadzone = InputMath.ValidateDeadzone(stickDeadzone); }
        catch (ArgumentOutOfRangeException) { throw new ArgumentOutOfRangeException(nameof(stickDeadzone)); }
    }
}
