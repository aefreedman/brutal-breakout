using System.Reflection;
using Brutal.Numerics;
using BrutalBreakout.Configuration;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;
using Xunit;

public sealed class BallTrailTests
{
    private static BallTrailSettings Trails(int points = 24, double duration = .18, bool enabled = true, int segments = 16384) =>
        new(enabled, points, duration, 1.5, .55, segments);
    // Synthetic immutable publications exercise all pool lives and saturation without native allocation.
    internal static ShmupSnapshot Snapshot(long tick, ShmupBallState[] balls, double? seconds = null, AimPreviewState preview = default) =>
        (ShmupSnapshot)typeof(ShmupSnapshot).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single().Invoke(
            new object[] { 432d, 720d, new double2(640, 600), new double2(0, -1), 100,
                seconds ?? tick / 120d, 600d, 10d, 1d, 0d, tick, 0L, 0L, 0L, balls.Length, balls.Length,
                balls, Array.Empty<ShmupBrickState>(), preview, 100 });
    private static ShmupBallState Ball(long tick, int id = 0, long generation = 1) =>
        new(id, generation, new(640 + id * .01, 600 - tick), new(0, -120), 6, 0);
    private static int Draw(BallTrailHistory history, ShmupSnapshot snapshot, out ShapeFrame frame)
    {
        using var draw = new ShapeDrawList(); draw.BeginFrame(1280, 720);
        int count = history.Draw(draw, snapshot, TestSettings.Colors.Ball); frame = draw.Finish(); return count;
    }
    [Fact]
    public void LiveStyleAndBudgetMigrationKeepNewestLifeSamplesAndNeverAddTickPoints()
    {
        var history = new BallTrailHistory(Trails(8, 10), 2);
        ShmupSnapshot snapshot = null!;
        for (int tick = 1; tick <= 6; tick++) history.Capture(snapshot = Snapshot(tick, [Ball(tick)]));
        Assert.Equal(6, history.PointCount);
        var style = new BallTrailSettings(false, 8, 20, 2, .25, 100);
        history.PrepareTuning(style)();
        Assert.Same(style, history.Settings); Assert.Equal(6, history.PointCount);
        Assert.Equal(0, Draw(history, snapshot, out _));
        var smaller = new BallTrailSettings(true, 3, 20, 3, .75, 50);
        var commit = history.PrepareTuning(smaller);
        Assert.Equal(6, history.PointCount); // prepare does not mutate the existing history
        commit();
        Assert.Equal(6, history.Capacity); Assert.Equal(3, history.PointCount);
        history.Synchronize(snapshot);
        Assert.Equal(3, history.PointCount); Assert.True(Draw(history, snapshot, out _) > 0);
        history.PrepareTuning(new(true, 12, 20, 1, .5, 100))();
        Assert.Equal(3, history.PointCount); Assert.Equal(24, history.Capacity);
        history.Capture(Snapshot(7, [Ball(7)])); Assert.Equal(4, history.PointCount);
        history.Capture(Snapshot(8, [Ball(8, generation: 2)])); Assert.Equal(1, history.PointCount);
    }

