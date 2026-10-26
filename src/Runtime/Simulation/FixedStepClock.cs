namespace BrutalBreakout.Simulation;

/// <summary>Bounded wall-time accumulator; drops new excess time without losing the prior fraction.
/// Reset on pause/focus transitions. Consume action edges on only the first returned tick.</summary>
public sealed class FixedStepClock
{
    public PhysicsSettings Settings { get; private set; }
    public void ApplyTuning(PhysicsSettings settings)
    {
        TuningCompatibility.Physics(Settings, settings);
        Settings = settings; // Preserve fractionalTicks.
    }
    public double StepSeconds => Settings.StepSeconds;
    public int MaxStepsPerAdvance => Settings.MaxCatchUpSteps;
    private double fractionalTicks;
    public double RemainderSeconds => fractionalTicks * StepSeconds;

    public FixedStepClock(PhysicsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
    }

    public int Advance(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        // Bound before multiplication, preserving the old fraction even with int.MaxValue caps.
        if (elapsedSeconds >= MaxStepsPerAdvance * StepSeconds) return MaxStepsPerAdvance;
        double incoming = Math.Min(elapsedSeconds * Settings.UpdateHz, MaxStepsPerAdvance);
        double whole = Math.Floor(incoming);
        double fraction = fractionalTicks + (incoming - whole);
        // Absolute tick tolerance handles ordinary partition roundoff, not elapsed-time debt.
        double carry = Math.Floor(fraction + 1e-12);
        double steps = whole + carry;
        fractionalTicks = Math.Max(0, fraction - carry);
        return (int)Math.Min(MaxStepsPerAdvance, steps);
    }

    public void Reset() => fractionalTicks = 0;
}
