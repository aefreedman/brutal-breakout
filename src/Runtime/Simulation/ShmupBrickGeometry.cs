using Brutal.Numerics;

namespace BrutalBreakout.Simulation;

public enum ShmupBrickShape { Square, TwoByOne, TwoByTwo, L, T }
public readonly record struct ShmupBrickRect(double2 Center, double2 HalfExtents);

/// <summary>Immutable logical geometry: grid squares joined by solid bridges, with concave cutouts preserved.</summary>
public sealed class ShmupBrickGeometry
{
    public const int MaximumRectangles = 4;
    private readonly ShmupBrickRect[][] geometry = new ShmupBrickRect[20][];
    private readonly double size, pitch;
    public ShmupBrickGeometry(double size, double pitch)
    {
        ShmupValidation.Positive(size, nameof(size)); ShmupValidation.Positive(pitch, nameof(pitch));
        if (pitch < size || !double.IsFinite(2 * pitch + size)) throw new ArgumentOutOfRangeException(nameof(pitch));
        (this.size, this.pitch) = (size, pitch);
        for (int s = 0; s < 5; s++) for (int q = 0; q < 4; q++)
        {
            var shape = (ShmupBrickShape)s;
            ShmupBrickRect[] parts = shape switch
            {
                ShmupBrickShape.L => [new(new(-pitch / 2, 0), new(size / 2, (2 * pitch + size) / 2)),
                    new(new(size / 2, pitch), new(pitch / 2, size / 2))],
                ShmupBrickShape.T => [new(new(0, -pitch / 2), new((2 * pitch + size) / 2, size / 2)),
                    new(new(0, size / 2), new(size / 2, pitch / 2))],
                _ => [new(default, BoundsSize(shape, 0) / 2)]
            };
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                for (int turn = 0; turn < q; turn++) p = new(new(-p.Center.Y, p.Center.X), new(p.HalfExtents.Y, p.HalfExtents.X));
                parts[i] = p;
            }
            geometry[s * 4 + q] = parts;
        }
    }
    public ReadOnlySpan<ShmupBrickRect> Rectangles(ShmupBrickShape shape, int quarterTurns)
    {
        Validate(shape, quarterTurns);
        return geometry[(int)shape * 4 + quarterTurns];
    }
    public double2 BoundsSize(ShmupBrickShape shape, int quarterTurns)
    {
        var cells = CellDimensions(shape, quarterTurns);
        return new((cells.Columns - 1) * pitch + size, (cells.Rows - 1) * pitch + size);
    }
    public static (int Columns, int Rows) CellDimensions(ShmupBrickShape shape, int quarterTurns)
    {
        Validate(shape, quarterTurns);
        (int columns, int rows) = shape switch
        {
            ShmupBrickShape.Square => (1, 1), ShmupBrickShape.TwoByOne => (2, 1),
            ShmupBrickShape.TwoByTwo => (2, 2), ShmupBrickShape.L => (2, 3), _ => (3, 2)
        };
        return quarterTurns % 2 == 0 ? (columns, rows) : (rows, columns);
    }
    private static void Validate(ShmupBrickShape shape, int turns)
    {
        if ((uint)shape > (uint)ShmupBrickShape.T || (uint)turns > 3) throw new ArgumentOutOfRangeException(nameof(shape));
    }
}
