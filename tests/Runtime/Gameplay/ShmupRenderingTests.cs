using System.Reflection;
using Brutal.Numerics;
using BrutalBreakout.Configuration;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;
using Xunit;

public sealed class ShmupRenderingTests
{
    private static ShmupSettings Settings() => ShmupConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "shmup.json")).Settings;
    private static RenderingSettings Rendering() => RenderingConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "rendering.json")).Settings;
    private static ShmupSnapshot Snapshot(double x = 640, long generation = 1) =>
        (ShmupSnapshot)typeof(ShmupSnapshot).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single().Invoke(
            new object[] { 432d, 720d, new double2(640, 600), new double2(0, -1), 100,
                2d, 600d, 10d, 1d, 0d, 240L, 0L, 0L, 0L, 2, 2,
                new[] { new ShmupBallState(1, generation, new(x, 400), new(0, -576), 2.52, 0) },
                new[] { new ShmupBrickState(2, generation, new(x, 200), 28.8, 100, InitialHealth: 100) }, default(AimPreviewState), 100 });
    private static long[] Identities(OrderedShapeFrame frame) => frame.Operations.ToArray().Where(o => o.Mesh is not null).Select(o => o.Mesh!.Identity).ToArray();
    [Fact]
    public void DefaultSharedAndLiveTriangleRoundTripRetainLimitsGeometryAndOwnedPublication()
    {
        var settings = Settings(); var policy = Rendering(); var snapshot = Snapshot();
        var balls = snapshot.Balls.ToArray(); var bricks = snapshot.Bricks.ToArray();
        using var owner = new ShmupRendering(policy); using var draw = new ShapeDrawList(); using var expected = new ShapeDrawList();
        Assert.Equal(ShmupRenderingPath.SharedMeshes, owner.Path); Assert.Same(policy.Limits, owner.Limits);
        owner.Build(draw, snapshot, settings, TestSettings.Colors, TestSettings.Style, 1280, 720, out var sharedEvidence, out var shared);
        Assert.True(shared.HasValue); Assert.Equal(2, shared.Value.Instances.Length); Assert.Same(policy.Limits, shared.Value.Limits);
        var ids = Identities(shared.Value);
        owner.ApplyPath(ShmupRenderingPath.CpuBuiltTriangles);
        var triangles = owner.Build(draw, snapshot, settings, TestSettings.Colors, TestSettings.Style, 1280, 720, out var triangleEvidence, out var cpu);
        Assert.Null(cpu); Assert.Equal(sharedEvidence, triangleEvidence);
        var reference = ShmupPresentation.Build(expected, snapshot, settings, TestSettings.Colors, TestSettings.Style, 1280, 720);
        Assert.Equal(reference.Vertices.ToArray(), triangles.Vertices.ToArray()); Assert.Equal(reference.Indices.ToArray(), triangles.Indices.ToArray());
        owner.ApplyPath(ShmupRenderingPath.SharedMeshes);
        // Unrelated tuning reparses equal geometry. Movement/life generations must not become cache keys.
        var reloaded = Settings(); var moved = Snapshot(645.125, 9);
        owner.Build(draw, moved, reloaded, TestSettings.Colors, TestSettings.Style, 1280, 720, out _, out shared);
        Assert.Equal(ids, Identities(shared!.Value)); Assert.Same(policy.Limits, shared.Value.Limits);
        Assert.Equal(balls, snapshot.Balls.ToArray()); Assert.Equal(bricks, snapshot.Bricks.ToArray());
        Assert.Equal(240, snapshot.Tick); Assert.Equal(2, snapshot.TimeSeconds);
        owner.Dispose(); owner.Dispose();
        Assert.Throws<ObjectDisposedException>(() => owner.ApplyPath(ShmupRenderingPath.SharedMeshes));
    }
    [Fact]
    public void ClassicFilledBallUsesExplicitCountWithoutChangingOtherGeometry()
    {
        using var simulation=new BallSimulation(TestSettings.Baseline,TestSettings.Gameplay,new(2,64,2));
        using var draw=new ShapeDrawList();
        BallSandbox.Build(draw,simulation,TestSettings.Colors,TestSettings.Style,0,1280,720,out var auto);
        foreach(int n in new[]{8,12,16})
        {
            BallSandbox.Build(draw,simulation,TestSettings.Colors,TestSettings.Style,0,1280,720,out var fixedBall,new(CircleDetailMode.FixedSideCount,n));
            Assert.Equal(n*3,fixedBall.BallIndices);
            Assert.Equal(auto.BrickIndices,fixedBall.BrickIndices);Assert.Equal(auto.HeldRingIndices,fixedBall.HeldRingIndices);
            Assert.Equal(auto.AimIndices,fixedBall.AimIndices);
        }
    }
    [Fact]
    public void ActualBallProducerUsesSamePolicyInBothPathsAndKeepsMeshesAcrossLiveEdits()
    {
        var settings=Settings();var startup=Rendering();var snapshot=Snapshot();
        using var owner=new ShmupRendering(startup);using var draw=new ShapeDrawList();using var reference=new ShapeDrawList();
        long first=0;
        foreach(int n in new[]{12,16,12})
        {
            var detail=new CircleDetail(CircleDetailMode.FixedSideCount,n);
            owner.Apply(new(ShmupRenderingPath.SharedMeshes,startup.Limits,detail));
            owner.Build(draw,snapshot,settings,TestSettings.Colors,TestSettings.Style,1280,720,out _,out var shared);
            var mesh=shared!.Value.Operations.ToArray().Single(o=>o.Mesh is { } m && m.IndexCount==n*3).Mesh!;
            if(n==12){if(first==0)first=mesh.Identity;else Assert.Equal(first,mesh.Identity);}
            Assert.Same(detail,owner.CircleDetail);
            owner.Apply(new(ShmupRenderingPath.CpuBuiltTriangles,startup.Limits,detail));
            var actual=owner.Build(draw,snapshot,settings,TestSettings.Colors,TestSettings.Style,1280,720,out _,out _);
            var expected=ShmupPresentation.Build(reference,snapshot,settings,TestSettings.Colors,TestSettings.Style,1280,720,out _,null,detail);
            Assert.Equal(expected.Vertices.ToArray(),actual.Vertices.ToArray());Assert.Equal(expected.Indices.ToArray(),actual.Indices.ToArray());
        }
        owner.Apply(startup);Assert.Same(startup.CircleDetail,owner.CircleDetail);
        Assert.Equal(240,snapshot.Tick);Assert.Equal(2,snapshot.TimeSeconds);
    }
    [Fact]
    public void ExhaustedMeshBudgetFallsBackInOrderToExactCpuTriangles()
    {
        var settings = Settings(); var snapshot = Snapshot();
        // Entire CPU mesh budget is charged to the fixed selection reservation: no mesh can be admitted.
        var policy = new RenderingSettings(ShmupRenderingPath.SharedMeshes, new(1, 8192, 8192, 2, 32), new(CrushKit.Brutal.Draw2D.CircleDetailMode.Automatic, 12));
        using var owner = new ShmupRendering(policy); using var draw = new ShapeDrawList(); using var expected = new ShapeDrawList();
        var triangles = owner.Build(draw, snapshot, settings, TestSettings.Colors, TestSettings.Style, 1280, 720, out _, out var shared);
        Assert.True(shared.HasValue); Assert.Empty(shared.Value.Instances.ToArray());
        var reference = ShmupPresentation.Build(expected, snapshot, settings, TestSettings.Colors, TestSettings.Style, 1280, 720);
        Assert.Equal(reference.Vertices.ToArray(), triangles.Vertices.ToArray()); Assert.Equal(reference.Indices.ToArray(), triangles.Indices.ToArray());
        Assert.Same(policy.Limits, shared.Value.Limits);
    }
}
