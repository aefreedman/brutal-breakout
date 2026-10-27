using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;

namespace BrutalBreakout.Configuration;

public enum ShmupRenderingPath { SharedMeshes, CpuBuiltTriangles }

/// <summary>Immutable startup resource policy. Defaults exist only in rendering.json; path choice is live.</summary>
public sealed class RenderingSettings
{
    public ShmupRenderingPath Path { get; }
    public BakedMeshLimits Limits { get; }
    public CircleDetail CircleDetail { get; }
    public RenderingSettings(ShmupRenderingPath path, BakedMeshLimits limits, CircleDetail circleDetail)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(circleDetail);
        if (!Enum.IsDefined(path)) throw new ArgumentOutOfRangeException(nameof(path));
        // Admission/ABI bounds, not fallback defaults. Include the producer's fixed selection reservation.
        if (limits.MeshCount > 4096 || limits.CpuBytes < 8192 || limits.CpuBytes > 268_435_456 ||
            limits.GpuBytes > 268_435_456 || limits.Instances > 1_000_000 || limits.Operations > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(limits), "Rendering budgets exceed supported allocation bounds.");
        Path = path; Limits = limits; CircleDetail = circleDetail;
    }
    public void Validate(ShmupSettings settings)
    {
        long instances = (long)settings.Capacity.MaxBalls + settings.Capacity.MaxBricks;
        if (Limits.Instances < instances || Limits.Operations < 2 * instances + 16)
            throw new ArgumentException("Rendering instance/operation budgets must cover Shmup capacity (operations >= 2 * instances + 16).");
    }
}

public sealed record LoadedRenderingConfiguration(string Path, RenderingSettings Settings);
public static class RenderingConfiguration
{
    private sealed record LimitsFile(int MeshCount, long CpuBytes, long GpuBytes, int Instances, int Operations);
    private sealed record CircleDetailFile(string Mode, int FixedSides);
    private sealed record RenderingFile(string Path, LimitsFile Limits, CircleDetailFile CircleDetail);
    public static LoadedRenderingConfiguration Load(string path, string? jsonText = null)
    {
        var loaded = ConfigurationFile.Load<RenderingFile, RenderingSettings>(path, "rendering", d =>
        {
            ArgumentNullException.ThrowIfNull(d.Limits);
            ArgumentNullException.ThrowIfNull(d.CircleDetail);
            var detailMode = d.CircleDetail.Mode switch
            {
                "automatic" => CircleDetailMode.Automatic,
                "fixedSideCount" => CircleDetailMode.FixedSideCount,
                _ => throw new ArgumentException("Circle detail mode must be automatic or fixedSideCount.")
            };
            var mode = d.Path switch
            {
                "sharedMeshes" => ShmupRenderingPath.SharedMeshes,
                "cpuBuiltTriangles" => ShmupRenderingPath.CpuBuiltTriangles,
                _ => throw new ArgumentException("Rendering path must be sharedMeshes or cpuBuiltTriangles.")
            };
            return new(mode, new(d.Limits.MeshCount, d.Limits.CpuBytes, d.Limits.GpuBytes, d.Limits.Instances, d.Limits.Operations), new(detailMode, d.CircleDetail.FixedSides));
        }, jsonText);
        return new(loaded.Path, loaded.Settings);
    }
    public static LoadedRenderingConfiguration LoadForMode(IReadOnlyList<string> args, string baseDirectory, string currentDirectory) =>
        Load(ConfigurationFile.SelectPath(args, true, "rendering", baseDirectory, currentDirectory)!);
}
