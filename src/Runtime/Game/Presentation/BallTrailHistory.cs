using Brutal.Numerics;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;

namespace BrutalBreakout.Presentation;

/// <summary>Game-owned fixed-tick polylines, not a physics history. Duration is an upper bound:
/// the finite per-ball sample budget can expire points first; within-tick bends are approximated.
/// World positions survive framebuffer/DPI changes. Only the synchronized live publication can draw.</summary>
public sealed class BallTrailHistory
{
    public BallTrailSettings Settings { get; }
    public int Capacity => positions.Length;
    public int PointCount { get; private set; }
    private readonly double2[] positions;
    private readonly double[] times;
    private readonly long[] generations;
    private readonly int[] heads, counts;
    private readonly bool[] seen;
    private ShmupSnapshot? current;

    public BallTrailHistory(BallTrailSettings settings, int maxBalls)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ValidateCapacity(maxBalls);
        Settings = settings;
        int slots = settings.Enabled ? maxBalls : 0;
        positions = new double2[slots * settings.MaxPointsPerBall];
        times = new double[positions.Length];
        generations = new long[slots]; heads = new int[slots]; counts = new int[slots]; seen = new bool[slots];
    }
    public void Clear()
    {
        Array.Clear(counts); Array.Clear(heads); Array.Clear(generations);
        PointCount = 0; current = null;
    }
    /// <summary>Synchronize publication identity and retire dead lives without adding render-frame samples.</summary>
    public void Synchronize(ShmupSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (current is not null && (snapshot.Tick < current.Tick || snapshot.TimeSeconds < current.TimeSeconds)) Clear();
        Array.Clear(seen);
        foreach (var ball in snapshot.Balls)
        {
            if ((uint)ball.Id >= (uint)counts.Length) continue;
            seen[ball.Id] = true;
            if (generations[ball.Id] != ball.Generation)
            {
                PointCount -= counts[ball.Id]; counts[ball.Id] = heads[ball.Id] = 0;
                generations[ball.Id] = ball.Generation;
            }
        }
        for (int id = 0; id < counts.Length; id++)
            if (!seen[id]) { PointCount -= counts[id]; counts[id] = heads[id] = 0; }
        current = snapshot;
    }
    /// <summary>Called once per completed fixed tick, including each catch-up step.</summary>
    public void Capture(ShmupSnapshot snapshot)
    {
        bool sameTick = current is not null && snapshot.Tick == current.Tick && snapshot.TimeSeconds == current.TimeSeconds;
        Synchronize(snapshot);
        if (sameTick) return; // Defeat and render-only publications do not age or duplicate samples.
        int cap = Settings.MaxPointsPerBall;
        foreach (var ball in snapshot.Balls)
        {
            int id = ball.Id;
            if ((uint)id >= (uint)counts.Length) continue;
            while (counts[id] > 0 && snapshot.TimeSeconds - times[id * cap + heads[id]] >= Settings.DurationSeconds)
            { heads[id] = (heads[id] + 1) % cap; counts[id]--; PointCount--; }
            if (counts[id] == cap) { heads[id] = (heads[id] + 1) % cap; counts[id]--; PointCount--; }
            int index = id * cap + (heads[id] + counts[id]) % cap;
            positions[index] = ball.Position; times[index] = snapshot.TimeSeconds;
            counts[id]++; PointCount++;
        }
    }
    /// <summary>Returns indexed geometry count; stale/retained publications deliberately have no trails.</summary>
    public int Draw(ShapeDrawList draw, ShmupSnapshot snapshot, LinearColor color)
    {
        if (!ReferenceEquals(current, snapshot) || !Settings.Enabled) return 0;
        int start = draw.IndexCount, cap = Settings.MaxPointsPerBall;
        int remaining = Settings.MaxRenderedSegments;
        int perBallBudget = Math.Max(1, remaining / Math.Max(1, snapshot.Balls.Length));
        foreach (var ball in snapshot.Balls)
        {
            int id = ball.Id;
            if ((uint)id >= (uint)counts.Length || generations[id] != ball.Generation) continue;
            // Under draw saturation shorten old tails first and share the budget across live balls.
            for (int p = counts[id] - 1; p >= Math.Max(1, counts[id] - perBallBudget) && remaining > 0; p--)
            {
                int previous = id * cap + (heads[id] + p - 1) % cap;
                int next = id * cap + (heads[id] + p) % cap;
                double life = 1 - (snapshot.TimeSeconds - times[previous]) / Settings.DurationSeconds;
                if (life <= 0 || positions[previous] == positions[next]) continue;
                // Butt caps keep the worst-case budget linear (six indices/segment), with no tessellated caps.
                float strength = (float)Math.Clamp(life, 0, 1);
                var a = positions[previous]; var b = positions[next];
                float width = (float)(ball.Radius * Settings.WidthRadiusRatio) * strength;
                if (width <= 0) continue; // A faded sub-FLOAT32 stroke has no visible geometry.
                draw.StrokeSegment(new((float)a.X, (float)a.Y), new((float)b.X, (float)b.Y),
                    width, LineCap.Butt,
                    new(color.R, color.G, color.B, color.A * (float)Settings.Opacity * strength));
                remaining--;
            }
        }
        return draw.IndexCount - start;
    }
}
