using CrushKit.Brutal.Ecs;

namespace BrutalBreakout.Simulation;

/// <summary>Explicit immutable thread budgets shared by the session and its native adapter.</summary>
public sealed class ExecutionSettings
{
    public ArchRuntimeSettings Jobs { get; }
    public int PhysicsWorkerCount { get; }

    public ExecutionSettings(int workerCount, int maxExpectedConcurrentJobs, int physicsWorkerCount)
    {
        Jobs = new(workerCount, maxExpectedConcurrentJobs);
        // Box2D 3.x supports at most 64 worker indices, including any inline work.
        if (physicsWorkerCount < 1 || physicsWorkerCount > 64)
            throw new ArgumentOutOfRangeException(nameof(physicsWorkerCount));
        PhysicsWorkerCount = physicsWorkerCount;
    }
}
