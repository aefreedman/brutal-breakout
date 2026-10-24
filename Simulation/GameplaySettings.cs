using Brutal.Numerics;

namespace BrutalBreakout.Simulation;

/// <summary>Logical canvas and inner wall faces. No implicit defaults.</summary>
public sealed class ArenaSettings
{
    public double Width { get; }
    public double Height { get; }
    public double Left { get; }
    public double Top { get; }
    public double Right { get; }
    public double Bottom { get; }
    public double WallThickness { get; }

    public ArenaSettings(double width, double height, double left, double top, double right, double bottom, double wallThickness)
    {
        Geometry.Positive(width, nameof(width)); Geometry.Positive(height, nameof(height));
        Geometry.Positive(wallThickness, nameof(wallThickness));
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom) ||
            left >= right || top >= bottom) throw new ArgumentException("Wall faces must be finite and ordered.");
        // ShapeDrawList's existing coordinate envelope, not a gameplay tuning range.
        if (width > 1 << 20 || height > 1 << 20) throw new ArgumentOutOfRangeException(nameof(width), "Canvas exceeds the renderer coordinate envelope (2^20).");
        (Width, Height, Left, Top, Right, Bottom, WallThickness) = (width, height, left, top, right, bottom, wallThickness);
    }
}

public sealed class BallSettings
{
    public double Radius { get; }
    public double Speed { get; }
    public double2 Spawn { get; }
    public double2 InitialAim { get; }
    public BallSettings(double radius, double speed, double spawnX, double spawnY, double initialAimX, double initialAimY)
    {
        Geometry.Positive(radius, nameof(radius)); Geometry.Positive(speed, nameof(speed));
        Spawn = new(spawnX, spawnY);
        var aim = new double2(initialAimX, initialAimY);
        if (!Arena.Finite(Spawn)) throw new ArgumentException("Spawn must be finite.");
        if (!Arena.Finite(aim) || (aim.X == 0 && aim.Y == 0)) throw new ArgumentException("Initial aim must be finite and nonzero.");
        InitialAim = Arena.Normalize(aim);
        Radius = radius; Speed = speed;
    }
}

/// <summary>Cross-validated geometry, safe before native world allocation.</summary>
public sealed class GameplaySettings
{
    public ArenaSettings Arena { get; }
    public BallSettings Ball { get; }
    public double MinX => Arena.Left + Ball.Radius;
    public double MaxX => Arena.Right - Ball.Radius;
    public double MinY => Arena.Top + Ball.Radius;
    public double MaxY => Arena.Bottom - Ball.Radius;
    internal float NativeRadius { get; }
    internal float NativeSpeed { get; }
    internal float CastMeters { get; }
    internal float2 NativeSpawn { get; }

    public GameplaySettings(ArenaSettings arena, BallSettings ball)
    {
        ArgumentNullException.ThrowIfNull(arena); ArgumentNullException.ThrowIfNull(ball);
        Arena = arena; Ball = ball;
        double t = arena.WallThickness;
        if (arena.Left - t < 0 || arena.Top - t < 0 || arena.Right + t > arena.Width || arena.Bottom + t > arena.Height)
            throw new ArgumentException("Walls must fit the canvas.");
        if (!(MinX < MaxX && MinY < MaxY) || ball.Spawn.X < MinX || ball.Spawn.X > MaxX || ball.Spawn.Y < MinY || ball.Spawn.Y > MaxY)
            throw new ArgumentException("Ball requires a nonempty radius-inset area containing its spawn.");
        NativeRadius = Geometry.NativePositive(ball.Radius);
        NativeSpeed = Geometry.NativePositive(ball.Speed);
        // Box2D uses squared lengths, circle mass/inertia, and their reciprocals in float arithmetic.
        float speedSquared = NativeSpeed * NativeSpeed;
        if (!float.IsFinite(speedSquared) || speedSquared == 0)
            throw new ArgumentException("Native squared speed must be positive and finite.");
        float radiusSquared = NativeRadius * NativeRadius;
        Geometry.SafeSquare(radiusSquared);
        float mass = MathF.PI * radiusSquared, inertia = .5f * mass * radiusSquared;
        if (!float.IsFinite(mass) || !float.IsFinite(inertia) || inertia <= 0 || !float.IsFinite(1f / mass) || !float.IsFinite(1f / inertia))
            throw new ArgumentException("Native circle mass/inertia and inverses must be finite.");
        NativeSpawn = new(Geometry.Meters(ball.Spawn.X), Geometry.Meters(ball.Spawn.Y));
        CastMeters = Geometry.NativePositive(Math.Sqrt(arena.Width * arena.Width + arena.Height * arena.Height) + 2 * ball.Radius);
        Geometry.SafeSquare(CastMeters);
        // Check both float-rendered and native inner faces/outer faces/insets. Tiny geometry at
        // a large offset must not collapse after conversion, even if the double inputs differ.
        CheckAxis(arena.Left, arena.Right, ball.Spawn.X);
        CheckAxis(arena.Top, arena.Bottom, ball.Spawn.Y);
        void CheckAxis(double low, double high, double spawn)
        {
            foreach (double scale in new[] { 1.0, (double)NativeArena.UnitsPerMeter })
            {
                float outerLow = (float)((low - t) / scale), innerLow = (float)(low / scale);
                float outerHigh = (float)((high + t) / scale), innerHigh = (float)(high / scale);
                float min = (float)((low + ball.Radius) / scale), max = (float)((high - ball.Radius) / scale);
                float s = (float)(spawn / scale);
                if (!(outerLow < innerLow && innerLow < min && min < max && max < innerHigh && innerHigh < outerHigh && s >= min && s <= max))
                    throw new ArgumentException("Geometry collapses or spawn leaves bounds in float precision.");
            }
            // Native rectangles are local half extents plus float body positions.
            CheckBox((low + high) / 2, (high - low) / 2 + t);
            CheckBox(low - t / 2, t / 2);
            CheckBox(high + t / 2, t / 2);
        }
        static void CheckBox(double center, double half)
        {
            float c = Geometry.Meters(center), h = Geometry.NativePositive(half);
            Geometry.SafeSquare(h);
            Geometry.SafeSquare(2 * h);
            if (!(c - h < c && c + h > c)) throw new ArgumentException("Native wall half extent collapses at its position.");
        }
    }
}

internal static class Geometry
{
    internal static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }
    internal static float Meters(double value) => (float)(value / NativeArena.UnitsPerMeter);
    internal static float NativePositive(double value)
    {
        float result = Meters(value);
        if (!float.IsFinite(result) || result <= 0) throw new ArgumentException("Native geometry/speed must be a positive finite float.");
        return result;
    }
    internal static void SafeSquare(float value)
    {
        float square = value * value;
        if (!float.IsFinite(square) || square <= 0 || !float.IsFinite(1f / square))
            throw new ArgumentException("Native squared geometry/speed and inverse must be positive finite floats.");
    }
}
