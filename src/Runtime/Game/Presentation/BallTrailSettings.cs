namespace BrutalBreakout.Presentation;

/// <summary>Immutable, explicitly supplied presentation budget. Defaults live only in trails.json.</summary>
public sealed record BallTrailSettings
{
    public bool Enabled { get; }
    public int MaxPointsPerBall { get; }
    public double DurationSeconds { get; }
    public double WidthRadiusRatio { get; }
    public double Opacity { get; }
    public BallTrailSettings(bool enabled, int maxPointsPerBall, double durationSeconds, double widthRadiusRatio, double opacity)
    {
        if (maxPointsPerBall < 2 || maxPointsPerBall > 1048576) throw new ArgumentOutOfRangeException(nameof(maxPointsPerBall));
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!double.IsFinite(widthRadiusRatio) || widthRadiusRatio <= 0 || widthRadiusRatio > 16) throw new ArgumentOutOfRangeException(nameof(widthRadiusRatio));
        if (!double.IsFinite(opacity) || opacity < 0 || opacity > 1) throw new ArgumentOutOfRangeException(nameof(opacity));
        (Enabled, MaxPointsPerBall, DurationSeconds, WidthRadiusRatio, Opacity) =
            (enabled, maxPointsPerBall, durationSeconds, widthRadiusRatio, opacity);
    }
    public void ValidateCapacity(int maxBalls)
    {
        if (maxBalls <= 0 || (long)maxBalls * MaxPointsPerBall > 1048576)
            throw new ArgumentOutOfRangeException(nameof(maxBalls), "Trail history must fit the 1048576-point budget.");
    }
}
