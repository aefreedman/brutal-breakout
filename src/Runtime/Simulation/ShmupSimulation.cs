using Arch.Core;
using Brutal.Numerics;

namespace BrutalBreakout.Simulation;

/// <summary>Synchronous, headless shmup mode. A fixed set of Arch entities and native bodies
/// is reused in place. Saturation drops new spawns (no deferred burst); publications own their data.</summary>
public sealed class ShmupSimulation : IDisposable
{
    private readonly object gate = new();
    private readonly World world;
    private readonly NativeShmupArena native;
    private readonly Entity[] balls, bricks;
    private ShmupSnapshot snapshot = null!;
    private bool disposed;
    private double width, time, fireDelay, rowDelay;
    private readonly ShmupAvatarController avatar;
    private long tick, row, droppedBalls, droppedBricks;
    public ShmupSettings Settings { get; }
    public PhysicsSettings Physics { get; }
    public ExecutionSettings Execution { get; }
    public double StepSeconds => Physics.StepSeconds;
    public ShmupSnapshot Snapshot => Volatile.Read(ref snapshot);
    private double DescentSpeed => Math.Min(Settings.Difficulty.MaximumDescentSpeed,
        Settings.Difficulty.DescentSpeed * Math.Pow(Settings.Difficulty.SpeedMultiplier, time / Settings.Difficulty.RampSeconds));
    private double RowInterval => Math.Max(Settings.Difficulty.MinimumRowInterval,
        Settings.Difficulty.RowInterval * Math.Pow(Settings.Difficulty.IntervalMultiplier, time / Settings.Difficulty.RampSeconds));
    private ref ShmupBallComponent Ball(int id) => ref world.Get<ShmupBallComponent>(balls[id]);
    private ref ShmupBrickComponent Brick(int id) => ref world.Get<ShmupBrickComponent>(bricks[id]);

