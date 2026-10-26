using System.Diagnostics;
using System.Reflection;
using Brutal.Numerics;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Performance;

internal sealed class StudyController : IStudyController
{
    private static readonly FieldInfo NativeField = typeof(ShmupSimulation).GetField("native", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("ShmupSimulation.native");
    private readonly ResolvedCase c;
    private readonly Layout layout;
    private readonly ShmupSimulation? simulation;
    private readonly NativeShmupArena native;
    private readonly bool[] balls, bricks, seenBalls, seenBricks;
    private readonly long[] generations;
    private readonly BrickSeed[] brickSeeds;
    private readonly List<BrickSeed> occupiedBricks = new();
    private readonly List<double2> occupiedBalls = new();
    private readonly double2 avatar;
    private int directionCursor, placementCursor;
    private long refillReuses;
    private long tick;
    private bool disposed;
    public StudyController(ResolvedCase c, Layout layout)
    {
        this.c = c; this.layout = layout;
        balls = new bool[c.Settings.Capacity.MaxBalls]; bricks = new bool[c.Settings.Capacity.MaxBricks];
        seenBalls = new bool[balls.Length]; seenBricks = new bool[bricks.Length];
        generations = new long[balls.Length]; brickSeeds = new BrickSeed[bricks.Length];
        occupiedBalls.Capacity = balls.Length; occupiedBricks.Capacity = bricks.Length;
        if (c.Spec.Scope == "native")
        {
            native = new(c.Physics, c.Settings, c.Execution);
            var controller = new ShmupAvatarController(c.Settings); controller.Reset(); avatar = controller.Position;
        }
        else
        {
            simulation = new(c.Physics, c.Settings, c.Execution);
            try
            {
                native = NativeField.GetValue(simulation) as NativeShmupArena
                    ?? throw new InvalidOperationException("Private native field changed type.");
                avatar = simulation.Snapshot.PlayerPosition;
            }
            catch { simulation.Dispose(); throw; }
        }
        try
        {
            native.Widen(c.Policy.Width);
            foreach (var seed in layout.Bricks)
            {
                if (!Layout.BrickFits(c.Settings, c.Policy.Width, seed, occupiedBricks, occupiedBalls, avatar))
                    throw new InvalidDataException("Initial brick collides with actual avatar/geometry.");
                ActivateBrick(seed); occupiedBricks.Add(seed);
            }
            for (int i = 0; i < c.Spec.Target; i++)
            {
                var p = layout.Balls[i];
                if (!Layout.CircleFits(c.Settings, c.Policy.Width, p, avatar, occupiedBricks)) throw new InvalidDataException("Initial ball does not fit actual world.");
                ActivateBall(p);
            }
        }
        catch { Dispose(); throw; }
    }
    private void ActivateBall(double2 p)
    {
        var direction = Direction(directionCursor++);
        int id = simulation?.SpawnBall(p, direction) ?? Array.FindIndex(balls, b => !b);
        if (id < 0) throw new InvalidOperationException("Validated ball capacity exhausted.");
        if (simulation is null) native.ActivateBall(id, p, direction);
        if (generations[id] > 0) refillReuses++;
        balls[id] = true; generations[id]++;
    }
    private double2 Direction(int i)
    {
        double angle = (i * 2.399963229728653 + c.Policy.Seed) % (2 * Math.PI);
        return new(Math.Cos(angle), Math.Sin(angle));
    }
    private void ActivateBrick(BrickSeed seed)
    {
        int id = simulation?.SpawnBrick(seed.Position, seed.Shape, seed.Turns) ?? Array.FindIndex(bricks, b => !b);
        if (id < 0) throw new InvalidOperationException("Validated brick capacity exhausted.");
        if (simulation is null) native.ActivateBrick(id, seed.Position, seed.Shape, seed.Turns);
        bricks[id] = true; brickSeeds[id] = seed;
    }
    private (int Balls, int Bricks) Maintain()
    {
        occupiedBricks.Clear(); occupiedBalls.Clear();
        for (int j = 0; j < bricks.Length; j++) if (bricks[j]) occupiedBricks.Add(brickSeeds[j] with { Position = native.BrickPosition(j) });
        for (int i = 0; i < balls.Length; i++) if (balls[i]) occupiedBalls.Add(native.BallPosition(i));
        int addedBricks = 0, addedBalls = 0;
        // At most 40 placement probes per tick; failed placement is observed as population variation.
        foreach (var seed in layout.Bricks)
        {
            if (occupiedBricks.Count >= c.Policy.Bricks) break;
            if (!Layout.BrickFits(c.Settings, c.Policy.Width, seed, occupiedBricks, occupiedBalls, avatar)) continue;
            ActivateBrick(seed); occupiedBricks.Add(seed); addedBricks++;
        }
        if (!c.Spec.Natural)
        {
            int count = occupiedBalls.Count;
            // Ball-ball fixtures do not interact, but refill still avoids every current circle.
            for (int probe = 0; probe < layout.Balls.Count && count < c.Spec.Target; probe++)
            {
                var p = layout.Balls[(placementCursor + probe) % layout.Balls.Count];
                if (!Layout.CircleFits(c.Settings, c.Policy.Width, p, avatar, occupiedBricks)) continue;
                bool blocked = false;
                foreach (var other in occupiedBalls)
                {
                    var d = p - other;
                    if (d.X * d.X + d.Y * d.Y <= 4 * c.Settings.Ball.Radius * c.Settings.Ball.Radius) { blocked = true; break; }
                }
                if (blocked) continue;
                ActivateBall(p); occupiedBalls.Add(p); addedBalls++; count++;
            }
        }
        placementCursor = (placementCursor + 1) % layout.Balls.Count;
        return (addedBalls, addedBricks);
    }
    public Publication Execute(StudyCommand command)
    {
        refillReuses = 0;
        long maintenanceStart = Stopwatch.GetTimestamp();
        var refill = Maintain();
        long maintenanceEnd = Stopwatch.GetTimestamp();
        int beforeBalls = balls.Count(b => b), beforeBricks = bricks.Count(b => b);
        var beforeSnapshot = simulation?.Snapshot;
        long stepStart = Stopwatch.GetTimestamp();
        if (simulation is not null) simulation.Step(command.Input);
        else
        {
            tick++;
            var d = c.Settings.Difficulty;
            double speed = Math.Min(d.MaximumDescentSpeed, d.DescentSpeed * Math.Pow(d.SpeedMultiplier, tick * c.Physics.StepSeconds / d.RampSeconds));
            native.Step(speed, avatar, avatar);
        }
        long stepEnd = Stopwatch.GetTimestamp();
        var snapshot = simulation?.Snapshot;
        bool restarted = simulation?.Events.HasFlag(ShmupEvents.Restart) ?? false;
        bool advanced = simulation is null || (!restarted && snapshot!.Tick == beforeSnapshot!.Tick + 1);
        int intervals = advanced ? native.LastIntervalCount : 0;
        int reconcile = advanced ? native.ReconcileVisits : 0;
        int retainedHits = advanced ? native.HitPairs.Length : 0;
        int? hits = null, walls = null, expired = null;
        if (simulation is null)
        {
            hits = 0; walls = 0; expired = 0;
            foreach (var pair in native.HitPairs) hits += native.BrickHits[pair.Ball, pair.Brick];
            for (int i = 0; i < balls.Length; i++) if (balls[i]) { walls += native.WallHits[i]; if (native.Expired[i]) expired++; }
        }
        long reconcileStart = Stopwatch.GetTimestamp();
        Array.Clear(seenBalls); Array.Clear(seenBricks);
        long reused = 0;
        int admittedSpawns = 0;
        if (snapshot is not null)
        {
            foreach (var b in snapshot.Balls)
            {
                seenBalls[b.Id] = true;
                if (!balls[b.Id] && !restarted) admittedSpawns++;
                if (generations[b.Id] > 0 && b.Generation > generations[b.Id]) reused += b.Generation - generations[b.Id];
                generations[b.Id] = b.Generation;
            }
            foreach (var b in snapshot.Bricks) { seenBricks[b.Id] = true; brickSeeds[b.Id] = new(b.Position, b.Shape, b.QuarterTurns); }
        }
        else
        {
            for (int i = 0; i < balls.Length; i++) if (balls[i])
            {
                double y = native.BallPosition(i).Y, r = c.Settings.Ball.Radius;
                seenBalls[i] = !native.Expired[i] && y + r >= 0 && y - r <= c.Settings.Arena.Height;
            }
            for (int i = 0; i < bricks.Length; i++) if (bricks[i])
            {
                var seed = brickSeeds[i];
                seenBricks[i] = native.BrickPosition(i).Y - c.Settings.Brick.Geometry.BoundsSize(seed.Shape, seed.Turns).Y / 2 <= c.Settings.Arena.Height;
            }
        }
        int retiredBalls = 0, retiredBricks = 0, outside = 0, inside = 0, bricksOutside = 0;
        for (int i = 0; i < balls.Length; i++)
        {
            if (balls[i] && !seenBalls[i] && !restarted)
            {
                retiredBalls++;
                double y = native.BallPosition(i).Y, r = c.Settings.Ball.Radius;
                if (y + r < 0 || y - r > c.Settings.Arena.Height) outside++; else inside++;
                if (simulation is null) native.ClearBall(i);
            }
            balls[i] = seenBalls[i];
        }
        for (int i = 0; i < bricks.Length; i++)
        {
            if (bricks[i] && !seenBricks[i] && !restarted)
            {
                retiredBricks++;
                var seed = brickSeeds[i];
                if (native.BrickPosition(i).Y - c.Settings.Brick.Geometry.BoundsSize(seed.Shape, seed.Turns).Y / 2 > c.Settings.Arena.Height) bricksOutside++;
                if (simulation is null) native.ClearBrick(i);
            }
            bricks[i] = seenBricks[i];
        }
        double maintenance = Milliseconds(maintenanceEnd - maintenanceStart + Stopwatch.GetTimestamp() - reconcileStart);
        return new(snapshot, simulation?.Events ?? ShmupEvents.None,
            new(beforeBalls, balls.Count(b => b), beforeBricks, bricks.Count(b => b), refill.Balls, refill.Bricks, admittedSpawns,
                retiredBalls, retiredBricks, outside, inside, expired, intervals, reconcile, hits, walls,
                simulation is null ? null : advanced ? simulation.DamagePairVisits : 0, retainedHits,
                native.TrackedPairCount, native.BodyCount, native.ShapeCount, bricksOutside, restarted,
                reused + refillReuses, snapshot?.DroppedBalls ?? 0, snapshot?.DroppedBricks ?? 0,
                snapshot?.IsDefeated ?? false, maintenance, Milliseconds(stepEnd - stepStart), advanced));
    }
    internal static double Milliseconds(long timestampTicks) => timestampTicks * 1000.0 / Stopwatch.Frequency;
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (simulation is not null) simulation.Dispose(); else native.Dispose();
    }
}
