using BrutalBreakout.Configuration;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;

namespace BrutalBreakout.Presentation;

/// <summary>App-lifetime, bounded geometry owner; never owns a session, snapshot or borrowed frame.</summary>
public sealed class ShmupRendering : IDisposable
{
    private BakedPresentationMeshes? meshes;
    private bool disposed;
    public ShmupRenderingPath Path { get; private set; }
    public BakedMeshLimits Limits { get; }
    public CircleDetail CircleDetail { get; private set; }
    public ShmupRendering(RenderingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Limits = settings.Limits; Path = settings.Path; CircleDetail = settings.CircleDetail;
    }
    public void Apply(RenderingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ApplyPath(settings.Path);
        CircleDetail = settings.CircleDetail;
    }
    // Called on the owner update, never from Draw/ImGui. Resource limits remain startup-only.
    public void ApplyPath(ShmupRenderingPath path)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Enum.IsDefined(path)) throw new ArgumentOutOfRangeException(nameof(path));
        Path = path;
    }
    public ShapeFrame Build(ShapeDrawList draw, ShmupSnapshot snapshot, ShmupSettings settings,
        ColorScheme colors, PresentationStyle style, int width, int height,
        out ShmupPresentation.GeometryEvidence evidence, out OrderedShapeFrame? ordered, BallTrailHistory? trails = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ordered = null;
        if (Path == ShmupRenderingPath.CpuBuiltTriangles)
            return ShmupPresentation.Build(draw, snapshot, settings, colors, style, width, height, out evidence, trails, CircleDetail);
        ordered = ShmupPresentation.BuildBaked(draw, snapshot, settings, colors, style, width, height, trails, meshes ??= new(Limits), out evidence, CircleDetail);
        return ordered.Value.Triangles;
    }
    public void Dispose()
    {
        if (disposed) return;
        meshes?.Dispose(); meshes = null; disposed = true;
    }
}
