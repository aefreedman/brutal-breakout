using BrutalBreakout.Simulation;
namespace BrutalBreakout.Presentation;

/// <summary>Immutable source-authored presentation dimensions. Defaults live only in style.json.</summary>
public sealed class PresentationStyle
{
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
        double catchLineGapMultiples)
    {
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
