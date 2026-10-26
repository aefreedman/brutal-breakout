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
    private double width, time, fireDelay, rowDelay, beltPhase, runMinimumWidth;
    private long beltRow, lastSpawnGridRow;
    private readonly ShmupAvatarController avatar;
    private long tick, row, droppedBalls, droppedBricks;
    public ShmupSettings Settings { get; private set; }
    public PhysicsSettings Physics { get; private set; }
    public ExecutionSettings Execution { get; private set; }
    public double StepSeconds => Physics.StepSeconds;
    public double RunMinimumWidth => runMinimumWidth;
    public ShmupSnapshot Snapshot => Volatile.Read(ref snapshot);
    /// <summary>Facts from the last command; cleared even when a defeated run cannot advance.</summary>
    public ShmupEvents Events { get; private set; }
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
    public void ApplyTuning(PhysicsSettings physics, ShmupSettings settings, ExecutionSettings? execution = null)
    {
        lock (gate)
        {
            Guard();
            TuningCompatibility.Physics(Physics, physics);
            if (execution is not null) TuningCompatibility.Execution(Execution, execution);
            TuningCompatibility.Shmup(Settings, settings, width);
            settings.ValidatePhysics(physics);
            double oldInterval = RowInterval, oldFire = 1 / Settings.Ball.FireRate;
            native.ApplyTuning(physics, settings, width);
            Physics = physics; Settings = settings; Execution = execution ?? Execution;
            avatar.ApplyTuning(settings, width);
            // Preserve fraction of an outstanding deadline; never reset elapsed time/grid phase.
            if (rowDelay > 0 && oldInterval != RowInterval) rowDelay = Math.Clamp(rowDelay / oldInterval * RowInterval, physics.StepSeconds, RowInterval);
            if (fireDelay > 0 && oldFire != 1 / settings.Ball.FireRate) fireDelay = Math.Clamp(fireDelay / oldFire / settings.Ball.FireRate, physics.StepSeconds, 1 / settings.Ball.FireRate);
            Publish();
        }
    }
    public void Restart() { lock (gate) { Guard(); Reset(); Events = ShmupEvents.Restart; } }
    private void Reset()
    {
        Events = ShmupEvents.None;
        native.Reset();
        // Retain generations across restart: a retained publication never aliases another life.
        for (int i = 0; i < balls.Length; i++) { long generation = Ball(i).Generation; Ball(i) = new() { Generation = generation }; }
        for (int i = 0; i < bricks.Length; i++) { long generation = Brick(i).Generation; Brick(i) = new() { Generation = generation }; }
        width = runMinimumWidth = Settings.Arena.InitialPlayAreaWidth; native.Widen(width);
        time = fireDelay = rowDelay = beltPhase = 0;
        beltRow = 0; lastSpawnGridRow = -1;
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
            Events = ShmupEvents.None;
            if (command.RestartPressed) { Reset(); Events = ShmupEvents.Restart; return; }
            if (avatar.Health == 0) return; // Freeze everything, including clocks and publication identity.
            double dt = StepSeconds;
            tick++; time += dt;
            avatar.Advance(command, dt, width);
            rowDelay -= dt;
            if (rowDelay <= 0) { SpawnRow(); rowDelay += RowInterval; }
            fireDelay = Math.Max(0, fireDelay - dt);
            if (command.FireHeld && fireDelay <= dt * 1e-9) { Fire(); fireDelay = 1 / Settings.Ball.FireRate; }
            double speed = DescentSpeed;
            native.Step(speed, avatar.PreviousPosition, avatar.Position);
            double travel = beltPhase + speed * dt;
            long rows = (long)Math.Floor(travel / Settings.Brick.ColumnPitch);
            beltPhase = travel % Settings.Brick.ColumnPitch;
            beltRow += rows; // Integer identity only; position math always uses the bounded phase.
            Resolve(); Publish();
        }
    }
    private void SpawnRow()
    {
        double size = Settings.Brick.Size, pitch = Settings.Brick.ColumnPitch;
        long gridRow = beltRow + (beltPhase > 0 ? 1 : 0);
        row++; // Deadlines remain independent of the physical grid; repeated rows never refill gaps.
        if (gridRow == lastSpawnGridRow) return;
        lastSpawnGridRow = gridRow;
        double bottomCellY = beltPhase - (beltPhase > 0 ? pitch : 0) - size / 2;
        double origin = Settings.Arena.Width / 2, left = origin - width / 2, right = origin + width / 2;
        int first = (int)Math.Ceiling((left + size / 2 - origin) / pitch);
        int last = (int)Math.Floor((right - size / 2 - origin) / pitch);
        // Coordinates and choices use GLOBAL signed lattice indices, never the unlocked column count.
        for (int column = first; column <= last; column++)
        {
            if (ShmupNoise.Sample(Settings.Noise.Seed, column * Settings.Noise.ColumnScale,
                (gridRow % 65536) * Settings.Noise.RowScale) < Settings.Noise.GapThreshold) continue;
            uint choice = Choice(Settings.Noise.Seed, column, gridRow);
            var shape = Settings.Brick.Shapes[(int)(choice % (uint)Settings.Brick.Shapes.Length)];
            int turns = Settings.Brick.QuarterTurns[(int)((choice / 17) % (uint)Settings.Brick.QuarterTurns.Length)];
            var bounds = Settings.Brick.Geometry.BoundsSize(shape, turns);
            var cells = ShmupBrickGeometry.CellDimensions(shape, turns);
            double2 position = new(origin + column * pitch + (cells.Columns - 1) * pitch / 2,
                bottomCellY - (cells.Rows - 1) * pitch / 2);
            if (position.X - bounds.X / 2 < left || position.X + bounds.X / 2 > right) continue;
            // Conservative reservations may leave gaps but never move occupied cells off the global grid.
            bool blocked = false;
            for (int i = 0; i < bricks.Length; i++)
            {
                ref var brick = ref Brick(i); if (!brick.Active) continue;
                var other = Settings.Brick.Geometry.BoundsSize(brick.Shape, brick.QuarterTurns);
                var delta = native.BrickPosition(i) - position;
                if (Math.Abs(delta.X) < (bounds.X + other.X) / 2 + 1e-4 &&
                    Math.Abs(delta.Y) < (bounds.Y + other.Y) / 2 + 1e-4) { blocked = true; break; }
            }
            if (!blocked) SpawnBrick(position, shape, turns);
        }
    }
    private static uint Choice(int seed, int column, long row)
    {
        unchecked
        {
            uint x = (uint)seed ^ (uint)column * 0x9e3779b9U ^ (uint)row * 0x85ebca6bU ^ (uint)(row >> 32);
            x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; return x ^ (x >> 16);
        }
    }
    private void Fire()
    {
        // Spawn at the avatar center: no own-body collision, and every firing direction remains inside the area.
        if (SpawnBall(avatar.Position, avatar.Aim) < 0) droppedBalls++;
        else Events |= ShmupEvents.Fire;
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
    internal int SpawnBrick(double2 position, ShmupBrickShape shape = ShmupBrickShape.Square, int quarterTurns = 0)
    {
        _ = Settings.Brick.Geometry.Rectangles(shape, quarterTurns);
        for (int i = 0; i < bricks.Length; i++)
        {
            ref var brick = ref Brick(i);
            if (brick.Active || brick.Generation == long.MaxValue) continue;
            brick = new() { Active = true, Generation = brick.Generation + 1, Health = Settings.Brick.Health, InitialHealth = Settings.Brick.Health, Shape = shape, QuarterTurns = quarterTurns };
            native.ActivateBrick(i, position, shape, quarterTurns); return i;
        }
        droppedBricks++; return -1;
    }
    private void Resolve()
    {
        int healthBefore = avatar.Health;
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
                Events |= ShmupEvents.BrickHit;
                brick.Health = (int)Math.Max(0L, (long)brick.Health - (long)Settings.Ball.Damage * hits);
            }
            // Native interval chronology owns the streak; ECS mirrors the final owned fact.
            ball.WallHits = native.WallStreak[i];
        }
        avatar.Contact(native.AvatarContact, StepSeconds);
        for (int j = 0; j < bricks.Length; j++)
        {
            ref var brick = ref Brick(j); if (!brick.Active) continue;
            bool escaped = native.BrickPosition(j).Y - Settings.Brick.Geometry.BoundsSize(brick.Shape, brick.QuarterTurns).Y / 2 > Settings.Arena.Height;
            if (brick.Health == 0 || escaped)
            {
                // A killed brick does not also inflict escape damage in the same tick.
                if (escaped && brick.Health > 0) avatar.Damage(Settings.Brick.EscapeDamage);
                if (brick.Health == 0) Events |= ShmupEvents.BrickDestroyed;
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
        if (avatar.Health < healthBefore) Events |= ShmupEvents.PlayerDamaged;
        if (avatar.Health == 0) Events |= ShmupEvents.Defeat;
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
            if (brick.Active) brickStates.Add(new(i, brick.Generation, native.BrickPosition(i), Settings.Brick.Size, brick.Health, brick.Shape, brick.QuarterTurns, brick.InitialHealth));
        }
        Volatile.Write(ref snapshot, new(width, Settings.Arena.Height, avatar.Position, avatar.Aim, avatar.Health, time, Settings.Ball.Speed,
            DescentSpeed, RowInterval, avatar.InvulnerabilityRemaining, tick, row, droppedBalls, droppedBricks,
            world.Size, native.BodyCount, ballStates.ToArray(), brickStates.ToArray(), native.Preview(avatar.Position, avatar.Aim), avatar.MaximumHealth));
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
