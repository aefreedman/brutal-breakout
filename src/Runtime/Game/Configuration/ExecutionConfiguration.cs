using BrutalBreakout.Simulation;

namespace BrutalBreakout.Configuration;

public sealed record LoadedExecutionConfiguration(string Path, ExecutionSettings Settings)
{
    public string Describe() => $"Execution configuration: workers={Settings.Jobs.WorkerCount}; physicsWorkers={Settings.PhysicsWorkerCount}; maxExpectedConcurrentJobs={Settings.Jobs.MaxExpectedConcurrentJobs}; path={Path}";
}

/// <summary>Game composition policy. The shared packages accept explicit settings, not game JSON.</summary>
public static class ExecutionConfiguration
{
    private sealed record ExecutionFile(int WorkerCount, int MaxExpectedConcurrentJobs, int PhysicsWorkerCount);

    public static LoadedExecutionConfiguration Load(string path) => Load(path, null);

    public static LoadedExecutionConfiguration Load(string path, string? jsonText)
    {
        var loaded = ConfigurationFile.Load<ExecutionFile, ExecutionSettings>(path, "execution", data =>
            new(data.WorkerCount, data.MaxExpectedConcurrentJobs, data.PhysicsWorkerCount), jsonText);
        return new(loaded.Path, loaded.Settings);
    }

    public static LoadedExecutionConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "execution", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }
}
