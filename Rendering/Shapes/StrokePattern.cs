using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Shapes.Tests")]
namespace BrutalBreakout.Rendering.Shapes;

/// <summary>Periodic centerline occupancy. Positive offset moves the lattice forward by a cycle fraction.
/// Lengths use the stroke measurement space; relative lengths multiply full width. Fixed counts fit
/// complete periods, not disconnected islands. Default(StrokePattern) is invalid.</summary>
public readonly struct StrokePattern
{
    private readonly byte kind; // 1 absolute dashes, 2 relative, 3 fixed; 4..6 dots
    private readonly double length, gap, offset;
    private readonly bool fit;
    private StrokePattern(byte kind, double length, double gap, double offset, bool fit)
    {
        this.kind = kind; this.length = length; this.gap = gap; this.offset = offset; this.fit = fit;
        Validate();
    }
    public static StrokePattern Dashes(double length, double gap, double offsetCycles = 0, bool fitClosed = false) => new(1, length, gap, offsetCycles, fitClosed);
    public static StrokePattern RelativeDashes(double lengthMultiples, double gapMultiples, double offsetCycles = 0, bool fitClosed = false) => new(2, lengthMultiples, gapMultiples, offsetCycles, fitClosed);
    public static StrokePattern FixedDashes(int count, double gapFraction = 0.5, double offsetCycles = 0) => new(3, count, gapFraction, offsetCycles, true);
    public static StrokePattern Dots(double spacing, double offsetCycles = 0, bool fitClosed = false) => new(4, spacing, 0, offsetCycles, fitClosed);
    public static StrokePattern RelativeDots(double spacingMultiples, double offsetCycles = 0, bool fitClosed = false) => new(5, spacingMultiples, 0, offsetCycles, fitClosed);
    public static StrokePattern FixedDots(int count, double offsetCycles = 0) => new(6, count, 0, offsetCycles, true);
    internal bool IsDots => kind >= 4;
    internal void Validate()
    {
        if (kind is < 1 or > 6 || !double.IsFinite(length) || length <= 0 || !double.IsFinite(gap) || gap < 0 ||
            !double.IsFinite(offset) || (kind == 3 && gap >= 1) || (kind is 3 or 6 && length > 4096))
            throw new ArgumentOutOfRangeException(nameof(StrokePattern), "Invalid pattern or fixed count (1..4096).");
    }
    internal readonly record struct Interval(double Start, double End);
    internal sealed record Samples(List<Interval> Intervals, List<double> Sites, bool Solid);
    internal Samples Sample(double total, double width, bool closed)
    {
        Validate();
        var spans = new List<Interval>(); var sites = new List<double>();
        if (total == 0 || width == 0) return new(spans, sites, false);
        bool fixedCount = kind is 3 or 6;
        double period = fixedCount ? total / length : (length + gap) * (kind is 2 or 5 ? width : 1);
        double duty = kind == 3 ? 1 - gap : length / (length + gap);
        if (!double.IsFinite(period) || period <= 0) throw new ArgumentException("Unrepresentable pattern period.");
        if (closed && fit && !fixedCount)
        {
            // Midpoints round upward; at least one and at most 4096 cycles.
            double cycles = Math.Max(1, Math.Floor(total / period + 0.5));
            if (cycles > 4096) throw new ArgumentException("Pattern exceeds 4096 periods.");
            period = total / cycles;
        }
        if (!IsDots && duty == 1) { spans.Add(new(0, total)); return new(spans, sites, true); }
        // Preflight before integer conversion or traversal. At most two clipped edge fragments.
        double ratio = total / period;
        if (!double.IsFinite(ratio) || ratio > 4096) throw new ArgumentException("Pattern exceeds 4096 intervals/sites.");
        double phase = offset % 1; if (phase < 0) phase += 1;
        double first = phase * period;
        int cyclesToVisit = (int)Math.Ceiling(ratio) + 1;
        for (int i = IsDots ? 0 : -1; i <= cyclesToVisit; i++)
        {
            double start = first + i * period;
            if (IsDots)
            {
                if (start < 0 || start > total || ((closed || fixedCount) && start == total)) continue;
                sites.Add(start);
            }
            else
            {
                double end = Math.Min(total, start + duty * period);
                start = Math.Max(0, start);
                if (end > start) spans.Add(new(start, end));
            }
            if (sites.Count > 4096 || spans.Count > 4096) throw new ArgumentException("Pattern exceeds 4096 intervals/sites.");
        }
        if (closed && spans.Count > 1 && spans[0].Start == 0 && spans[^1].End == total)
        {
            // One unwrapped seam fragment, traversed through total back to zero without seam caps.
            var merged = new Interval(spans[^1].Start, total + spans[0].End);
            spans.RemoveAt(spans.Count - 1); spans.RemoveAt(0); spans.Add(merged);
        }
        return new(spans, sites, false);
    }
}
