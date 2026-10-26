using Brutal.Numerics;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Performance;

internal readonly record struct BrickSeed(double2 Position, ShmupBrickShape Shape, int Turns);
internal sealed class Layout
{
    public IReadOnlyList<double2> Balls { get; }
    public IReadOnlyList<BrickSeed> Bricks { get; }
    internal Layout(ShmupSettings settings, double width, IReadOnlyList<double2> balls, IReadOnlyList<BrickSeed> bricks)
    {
        Validate(settings, width, balls, bricks);
        Balls = Array.AsReadOnly(balls.ToArray()); Bricks = Array.AsReadOnly(bricks.ToArray());
    }
    public Layout(ShmupSettings settings, Policy policy)
    {
        // Each 100-cell prefix layer covers all 10x10 screen strata once. Within a stratum
        // all 40 sites are visited in seeded order. Thus 500/1000/2000/4000 share exact prefixes.
        var random = new Random(policy.Seed);
        var strata = Enumerable.Range(0, 100).ToArray();
        var sites = Enumerable.Range(0, 40).ToArray();
        random.Shuffle(strata); random.Shuffle(sites);
        var positions = new List<double2>(4000);
        foreach (int site in sites) foreach (int stratum in strata)
        {
            int x = stratum % 10 * 10 + site % 10;
            int y = stratum / 10 * 4 + site / 10;
            positions.Add(new((x + .5) * policy.Width / 100, 430 + (y + .5) * 6.25));
        }
        var bricks = new List<BrickSeed>(policy.Bricks);
        var shapes = Enum.GetValues<ShmupBrickShape>();
        for (int i = 0; i < policy.Bricks; i++)
            bricks.Add(new(new(64 + i % 10 * 128, 54 + i / 10 * 104), shapes[(i + policy.Seed) % shapes.Length], (i + policy.Seed) % 4));
        Validate(settings, policy.Width, positions, bricks);
        Balls = positions.AsReadOnly(); Bricks = bricks.AsReadOnly();
    }
    public static bool CircleFits(ShmupSettings s, double width, double2 p, double2 avatar,
        IEnumerable<BrickSeed> bricks)
    {
        double r = s.Ball.Radius, left = (s.Arena.Width - width) / 2;
        if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X - r <= left ||
            p.X + r >= left + width || p.Y - r <= 0 || p.Y + r >= s.Arena.Height) return false;
        var d = p - avatar;
        if (d.X * d.X + d.Y * d.Y <= Math.Pow(r + s.Avatar.Radius, 2)) return false;
        foreach (var brick in bricks) foreach (var rect in s.Brick.Geometry.Rectangles(brick.Shape, brick.Turns))
        {
            var delta = p - brick.Position - rect.Center;
            double dx = Math.Max(0, Math.Abs(delta.X) - rect.HalfExtents.X);
            double dy = Math.Max(0, Math.Abs(delta.Y) - rect.HalfExtents.Y);
            if (dx * dx + dy * dy <= r * r) return false;
        }
        return true;
    }
    public static bool BrickFits(ShmupSettings s, double width, BrickSeed seed, IEnumerable<BrickSeed> occupied,
        IEnumerable<double2> balls, double2 avatar)
    {
        var b = s.Brick.Geometry.BoundsSize(seed.Shape, seed.Turns);
        double left = (s.Arena.Width - width) / 2;
        if (seed.Position.X - b.X / 2 <= left || seed.Position.X + b.X / 2 >= left + width ||
            seed.Position.Y - b.Y / 2 <= 0 || seed.Position.Y + b.Y / 2 >= s.Arena.Height) return false;
        foreach (var other in occupied)
        {
            var o = s.Brick.Geometry.BoundsSize(other.Shape, other.Turns);
            var d = seed.Position - other.Position;
            if (Math.Abs(d.X) <= (b.X + o.X) / 2 && Math.Abs(d.Y) <= (b.Y + o.Y) / 2) return false;
        }
        // Test compound rectangles against ball circles and avatar, not only unrotated square size.
        foreach (var p in balls.Append(avatar)) foreach (var rect in s.Brick.Geometry.Rectangles(seed.Shape, seed.Turns))
        {
            var d = p - seed.Position - rect.Center;
            double dx = Math.Max(0, Math.Abs(d.X) - rect.HalfExtents.X), dy = Math.Max(0, Math.Abs(d.Y) - rect.HalfExtents.Y);
            double radius = p.Equals(avatar) ? s.Avatar.Radius : s.Ball.Radius;
            if (dx * dx + dy * dy <= radius * radius) return false;
        }
        return true;
    }
    public static void Validate(ShmupSettings s, double width, IReadOnlyList<double2> balls, IReadOnlyList<BrickSeed> bricks)
    {
        // Avatar reset position is validated against the actual initial publication again at construction.
        var controller = new ShmupAvatarController(s); controller.Reset();
        var avatar = controller.Position;
        for (int i = 0; i < bricks.Count; i++)
            if (!BrickFits(s, width, bricks[i], bricks.Take(i), balls, avatar))
                throw new InvalidDataException($"Brick seed {i} does not fit current geometry.");
        for (int i = 0; i < balls.Count; i++)
        {
            if (!CircleFits(s, width, balls[i], avatar, bricks)) throw new InvalidDataException($"Ball seed {i} does not fit.");
            for (int j = 0; j < i; j++)
            {
                var d = balls[i] - balls[j];
                if (d.X * d.X + d.Y * d.Y <= 4 * s.Ball.Radius * s.Ball.Radius)
                    throw new InvalidDataException("Ball seed overlap.");
            }
        }
    }
}
