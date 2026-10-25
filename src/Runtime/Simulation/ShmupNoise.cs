namespace BrutalBreakout.Simulation;

/// <summary>Seeded two-dimensional gradient Perlin noise. Stable integer hashing, no global RNG.</summary>
internal static class ShmupNoise
{
    internal static double Sample(int seed, double x, double y)
    {
        // Periodic reduction keeps lattice arithmetic defined even after extremely long runs.
        x %= 65536; y %= 65536;
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy;
        double u = Fade(fx), v = Fade(fy);
        double a = Lerp(Gradient(seed, ix, iy, fx, fy), Gradient(seed, ix + 1, iy, fx - 1, fy), u);
        double b = Lerp(Gradient(seed, ix, iy + 1, fx, fy - 1), Gradient(seed, ix + 1, iy + 1, fx - 1, fy - 1), u);
        return Math.Clamp(.5 + .5 * Lerp(a, b, v), 0, 1);
    }
    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static double Gradient(int seed, int x, int y, double dx, double dy)
    {
        uint h;
        unchecked
        {
            h = (uint)seed ^ ((uint)x * 0x9e3779b9u) ^ ((uint)y * 0x85ebca6bu);
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
        }
        return (h & 7) switch { 0 => dx, 1 => -dx, 2 => dy, 3 => -dy,
            4 => (dx + dy) * .7071067811865476, 5 => (dx - dy) * .7071067811865476,
            6 => (-dx + dy) * .7071067811865476, _ => (-dx - dy) * .7071067811865476 };
    }
}
