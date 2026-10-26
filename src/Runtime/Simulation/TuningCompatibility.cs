namespace BrutalBreakout.Simulation;

/// <summary>Preflight before any owner/native mutation. These are topology constraints, not document boundaries.</summary>
public static class TuningCompatibility
{
    public static void Execution(ExecutionSettings old, ExecutionSettings next)
    {
        if (old.PhysicsWorkerCount != next.PhysicsWorkerCount || old.Jobs.WorkerCount != next.Jobs.WorkerCount ||
            old.Jobs.MaxExpectedConcurrentJobs != next.Jobs.MaxExpectedConcurrentJobs)
            throw new ArgumentException("Execution topology requires reset.");
    }
    public static void Physics(PhysicsSettings old, PhysicsSettings next)
    {
        if (old.UpdateHz != next.UpdateHz || old.MaxCatchUpSteps != next.MaxCatchUpSteps)
            throw new ArgumentException("Tick clock/event capacity changes require reset.");
    }
    public static void Shmup(ShmupSettings old, ShmupSettings next, double width)
    {
        if (old.Arena.Width != next.Arena.Width || old.Arena.Height != next.Arena.Height ||
            old.Capacity.MaxBalls != next.Capacity.MaxBalls || old.Capacity.MaxBricks != next.Capacity.MaxBricks ||
            old.Brick.Size != next.Brick.Size || old.Brick.ColumnPitch != next.Brick.ColumnPitch ||
            old.Ball.BounceOnBricks != next.Ball.BounceOnBricks)
            throw new ArgumentException("Shmup topology/contact mode changes require reset.");
        if (2 * Math.Max(next.Avatar.Radius, next.Ball.Radius) >= width)
            throw new ArgumentException("Live radii must fit the current run width; leave pending or widen first.");
    }
    public static void Gameplay(GameplaySettings old, GameplaySettings next)
    {
        var a = old.Arena; var b = next.Arena;
        if (a.Width != b.Width || a.Height != b.Height || a.Left != b.Left || a.Right != b.Right ||
            a.Top != b.Top || a.Bottom != b.Bottom || a.WallThickness != b.WallThickness ||
            (old.Breakout is null) != (next.Breakout is null) ||
            old.Breakout is { } x && next.Breakout is { } y && !x.Bricks.SequenceEqual(y.Bricks))
            throw new ArgumentException("Breakout fixture topology changes require reset.");
    }
}
