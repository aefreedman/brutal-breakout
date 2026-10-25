namespace BrutalBreakout.Simulation;

/// <summary>Immutable tuning supplied by the host's authoritative JSON; no runtime defaults.</summary>
public sealed class ShmupSettings
{
    public ShmupArenaSettings Arena { get; }
    public ShmupAvatarSettings Avatar { get; }
    public ShmupBallSettings Ball { get; }
    public ShmupBrickSettings Brick { get; }
    public ShmupDifficultySettings Difficulty { get; }
    public ShmupNoiseSettings Noise { get; }
    public ShmupCapacitySettings Capacity { get; }
    public ShmupSettings(ShmupArenaSettings arena, ShmupAvatarSettings avatar, ShmupBallSettings ball,
        ShmupBrickSettings brick, ShmupDifficultySettings difficulty, ShmupNoiseSettings noise, ShmupCapacitySettings capacity)
    {
        ArgumentNullException.ThrowIfNull(arena); ArgumentNullException.ThrowIfNull(avatar);
        ArgumentNullException.ThrowIfNull(ball); ArgumentNullException.ThrowIfNull(brick);
        ArgumentNullException.ThrowIfNull(difficulty); ArgumentNullException.ThrowIfNull(noise);
        ArgumentNullException.ThrowIfNull(capacity);
        (Arena, Avatar, Ball, Brick, Difficulty, Noise, Capacity) = (arena, avatar, ball, brick, difficulty, noise, capacity);
        if (2 * Math.Max(avatar.Radius, ball.Radius) >= Math.Min(arena.InitialPlayAreaWidth, arena.Height) ||
            brick.Size > arena.InitialPlayAreaWidth || brick.ColumnPitch < brick.Size)
            throw new ArgumentException("Actors must fit the initial area and brick pitch must accommodate brick size.");
        if (Math.Ceiling(arena.Width / brick.ColumnPitch) > 16384 ||
            !double.IsFinite(16384 * noise.ColumnScale) || !double.IsFinite(65536 * noise.RowScale))
            throw new ArgumentException("Row geometry/noise exceeds supported bounds.");
        // Four permanent convex slots per brick, plus ball circles and two walls. Check
        // all supported shapes (including internal test/reuse activation) before native allocation.
        if ((long)capacity.MaxBricks * ShmupBrickGeometry.MaximumRectangles + capacity.MaxBalls + 2 > 65538)
            throw new ArgumentException("Fixture capacity exceeds the bounded native budget.");
        foreach (var shape in Enum.GetValues<ShmupBrickShape>()) for (int q = 0; q < 4; q++)
        {
            var bounds = brick.Geometry.BoundsSize(shape, q);
            Geometry.NativePositive(bounds.X); Geometry.NativePositive(bounds.Y);
            foreach (var rect in brick.Geometry.Rectangles(shape, q))
            {
                float hx = Geometry.NativePositive(rect.HalfExtents.X);
                float hy = Geometry.NativePositive(rect.HalfExtents.Y);
                Geometry.SafeSquare(hx); Geometry.SafeSquare(hy);
                float cx = Geometry.Meters(rect.Center.X);
                float cy = Geometry.Meters(rect.Center.Y);
                if (!(cx - hx < cx && cx + hx > cx && cy - hy < cy && cy + hy > cy))
                    throw new ArgumentException("Native fixture geometry collapses.");
                foreach (double location in new[] { arena.Width + bounds.X, arena.Height + bounds.Y + brick.ColumnPitch })
                {
                    float p = Geometry.Meters(location);
                    if (!(p - hx < p && p + hx > p && p - hy < p && p + hy > p))
                        throw new ArgumentException("Native compound geometry collapses at its position.");
                }
            }
        }
        // Check native float geometry, circle mass/inertia and float positions before creating a world.
        foreach (double size in new[] { avatar.Radius, ball.Radius, brick.Size / 2 })
        {
            float r = Geometry.NativePositive(size);
            Geometry.SafeSquare(r);
            float mass = MathF.PI * r * r, inertia = .5f * mass * r * r;
            if (!float.IsFinite(inertia) || inertia <= 0 || !float.IsFinite(1 / inertia) || !float.IsFinite(1 / mass))
                throw new ArgumentException("Native mass/inertia is not representable.");
            foreach (double center in new[] { arena.Width, arena.Height + brick.Size })
            {
                float c = Geometry.Meters(center);
                if (!(c - r < c && c + r > c)) throw new ArgumentException("Native geometry collapses at its position.");
            }
        }
    }
    public void ValidatePhysics(PhysicsSettings physics)
    {
        ArgumentNullException.ThrowIfNull(physics);
        foreach (double speed in new[] { Ball.Speed, Avatar.MoveSpeed, Difficulty.MaximumDescentSpeed })
        {
            float v = Geometry.NativePositive(speed), travel = v * (float)physics.StepSeconds;
            Geometry.SafeSquare(v);
            if (!float.IsFinite(travel * travel)) throw new ArgumentException("Native displacement overflows.");
        }
        double intervals = Math.Ceiling((Ball.Speed + Difficulty.MaximumDescentSpeed) * physics.StepSeconds / (Ball.Radius / 2));
        float intervalStep = (float)(physics.StepSeconds / Math.Max(1, intervals));
        float intervalSubstep = intervalStep / physics.Substeps;
        if (!double.IsFinite(intervals) || intervals > 4096 || intervalStep <= 0 || intervalSubstep <= 0 ||
            !float.IsFinite(1f / intervalStep) || !float.IsFinite(1f / intervalSubstep))
            throw new ArgumentException("Collision chronology requires at most 4096 representable native intervals per tick.");
        double gridTravel = Difficulty.MaximumDescentSpeed * physics.StepSeconds / Brick.ColumnPitch;
        if (!double.IsFinite(gridTravel) || gridTravel >= 65536)
            throw new ArgumentException("Descent travel exceeds the bounded grid phase budget.");
        // At most one firing/row deadline per tick. Reject undersampling rather than silently drop time.
        if (Ball.FireRate > physics.UpdateHz || Difficulty.MinimumRowInterval < physics.StepSeconds)
            throw new ArgumentException("Firing and row intervals must be at least one physics tick.");
    }
}

