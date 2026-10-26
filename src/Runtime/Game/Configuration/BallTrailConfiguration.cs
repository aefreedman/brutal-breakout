using BrutalBreakout.Presentation;

namespace BrutalBreakout.Configuration;

public sealed record LoadedBallTrailConfiguration(string Path, BallTrailSettings Settings);

public static class BallTrailConfiguration
{
    private sealed record TrailFile(bool Enabled, int MaxPointsPerBall, double DurationSeconds, double WidthRadiusRatio, double Opacity, int MaxRenderedSegments);
    public static LoadedBallTrailConfiguration Load(string path) => Load(path, null);

    public static LoadedBallTrailConfiguration Load(string path, string? jsonText)
    {
        var loaded = ConfigurationFile.Load<TrailFile, BallTrailSettings>(path, "trails", d =>
            new(d.Enabled, d.MaxPointsPerBall, d.DurationSeconds, d.WidthRadiusRatio, d.Opacity, d.MaxRenderedSegments), jsonText);
        return new(loaded.Path, loaded.Settings);
    }
    public static void ValidateCapacity(LoadedBallTrailConfiguration trails, LoadedShmupConfiguration shmup)
    {
        try { trails.Settings.ValidateCapacity(shmup.Settings.Capacity.MaxBalls); }
        catch (ArgumentException error)
        {
            throw new InvalidDataException($"Incompatible trails configuration '{trails.Path}' and shmup configuration '{shmup.Path}': {error.Message}", error);
        }
    }
    public static LoadedBallTrailConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "trails", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }
}
