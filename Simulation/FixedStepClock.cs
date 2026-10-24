namespace BrutalBreakout.Simulation;

/// <summary>Bounded wall-time accumulator; never advances gameplay itself.
/// Each Advance accepts at most 8 ticks of elapsed time, dropping excess time (no backlog).
/// The previous fractional tick is retained. Reset on pause/focus transitions.
/// Callers must consume action edges on only the first returned tick.</summary>
public sealed class FixedStepClock
{
    public const double StepSeconds = BallSimulation.StepSeconds;
    public const int MaxStepsPerAdvance = 8;
    private double fractionalTicks;
    public double RemainderSeconds => fractionalTicks * StepSeconds;

    public int Advance(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        double ticks = fractionalTicks + Math.Min(elapsedSeconds, MaxStepsPerAdvance * StepSeconds) * 120;
        // Roundoff tolerance at exact tick boundaries, not a variable gameplay timestep.
        int steps = Math.Min(MaxStepsPerAdvance, (int)Math.Floor(ticks + 1e-12));
        fractionalTicks = Math.Max(0, ticks - steps);
        return steps;
    }

    public void Reset() => fractionalTicks = 0;
}