public sealed class ShmupArenaSettings
{
    public double Width { get; } public double Height { get; } public double InitialPlayAreaWidth { get; }
    public ShmupArenaSettings(double width, double height, double initialPlayAreaWidth)
    {
        ShmupValidation.Positive(width, nameof(width)); ShmupValidation.Positive(height, nameof(height));
        ShmupValidation.Positive(initialPlayAreaWidth, nameof(initialPlayAreaWidth));
        if (initialPlayAreaWidth > width || width > 1 << 20 || height > 1 << 20) throw new ArgumentOutOfRangeException(nameof(width));
        (Width, Height, InitialPlayAreaWidth) = (width, height, initialPlayAreaWidth);
    }
}
public sealed class ShmupAvatarSettings
{
    public double Radius { get; } public double MoveSpeed { get; } public int Health { get; }
    public int ContactDamage { get; } public double InvulnerabilitySeconds { get; }
    public ShmupAvatarSettings(double radius, double moveSpeed, int health, int contactDamage, double invulnerabilitySeconds)
    {
        ShmupValidation.Positive(radius, nameof(radius)); ShmupValidation.Positive(moveSpeed, nameof(moveSpeed));
        ShmupValidation.Positive(health, nameof(health)); ShmupValidation.Positive(contactDamage, nameof(contactDamage));
        ShmupValidation.Positive(invulnerabilitySeconds, nameof(invulnerabilitySeconds));
        (Radius, MoveSpeed, Health, ContactDamage, InvulnerabilitySeconds) = (radius, moveSpeed, health, contactDamage, invulnerabilitySeconds);
    }
}
public sealed class ShmupBallSettings
{
    public double Radius { get; } public double Speed { get; } public double FireRate { get; }
    public int Damage { get; } public bool BounceOnBricks { get; } public int MaxWallHits { get; }
    public ShmupBallSettings(double radius, double speed, double fireRate, int damage, bool bounceOnBricks, int maxWallHits)
    {
        ShmupValidation.Positive(radius, nameof(radius)); ShmupValidation.Positive(speed, nameof(speed));
        ShmupValidation.Positive(fireRate, nameof(fireRate)); ShmupValidation.Positive(damage, nameof(damage));
        ShmupValidation.Positive(maxWallHits, nameof(maxWallHits));
        (Radius, Speed, FireRate, Damage, BounceOnBricks, MaxWallHits) = (radius, speed, fireRate, damage, bounceOnBricks, maxWallHits);
    }
}
public sealed class ShmupBrickSettings
{
    public ShmupBrickGeometry Geometry { get; }
    public double Size { get; } public double ColumnPitch { get; } public int Health { get; } public int EscapeDamage { get; }
    private readonly ShmupBrickShape[] shapes;
    private readonly int[] quarterTurns;
    public ReadOnlySpan<ShmupBrickShape> Shapes => shapes;
    public ReadOnlySpan<int> QuarterTurns => quarterTurns;
    // Compatibility constructor intentionally retains square-only legacy behavior.
    public ShmupBrickSettings(double size, double columnPitch, int health, int escapeDamage)
        : this(size, columnPitch, health, escapeDamage, [ShmupBrickShape.Square], [0]) { }
    public ShmupBrickSettings(double size, double columnPitch, int health, int escapeDamage,
        IReadOnlyList<ShmupBrickShape> shapes, IReadOnlyList<int> quarterTurns)
    {
        ArgumentNullException.ThrowIfNull(shapes); ArgumentNullException.ThrowIfNull(quarterTurns);
        this.shapes = shapes.ToArray(); this.quarterTurns = quarterTurns.ToArray();
        if (this.shapes.Length == 0 || this.quarterTurns.Length == 0 ||
            this.shapes.Distinct().Count() != this.shapes.Length || this.quarterTurns.Distinct().Count() != this.quarterTurns.Length ||
            this.shapes.Any(s => (uint)s > (uint)ShmupBrickShape.T) || this.quarterTurns.Any(q => (uint)q > 3))
            throw new ArgumentException("Shape and rotation choices must be nonempty, unique and valid.");
        ShmupValidation.Positive(size, nameof(size)); ShmupValidation.Positive(columnPitch, nameof(columnPitch));
        ShmupValidation.Positive(health, nameof(health)); ShmupValidation.Positive(escapeDamage, nameof(escapeDamage));
        (Size, ColumnPitch, Health, EscapeDamage) = (size, columnPitch, health, escapeDamage);
        Geometry = new(size, columnPitch);
    }
}
public sealed class ShmupDifficultySettings
{
    public double RowInterval { get; } public double MinimumRowInterval { get; }
    public double DescentSpeed { get; } public double MaximumDescentSpeed { get; }
    public double RampSeconds { get; } public double IntervalMultiplier { get; } public double SpeedMultiplier { get; }
    public ShmupDifficultySettings(double rowInterval, double minimumRowInterval, double descentSpeed, double maximumDescentSpeed,
        double rampSeconds, double intervalMultiplier, double speedMultiplier)
    {
        ShmupValidation.Positive(rowInterval, nameof(rowInterval)); ShmupValidation.Positive(minimumRowInterval, nameof(minimumRowInterval));
        ShmupValidation.Positive(descentSpeed, nameof(descentSpeed)); ShmupValidation.Positive(maximumDescentSpeed, nameof(maximumDescentSpeed));
        ShmupValidation.Positive(rampSeconds, nameof(rampSeconds)); ShmupValidation.Positive(intervalMultiplier, nameof(intervalMultiplier));
        ShmupValidation.Positive(speedMultiplier, nameof(speedMultiplier));
        if (minimumRowInterval > rowInterval || maximumDescentSpeed < descentSpeed || intervalMultiplier > 1 || speedMultiplier < 1)
            throw new ArgumentException("Difficulty must increase monotonically within its limits.");
        (RowInterval, MinimumRowInterval, DescentSpeed, MaximumDescentSpeed, RampSeconds, IntervalMultiplier, SpeedMultiplier) =
            (rowInterval, minimumRowInterval, descentSpeed, maximumDescentSpeed, rampSeconds, intervalMultiplier, speedMultiplier);
    }
}
public sealed class ShmupNoiseSettings
{
    public int Seed { get; } public double ColumnScale { get; } public double RowScale { get; } public double GapThreshold { get; }
    public ShmupNoiseSettings(int seed, double columnScale, double rowScale, double gapThreshold)
    {
        ShmupValidation.Positive(columnScale, nameof(columnScale)); ShmupValidation.Positive(rowScale, nameof(rowScale));
        if (!double.IsFinite(gapThreshold) || gapThreshold < 0 || gapThreshold > 1) throw new ArgumentOutOfRangeException(nameof(gapThreshold));
        (Seed, ColumnScale, RowScale, GapThreshold) = (seed, columnScale, rowScale, gapThreshold);
    }
}
public sealed class ShmupCapacitySettings
{
    public int MaxBalls { get; } public int MaxBricks { get; }
    public ShmupCapacitySettings(int maxBalls, int maxBricks)
    {
        ShmupValidation.Positive(maxBalls, nameof(maxBalls)); ShmupValidation.Positive(maxBricks, nameof(maxBricks));
        if ((long)maxBalls + maxBricks > 16384 || (long)maxBalls * maxBricks > 4_194_304)
            throw new ArgumentException("Capacity exceeds the bounded entity/contact budget.");
        (MaxBalls, MaxBricks) = (maxBalls, maxBricks);
    }
}
internal static class ShmupValidation
{
    internal static void Positive(double value, string name) { if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name); }
    internal static void Positive(int value, string name) { if (value <= 0) throw new ArgumentOutOfRangeException(name); }
}
