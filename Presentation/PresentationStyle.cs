using BrutalBreakout.Simulation;
namespace BrutalBreakout.Presentation;

/// <summary>Immutable source-authored presentation dimensions. Defaults live only in style.json.</summary>
public sealed class PresentationStyle
{
    public HudStyle Hud { get; }
    public double HeldRingRadiusScreenFraction { get; }
    public double HeldRingWidthScreenFraction { get; }
    public int HeldRingDashCount { get; }
    public double HeldRingGapFraction { get; }
    public double HeldRingCyclesPerSecond { get; }
    public double AimWidthRadiusRatio { get; }
    public double ImpactRingWidthRadiusRatio { get; }
    public double ReflectedLengthRadiusRatio { get; }
    public double FallbackAimLengthRadiusRatio { get; }
    public double BrickCornerFraction { get; }
    public double CatchLineWidthScreenFraction { get; }
    public double CatchLineDashLengthMultiples { get; }
    public double CatchLineGapMultiples { get; }
    public PresentationStyle(double heldRingRadiusScreenFraction,
        double heldRingWidthScreenFraction,
        int heldRingDashCount,
        double heldRingGapFraction,
        double heldRingCyclesPerSecond,
        double aimWidthRadiusRatio,
        double impactRingWidthRadiusRatio,
        double reflectedLengthRadiusRatio,
        double fallbackAimLengthRadiusRatio,
        double brickCornerFraction,
        double catchLineWidthScreenFraction,
        double catchLineDashLengthMultiples,
        double catchLineGapMultiples,
        HudStyle hud)
    {
        ArgumentNullException.ThrowIfNull(hud);
        Hud = hud;
        Positive(heldRingRadiusScreenFraction);
        Positive(heldRingWidthScreenFraction);
        Positive(heldRingCyclesPerSecond);
        Positive(aimWidthRadiusRatio);
        Positive(impactRingWidthRadiusRatio);
        Positive(reflectedLengthRadiusRatio);
        Positive(fallbackAimLengthRadiusRatio);
        Positive(brickCornerFraction);
        Positive(catchLineWidthScreenFraction);
        Positive(catchLineDashLengthMultiples);
        Positive(catchLineGapMultiples);
        if (heldRingDashCount is < 1 or > 4096 || !double.IsFinite(heldRingGapFraction) || heldRingGapFraction < 0 || heldRingGapFraction >= 1)
            throw new ArgumentOutOfRangeException(nameof(heldRingDashCount), "Invalid held-ring pattern.");
        // Rounded caps must remain disjoint, including across the closed seam.
        double halfWidthRatio = heldRingWidthScreenFraction / (2 * heldRingRadiusScreenFraction);
        if (halfWidthRatio >= 1 || Math.Tau / heldRingDashCount * heldRingGapFraction <= 2 * Math.Asin(halfWidthRatio))
            throw new ArgumentException("Held-ring gaps must separate rounded caps.");
        if (brickCornerFraction > .5 || catchLineGapMultiples < 1)
            throw new ArgumentException("Invalid rounded brick or catch-line spacing.");
        HeldRingRadiusScreenFraction = heldRingRadiusScreenFraction;
        HeldRingWidthScreenFraction = heldRingWidthScreenFraction;
        HeldRingDashCount = heldRingDashCount;
        HeldRingGapFraction = heldRingGapFraction;
        HeldRingCyclesPerSecond = heldRingCyclesPerSecond;
        AimWidthRadiusRatio = aimWidthRadiusRatio;
        ImpactRingWidthRadiusRatio = impactRingWidthRadiusRatio;
        ReflectedLengthRadiusRatio = reflectedLengthRadiusRatio;
        FallbackAimLengthRadiusRatio = fallbackAimLengthRadiusRatio;
        BrickCornerFraction = brickCornerFraction;
        CatchLineWidthScreenFraction = catchLineWidthScreenFraction;
        CatchLineDashLengthMultiples = catchLineDashLengthMultiples;
        CatchLineGapMultiples = catchLineGapMultiples;
    }
    private static void Positive(double value)
    {
        if (!double.IsFinite(value) || value <= 0 || (float)value == 0 || value > float.MaxValue / int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "Dimensions must be positive, finite and FLOAT32-safe.");
    }
    /// <summary>Pure cross-configuration validation, also called before native startup.</summary>
    public void Validate(GameplaySettings game)
    {
        ArgumentNullException.ThrowIfNull(game);
        Hud.Validate(game);
        foreach (double ratio in new[] { AimWidthRadiusRatio, ImpactRingWidthRadiusRatio, ReflectedLengthRadiusRatio, FallbackAimLengthRadiusRatio })
            Positive(ratio * game.Ball.Radius);
        double periods = (game.Arena.Right - game.Arena.Left) / Math.Min(game.Arena.Width, game.Arena.Height) /
            (CatchLineWidthScreenFraction * (CatchLineDashLengthMultiples + CatchLineGapMultiples));
        if (!double.IsFinite(periods) || periods > 4095)
            throw new ArgumentException("Catch-line pattern exceeds supported intervals.");
    }
    public double RingOffset(double renderSeconds)
    {
        if (!double.IsFinite(renderSeconds) || renderSeconds < 0) throw new ArgumentOutOfRangeException(nameof(renderSeconds));
        // Reduce before multiplication: finite very long runtimes never overflow phase.
        return (renderSeconds % (1 / HeldRingCyclesPerSecond)) * HeldRingCyclesPerSecond;
    }
}

