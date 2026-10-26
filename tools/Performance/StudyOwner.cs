using System.Diagnostics;
using System.Runtime.ExceptionServices;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Performance;

internal readonly record struct StudyCommand(long Sequence, ShmupCommand Input, long Enqueued);
internal readonly record struct TickFacts(int BallsBefore, int BallsAfter, int BricksBefore, int BricksAfter,
    int RefilledBalls, int RefilledBricks, int AdmittedBallSpawns, int RetiredBalls, int RetiredBricks, int RetiredOutsideScreen,
    int RetiredInsideScreenCauseUnknown, int? NativeExpiredExact, int Intervals, int ReconcileVisits,
    int? NativeHitEntriesExact, int? NativeWallHitsExact, int? DamagePairVisits,
    int RetainedHitPairs, int TrackedPairs, int Bodies, int Shapes, int RetiredBricksOutsideScreen, bool Restarted, long BallGenerationReuses, long DroppedBalls, long DroppedBricks, bool Defeated,
    double MaintenanceMs, double StepMs, bool TickAdvanced);
internal sealed record Publication(ShmupSnapshot? Snapshot, ShmupEvents Events, TickFacts Facts);
internal sealed record Completion(long Sequence, long Enqueued, long Started, long Published, Publication Publication);
internal interface IStudyController : IDisposable
{
    Publication Execute(StudyCommand command);
}

/// <summary>App-local protocol experiment: ONE accepted command total, including an unobserved
/// completion. No latest-only overwrite, queued second command, native cancellation or thread migration.</summary>
internal sealed class StudyOwner : IDisposable
{
    private readonly object gate = new();
    private readonly Thread thread;
    private readonly Func<IStudyController> factory;
    private bool initialized, stopping, accepted, pending, exited, disposed;
    private StudyCommand command;
    private Completion? completion;
    private ExceptionDispatchInfo? fault;
    public int OwnerThreadId { get; private set; }
    public Exception? TeardownFailure { get; private set; }
    public StudyOwner(Func<IStudyController> factory)
    {
        this.factory = factory;
        thread = new(Run) { IsBackground = true, Name = "Breakout study exclusive owner" };
        thread.Start();
        lock (gate)
        {
            while (!initialized) Monitor.Wait(gate);
            if (fault is null) return;
        }
        thread.Join(); fault!.Throw();
    }
    public bool TrySubmit(long sequence, ShmupCommand input)
    {
        lock (gate)
        {
            fault?.Throw();
            if (stopping || accepted) return false;
            command = new(sequence, input, Stopwatch.GetTimestamp());
            accepted = pending = true; completion = null;
            Monitor.PulseAll(gate); return true;
        }
    }
    public Completion Observe()
    {
        lock (gate)
        {
            if (!accepted) throw new InvalidOperationException("No accepted command.");
            while (completion is null && fault is null && !exited) Monitor.Wait(gate);
            fault?.Throw();
            var result = completion ?? throw new InvalidOperationException("Owner exited without completion.");
            completion = null; accepted = false; return result;
        }
    }
    private void Run()
    {
        IStudyController? controller = null;
        try
        {
            OwnerThreadId = Environment.CurrentManagedThreadId;
            controller = factory();
            lock (gate) { initialized = true; Monitor.PulseAll(gate); }
            while (true)
            {
                StudyCommand next;
                lock (gate)
                {
                    while (!pending && !stopping) Monitor.Wait(gate);
                    if (!pending) break;
                    next = command; pending = false;
                }
                long start = Stopwatch.GetTimestamp();
                var publication = controller.Execute(next);
                var result = new Completion(next.Sequence, next.Enqueued, start, Stopwatch.GetTimestamp(), publication);
                lock (gate) { completion = result; Monitor.PulseAll(gate); }
            }
        }
        catch (Exception error) { lock (gate) { fault = ExceptionDispatchInfo.Capture(error); Monitor.PulseAll(gate); } }
        finally
        {
            try { controller?.Dispose(); }
            catch (Exception error) { lock (gate) { TeardownFailure = error; fault ??= ExceptionDispatchInfo.Capture(error); } }
            lock (gate) { initialized = exited = true; Monitor.PulseAll(gate); }
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            stopping = disposed = true; Monitor.PulseAll(gate);
        }
        // Cooperative drain. External process deadline, NOT a foreign-thread Dispose, handles a blocked native call.
        thread.Join();
        lock (gate) fault?.Throw();
    }
}
