using Brutal.Numerics;
using BrutalBreakout.Configuration;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;
using Xunit;

public sealed class AimGeometryTests
{
    private static readonly double2 Position = new(640, 600), Direction = new(0, -1);
    private static readonly WallContact Hit = new(400, new(640, 200), new(1, 0)) { Normal = new(0, 1) };
    private static ShapeFrame Build(WallContact hit, WallContact next = default, bool open = false)
    {
        using var draw = new ShapeDrawList(); draw.BeginFrame(1280, 720);
        AimGeometry.Draw(draw, Position, Direction, 6, hit, next, TestSettings.Style, TestSettings.Colors.Aim, open);
        return draw.Finish();
    }
    private static float2[] Points(ShapeFrame frame) => frame.Indices.ToArray().Select(i => frame.Vertices.Span[(int)i].Position).ToArray();
    [Fact]
    public void SharedGeometryExactlyMatchesOriginalLongLineRingAndClippedReflectedLeg()
    {
        var style = TestSettings.Style; var color = TestSettings.Colors.Aim;
        using var expected = new ShapeDrawList(); expected.BeginFrame(1280, 720);
        expected.StrokeSegment(new(640, 594), new(640, 206), (float)(6 * style.AimWidthRadiusRatio), LineCap.Butt, color);
        expected.StrokeCircle(new(640, 200), 6, (float)(6 * style.ImpactRingWidthRadiusRatio), color);
        expected.StrokeSegment(new(646, 200), new(650, 200), (float)(6 * style.AimWidthRadiusRatio), LineCap.Butt, color);
        var reference = expected.Finish();
        var actual = Build(Hit, new(10, new(650, 200), new(-1, 0)));
        Assert.Equal(reference.Indices.ToArray(), actual.Indices.ToArray());
        Assert.Equal(reference.Vertices.ToArray(), actual.Vertices.ToArray());
        Assert.Contains(Points(actual), p => p.Y <= 206 && p.Y >= 200 && p.X >= 649);
        Assert.DoesNotContain(Points(actual), p => p.X > 650.01f);
    }
    [Fact]
    public void NextObstacleInsideImpactRadiusSuppressesReflectedLeg()
    {
        var actual = Build(Hit, new(5, new(645, 200), new(-1, 0)));
        using var expected = new ShapeDrawList(); expected.BeginFrame(1280, 720);
        expected.StrokeSegment(new(640, 594), new(640, 206), (float)(6 * TestSettings.Style.AimWidthRadiusRatio), LineCap.Butt, TestSettings.Colors.Aim);
        expected.StrokeCircle(new(640, 200), 6, (float)(6 * TestSettings.Style.ImpactRingWidthRadiusRatio), TestSettings.Colors.Aim);
        Assert.Equal(expected.Finish().Vertices.ToArray(), actual.Vertices.ToArray());
    }
    [Fact]
    public void OpenExitMissDrawsFullUnreflectedLineAndAmbiguousMissOnlyLegacyFallback()
    {
        var miss = new WallContact(600, new(640, 0), default) { HasHit = false };
        var open = Build(miss, open: true); var legacy = Build(miss); var ambiguous = Build(default, open: true);
        Assert.Equal(6, open.Indices.Length); Assert.Equal(0, Points(open).Min(p => p.Y));
        Assert.True(Points(legacy).Min(p => p.Y) > 500);
        Assert.Equal(legacy.Vertices.ToArray(), ambiguous.Vertices.ToArray());
        // A reflected leg at an open edge is clipped to that edge, too.
        var nextMiss = new WallContact(9, new(649, 200), default) { HasHit = false };
        Assert.True(Points(Build(Hit, nextMiss, open: true)).Max(p => p.X) <= 649.01f);
    }
    [Fact]
    public void ShmupUsesProjectileRadiusAndSnapshotPreviewBehindAvatarAndBalls()
    {
        var settings = ShmupConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "shmup.json")).Settings;
        Assert.NotEqual(settings.Avatar.Radius, settings.Ball.Radius);
        var snapshot = BallTrailTests.Snapshot(0, Array.Empty<ShmupBallState>(), preview: new(Hit, default));
        using var draw = new ShapeDrawList();
        var frame = ShmupPresentation.Build(draw, snapshot, settings, TestSettings.Colors, TestSettings.Style, 1280, 720, out var evidence);
        using var expected = new ShapeDrawList(); expected.BeginFrame(1280, 720);
        AimGeometry.Draw(expected, snapshot.PlayerPosition, snapshot.AimDirection, settings.Ball.Radius, Hit, default,
            TestSettings.Style, TestSettings.Colors.Aim, drawOpenMiss: true);
        var reference = expected.Finish(); Assert.Equal(reference.Indices.Length, evidence.AimIndices);
        var color = TestSettings.Colors.Aim; var target = new float4(color.R, color.G, color.B, color.A);
        var aimIndices = frame.Indices.ToArray().Select((index, offset) => (index, offset))
            .Where(p => frame.Vertices.Span[(int)p.index].Color == target).ToArray();
        Assert.Equal(Points(reference), aimIndices.Select(p => frame.Vertices.Span[(int)p.index].Position).ToArray());
        // Last aim index precedes the player's opaque filled disc in painter order.
        Assert.True(aimIndices.Last().offset < frame.Indices.Length - 6);
    }
    [Fact]
    public void BallSandboxStillUsesItsOriginalPreviewAndSecondNativeQuery()
    {
        using var simulation = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        var hit = simulation.Preview();
        var next = hit.HasHit ? simulation.Query(hit.Position + hit.Normal * 1, hit.ReflectedDirection) : default;
        using var draw = new ShapeDrawList();
        var actual = BallSandbox.Build(draw, simulation, TestSettings.Colors, TestSettings.Style, 0, 1280, 720, out var evidence);
        using var expected = new ShapeDrawList(); expected.BeginFrame(1280, 720);
        AimGeometry.Draw(expected, simulation.State.Position, simulation.State.AimDirection, TestSettings.Gameplay.Ball.Radius,
            hit, next, TestSettings.Style, TestSettings.Colors.Aim);
        var reference = expected.Finish(); Assert.Equal(reference.Indices.Length, evidence.AimIndices);
        var color = TestSettings.Colors.Aim; var target = new float4(color.R, color.G, color.B, color.A);
        var points = actual.Indices.ToArray().Select(i => actual.Vertices.Span[(int)i])
            .Where(v => v.Color == target).Select(v => v.Position).ToArray();
        Assert.Equal(Points(reference), points);
    }
}
