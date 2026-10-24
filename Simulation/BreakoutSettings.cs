using System.Collections.ObjectModel;

namespace BrutalBreakout.Simulation;

/// <summary>An immutable single-hit brick rectangle in logical coordinates, with a top-left origin.</summary>
public sealed class BrickSettings
{
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public BrickSettings(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentException("Brick origin must be finite.");
        Geometry.Positive(width, nameof(width)); Geometry.Positive(height, nameof(height));
        if (!double.IsFinite(x + width) || !double.IsFinite(y + height)) throw new ArgumentException("Brick edges must be finite.");
        (X, Y, Width, Height) = (x, y, width, height);
    }
}

/// <summary>Explicit round rules and layout. Zero max bounces disables the bounce-expiry rule, as in Unity.</summary>
public sealed class BreakoutSettings
{
    // A bounded layout/validation budget, not a default number of bricks.
    public const int MaximumBrickCount = 1024;
    public int StartingBalls { get; }
    public double CatchZoneHeightFraction { get; }
    public int MaxBouncesWithoutBrick { get; }
    public ReadOnlyCollection<BrickSettings> Bricks { get; }

    public BreakoutSettings(int startingBalls, double catchZoneHeightFraction, int maxBouncesWithoutBrick,
        IReadOnlyList<BrickSettings> bricks)
    {
        ArgumentNullException.ThrowIfNull(bricks);
        if (startingBalls <= 0) throw new ArgumentOutOfRangeException(nameof(startingBalls));
        if (!double.IsFinite(catchZoneHeightFraction) || catchZoneHeightFraction <= 0 || catchZoneHeightFraction >= 1)
            throw new ArgumentOutOfRangeException(nameof(catchZoneHeightFraction));
        if (maxBouncesWithoutBrick < 0) throw new ArgumentOutOfRangeException(nameof(maxBouncesWithoutBrick));
        if (bricks.Count == 0 || bricks.Count > MaximumBrickCount) throw new ArgumentOutOfRangeException(nameof(bricks));
        var copy = new BrickSettings[bricks.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = bricks[i] ?? throw new ArgumentException("Bricks cannot contain null.", nameof(bricks));
        Bricks = Array.AsReadOnly(copy);
        (StartingBalls, CatchZoneHeightFraction, MaxBouncesWithoutBrick) = (startingBalls, catchZoneHeightFraction, maxBouncesWithoutBrick);
    }

    internal void Validate(ArenaSettings arena, BallSettings ball)
    {
        double line = arena.Height - arena.Height * CatchZoneHeightFraction;
        float nativeLine = Geometry.Meters(line);
        if (!(line > arena.Top && line <= ball.Spawn.Y) ||
            !(nativeLine > Geometry.Meters(arena.Top) && nativeLine <= Geometry.Meters(ball.Spawn.Y)) ||
            !((float)line > (float)arena.Top && (float)line <= (float)ball.Spawn.Y))
            throw new ArgumentException("The catch line must leave a target area and contain the spawn in its lower zone.");
        for (int i = 0; i < Bricks.Count; i++)
        {
            var b = Bricks[i];
            if (b.X < arena.Left || b.Y < arena.Top || b.X + b.Width > arena.Right || b.Y + b.Height > line)
                throw new ArgumentException("Every brick must fit inside the walls and above the catch line.");
            CheckAxis(b.X, b.Width); CheckAxis(b.Y, b.Height);
            // Full-circle spawn must not intersect even a brick ending exactly on the line.
            double nearestX = Math.Clamp(ball.Spawn.X, b.X, b.X + b.Width);
            double nearestY = Math.Clamp(ball.Spawn.Y, b.Y, b.Y + b.Height);
            double dx = nearestX - ball.Spawn.X, dy = nearestY - ball.Spawn.Y;
            if (dx * dx + dy * dy <= ball.Radius * ball.Radius)
                throw new ArgumentException("The spawn circle must not touch a brick.");
            float cx = Geometry.Meters(b.X + b.Width / 2), cy = Geometry.Meters(b.Y + b.Height / 2);
            float hx = Geometry.Meters(b.Width / 2), hy = Geometry.Meters(b.Height / 2);
            float sx = Geometry.Meters(ball.Spawn.X), sy = Geometry.Meters(ball.Spawn.Y);
            float ndx = Math.Clamp(sx, cx - hx, cx + hx) - sx, ndy = Math.Clamp(sy, cy - hy, cy + hy) - sy;
            float radius = Geometry.NativePositive(ball.Radius);
            if (ndx * ndx + ndy * ndy <= radius * radius)
                throw new ArgumentException("The native spawn circle must not touch a brick after float conversion.");
            for (int j = 0; j < i; j++)
            {
                var other = Bricks[j];
                if (b.X < other.X + other.Width && other.X < b.X + b.Width &&
                    b.Y < other.Y + other.Height && other.Y < b.Y + b.Height)
                    throw new ArgumentException("Brick rectangles must not overlap.");
            }
        }
        static void CheckAxis(double low, double size)
        {
            float high = (float)(low + size), lo = (float)low;
            float center = Geometry.Meters(low + size / 2), half = Geometry.NativePositive(size / 2);
            Geometry.SafeSquare(half); Geometry.SafeSquare(2 * half);
            if (!(lo < high && center - half < center && center + half > center))
                throw new ArgumentException("Brick geometry collapses in float precision.");
        }
    }
}

public enum RoundPhase { Playing, Won, Lost }

/// <summary>Stable layout index and immutable bounds; destroyed bricks retain their IDs until restart.</summary>
public readonly record struct BrickState(int Id, BrickSettings Bounds, bool Alive);
