using Brutal.Numerics;

namespace BrutalBreakout.Simulation;

public readonly record struct WallContact(double Distance, double2 Position, double2 ReflectedDirection)
{
    public bool HasHit { get; init; } = true;
}

/// <summary>Provisional closed sandbox. Logical units, +X right, +Y down. Bounds describe wall faces.</summary>
public static class Arena
{
    public const double Width = 1280, Height = 720, Left = 48, Top = 48, Right = 1232, Bottom = 672;
    public const double Radius = 12, Speed = 600;
    public const double MinX = Left + Radius, MaxX = Right - Radius;
    public const double MinY = Top + Radius, MaxY = Bottom - Radius;

    // Published-package audit (2026-10-24): inspected exact published API dumps in
    // C:/tmp/brutal-published-audit-20260804/Brutal.{Numerics,Maths}.api.txt and
    // Numerics/lib/net10.0/Brutal.Numerics.{dll,xml}, package version 2026.10.2.
    // Numerics double2 exports Normalize, NormalizeOrZero, Reflect, Distance and vector operators.
    // Searches for sweep/collision/intersect/clock/timer/accumulator/fixed found no relevant exports;
    // ray matches were Grayscale, not ray geometry. Maths 2026.10.3 exports only RandomExtensions.
    // Limited custom gap: radius-inset rectangle time-of-contact and bounded game tick scheduling.
    // No native code/probe execution required; this library/tests compile against the locked package.

    /// <summary>First ball-center wall contact, including immediate outward boundary contact.
    /// Direction must be finite and nonzero; position must be inside the radius-inset arena.</summary>
    public static WallContact Query(double2 position, double2 direction)
    {
        if (!Finite(position) || position.X < MinX || position.X > MaxX || position.Y < MinY || position.Y > MaxY)
            throw new ArgumentOutOfRangeException(nameof(position));
        if (!Finite(direction) || (direction.X == 0 && direction.Y == 0))
            throw new ArgumentOutOfRangeException(nameof(direction));
        return QueryUnit(position, Normalize(direction));
    }

    internal static bool Finite(double2 v) => double.IsFinite(v.X) && double.IsFinite(v.Y);

    // Scale before the published normalization helper to avoid overflow/underflow for finite inputs.
    internal static double2 Normalize(double2 v) => double2.Normalize(v / Math.Max(Math.Abs(v.X), Math.Abs(v.Y)));

    internal static WallContact QueryUnit(double2 p, double2 d)
    {
        double x = d.X > 0 ? (MaxX - p.X) / d.X : d.X < 0 ? (MinX - p.X) / d.X : double.PositiveInfinity;
        double y = d.Y > 0 ? (MaxY - p.Y) / d.Y : d.Y < 0 ? (MinY - p.Y) / d.Y : double.PositiveInfinity;
        double distance = Math.Min(x, y);
        // Relative tolerance merges round-off at simultaneous corners, not separate visible impacts.
        double tolerance = 1e-12 * Math.Max(1, distance);
        bool hitX = Math.Abs(x - distance) <= tolerance;
        bool hitY = Math.Abs(y - distance) <= tolerance;
        double2 contact = p + d * distance;
        if (hitX) contact.X = d.X > 0 ? MaxX : MinX;
        if (hitY) contact.Y = d.Y > 0 ? MaxY : MinY;
        double2 reflected = d;
        if (hitX) reflected = double2.Reflect(reflected, double2.UnitX);
        if (hitY) reflected = double2.Reflect(reflected, double2.UnitY);
        return new(distance, contact, reflected);
    }
}
