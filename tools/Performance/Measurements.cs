using BrutalBreakout.Simulation;

namespace BrutalBreakout.Performance;

internal readonly record struct RawTick(long Sequence, int SourceInputFrame, bool Warm, long Enqueued, long Started, long Published,
    long Observed, long Displayed, double CaptureMs, double GeometryMs, int Indices,
    int DisplayInputSequenceAge, int TrailPoints, ShmupEvents Events, TickFacts Facts,
    long AllocatedBytes, int Gen0, int Gen1, int Gen2, double GcPauseMs);
internal sealed record Distribution(int N, bool LowN, double? P50, double? P95, double? P99, double? Max)
{
    public static Distribution Of(IEnumerable<double> values)
    {
        var a = values.ToArray();
        if (a.Any(v => !double.IsFinite(v))) throw new InvalidDataException("Nonfinite sample.");
        Array.Sort(a);
        double? Q(double p) => a.Length == 0 ? null : a[Math.Max(0, (int)Math.Ceiling(p * a.Length) - 1)];
        return new(a.Length, a.Length < 100, Q(.5), Q(.95), Q(.99), Q(1));
    }
}
internal sealed record ClockTape(int[] InputFrames, double OfferedSeconds, double AdmittedSeconds, double DroppedSeconds, double RemainderSeconds)
{
    public static ClockTape Build(PhysicsSettings physics, int frames, double? offeredLimitSeconds = null)
    {
        var clock = new FixedStepClock(physics);
        var tape = new List<int>(checked(frames * physics.MaxCatchUpSteps));
        double offered = 0, dropped = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            // Explicit 60Hz host/input tape; stalls repeat at known frame identities.
            double elapsed = 1.0 / 60 + ((frame % 600) switch { 60 => .1, 300 => .25, 540 => .05, _ => 0 });
            if (offeredLimitSeconds is { } limit)
            {
                elapsed = Math.Min(elapsed, Math.Max(0, limit - offered));
                if (elapsed <= 1e-12) break;
            }
            double before = clock.RemainderSeconds;
            int admitted = clock.Advance(elapsed);
            offered += elapsed;
            dropped += Math.Max(0, before + elapsed - admitted * physics.StepSeconds - clock.RemainderSeconds);
            for (int i = 0; i < admitted; i++) tape.Add(frame);
        }
        return new(tape.ToArray(), offered, tape.Count * physics.StepSeconds, dropped, clock.RemainderSeconds);
    }
}
internal sealed class WorkAccounting
{
    public long Accepted { get; private set; }
    public long Completed { get; private set; }
    public long Captured { get; private set; }
    public long Presented { get; private set; }
    public long Backpressure { get; set; }
    public void Accept() => Accepted++;
    public void Complete(long sequence)
    {
        if (sequence != Completed || Completed >= Accepted) throw new InvalidOperationException("Completion sequence lost/duplicated.");
        Completed++;
    }
    public void Capture() => Captured++;
    public void Present() => Presented++;
    public void Validate(bool whole)
    {
        if (Accepted != Completed || (whole && (Completed != Captured || Completed != Presented)))
            throw new InvalidOperationException("Accepted/fill/drain/capture/presentation count mismatch.");
    }
}
