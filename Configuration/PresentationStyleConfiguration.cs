using BrutalBreakout.Presentation;
namespace BrutalBreakout.Configuration;

public sealed record LoadedPresentationStyleConfiguration(string Path, PresentationStyle Settings)
{
    public string Describe() => $"Style configuration: path={Path}; " +
        string.Join("; ", typeof(PresentationStyle).GetProperties().Select(p =>
            $"{p.Name}={Convert.ToString(p.GetValue(Settings), System.Globalization.CultureInfo.InvariantCulture)}"));
}

/// <summary>Strict required startup-only presentation schema.</summary>
public static class PresentationStyleConfiguration
{
    public static LoadedPresentationStyleConfiguration Load(string path)
    {
        var loaded = ConfigurationFile.Load<StyleFile, PresentationStyle>(path, "style", data => new(
            data.HeldRingRadiusScreenFraction, data.HeldRingWidthScreenFraction, data.HeldRingDashCount, data.HeldRingGapFraction, data.HeldRingCyclesPerSecond, data.AimWidthRadiusRatio, data.ImpactRingWidthRadiusRatio, data.ReflectedLengthRadiusRatio, data.FallbackAimLengthRadiusRatio, data.BrickCornerFraction, data.CatchLineWidthScreenFraction, data.CatchLineDashLengthMultiples, data.CatchLineGapMultiples));
        return new(loaded.Path, loaded.Settings);
    }
    private sealed record StyleFile(double HeldRingRadiusScreenFraction, double HeldRingWidthScreenFraction, int HeldRingDashCount, double HeldRingGapFraction, double HeldRingCyclesPerSecond, double AimWidthRadiusRatio, double ImpactRingWidthRadiusRatio, double ReflectedLengthRadiusRatio, double FallbackAimLengthRadiusRatio, double BrickCornerFraction, double CatchLineWidthScreenFraction, double CatchLineDashLengthMultiples, double CatchLineGapMultiples);
    public static LoadedPresentationStyleConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "style", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }
}