    [Fact]
    public void StrictAuthoritativeConfigurationAndNoImplicitFeatureInHeadlessSession()
    {
        var settings = BallTrailConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "trails.json")).Settings;
        Assert.True(settings.Enabled); Assert.Equal(24, settings.MaxPointsPerBall); Assert.Equal(.18, settings.DurationSeconds);
        Assert.Equal(1.5, settings.WidthRadiusRatio); Assert.Equal(.55, settings.Opacity); Assert.Equal(16384, settings.MaxRenderedSegments);
        Assert.Null(BallTrailConfiguration.LoadForMode(Array.Empty<string>(), false, "unused", Environment.CurrentDirectory));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.ValidateCapacity(1048576));
        using var session = new ShmupSession(TestSettings.Baseline, Shmup(), TestSettings.Input, new(2, 64, 2));
        Assert.Null(session.Trails);
        var disabled = new BallTrailHistory(Trails(enabled: false), 512);
        disabled.Capture(Snapshot(1, new[] { Ball(1) }));
        Assert.Equal(512 * 24, disabled.Capacity); Assert.Equal(1, disabled.PointCount); // disabled rendering retains bounded history for live enable
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"enabled\":true,\"maxPointsPerBall\":24,\"durationSeconds\":0.18,\"widthRadiusRatio\":1.5,\"opacity\":0.55}")]
    [InlineData("{\"enabled\":true,\"maxPointsPerBall\":1,\"durationSeconds\":0.18,\"widthRadiusRatio\":1.5,\"opacity\":0.55,\"maxRenderedSegments\":16384}")]
    [InlineData("{\"enabled\":true,\"maxPointsPerBall\":24,\"durationSeconds\":-1,\"widthRadiusRatio\":1.5,\"opacity\":0.55,\"maxRenderedSegments\":16384}")]
    [InlineData("{\"enabled\":true,\"maxPointsPerBall\":24,\"durationSeconds\":0.18,\"widthRadiusRatio\":0,\"opacity\":0.55,\"maxRenderedSegments\":16384}")]
    [InlineData("{\"enabled\":true,\"maxPointsPerBall\":24,\"durationSeconds\":0.18,\"widthRadiusRatio\":1.5,\"opacity\":2,\"maxRenderedSegments\":16384}")]
    [InlineData("{\"enabled\":true,\"maxPointsPerBall\":24,\"durationSeconds\":0.18,\"widthRadiusRatio\":1.5,\"opacity\":0.55,\"maxRenderedSegments\":16385}")]
    [InlineData("{\"enabled\":false,\"enabled\":true}")]
    [InlineData("{\"unknown\":1}")]
    public void InvalidSchemasFailStrictly(string json)
    {
        string path = Path.GetTempFileName();
        try { File.WriteAllText(path, json); Assert.Throws<InvalidDataException>(() => BallTrailConfiguration.Load(path)); }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData(double.NaN, 1.5, .55)]
    [InlineData(double.PositiveInfinity, 1.5, .55)]
    [InlineData(.18, double.NaN, .55)]
    [InlineData(.18, 1.5, double.NaN)]
    public void NonfiniteDimensionsRejected(double duration, double width, double opacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BallTrailSettings(true, 24, duration, width, opacity, 16384));

    [Fact]
    public void RingBudgetGenerationDespawnResetAndRetainedSnapshotsNeverBridge()
    {
        var settings = Trails(3, 10); var history = new BallTrailHistory(settings, 2);
        Assert.Same(settings, history.Settings); Assert.Equal(6, history.Capacity);
        var retained = Snapshot(1, new[] { Ball(1) }); history.Capture(retained);
        ShmupSnapshot live = retained;
        for (int tick = 2; tick < 20; tick++) history.Capture(live = Snapshot(tick, new[] { Ball(tick) }));
        Assert.Equal(3, history.PointCount); Assert.True(Draw(history, live, out _) > 0);
        Assert.Equal(0, Draw(history, retained, out _));
        history.Capture(live = Snapshot(20, new[] { Ball(20, generation: 2) }));
        Assert.Equal(1, history.PointCount); Assert.Equal(0, Draw(history, live, out _));
        history.Capture(Snapshot(21, Array.Empty<ShmupBallState>())); Assert.Equal(0, history.PointCount);
        history.Capture(Snapshot(22, new[] { Ball(22, generation: 3) }));
        history.Synchronize(Snapshot(0, Array.Empty<ShmupBallState>())); Assert.Equal(0, history.PointCount);
        history.Clear(); Assert.Equal(0, history.PointCount);
    }
    [Theory]
    [InlineData("colors.json")]
    [InlineData("ColorSchemes/night.json")]
    public void ShippedTrailTintIsWhiteIndependentOfBallColorAndKeepsItsFade(string scheme)
    {
        var colors = ColorSchemeConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", scheme)).Settings;
        Assert.Equal(new LinearColor(1, 1, 1), colors.Trail);
        Assert.NotEqual(colors.Ball, colors.Trail);
        var history = new BallTrailHistory(Trails(), 1);
        history.Capture(Snapshot(1, new[] { Ball(1) }, 0));
        history.Capture(Snapshot(2, new[] { Ball(2) }, .06));
        var snapshot = Snapshot(3, new[] { Ball(3) }, .12); history.Capture(snapshot);
        using var draw = new ShapeDrawList();
        var frame = ShmupPresentation.Build(draw, snapshot, Shmup(), colors, TestSettings.Style, 1280, 720,
            out var evidence, history);
        Assert.True(evidence.TrailIndices > 0);
        var trailVertices = frame.Vertices.ToArray().Where(v => v.Color.W is > 0 and < 1).ToArray();
        Assert.NotEmpty(trailVertices);
        Assert.All(trailVertices, v =>
        {
            // White straight RGB becomes equal RGB/alpha at the single premultiplication boundary.
            Assert.Equal(v.Color.W, v.Color.X);
            Assert.Equal(v.Color.W, v.Color.Y);
            Assert.Equal(v.Color.W, v.Color.Z);
            Assert.InRange(v.Color.W, 0, .55f);
        });
        Assert.True(trailVertices.Select(v => v.Color.W).Distinct().Count() > 1);
    }
    [Fact]
    public void FadeAndTaperUseSimulationAgeAndExpireOldPoints()
    {
        var history = new BallTrailHistory(Trails(24, .18), 1);
        history.Capture(Snapshot(1, new[] { Ball(1) }, 0));
        history.Capture(Snapshot(2, new[] { Ball(2) }, .06));
        var snapshot = Snapshot(3, new[] { Ball(3) }, .12); history.Capture(snapshot);
        Assert.Equal(12, Draw(history, snapshot, out var frame));
        var vertices = frame.Vertices.ToArray();
        Assert.All(vertices, v => Assert.InRange(v.Color.W, 0, .55f));
        Assert.True(vertices.Select(v => v.Color.W).Distinct().Count() > 1);
        // Newer segment is first; its stroke is wider than the older segment.
        Assert.True(Math.Abs(vertices[0].Position.X - vertices[1].Position.X) >
            Math.Abs(vertices[4].Position.X - vertices[5].Position.X));
        history.Capture(snapshot); Assert.Equal(3, history.PointCount);
        history.Capture(snapshot = Snapshot(4, new[] { Ball(4) }, .4));
        Assert.Equal(1, history.PointCount); Assert.Equal(0, Draw(history, snapshot, out _));
    }
    [Theory]
    [InlineData(512, 24, 1)]
    [InlineData(1024, 1024, 10)]
    public void FullCapacityStorageAndDrawAreBounded(int balls, int points, double duration)
    {
        var history = new BallTrailHistory(Trails(points, duration), balls);
        ShmupSnapshot snapshot = null!;
        for (int tick = 1; tick <= points; tick++)
        {
            snapshot = Snapshot(tick, Enumerable.Range(0, balls).Select(id => Ball(tick, id)).ToArray(), tick / 1000d);
            history.Capture(snapshot);
        }
        Assert.Equal(balls * points, history.PointCount);
        int indices = Draw(history, snapshot, out var frame);
        Assert.True(indices > 0); Assert.InRange(indices, 1, 16384 * 6);
        Assert.InRange(frame.Vertices.Length, 1, 16384 * 4);
    }
    private static ShmupSettings Shmup() => ShmupConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "shmup.json")).Settings;
    private static InputSnapshot Input(bool fire = true) => new()
    { Focused = true, PadConnected = true, PadRightTrigger = fire ? 1 : 0, PadRightStick = new(0, 1), Cursor = new(double.NaN, double.NaN) };
    [Fact]
    public void FixedTicksCatchupRenderOnlyPauseRestartModeSwitchAndDpi()
    {
        var settings = Shmup(); var trails = Trails();
        using var host = new GameplaySessionHost(TestSettings.Baseline, TestSettings.Gameplay, settings, TestSettings.Input,
            new(2, 64, 2), GameplayMode.Shmup, trails);
        var session = host.Shmup!; var view = new ShmupViewport(640, 480, 1280, 960, settings.Arena);
        Assert.Same(trails, session.Trails!.Settings);
        session.Advance(Input(false), 0, view);
        Assert.Equal(4, session.Advance(Input(), TestSettings.Baseline.StepSeconds * 4, view));
        Assert.Equal(4, session.Trails.PointCount);
        var snapshot = session.Simulation.Snapshot;
        int oldIndices = Draw(session.Trails, snapshot, out _); Assert.True(oldIndices > 0);
        for (int i = 0; i < 30; i++) session.Advance(Input(), 0, view);
        Assert.Equal(4, session.Trails.PointCount);
        session.Advance(default, 100, view); session.Advance(Input(), 100, view);
        Assert.Same(snapshot, session.Simulation.Snapshot); Assert.Equal(4, session.Trails.PointCount);
        using var draw = new ShapeDrawList();
        var original = ShmupPresentation.Build(draw, snapshot, settings, TestSettings.Colors, TestSettings.Style, 1280, 720, out var first, session.Trails);
        // ShapeFrame is a draw-list view: own the evidence before the next BeginFrame reuses storage.
        var originalTrail = original.Vertices.ToArray().Where(v => v.Color.W < 1).Select(v => v.Position).ToArray();
        var frame = ShmupPresentation.Build(draw, snapshot, settings, TestSettings.Colors, TestSettings.Style, 1280, 960, out var second, session.Trails);
        Assert.Equal(first.TrailIndices, second.TrailIndices); Assert.True(second.TrailIndices > 0);
        var letterboxTrail = frame.Vertices.ToArray().Where(v => v.Color.W < 1).Select(v => v.Position).ToArray();
        Assert.Equal(originalTrail.Select(p => new float2(p.X, p.Y + 120)).ToArray(), letterboxTrail);
        var resized = ShmupPresentation.Build(draw, snapshot, settings, TestSettings.Colors, TestSettings.Style, 960, 540, session.Trails);
        var resizedTrail = resized.Vertices.ToArray().Where(v => v.Color.W < 1).Select(v => v.Position).ToArray();
        Assert.Equal(originalTrail.Length, resizedTrail.Length);
        for (int i = 0; i < originalTrail.Length; i++)
        {
            Assert.InRange(Math.Abs(originalTrail[i].X * .75f - resizedTrail[i].X), 0, .001f);
            Assert.InRange(Math.Abs(originalTrail[i].Y * .75f - resizedTrail[i].Y), 0, .001f);
        }
        Assert.All(frame.Vertices.ToArray(), v => Assert.True(float.IsFinite(v.Position.X) && float.IsFinite(v.Position.Y)));
        session.Simulation.Widen(settings.Arena.Width); session.Advance(Input(false), 0, view);
        Assert.Equal(4, session.Trails.PointCount);
        session.Advance(Input(false) with { KeyboardRestart = true }, TestSettings.Baseline.StepSeconds, view);
        Assert.Equal(0, session.Trails.PointCount);
        host.Switch(GameplayMode.Breakout); Assert.Equal(0, session.Trails.PointCount);
        host.Switch(GameplayMode.Shmup); Assert.Same(trails, host.Shmup!.Trails!.Settings); Assert.Equal(0, host.Shmup.Trails.PointCount);
    }
}
