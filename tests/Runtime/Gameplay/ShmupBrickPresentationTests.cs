using System.Reflection;
using Brutal.Numerics;
using BrutalBreakout.Configuration;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;
using Xunit;

public sealed class ShmupBrickPresentationTests
{
    private static ColorScheme Colors()
    {
        var c = TestSettings.Colors;
        return new(c.Name, c.Background, c.Walls, c.Ball, c.CatchReady, c.CatchLine, c.Aim,
            new(.2f, .3f, .4f, 1), c.BrickSecondary, new(.4f, .3f, .2f, 1), c.Hud, c.Win,
            c.Loss, c.BackgroundBottom, c.HeldRing, c.Trail);
    }

    public static IEnumerable<object[]> Placements()
    {
        foreach (var shape in Enum.GetValues<ShmupBrickShape>())
            for (int turn = 0; turn < 4; turn++)
                foreach (int width in new[] { 960, 1280 })
                    yield return new object[] { shape, turn, width };
    }

    private static ShmupSnapshot Snapshot(ShmupBrickState brick) =>
        (ShmupSnapshot)typeof(ShmupSnapshot).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single().Invoke(
            new object[] { 432d, 720d, new double2(640, 600), new double2(0, -1), 100,
                0d, 600d, 10d, 1d, 0d, 0L, 0L, 0L, 0L, 1, 1,
                Array.Empty<ShmupBallState>(), new[] { brick }, default(AimPreviewState) });

