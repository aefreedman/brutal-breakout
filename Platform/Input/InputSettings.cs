namespace BrutalBreakout.Platform.Input;

/// <summary>Validated, immutable input tuning shared for a session lifetime. No implicit defaults.</summary>
public sealed class InputSettings
{
    public float StickDeadzone { get; }

    public InputSettings(double stickDeadzone)
    {
        float value = (float)stickDeadzone;
        if (!double.IsFinite(stickDeadzone) || stickDeadzone < 0 || stickDeadzone >= 1 ||
            !float.IsFinite(value) || value >= 1 || (stickDeadzone > 0 && value == 0))
            throw new ArgumentOutOfRangeException(nameof(stickDeadzone),
                "Stick deadzone must be in [0, 1) and representable as a finite float without underflow to zero.");
        StickDeadzone = value;
    }
}
