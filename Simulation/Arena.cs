using Brutal.Numerics;

namespace BrutalBreakout.Simulation;

public readonly record struct WallContact(double Distance, double2 Position, double2 ReflectedDirection)
{
    public bool HasHit { get; init; } = true;
    /// <summary>Native surface normal pointing into free space. Zero when HasHit is false.</summary>
    public double2 Normal { get; init; }
}

/// <summary>Mathematical helpers for logical coordinates (+X right, +Y down).</summary>
public static class Arena
{
    internal static bool Finite(double2 v) => double.IsFinite(v.X) && double.IsFinite(v.Y);

    // Scale before the published normalization helper to avoid overflow/underflow for finite inputs.
    internal static double2 Normalize(double2 v) => double2.Normalize(v / Math.Max(Math.Abs(v.X), Math.Abs(v.Y)));

}
