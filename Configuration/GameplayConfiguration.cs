using BrutalBreakout.Simulation;

namespace BrutalBreakout.Configuration;

public sealed record LoadedGameplayConfiguration(string Path, GameplaySettings Settings)
{
    public string Describe()
    {
        var a = Settings.Arena;
        var b = Settings.Ball;
        return FormattableString.Invariant(
            $"Gameplay configuration: width={a.Width:R}; height={a.Height:R}; left={a.Left:R}; top={a.Top:R}; right={a.Right:R}; bottom={a.Bottom:R}; wallThickness={a.WallThickness:R}; radius={b.Radius:R}; speed={b.Speed:R}; spawnX={b.Spawn.X:R}; spawnY={b.Spawn.Y:R}; initialAimX={b.InitialAim.X:R}; initialAimY={b.InitialAim.Y:R}; startingBalls={Settings.Breakout!.StartingBalls}; catchZoneHeightFraction={Settings.Breakout.CatchZoneHeightFraction:R}; maxBouncesWithoutBrick={Settings.Breakout.MaxBouncesWithoutBrick}; bricks={Settings.Breakout.Bricks.Count}; path={Path}");
    }
}

/// <summary>Strict logical gameplay geometry. Defaults exist only in gameplay.json.</summary>
public static class GameplayConfiguration
{
    private sealed record ArenaFile(double Width, double Height, double Left, double Top,
        double Right, double Bottom, double WallThickness);
    private sealed record BallFile(double Radius, double Speed, double SpawnX, double SpawnY,
        double InitialAimX, double InitialAimY);
    private sealed record BrickFile(double X, double Y, double Width, double Height);
    private sealed record BreakoutFile(int StartingBalls, double CatchZoneHeightFraction, int MaxBouncesWithoutBrick, BrickFile[] Bricks);
    private sealed record GameplayFile(ArenaFile Arena, BallFile Ball, BreakoutFile Breakout);

    public static LoadedGameplayConfiguration Load(string path)
    {
        var loaded = ConfigurationFile.Load<GameplayFile, GameplaySettings>(path, "gameplay", data =>
        {
            ArgumentNullException.ThrowIfNull(data.Arena);
            ArgumentNullException.ThrowIfNull(data.Ball);
            ArgumentNullException.ThrowIfNull(data.Breakout);
            ArgumentNullException.ThrowIfNull(data.Breakout.Bricks);
            var rules = data.Breakout;
            var bricks = rules.Bricks.Select(b => b is null ? throw new ArgumentException("Bricks cannot contain null.") : new BrickSettings(b.X, b.Y, b.Width, b.Height)).ToArray();
            var a = data.Arena;
            var b = data.Ball;
            return new(new(a.Width, a.Height, a.Left, a.Top, a.Right, a.Bottom, a.WallThickness),
                new(b.Radius, b.Speed, b.SpawnX, b.SpawnY, b.InitialAimX, b.InitialAimY),
                new(rules.StartingBalls, rules.CatchZoneHeightFraction, rules.MaxBouncesWithoutBrick, bricks));
        });
        return new(loaded.Path, loaded.Settings);
    }

    public static LoadedGameplayConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "gameplay", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }

    public static void ValidatePhysics(LoadedGameplayConfiguration gameplay, LoadedPhysicsConfiguration physics)
    {
        try { gameplay.Settings.ValidatePhysics(physics.Settings); }
        catch (ArgumentException error)
        {
            throw new InvalidDataException(
                $"Incompatible gameplay configuration '{gameplay.Path}' and physics configuration '{physics.Path}': {error.Message}", error);
        }
    }
}
