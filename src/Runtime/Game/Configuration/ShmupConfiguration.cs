using BrutalBreakout.Simulation;

namespace BrutalBreakout.Configuration;

public sealed record LoadedShmupConfiguration(string Path, ShmupSettings Settings)
{
    public string Describe() => FormattableString.Invariant(
        $"Shmup configuration: width={Settings.Arena.Width:R}; height={Settings.Arena.Height:R}; playAreaWidth={Settings.Arena.InitialPlayAreaWidth:R}; path={Path}");
}

/// <summary>Strict startup schema. All tunable defaults live only in shmup.json.</summary>
public static class ShmupConfiguration
{
    private sealed record ArenaFile(double Width, double Height, double InitialPlayAreaWidth);
    private sealed record AvatarFile(double Radius, double MoveSpeed, int Health, int ContactDamage, double InvulnerabilitySeconds);
    private sealed record BallFile(double Radius, double Speed, double FireRate, int Damage, bool BounceOnBricks, int MaxWallHits);
    private sealed record BrickFile(double Size, double ColumnPitch, int Health, int EscapeDamage,
        string[] Shapes, int[] QuarterTurns);
    private sealed record DifficultyFile(double RowInterval, double MinimumRowInterval, double DescentSpeed,
        double MaximumDescentSpeed, double RampSeconds, double IntervalMultiplier, double SpeedMultiplier);
    private sealed record NoiseFile(int Seed, double ColumnScale, double RowScale, double GapThreshold);
    private sealed record CapacityFile(int MaxBalls, int MaxBricks);
    private sealed record ShmupFile(ArenaFile Arena, AvatarFile Avatar, BallFile Ball, BrickFile Brick,
        DifficultyFile Difficulty, NoiseFile Noise, CapacityFile Capacity);

    public static LoadedShmupConfiguration Load(string path)
    {
        var loaded = ConfigurationFile.Load<ShmupFile, ShmupSettings>(path, "shmup", data =>
        {
            ArgumentNullException.ThrowIfNull(data.Arena); ArgumentNullException.ThrowIfNull(data.Avatar);
            ArgumentNullException.ThrowIfNull(data.Ball); ArgumentNullException.ThrowIfNull(data.Brick);
            ArgumentNullException.ThrowIfNull(data.Difficulty); ArgumentNullException.ThrowIfNull(data.Noise);
            ArgumentNullException.ThrowIfNull(data.Capacity);
            var a = data.Arena; var v = data.Avatar; var b = data.Ball; var r = data.Brick;
            var d = data.Difficulty; var n = data.Noise; var c = data.Capacity;
            return new(new(a.Width, a.Height, a.InitialPlayAreaWidth),
                new(v.Radius, v.MoveSpeed, v.Health, v.ContactDamage, v.InvulnerabilitySeconds),
                new(b.Radius, b.Speed, b.FireRate, b.Damage, b.BounceOnBricks, b.MaxWallHits),
                new(r.Size, r.ColumnPitch, r.Health, r.EscapeDamage, ParseShapes(r.Shapes), r.QuarterTurns),
                new(d.RowInterval, d.MinimumRowInterval, d.DescentSpeed, d.MaximumDescentSpeed,
                    d.RampSeconds, d.IntervalMultiplier, d.SpeedMultiplier),
                new(n.Seed, n.ColumnScale, n.RowScale, n.GapThreshold), new(c.MaxBalls, c.MaxBricks));
        });
        return new(loaded.Path, loaded.Settings);
    }
    private static ShmupBrickShape[] ParseShapes(string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return names.Select(name => name switch
        {
            "square" => ShmupBrickShape.Square,
            "2x1" => ShmupBrickShape.TwoByOne,
            "2x2" => ShmupBrickShape.TwoByTwo,
            "l" => ShmupBrickShape.L,
            "t" => ShmupBrickShape.T,
            _ => throw new ArgumentException($"Unknown brick shape '{name ?? "<null>"}'.", nameof(names))
        }).ToArray();
    }

    public static LoadedShmupConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "shmup", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }
    public static void ValidatePhysics(LoadedShmupConfiguration shmup, LoadedPhysicsConfiguration physics)
    {
        try { shmup.Settings.ValidatePhysics(physics.Settings); }
        catch (ArgumentException error)
        {
            throw new InvalidDataException($"Incompatible shmup configuration '{shmup.Path}' and physics configuration '{physics.Path}': {error.Message}", error);
        }
    }
}
