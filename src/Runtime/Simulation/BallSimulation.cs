using Brutal.Numerics;
using CrushKit.Brutal.Ecs;

namespace BrutalBreakout.Simulation;

[Flags]
public enum SimulationEvents { None = 0, Launch = 1, Catch = 2, Brick = 4, Miss = 8, Win = 16, Loss = 32, Restart = 64 }

public enum BallPhase { Aiming, Flying }

/// <summary>Input edges belong to the caller. Send each pressed edge on only one Step.
/// Null or zero aim preserves aim; nonfinite aim is ignored. Restart wins over every other field.</summary>
public readonly record struct SimulationCommand(
    double2? AimDirection = null, bool PrimaryPressed = false, bool RestartPressed = false);

public readonly struct BallState
{
    public double2 Position { get; }
    public double2 Velocity { get; }
    public double2 AimDirection { get; }
    public BallPhase Phase { get; }
    public bool CatchEligible { get; }

    internal BallState(double2 position, double2 velocity, double2 aimDirection, BallPhase phase, bool catchEligible)
        => (Position, Velocity, AimDirection, Phase, CatchEligible) = (position, velocity, aimDirection, phase, catchEligible);
}

/// <summary>Game facade over an Arch world and dependency jobs. Publications are immutable;
/// submissions may come from any thread and execute in submission order.</summary>
public sealed class BallSimulation : IDisposable
{
    private readonly ArchRuntime runtime;
    private readonly BreakoutWorld simulation;
    private readonly object submissionGate = new();
    private SimulationSnapshot snapshot;
    private Task? shutdown;

    public PhysicsSettings Settings { get; private set; }
    public GameplaySettings Gameplay { get; private set; }
    public ExecutionSettings Execution { get; private set; }
    public double StepSeconds => Settings.StepSeconds;
    public double CatchLineY => Gameplay.CatchLineY;
    public SimulationSnapshot Snapshot => Volatile.Read(ref snapshot);
    public BallState State => Snapshot.State;
    public SimulationEvents Events => Snapshot.Events;
    public ReadOnlySpan<BrickState> Bricks => Snapshot.Bricks;
    public int RemainingBricks => Snapshot.RemainingBricks;
    public int BallsRemaining => Snapshot.BallsRemaining;
    public RoundPhase RoundPhase => Snapshot.RoundPhase;
    public long BouncesWithoutBrick => Snapshot.BouncesWithoutBrick;

    public BallSimulation(PhysicsSettings physics, GameplaySettings gameplay, ExecutionSettings execution)
    {
        ArgumentNullException.ThrowIfNull(physics);
        ArgumentNullException.ThrowIfNull(gameplay);
        ArgumentNullException.ThrowIfNull(execution);
        gameplay.ValidatePhysics(physics); // Reject incompatible settings before workers/native allocation.
        Settings = physics;
        Gameplay = gameplay;
        Execution = execution;
        runtime = new(execution.Jobs);
        BreakoutWorld? created = null;
        SimulationSnapshot? initial = null;
        try
        {
            runtime.Initialize(world =>
            {
                created = new(world, physics, gameplay, execution);
                initial = created.Publish();
            });
            simulation = created!;
            snapshot = initial!;
        }
        catch
        {
            try { created?.Dispose(); }
            finally { runtime.Dispose(); }
            throw;
        }
    }

    /// <summary>Queues one complete tick. Independent ball/round and brick systems can overlap.
    /// The task completes after all branches join and a consistent snapshot is published.</summary>
    public Task<SimulationSnapshot> StepAsync(SimulationCommand command = default)
    {
        lock (submissionGate)
        {
            ObjectDisposedException.ThrowIf(shutdown is not null, this);
            SimulationSnapshot? result = null;
            var completion = runtime.RunAsync(
                new WorldJob("input-physics", _ => simulation.PreparePhysics(command)),
                new WorldJob("ball-round-rules", _ => simulation.ResolveGameplay(), 0),
                new WorldJob("brick-state", _ => simulation.SynchronizeBricks(), 0),
                new WorldJob("publish", _ =>
                {
                    result = simulation.Publish();
                    Volatile.Write(ref snapshot, result);
                }, 1, 2));
            return CompletedSnapshot(completion);

            async Task<SimulationSnapshot> CompletedSnapshot(Task task)
            {
                await task.ConfigureAwait(false);
                return result!;
            }
        }
    }

    /// <summary>Blocking compatibility boundary for the current host, not a single-threaded simulation.</summary>
    public void Step(SimulationCommand command = default) => StepAsync(command).GetAwaiter().GetResult();
    public WallContact Preview()
    {
        lock (submissionGate)
        {
            ObjectDisposedException.ThrowIf(shutdown is not null, this);
            WallContact hit = default;
            // Read the pose inside the ordered job, not before pending ticks finish.
            runtime.RunAsync(new WorldJob("preview", _ =>
            {
                var state = Snapshot.State;
                hit = simulation.Query(state.Position, state.AimDirection);
            })).GetAwaiter().GetResult();
            return hit;
        }
    }
    public WallContact Query(double2 origin, double2 direction)
    {
        lock (submissionGate)
        {
            ObjectDisposedException.ThrowIf(shutdown is not null, this);
            WallContact hit = default;
            runtime.RunAsync(new WorldJob("shape-query", _ => hit = simulation.Query(origin, direction)))
                .GetAwaiter().GetResult();
            return hit;
        }
    }

    public void ApplyTuning(PhysicsSettings physics, GameplaySettings gameplay, ExecutionSettings? execution = null)
    {
        lock (submissionGate)
        {
            ObjectDisposedException.ThrowIf(shutdown is not null, this);
            TuningCompatibility.Physics(Settings, physics);
            if (execution is not null) TuningCompatibility.Execution(Execution, execution);
            TuningCompatibility.Gameplay(Gameplay, gameplay);
            gameplay.ValidatePhysics(physics);
            runtime.RunAsync(new WorldJob("apply-tuning", _ =>
            {
                simulation.ApplyTuning(physics, gameplay);
                Settings = physics; Gameplay = gameplay; Execution = execution ?? Execution;
                Volatile.Write(ref snapshot, simulation.Publish());
            })).GetAwaiter().GetResult();
        }
    }
    public void Dispose()
    {
        Task completion;
        lock (submissionGate)
        {
            shutdown ??= runtime.RunAsync(new WorldJob("destroy-physics", _ => simulation.Dispose()));
            completion = shutdown;
        }
        try { completion.GetAwaiter().GetResult(); }
        finally { runtime.Dispose(); }
    }
}
