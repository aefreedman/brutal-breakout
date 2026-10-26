using System.Diagnostics;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;

namespace BrutalBreakout.Performance;

internal sealed record MemorySample(int AfterCompleted, MemoryPressure Pressure, double ObserverMs);
internal sealed class CaseResult
{
    public required string Id { get; init; }
    public string Status { get; set; } = "setup-failure";
    public string? Error { get; set; }
    public string? TeardownError { get; set; }
    public required ResolvedCase Case { get; init; }
    public object? Layout { get; set; }
    public required string Build { get; init; }
    public string JitPolicy { get; } = "normal runtime tiered JIT; warmup does not establish stability";
    public double WallMs { get; set; }
    public double CpuMs { get; set; }
    public long AllocatedBefore { get; set; }
    public long AllocatedAfter { get; set; }
    public double GcPauseBeforeMs { get; set; }
    public double GcPauseAfterMs { get; set; }
    public double SetupMs { get; set; }
    public double TeardownMs { get; set; }
    public double WarmElapsedMs { get; set; }
    public double MeasuredElapsedMs { get; set; }
    public bool SteadyStatisticsAvailable { get; set; }
    public required WorkAccounting Work { get; init; }
    public int WarmCompleted { get; set; }
    public int MeasuredCompleted { get; set; }
    public double CompletedSimulationSeconds { get; set; }
    public bool FixedDurationComparable { get; set; }
    public ClockTape? ClockTapePlanned { get; set; }
    public int? ClockTapeCommandsPlanned { get; set; }
    public required List<MemorySample> Memory { get; init; }
    public Distribution? Step { get; set; }
    public Distribution? EndToEndCommand { get; set; }
    public Distribution? OwnerAdmissionDelay { get; set; }
    public Distribution? Maintenance { get; set; }
    public Distribution? Capture { get; set; }
    public Distribution? Geometry { get; set; }
    public Distribution? EnqueueToObserve { get; set; }
    public Distribution? PublicationDisplayAge { get; set; }
    public int? BallMinimum { get; set; }
    public int? BallMaximum { get; set; }
    public int? BrickMinimum { get; set; }
    public int? BrickMaximum { get; set; }
    public long NativeIntervals { get; set; }
    public long SolverSubsteps { get; set; }
    public string CounterScope { get; } = "whole: DamagePairVisits exact pair-visits; Events coalesced; retained hits survivor-only; expiry exact unavailable; outside/inside retirement poses do not prove cause. native: exact hit entries/wall hits/Expired before maintenance. Native and whole not subtractable.";
}
internal sealed record CaseRun(CaseResult Result, RawTick[] Raw);
internal static class CaseRunner
{
    internal static object LayoutManifest(Layout layout, ResolvedCase c) => new
    {
        seed = c.Policy.Seed, width = c.Policy.Width, ballRegionTop = 430, ballRowPitch = 6.25,
        columns = 100, rows = 40, strata = "10x10; shuffled 40-site layers; every 100-prefix covers all strata",
        balls = layout.Balls.Select(p => new { x = p.X, y = p.Y }).ToArray(),
        bricks = layout.Bricks.Select(b => new { x = b.Position.X, y = b.Position.Y, shape = b.Shape.ToString(), quarterTurns = b.Turns }).ToArray(),
        synthetic = "high HP/wall limit, noise threshold1, bounded validated refill; natural case starts near cap then firing/lives determine population",
        limits = "refill at most4000 ball probes and40 brick probes/tick; current geometry/avatar/circle checks; failed placements remain population deficits"
    };
    public static CaseRun Run(ResolvedCase c, Func<ResolvedCase, Layout, IStudyController>? factory = null,
        Func<MemoryPressure>? memoryProvider = null, Func<double>? budgetElapsedSeconds = null)
    {
        // Clock begins BEFORE layout, history, native construction and includes owner-thread teardown.
        var wall = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var result = new CaseResult { Id = c.Spec.Id, Case = c, Build = typeof(CaseRunner).Assembly.ManifestModule.ModuleVersionId.ToString(),
            Work = new(), Memory = new(), AllocatedBefore = GC.GetTotalAllocatedBytes(false), GcPauseBeforeMs = GC.GetTotalPauseDuration().TotalMilliseconds };
        var raw = new RawTick[c.TotalTicks];
        IStudyController? direct = null;
        StudyOwner? owner = null;
        ShapeDrawList? draw = null;
        BallTrailHistory? trails = null;
        int n = 0;
        string phase = "setup";
        long warmStart = 0, measureStart = 0;
        bool memoryOkay = true;
        int[]? clockFrames = null;
        factory ??= (settings, layout) => new StudyController(settings, layout);
        try
        {
            Sample(true);
            if (!memoryOkay) { result.Status = "memory-preflight-skip"; return Finish(); }
            var layout = new Layout(c.Settings, c.Policy);
            result.Layout = LayoutManifest(layout, c);
            if (c.Spec.Recreate)
            {
                // Predeclared lifecycle control: an extra full construction/disposal, no reused state.
                using var recreated = factory(c, layout);
            }
            if (c.Spec.Scope == "whole") { draw = new(); trails = c.Trails is null ? null : new(c.Trails, c.Settings.Capacity.MaxBalls); }
            if (c.Spec.Execution == "direct") direct = factory(c, layout);
            else owner = new(() => factory(c, layout));
            if (c.Spec.Catchup.HasValue)
            {
                result.ClockTapePlanned = ClockTape.Build(c.Physics, (int)Math.Ceiling(c.Policy.MeasureSeconds * 60), c.Policy.MeasureSeconds);
                clockFrames = result.ClockTapePlanned.InputFrames;
                // Smoke explicitly bounds command work, including scripted modes.
                if (c.WarmTicks == 0 && c.MeasureTicks <= 120) clockFrames = clockFrames.Take(c.MeasureTicks).ToArray();
                result.ClockTapeCommandsPlanned = clockFrames.Length;
            }
            result.SetupMs = wall.Elapsed.TotalMilliseconds;
            int total = c.WarmTicks + (clockFrames?.Length ?? c.MeasureTicks);
            if (total > raw.Length) throw new InvalidDataException("Clock tape exceeds raw storage budget.");
            phase = c.WarmTicks > 0 ? "warm" : "measure";
            warmStart = Stopwatch.GetTimestamp();
            if (c.WarmTicks == 0) measureStart = warmStart;
            Completion? last = null;
            for (int sequence = 0; sequence < total; sequence++)
            {
                if (sequence == c.WarmTicks)
                {
                    phase = "measure";
                    measureStart = Stopwatch.GetTimestamp();
                    result.WarmElapsedMs = StudyController.Milliseconds(measureStart - warmStart);
                }
                if (!CanContinue()) break;
                var input = Command(sequence);
                Completion completed;
                if (owner is null)
                {
                    long enqueued = Stopwatch.GetTimestamp(); result.Work.Accept();
                    long start = Stopwatch.GetTimestamp(); var publication = direct!.Execute(new(sequence, input, enqueued));
                    completed = new(sequence, enqueued, start, Stopwatch.GetTimestamp(), publication);
                }
                else
                {
                    if (!owner.TrySubmit(sequence, input)) { result.Work.Backpressure++; throw new InvalidOperationException("Unexpected outstanding work at admission."); }
                    result.Work.Accept();
                    // CPU presentation of last immutable state overlaps ONLY this one accepted unit.
                    if (last is not null) { Display(last, sequence); last = null; }
                    completed = owner.Observe();
                }
                long observed = Stopwatch.GetTimestamp();
                result.Work.Complete(completed.Sequence);
                long captureStart = Stopwatch.GetTimestamp();
                if (completed.Publication.Snapshot is { } snapshot)
                {
                    trails?.Capture(snapshot); result.Work.Capture();
                }
                long captureEnd = Stopwatch.GetTimestamp();
                bool warm = sequence < c.WarmTicks;
                raw[n++] = new(sequence, SourceFrame(sequence), warm, completed.Enqueued, completed.Started, completed.Published, observed, 0,
                    StudyController.Milliseconds(captureEnd - captureStart), 0, 0, 0, trails?.PointCount ?? 0,
                    completed.Publication.Events, completed.Publication.Facts,
                    GC.GetTotalAllocatedBytes(false), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration().TotalMilliseconds);
                if (warm) result.WarmCompleted++; else result.MeasuredCompleted++;
                if (c.Spec.Execution == "overlap") last = completed;
                else Display(completed, sequence);
                if (sequence % 60 == 0) Sample(false);
            }
            // Final accepted completion gets its capture AND presentation; no missing final/0th frame.
            if (last is not null) Display(last, checked((int)last.Sequence));
            result.Work.Validate(c.Spec.Scope == "whole");
            if (result.Status == "setup-failure") result.Status = "completed";
            if (measureStart != 0) result.MeasuredElapsedMs = StudyController.Milliseconds(Stopwatch.GetTimestamp() - measureStart);
        }
        catch (Exception error)
        {
            result.Status = phase + "-failure"; result.Error = error.ToString();
        }
        finally
        {
            long teardown = Stopwatch.GetTimestamp();
            try { if (owner is not null) owner.Dispose(); else direct?.Dispose(); }
            catch (Exception error)
            {
                // Owner Dispose also propagates a sticky step fault; do not mislabel that as a new teardown failure.
                if (owner is null || owner.TeardownFailure is not null || result.Error is null)
                { result.Status = "teardown-failure"; result.TeardownError = (owner?.TeardownFailure ?? error).ToString(); }
            }
            finally { draw?.Dispose(); }
            result.TeardownMs = StudyController.Milliseconds(Stopwatch.GetTimestamp() - teardown);
            result.WallMs = wall.Elapsed.TotalMilliseconds; result.CpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        }
        return Finish();

        ShmupCommand Command(int sequence)
        {
            // Fixed input tape is independent of measured execution speed. Same geometry/speed/commands
            // across one-factor cases. Natural firing is the sole declared spawn/reuse exception.
            int frame = SourceFrame(sequence);
            double angle = frame / 60.0 * .2;
            return new(default, new(Math.Sin(angle), -Math.Cos(angle)), c.Spec.Natural);
        }
        int SourceFrame(int sequence) => sequence < c.WarmTicks ? sequence : clockFrames?[sequence - c.WarmTicks] ?? sequence;
        bool CanContinue()
        {
            if ((budgetElapsedSeconds?.Invoke() ?? wall.Elapsed.TotalSeconds) >= c.Policy.CaseWallSeconds) { result.Status = phase + "-truncation-wall"; return false; }
            if (!memoryOkay) { result.Status = phase + "-truncation-memory"; return false; }
            return true;
        }
        void Sample(bool preflight)
        {
            long start = Stopwatch.GetTimestamp(); var pressure = (memoryProvider ?? MemoryGuard.Sample)();
            double observer = StudyController.Milliseconds(Stopwatch.GetTimestamp() - start);
            result.Memory.Add(new(n, pressure, observer));
            memoryOkay = MemoryGuard.Allows(pressure, c.Policy, c.StorageEstimateBytes, preflight);
        }
        void Display(Completion completed, int currentInputSequence)
        {
            if (completed.Publication.Snapshot is not { } snapshot) return;
            long start = Stopwatch.GetTimestamp();
            var frame = ShmupPresentation.Build(draw!, snapshot, c.Settings, c.Colors, c.Style, 1280, 720, trails);
            // Borrowed frame is inspected synchronously, never retained or transferred to producer.
            int indices = frame.Indices.Length;
            long displayed = Stopwatch.GetTimestamp();
            int index = checked((int)completed.Sequence);
            raw[index] = raw[index] with { Displayed = displayed, GeometryMs = StudyController.Milliseconds(displayed - start),
                Indices = indices, DisplayInputSequenceAge = currentInputSequence - index };
            result.Work.Present();
        }
        CaseRun Finish()
        {
            result.WallMs = wall.Elapsed.TotalMilliseconds;
            if (result.Status == "completed" && result.WallMs > c.Policy.CaseWallSeconds * 1000) result.Status = "teardown-overrun-wall";
            result.CpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            result.AllocatedAfter = GC.GetTotalAllocatedBytes(false); result.GcPauseAfterMs = GC.GetTotalPauseDuration().TotalMilliseconds;
            result.SteadyStatisticsAvailable = !c.Smoke && n > 0 && result.WarmCompleted == c.WarmTicks;
            var measured = raw.Take(n).Where(r => !r.Warm).ToArray();
            result.CompletedSimulationSeconds = measured.Count(r => r.Facts.TickAdvanced) * c.Physics.StepSeconds;
            result.FixedDurationComparable = !c.Smoke && result.Status == "completed" && clockFrames is null && result.MeasuredCompleted == c.MeasureTicks &&
                result.SteadyStatisticsAvailable && Math.Abs(result.CompletedSimulationSeconds - c.Policy.MeasureSeconds) < 1e-9;
            if (result.SteadyStatisticsAvailable || (c.Smoke && n > 0))
            {
                result.Step = Distribution.Of(measured.Select(r => r.Facts.StepMs));
                result.EndToEndCommand = Distribution.Of(measured.Select(r => StudyController.Milliseconds((r.Displayed == 0 ? r.Observed : r.Displayed) - r.Enqueued)));
                result.OwnerAdmissionDelay = Distribution.Of(measured.Select(r => StudyController.Milliseconds(r.Started - r.Enqueued)));
                result.Maintenance = Distribution.Of(measured.Select(r => r.Facts.MaintenanceMs));
                result.Capture = Distribution.Of(measured.Select(r => r.CaptureMs));
                result.Geometry = Distribution.Of(measured.Select(r => r.GeometryMs));
                result.EnqueueToObserve = Distribution.Of(measured.Select(r => StudyController.Milliseconds(r.Observed - r.Enqueued)));
                result.PublicationDisplayAge = Distribution.Of(measured.Where(r => r.Displayed != 0).Select(r => StudyController.Milliseconds(r.Displayed - r.Published)));
            }
            if (measured.Length > 0)
            {
                result.BallMinimum = measured.Min(r => Math.Min(r.Facts.BallsBefore, r.Facts.BallsAfter));
                result.BallMaximum = measured.Max(r => Math.Max(r.Facts.BallsBefore, r.Facts.BallsAfter));
                result.BrickMinimum = measured.Min(r => Math.Min(r.Facts.BricksBefore, r.Facts.BricksAfter));
                result.BrickMaximum = measured.Max(r => Math.Max(r.Facts.BricksBefore, r.Facts.BricksAfter));
                result.NativeIntervals = measured.Sum(r => (long)r.Facts.Intervals);
                result.SolverSubsteps = result.NativeIntervals * c.Physics.Substeps;
            }
            return new(result, raw.Take(n).ToArray());
        }
    }
}
