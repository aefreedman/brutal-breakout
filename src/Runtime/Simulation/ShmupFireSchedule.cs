namespace BrutalBreakout.Simulation;

/// <summary>Half-open tick deadlines. Scratch is owner supplied; every attempt consumes time,
/// including dropped shots. Idle cools to ready, never banks missed deadlines.</summary>
internal static class ShmupFireSchedule
{
    internal static int Fill(Span<double> births, ref double remaining, double dt, double period, bool held)
    {
        if (!held) { remaining = Math.Max(0, remaining - dt); return 0; }
        int count = 0;
        double next = remaining, tolerance = dt * 1e-9;
        for (; count < births.Length && next < dt - tolerance; count++, next += period)
            births[count] = Math.Max(0, next);
        // Validation guarantees enough scratch for all deadlines; never retain firing debt.
        remaining = Math.Max(0, next - dt);
        return count;
    }
}
