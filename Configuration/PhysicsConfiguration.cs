using BrutalBreakout.Simulation;

namespace BrutalBreakout.Configuration;

public sealed record LoadedPhysicsConfiguration(string Path, PhysicsSettings Settings)
{
    public string Describe() => FormattableString.Invariant(
        $"Physics configuration: updateHz={Settings.UpdateHz:R}; stepSeconds={Settings.StepSeconds:R}; substeps={Settings.Substeps}; maxCatchUpSteps={Settings.MaxCatchUpSteps}; contactHertz={Settings.ContactHertz:R}; contactDampingRatio={Settings.ContactDampingRatio:R}; path={Path}");
}

/// <summary>Strict startup-only file loading. Production defaults exist only in physics.json.</summary>
public static class PhysicsConfiguration
{
    private sealed record PhysicsFile(double UpdateHz, int Substeps, int MaxCatchUpSteps,
        double ContactHertz, double ContactDampingRatio);

    public static LoadedPhysicsConfiguration Load(string path)
    {
        var loaded = ConfigurationFile.Load<PhysicsFile, PhysicsSettings>(path, "physics", data =>
            new(data.UpdateHz, data.Substeps, data.MaxCatchUpSteps, data.ContactHertz, data.ContactDampingRatio));
        return new(loaded.Path, loaded.Settings);
    }

    /// <summary>Explicit relative paths use caller cwd; the implicit asset uses the executable directory.</summary>
    public static LoadedPhysicsConfiguration? LoadForMode(IReadOnlyList<string> args, bool needsPhysics,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needsPhysics, "physics", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }
}