    [Theory]
    [MemberData(nameof(Placements))]
    public void SharedSolidRectanglesMatchBoundsAreaCutoutsColorsAndWorldTransform(ShmupBrickShape shape, int turn, int width)
    {
        var settings = ShmupConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "shmup.json")).Settings;
        var colors = Colors();
        var rectangles = settings.Brick.Geometry.Rectangles(shape, turn).ToArray();
        var bounds = settings.Brick.Geometry.BoundsSize(shape, turn);
        int height = width * 9 / 16;
        float scale = width / 1280f;
        foreach (double unit in new[] { 18d, 28.8d })
        foreach (int health in new[] { settings.Brick.Health / 2, settings.Brick.Health / 2 + 1 })
        {
            // Legacy Size is metadata: authoritative settings supply the shared physical geometry.
            var brick = new ShmupBrickState(7, 11, new(640, 200), unit, health, shape, turn);
            var snapshot = Snapshot(brick);
            var retained = snapshot.Bricks[0];
            var color = health * 2 > settings.Brick.Health ? colors.BrickPrimary : colors.BrickAccent;
            var target = new float4(color.R, color.G, color.B, color.A);
            var primary = new float4(colors.BrickPrimary.R, colors.BrickPrimary.G, colors.BrickPrimary.B, 1);
            var accent = new float4(colors.BrickAccent.R, colors.BrickAccent.G, colors.BrickAccent.B, 1);
            using var draw = new ShapeDrawList();
            var frame = ShmupPresentation.Build(draw, snapshot, settings, colors, TestSettings.Style, width, height);
            var actual = frame.Indices.ToArray().Select(i => frame.Vertices.Span[(int)i])
                .Where(v => v.Color == primary || v.Color == accent).ToArray();
            Assert.NotEmpty(actual);
            Assert.All(actual, v => Assert.Equal(target, v.Color));
            using var expected = new ShapeDrawList();
            expected.BeginFrame(width, height);
            expected.SetTransform(new float3x2(scale, 0, 0, scale, 0, 0));
            foreach (var rectangle in rectangles)
            {
                var minimum = brick.Position + (rectangle.Center - rectangle.HalfExtents);
                var maximum = brick.Position + (rectangle.Center + rectangle.HalfExtents);
                var start = new float2((float)minimum.X, (float)minimum.Y);
                var end = new float2((float)maximum.X, (float)maximum.Y);
                expected.FillRect(start, end - start, color);
            }
            var reference = expected.Finish();
            Assert.Equal(reference.Indices.ToArray().Select(i => reference.Vertices.Span[(int)i]).ToArray(), actual);
            Assert.Equal((brick.Position.X - bounds.X / 2) * scale, actual.Min(v => (double)v.Position.X), 3);
            Assert.Equal((brick.Position.X + bounds.X / 2) * scale, actual.Max(v => (double)v.Position.X), 3);
            Assert.Equal((brick.Position.Y - bounds.Y / 2) * scale, actual.Min(v => (double)v.Position.Y), 3);
            Assert.Equal((brick.Position.Y + bounds.Y / 2) * scale, actual.Max(v => (double)v.Position.Y), 3);

            double area = 0;
            for (int i = 0; i < actual.Length; i += 3)
                area += Math.Abs(Cross(actual[i].Position, actual[i + 1].Position, actual[i + 2].Position)) / 2;
            double areaExpected = shape switch { ShmupBrickShape.Square => 829.44, ShmupBrickShape.TwoByOne => 1866.24, ShmupBrickShape.TwoByTwo => 4199.04, _ => 3939.84 };
            Assert.Equal(areaExpected * scale * scale, area, 1);

            int emptySamples = 0;
            // Probe interiors and open areas; no bounding-box fill or extra outline is allowed.
            for (double x = -bounds.X / 2 + .125; x < bounds.X / 2; x += 3.6)
            for (double y = -bounds.Y / 2 + .125; y < bounds.Y / 2; y += 3.6)
            {
                bool solid = rectangles.Any(r => Math.Abs(x - r.Center.X) <= r.HalfExtents.X &&
                    Math.Abs(y - r.Center.Y) <= r.HalfExtents.Y);
                var point = new float2((float)((brick.Position.X + x) * scale),
                    (float)((brick.Position.Y + y) * scale));
                Assert.Equal(solid, Covered(actual.Select(v => v.Position).ToArray(), point));
                if (!solid) emptySamples++;
            }
            if (shape is ShmupBrickShape.L or ShmupBrickShape.T) Assert.True(emptySamples > 0);
            // Shared cell edges are covered too, rather than separated by a stroke/inset.
            foreach (var a in rectangles)
            foreach (var b in rectangles)
            {
                if (a.Center.X + a.HalfExtents.X != b.Center.X - b.HalfExtents.X) continue;
                double low = Math.Max(a.Center.Y - a.HalfExtents.Y, b.Center.Y - b.HalfExtents.Y);
                double high = Math.Min(a.Center.Y + a.HalfExtents.Y, b.Center.Y + b.HalfExtents.Y);
                if (low >= high) continue;
                var joint = new float2((float)(brick.Position.X + (a.Center.X + a.HalfExtents.X)) * scale,
                    (float)(brick.Position.Y + (low + high) / 2) * scale);
                Assert.True(Covered(actual.Select(v => v.Position).ToArray(), joint));
            }
            Assert.Equal(retained, snapshot.Bricks[0]);
            Assert.Equal(shape, retained.Shape); Assert.Equal(turn, retained.QuarterTurns);
            Assert.Equal(unit, retained.Size); Assert.Equal(new double2(640, 200), retained.Position);
        }
    }

    private static double Cross(float2 a, float2 b, float2 c) =>
        ((double)b.X - a.X) * (c.Y - a.Y) - ((double)b.Y - a.Y) * (c.X - a.X);

    private static bool Covered(float2[] triangles, float2 p)
    {
        for (int i = 0; i < triangles.Length; i += 3)
        {
            double a = Cross(triangles[i], triangles[i + 1], p);
            double b = Cross(triangles[i + 1], triangles[i + 2], p);
            double c = Cross(triangles[i + 2], triangles[i], p);
            if ((a >= -1e-5 && b >= -1e-5 && c >= -1e-5) || (a <= 1e-5 && b <= 1e-5 && c <= 1e-5))
                return true;
        }
        return false;
    }

    [Fact]
    public void ShapeAndRotationSettingsCopyMutableInputsAndLegacyFixturesStaySquareOnly()
    {
        var shapes = Enum.GetValues<ShmupBrickShape>();
        var turns = new[] { 0, 1, 2, 3 };
        var settings = new ShmupBrickSettings(28.8, 36, 100, 10, shapes, turns);
        shapes[0] = ShmupBrickShape.T; turns[0] = 3;
        Assert.Equal(Enum.GetValues<ShmupBrickShape>(), settings.Shapes.ToArray());
        Assert.Equal(new[] { 0, 1, 2, 3 }, settings.QuarterTurns.ToArray());
        var fixture = new ShmupBrickSettings(28.8, 36, 100, 10);
        Assert.Equal(new[] { ShmupBrickShape.Square }, fixture.Shapes.ToArray());
        Assert.Equal(new[] { 0 }, fixture.QuarterTurns.ToArray());
    }
}