    public ShmupSimulation(PhysicsSettings physics, ShmupSettings settings, ExecutionSettings execution)
    {
        ArgumentNullException.ThrowIfNull(physics); ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(execution);
        settings.ValidatePhysics(physics); // All managed validation precedes ECS/native allocation.
        (Physics, Settings, Execution) = (physics, settings, execution);
        avatar = new(settings);
        world = World.Create();
        try
        {
            balls = new Entity[settings.Capacity.MaxBalls]; bricks = new Entity[settings.Capacity.MaxBricks];
            for (int i = 0; i < balls.Length; i++) balls[i] = world.Create(new ShmupBallComponent());
            for (int i = 0; i < bricks.Length; i++) bricks[i] = world.Create(new ShmupBrickComponent());
            native = new(physics, settings, execution);
        }
        catch { World.Destroy(world); throw; }
        try { Reset(); }
        catch { native.Dispose(); World.Destroy(world); throw; }
    }
    private void Guard() { ObjectDisposedException.ThrowIf(disposed, this); native.Guard(); }
    public void Restart() { lock (gate) { Guard(); Reset(); } }
    private void Reset()
    {
        native.Reset();
        // Retain generations across restart: a retained publication never aliases another life.
        for (int i = 0; i < balls.Length; i++) { long generation = Ball(i).Generation; Ball(i) = new() { Generation = generation }; }
        for (int i = 0; i < bricks.Length; i++) { long generation = Brick(i).Generation; Brick(i) = new() { Generation = generation }; }
        width = Settings.Arena.InitialPlayAreaWidth; native.Widen(width);
        time = fireDelay = rowDelay = 0;
        tick = row = droppedBalls = droppedBricks = 0;
        avatar.Reset();
        Publish();
    }
    /// <summary>Width is centered on the configured canvas. Narrowing/nonfinite/out-of-canvas widths are rejected.</summary>
    public void Widen(double width)
    {
        lock (gate)
        {
            Guard();
            if (!double.IsFinite(width) || width < this.width || width > Settings.Arena.Width) throw new ArgumentOutOfRangeException(nameof(width));
            if (width == this.width) return;
            native.Widen(width); this.width = width; Publish();
        }
    }
    public void Step(ShmupCommand command = default)
    {
        lock (gate)
        {
            Guard();
            if (command.RestartPressed) { Reset(); return; }
            if (avatar.Health == 0) return; // Freeze everything, including clocks and publication identity.
            double dt = StepSeconds;
            tick++; time += dt;
            avatar.Advance(command, dt, width);
            rowDelay -= dt;
            if (rowDelay <= 0) { SpawnRow(); rowDelay += RowInterval; }
            fireDelay = Math.Max(0, fireDelay - dt);
            if (command.FireHeld && fireDelay <= dt * 1e-9) { Fire(); fireDelay = 1 / Settings.Ball.FireRate; }
            native.Step(DescentSpeed, avatar.PreviousPosition, avatar.Position);
            Resolve(); Publish();
        }
    }
    private void SpawnRow()
    {
        double size = Settings.Brick.Size, pitch = Settings.Brick.ColumnPitch;
        int columns = (int)Math.Floor((width - size) / pitch) + 1;
        double start = Settings.Arena.Width / 2 - (columns - 1) * pitch / 2;
        // Noise coordinates are row/column indices, not pixel positions; seeded gaps are resolution-independent.
        for (int column = 0; column < columns; column++)
        {
            if (ShmupNoise.Sample(Settings.Noise.Seed, column * Settings.Noise.ColumnScale,
                (row % 65536) * Settings.Noise.RowScale) < Settings.Noise.GapThreshold) continue;
            SpawnBrick(new(start + column * pitch, -size / 2));
        }
        row++;
    }
    private void Fire()
    {
        // Spawn at the avatar center: no own-body collision, and every firing direction remains inside the area.
        if (SpawnBall(avatar.Position, avatar.Aim) < 0) droppedBalls++;
    }
    internal int SpawnBall(double2 position, double2 direction)
    {
        for (int i = 0; i < balls.Length; i++)
        {
            ref var ball = ref Ball(i);
            if (ball.Active || ball.Generation == long.MaxValue) continue;
            long generation = ball.Generation + 1;
            ball = new() { Active = true, Generation = generation }; // Reset wall streak and all per-life facts.
            native.ActivateBall(i, position, Arena.Normalize(direction)); return i;
        }
        return -1;
    }
    internal int SpawnBrick(double2 position)
    {
        for (int i = 0; i < bricks.Length; i++)
        {
            ref var brick = ref Brick(i);
            if (brick.Active || brick.Generation == long.MaxValue) continue;
            brick = new() { Active = true, Generation = brick.Generation + 1, Health = Settings.Brick.Health };
            native.ActivateBrick(i, position); return i;
        }
        droppedBricks++; return -1;
    }
    private void Resolve()
    {
        // First gather every pair's damage without disabling/reusing resources. Brick interaction wins
        // over simultaneous wall contacts and resets the consecutive wall streak deterministically.
        for (int i = 0; i < balls.Length; i++)
        {
            ref var ball = ref Ball(i); if (!ball.Active) continue;
            for (int j = 0; j < bricks.Length; j++)
            {
                ref var brick = ref Brick(j);
                int hits = native.BrickHits[i, j];
                if (!brick.Active || hits == 0) continue;
                brick.Health = (int)Math.Max(0L, (long)brick.Health - (long)Settings.Ball.Damage * hits);
            }
            // Native interval chronology owns the streak; ECS mirrors the final owned fact.
            ball.WallHits = native.WallStreak[i];
        }
        avatar.Contact(native.AvatarContact, StepSeconds);
        for (int j = 0; j < bricks.Length; j++)
        {
            ref var brick = ref Brick(j); if (!brick.Active) continue;
            bool escaped = native.BrickPosition(j).Y - Settings.Brick.Size / 2 > Settings.Arena.Height;
            if (brick.Health == 0 || escaped)
            {
                // A killed brick does not also inflict escape damage in the same tick.
                if (escaped && brick.Health > 0) avatar.Damage(Settings.Brick.EscapeDamage);
                native.ClearBrick(j); brick.Active = false;
            }
        }
        for (int i = 0; i < balls.Length; i++)
        {
            ref var ball = ref Ball(i); if (!ball.Active) continue;
            double y = native.BallPosition(i).Y, r = Settings.Ball.Radius;
            if (y + r < 0 || y - r > Settings.Arena.Height || native.Expired[i])
            { native.ClearBall(i); ball.Active = false; }
        }
    }
    private void Publish()
    {
        var ballStates = new List<ShmupBallState>(); var brickStates = new List<ShmupBrickState>();
        for (int i = 0; i < balls.Length; i++)
        {
            ref var ball = ref Ball(i);
            if (ball.Active) ballStates.Add(new(i, ball.Generation, native.BallPosition(i), native.BallVelocity(i), Settings.Ball.Radius, ball.WallHits));
        }
        for (int i = 0; i < bricks.Length; i++)
        {
            ref var brick = ref Brick(i);
            if (brick.Active) brickStates.Add(new(i, brick.Generation, native.BrickPosition(i), Settings.Brick.Size, brick.Health));
        }
        Volatile.Write(ref snapshot, new(width, Settings.Arena.Height, avatar.Position, avatar.Aim, avatar.Health, time, Settings.Ball.Speed,
            DescentSpeed, RowInterval, avatar.InvulnerabilityRemaining, tick, row, droppedBalls, droppedBricks,
            world.Size, native.BodyCount, ballStates.ToArray(), brickStates.ToArray()));
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            try { native.Dispose(); } finally { World.Destroy(world); disposed = true; }
        }
    }
}
