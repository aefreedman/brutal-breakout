using Brutal.Numerics;

namespace BrutalBreakout.Simulation;

public enum ShmupBrickShape { Square, TwoByOne, TwoByTwo, L, T }
public readonly record struct ShmupBrickRect(double2 Center, double2 HalfExtents);

/// <summary>Immutable unit-cell geometry, centered on its rotated bounding box. Joined cells have no pitch padding.</summary>
public static class ShmupBrickGeometry
{
    public const int MaximumRectangles = 4;
    private static readonly ShmupBrickRect[][] geometry = Build();
    public static ReadOnlySpan<ShmupBrickRect> Rectangles(ShmupBrickShape shape, int quarterTurns)
    {
        Validate(shape, quarterTurns);
        return geometry[(int)shape * 4 + quarterTurns];
    }
    public static double2 BoundsSize(ShmupBrickShape shape, int quarterTurns)
    {
        Validate(shape, quarterTurns);
        double2 size = shape switch
        {
            ShmupBrickShape.Square => new(1, 1), ShmupBrickShape.TwoByOne => new(2, 1),
            ShmupBrickShape.TwoByTwo => new(2, 2), ShmupBrickShape.L => new(2, 3), _ => new(3, 2)
        };
        return quarterTurns % 2 == 0 ? size : new(size.Y, size.X);
    }
    private static void Validate(ShmupBrickShape shape, int turns)
    {
        if ((uint)shape > (uint)ShmupBrickShape.T || (uint)turns > 3) throw new ArgumentOutOfRangeException(nameof(shape));
    }
    private static ShmupBrickRect[][] Build()
    {
        var result = new ShmupBrickRect[20][];
        for (int s = 0; s < 5; s++) for (int q = 0; q < 4; q++)
        {
            var shape = (ShmupBrickShape)s;
            if (s < 3) { result[s * 4 + q] = [new(default, BoundsSize(shape, q) / 2)]; continue; }
            double2[] cells = shape == ShmupBrickShape.L
                ? [new(-.5, -1), new(-.5, 0), new(-.5, 1), new(.5, 1)]
                : [new(-1, -.5), new(0, -.5), new(1, -.5), new(0, .5)];
            var rects = new ShmupBrickRect[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                double2 p = cells[i];
                for (int turn = 0; turn < q; turn++) p = new(-p.Y, p.X);
                rects[i] = new(p, new(.5, .5));
            }
            result[s * 4 + q] = rects;
        }
        return result;
    }
}
