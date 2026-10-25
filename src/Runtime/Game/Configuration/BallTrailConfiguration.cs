using BrutalBreakout.Presentation;

namespace BrutalBreakout.Configuration;

public sealed record LoadedBallTrailConfiguration(string Path, BallTrailSettings Settings);

public static class BallTrailConfiguration
{
    private sealed record TrailFile(bool Enabled, int MaxPointsPerBall, double DurationSeconds, double WidthRadiusRatio, double Opacity);
    public static LoadedBallTrailConfiguration Load(string path)
    {
        var loaded = ConfigurationFile.Load<TrailFile, BallTrailSettings>(path, "trails", d =>
            new(d.Enabled, d.MaxPointsPerBall, d.DurationSeconds, d.WidthRadiusRatio, d.Opacity));
        return new(loaded.Path, loaded.Settings);
    }
    public static LoadedBallTrailConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "trails", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }
}
