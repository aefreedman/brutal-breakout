using BrutalBreakout.Performance;
using BrutalBreakout.Presentation;
using CrushKit.Brutal.Draw2D;
using Xunit;

namespace BrutalBreakout.Performance.Tests;

public sealed class RenderCpuStudyTests
{
    private static ResolvedCase Resolve()
    {
        var manifest = Manifest.Load(Path.Combine(AppContext.BaseDirectory, "study.json"));
        return ResolvedCase.Load(new CaseSpec { Id = "render-tests", Target = 500 }, manifest.Policy,
            Path.Combine(AppContext.BaseDirectory, "Config"), 1);
    }
    [Fact]
    public void FrozenGeometryCasesIncludeLoadResolutionTrailAndEndControl()
    {
        var variants = RenderCpuStudy.Variants();
        Assert.Equal(13, variants.Length);
        Assert.Equal(13, variants.Select(v => v.Id).Distinct().Count());
        Assert.Equal(4, variants.Where(v => v.Height == 720 && v.Trails).Select(v => v.Balls).Distinct().Count());
        Assert.Contains(variants, v => v.Width == 3840 && v.Height == 2160 && !v.Trails);
        Assert.Equal(variants[4] with { Id = variants[^1].Id }, variants[^1]);
    }
    [Fact]
    public void NativeWorldFreeFixtureRetainsEachTickHistoryAndFrozenOwnedSnapshot()
    {
        var c = Resolve(); var layout = new Layout(c.Settings, c.Policy);
        var withTrails = RenderCpuStudy.Fixture(c, layout, 500, true);
        var withoutTrails = RenderCpuStudy.Fixture(c, layout, 500, false);
        Assert.Equal(500, withTrails.Snapshot.Balls.Length);
        Assert.Equal(40, withTrails.Snapshot.Bricks.Length);
        Assert.Equal(withTrails.Snapshot.Balls.ToArray(), withoutTrails.Snapshot.Balls.ToArray());
        Assert.Null(withoutTrails.History);
        Assert.True(withTrails.History!.PointCount > 500);
        var ownedBalls = withTrails.Snapshot.Balls.ToArray();
        using var draw = new ShapeDrawList();
        var frame = ShmupPresentation.Build(draw, withTrails.Snapshot, c.Settings, c.Colors, c.Style, 1280, 720,
            out var evidence, withTrails.History);
        Assert.True(evidence.TrailIndices > 0);
        var ownedVertices = frame.Vertices.ToArray(); var ownedIndices = frame.Indices.ToArray();
        using var bytes = new MemoryStream(); RenderCpuStudy.WriteFrame(bytes, frame);
        Assert.Equal(24L + ownedVertices.Length * 24L + ownedIndices.Length * 4L + frame.Commands.Length * 24L, bytes.Length);
        bytes.Position = 0;
        using var reader = new BinaryReader(bytes, System.Text.Encoding.UTF8, true);
        Assert.Equal(RenderCpuStudy.FrameMagic, reader.ReadUInt32());
        Assert.Equal(1280, reader.ReadInt32()); Assert.Equal(720, reader.ReadInt32());
        Assert.Equal(ownedVertices.Length, reader.ReadInt32()); Assert.Equal(ownedIndices.Length, reader.ReadInt32());
        Assert.Equal(frame.Commands.Length, reader.ReadInt32());
        var repeat = ShmupPresentation.Build(draw, withTrails.Snapshot, c.Settings, c.Colors, c.Style, 1280, 720, withTrails.History);
        Assert.Equal(ownedVertices, repeat.Vertices.ToArray()); Assert.Equal(ownedIndices, repeat.Indices.ToArray());
        Assert.Equal(ownedBalls, withTrails.Snapshot.Balls.ToArray());
    }
}