/// <summary>Immutable logical-arena HUD dimensions. Artwork fractions remain geometry definitions.</summary>
public sealed class HudStyle
{
    public double BarMaxWidth { get; }
    public double BarMaxArenaWidthFraction { get; }
    public double BarHeight { get; }
    public double BarOutlineWidth { get; }
    public double BarMinCenterY { get; }
    public double BarTopMarginFraction { get; }
    public double TerminalMaxSize { get; }
    public double TerminalMaxArenaSizeFraction { get; }
    public double TerminalMarkerWidth { get; }

    public HudStyle(double barMaxWidth, double barMaxArenaWidthFraction, double barHeight,
        double barOutlineWidth, double barMinCenterY, double barTopMarginFraction,
        double terminalMaxSize, double terminalMaxArenaSizeFraction, double terminalMarkerWidth)
    {
        foreach (double value in new[] { barMaxWidth, barMaxArenaWidthFraction, barHeight, barOutlineWidth,
            barMinCenterY, barTopMarginFraction, terminalMaxSize, terminalMaxArenaSizeFraction, terminalMarkerWidth })
            if (!double.IsFinite(value) || value <= 0 || (float)value == 0 || value > 1 << 20)
                throw new ArgumentOutOfRangeException(nameof(value), "HUD dimensions must be positive, finite, FLOAT32-safe and within the shape coordinate envelope.");
        if (barMaxArenaWidthFraction > 1 || barTopMarginFraction > 1 || terminalMaxArenaSizeFraction > 1)
            throw new ArgumentOutOfRangeException(nameof(barMaxArenaWidthFraction), "HUD fractions cannot exceed one.");
        BarMaxWidth = barMaxWidth;
        BarMaxArenaWidthFraction = barMaxArenaWidthFraction;
        BarHeight = barHeight;
        BarOutlineWidth = barOutlineWidth;
        BarMinCenterY = barMinCenterY;
        BarTopMarginFraction = barTopMarginFraction;
        TerminalMaxSize = terminalMaxSize;
        TerminalMaxArenaSizeFraction = terminalMaxArenaSizeFraction;
        TerminalMarkerWidth = terminalMarkerWidth;
    }

    internal void Validate(GameplaySettings game)
    {
        if (game.Breakout is null) return;
        var a = game.Arena;
        double bar = Math.Min(BarMaxWidth, (a.Right - a.Left) * BarMaxArenaWidthFraction);
        double y = Math.Max(BarMinCenterY, (a.Top - a.WallThickness) * BarTopMarginFraction);
        double size = Math.Min(TerminalMaxSize, Math.Min(a.Right - a.Left, a.Bottom - a.Top) * TerminalMaxArenaSizeFraction);
        if (TerminalMarkerWidth > size || !((float)((a.Left + a.Right) / 2 - size / 4) < (float)((a.Left + a.Right) / 2)) ||
            !((float)(game.CatchLineY + size / 2) > (float)game.CatchLineY))
            throw new ArgumentException("Terminal marker width must not exceed its size, and artwork points must remain distinct in float precision.");
        Check(a.Left, bar, BarOutlineWidth);
        Check(a.Right - bar, bar, BarOutlineWidth);
        Check(y - BarHeight / 2, BarHeight, BarOutlineWidth);
        Check((a.Left + a.Right) / 2 - size, 2 * size, TerminalMarkerWidth);
        Check(game.CatchLineY - size, 2 * size, TerminalMarkerWidth);
        static void Check(double origin, double extent, double stroke)
        {
            float low = (float)origin, high = (float)(origin + extent);
            if (!(low < high) || Math.Abs(origin - stroke / 2) > 1 << 20 || Math.Abs(origin + extent + stroke / 2) > 1 << 20 ||
                !((float)(origin - stroke / 2) < low && (float)(origin + extent + stroke / 2) > high))
                throw new ArgumentException("HUD geometry collapses in float precision or exceeds the shape coordinate envelope.");
        }
    }
}
