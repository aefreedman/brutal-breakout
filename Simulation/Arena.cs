using Brutal.Numerics;

namespace BrutalBreakout.Simulation;

public readonly record struct WallContact(double Distance, double2 Position, double2 ReflectedDirection)
{
    public bool HasHit { get; init; } = true;
    /// <summary>Native surface normal pointing into free space. Zero when HasHit is false.</summary>
    public double2 Normal { get; init; }
}

/// <summary>Provisional closed sandbox. Logical units, +X right, +Y down. Bounds describe wall faces.</summary>
public static class Arena
{
    public const double Width = 1280, Height = 720, Left = 48, Top = 48, Right = 1232, Bottom = 672;
    public const double Radius = 12, Speed = 600;
    public const double MinX = Left + Radius, MaxX = Right - Radius;
    public const double MinY = Top + Radius, MaxY = Bottom - Radius;

    internal static bool Finite(double2 v) => double.IsFinite(v.X) && double.IsFinite(v.Y);

    // Scale before the published normalization helper to avoid overflow/underflow for finite inputs.
    internal static double2 Normalize(double2 v) => double2.Normalize(v / Math.Max(Math.Abs(v.X), Math.Abs(v.Y)));

}
