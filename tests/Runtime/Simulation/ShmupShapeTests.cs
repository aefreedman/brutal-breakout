using Brutal.Numerics;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class ShmupShapeTests
{
    private static ShmupSettings Settings(bool bounce = true, double width = 1280, double threshold = 1,
        double speed = 600, double descent = 120, int bricks = 64, bool shapes = true)
    {
        var b = ShmupSettingsTests.Settings(bounce: bounce, maxBalls: 2, maxBricks: bricks, threshold: threshold,
            width: width, speed: speed, descent: descent, health: int.MaxValue, rowInterval: .35);
        return new(b.Arena, b.Avatar, b.Ball,
            new(28.8, 36, 100, 10, shapes ? Enum.GetValues<ShmupBrickShape>() : [ShmupBrickShape.Square], shapes ? [0, 1, 2, 3] : [0]),
            b.Difficulty, b.Noise, b.Capacity);
    }
    private static double2 Rotate(double2 p, int q)
    { for (int i = 0; i < q; i++) p = new(-p.Y, p.X); return p; }
    private static ShmupSimulation Sim(ShmupSettings s) => new(TestSettings.Baseline, s, new(1, 64, 1));

    [Fact]
    public void GeometryAllRotationsHasExactAreaBoundsAndNoInternalOverlap()
    {
        var geometry = new ShmupBrickSettings(28.8, 36, 100, 10).Geometry;
        double2[] expectedBounds = [new(28.8, 28.8), new(64.8, 28.8), new(64.8, 64.8), new(64.8, 100.8), new(100.8, 64.8)];
        double[] expectedAreas = [829.44, 1866.24, 4199.04, 3939.84, 3939.84];
        foreach (var shape in Enum.GetValues<ShmupBrickShape>()) for (int q = 0; q < 4; q++)
        {
            var rects = geometry.Rectangles(shape, q).ToArray();
            var b = expectedBounds[(int)shape];
            var size = q % 2 == 0 ? b : new(b.Y, b.X);
            Assert.Equal(size, geometry.BoundsSize(shape, q));
            Assert.Equal(-size.X / 2, rects.Min(r => r.Center.X - r.HalfExtents.X), 8);
            Assert.Equal(size.X / 2, rects.Max(r => r.Center.X + r.HalfExtents.X), 8);
            Assert.Equal(-size.Y / 2, rects.Min(r => r.Center.Y - r.HalfExtents.Y), 8);
            Assert.Equal(size.Y / 2, rects.Max(r => r.Center.Y + r.HalfExtents.Y), 8);
            Assert.Equal(expectedAreas[(int)shape], rects.Sum(r => 4 * r.HalfExtents.X * r.HalfExtents.Y), 7);
            for (int i = 0; i < rects.Length; i++) for (int j = i + 1; j < rects.Length; j++)
                Assert.True(Math.Abs(rects[i].Center.X - rects[j].Center.X) >= rects[i].HalfExtents.X + rects[j].HalfExtents.X - 1e-10 ||
                    Math.Abs(rects[i].Center.Y - rects[j].Center.Y) >= rects[i].HalfExtents.Y + rects[j].HalfExtents.Y - 1e-10);
            // Independent union of hypothetical 1x1 squares and bridges between adjacent cells.
            double2[] cells = shape switch
            {
                ShmupBrickShape.Square => [new(0, 0)],
                ShmupBrickShape.TwoByOne => [new(-18, 0), new(18, 0)],
                ShmupBrickShape.TwoByTwo => [new(-18, -18), new(18, -18), new(-18, 18), new(18, 18)],
                ShmupBrickShape.L => [new(-18, -36), new(-18, 0), new(-18, 36), new(18, 36)],
                _ => [new(-36, -18), new(0, -18), new(36, -18), new(0, 18)]
            };
            for (double x = -b.X / 2 - .9; x < b.X / 2 + 1; x += 1.8)
            for (double y = -b.Y / 2 - .9; y < b.Y / 2 + 1; y += 1.8)
            {
                bool expected = cells.Any(c => Math.Abs(x - c.X) <= 14.4 && Math.Abs(y - c.Y) <= 14.4);
                foreach (var a in cells) foreach (var c in cells)
                    if (Math.Abs(a.X - c.X) + Math.Abs(a.Y - c.Y) == 36)
                        expected |= x >= Math.Min(a.X, c.X) - 14.4 && x <= Math.Max(a.X, c.X) + 14.4 &&
                            y >= Math.Min(a.Y, c.Y) - 14.4 && y <= Math.Max(a.Y, c.Y) + 14.4;
                if (shape == ShmupBrickShape.TwoByTwo) expected |= Math.Abs(x) <= 3.6 && Math.Abs(y) <= 3.6;
                var p = Rotate(new(x, y), q);
                Assert.Equal(expected, rects.Any(r => Math.Abs(p.X - r.Center.X) <= r.HalfExtents.X + 1e-10 &&
                    Math.Abs(p.Y - r.Center.Y) <= r.HalfExtents.Y + 1e-10));
            }
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativePartsCoverGridCellsFilledBridgesAndExpandedOuterEdgesForEveryRotation(bool bounce)
    {
        using var native = new NativeShmupArena(TestSettings.Baseline,
            Settings(bounce: bounce, speed: 1, descent: 1, bricks: 1), new(1, 64, 1));
        var center = new double2(640, 300);
        foreach (var shape in Enum.GetValues<ShmupBrickShape>()) for (int q = 0; q < 4; q++)
        {
            double2[] probes = shape switch
            {
                ShmupBrickShape.Square => [new(0, 0)],
                ShmupBrickShape.TwoByOne => [new(-18, 0), new(18, 0), new(0, 0)],
                ShmupBrickShape.TwoByTwo => [new(-18, -18), new(18, 18), new(0, 0)],
                ShmupBrickShape.L => [new(-18, -36), new(18, 36), new(-18, -18), new(-18, 18), new(0, 36)],
                _ => [new(-36, -18), new(36, -18), new(0, 18), new(-18, -18), new(18, -18), new(0, 0)]
            };
            foreach (var probe in probes)
            {
                native.ClearBrick(0); native.ClearBall(0);
                native.ActivateBrick(0, center, shape, q);
                var point = center + Rotate(probe, q);
                native.ActivateBall(0, point, new(1, 0));
                native.Step(0, point, point);
                Assert.True(native.AvatarContact);
                Assert.Equal(1, native.BrickHits[0, 0]);
            }
            if (!bounce || shape == ShmupBrickShape.Square) continue;
            // These rays lie beyond the previous compact outer edge, but inside the grid footprint.
            double2 ray = shape switch
            {
                ShmupBrickShape.TwoByOne or ShmupBrickShape.TwoByTwo => new(31, 100),
                ShmupBrickShape.L => new(31, 100),
                _ => new(49, -100)
            };
            var hit = native.Query(center + Rotate(ray, q), Rotate(new(0, ray.Y > 0 ? -1 : 1), q));
            Assert.True(hit.HasHit);
            Assert.InRange(hit.Distance, 40, 100);
        }
    }
    [Fact]
    public void ChoiceArraysAreCopiedAndValidatedAndLegacyRemainsSquare()
    {
        ShmupBrickShape[] shapes = [ShmupBrickShape.L]; int[] turns = [1];
        var s = new ShmupBrickSettings(28.8, 32.4, 100, 10, shapes, turns);
        shapes[0] = ShmupBrickShape.T; turns[0] = 3;
        Assert.Equal(ShmupBrickShape.L, s.Shapes[0]); Assert.Equal(1, s.QuarterTurns[0]);
        Assert.Equal([ShmupBrickShape.Square], new ShmupBrickSettings(28.8, 32.4, 100, 10).Shapes.ToArray());
        Assert.Throws<ArgumentException>(() => new ShmupBrickSettings(1, 1, 1, 1, [], [0]));
        Assert.Throws<ArgumentException>(() => new ShmupBrickSettings(1, 1, 1, 1, [ShmupBrickShape.L, ShmupBrickShape.L], [0]));
        Assert.Throws<ArgumentException>(() => new ShmupBrickSettings(1, 1, 1, 1, [(ShmupBrickShape)99], [0]));
        Assert.Throws<ArgumentException>(() => new ShmupBrickSettings(1, 1, 1, 1, [ShmupBrickShape.L], [4]));
        Assert.Throws<ArgumentException>(() => new ShmupBrickSettings(1, 1, 1, 1, [ShmupBrickShape.L], [0, 0]));
    }
    [Theory]
    [InlineData(0)] [InlineData(.35)]
    public void LatticeAndGlobalNoiseSurviveNonMultipleWidthsAndUnlockThresholds(double threshold)
    {
        double[] widths = [199, 222.999, 223.2, 224, 301, 777];
        foreach (double width in widths)
        {
            using var narrow = Sim(Settings(width: width, threshold: threshold, shapes: false));
            using var wide = Sim(Settings(width: 1000, threshold: threshold, shapes: false));
            narrow.Step(); wide.Step();
            var expected = wide.Snapshot.Bricks.ToArray().Where(b => Math.Abs(b.Position.X - 640) + b.Size / 2 <= width / 2 + .0001).Select(b => b.Position.X).ToArray();
            Assert.Equal(expected, narrow.Snapshot.Bricks.ToArray().Select(b => b.Position.X));
            foreach (var brick in narrow.Snapshot.Bricks)
                Assert.InRange(Math.Abs((brick.Position.X - 640) / 36 - Math.Round((brick.Position.X - 640) / 36)), 0, 1e-5);
            var old = narrow.Snapshot.Bricks.ToArray(); narrow.Widen(1000);
            Assert.Equal(old, narrow.Snapshot.Bricks.ToArray());
        }
    }
    [Fact]
    public void SeededSpawningIncludesAllShapesAndRotationsWithoutBoundingOverlap()
    {
        using var a = Sim(Settings(threshold: 0)); using var b = Sim(Settings(threshold: 0));
        var seen = new HashSet<ShmupBrickShape>(); var turns = new HashSet<int>();
        double travel = 0;
        for (int tick = 0; tick < 800; tick++)
        {
            a.Step(); b.Step(); Assert.Equal(a.Snapshot.Bricks.ToArray(), b.Snapshot.Bricks.ToArray());
            travel += a.Snapshot.DescentSpeed * a.StepSeconds;
            var live = a.Snapshot.Bricks.ToArray();
            for (int i = 0; i < live.Length; i++)
            {
                seen.Add(live[i].Shape); turns.Add(live[i].QuarterTurns);
                var cells = ShmupBrickGeometry.CellDimensions(live[i].Shape, live[i].QuarterTurns);
                double column = (live[i].Position.X - (cells.Columns - 1) * 18 - 640) / 36;
                double row = (live[i].Position.Y + (cells.Rows - 1) * 18 + 14.4 - travel) / 36;
                Assert.InRange(Math.Abs(column - Math.Round(column)), 0, 1e-5);
                Assert.InRange(Math.Abs(row - Math.Round(row)), 0, .001);
                var s = a.Settings.Brick.Geometry.BoundsSize(live[i].Shape, live[i].QuarterTurns);
                Assert.InRange(live[i].Position.X, s.X / 2 - .001, 1280 - s.X / 2 + .001);
                for (int j = i + 1; j < live.Length; j++)
                {
                    var t = a.Settings.Brick.Geometry.BoundsSize(live[j].Shape, live[j].QuarterTurns);
                    var d = live[i].Position - live[j].Position;
                    Assert.True(Math.Abs(d.X) >= (s.X + t.X) / 2 - .002 || Math.Abs(d.Y) >= (s.Y + t.Y) / 2 - .002);
                }
            }
        }
        Assert.Equal(5, seen.Count); Assert.Equal(4, turns.Count);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void EvenDimensionFootprintsKeepCellLatticeAndWallFitAcrossWidening(int turns)
    {
        var b = Settings(width: 199, threshold: 0, bricks: 8);
        var settings = new ShmupSettings(b.Arena, b.Avatar, b.Ball,
            new(28.8, 36, 100, 10, [ShmupBrickShape.TwoByOne, ShmupBrickShape.TwoByTwo], [turns]),
            b.Difficulty, b.Noise, b.Capacity);
        using var sim = Sim(settings);
        sim.Step(); var before = sim.Snapshot.Bricks.ToArray(); Assert.NotEmpty(before);
        sim.Widen(301); Assert.Equal(before, sim.Snapshot.Bricks.ToArray());
        for (int i = 0; i < 80; i++) sim.Step();
        foreach (var brick in sim.Snapshot.Bricks)
        {
            var cells = ShmupBrickGeometry.CellDimensions(brick.Shape, brick.QuarterTurns);
            double leftCell = brick.Position.X - (cells.Columns - 1) * 18;
            for (int column = 0; column < cells.Columns; column++)
            {
                double lattice = (leftCell + column * 36 - 640) / 36;
                Assert.InRange(Math.Abs(lattice - Math.Round(lattice)), 0, 1e-5);
            }
            var bounds = settings.Brick.Geometry.BoundsSize(brick.Shape, brick.QuarterTurns);
            Assert.InRange(brick.Position.X - bounds.X / 2, 640 - 301d / 2 - .001, 640 + 301d / 2);
            Assert.InRange(brick.Position.X + bounds.X / 2, 640 - 301d / 2, 640 + 301d / 2 + .001);
        }
    }
    [Fact]
    public void PitchAwareCatalogIsCachedPerSettingsAndRejectsUnrepresentableGeometryBeforeAllocation()
    {
        var b = Settings(bricks: 1);
        Assert.Same(b.Brick.Geometry, b.Brick.Geometry);
        var other = new ShmupBrickSettings(28.8, 40, 100, 10);
        Assert.NotSame(b.Brick.Geometry, other.Geometry);
        Assert.Equal(68.8, other.Geometry.BoundsSize(ShmupBrickShape.TwoByOne, 0).X);
        Assert.ThrowsAny<ArgumentException>(() => new ShmupBrickSettings(28.8, double.MaxValue, 100, 10));
        Assert.ThrowsAny<ArgumentException>(() => new ShmupSettings(b.Arena, b.Avatar, b.Ball,
            new(28.8, 1e30, 100, 10), b.Difficulty, b.Noise, b.Capacity));
    }
    [Fact]
    public void RepeatedGridRowsDoNotRefillOrAccumulateSpawnDebtAndRestartResetsPhase()
    {
        var basis = Settings(width: 40, threshold: 0, descent: 1, bricks: 2, shapes: false);
        var settings = new ShmupSettings(basis.Arena, basis.Avatar, basis.Ball, basis.Brick,
            new(TestSettings.Baseline.StepSeconds, TestSettings.Baseline.StepSeconds, 1, 1, 60, 1, 1), basis.Noise, basis.Capacity);
        using var sim = Sim(settings);
        sim.Step(); var first = Assert.Single(sim.Snapshot.Bricks.ToArray());
        for (int i = 0; i < 49; i++) sim.Step();
        Assert.Equal(50, sim.Snapshot.RowsSpawned);
        Assert.Equal(2, sim.Snapshot.Bricks.Length);
        Assert.Equal(0, sim.Snapshot.DroppedBricks);
        sim.Restart(); sim.Step();
        Assert.Equal(first.Position, Assert.Single(sim.Snapshot.Bricks.ToArray()).Position);
    }
    [Theory]
    [InlineData(ShmupBrickShape.L, 0)] [InlineData(ShmupBrickShape.L, 1)]
    [InlineData(ShmupBrickShape.L, 2)] [InlineData(ShmupBrickShape.L, 3)]
    [InlineData(ShmupBrickShape.T, 0)] [InlineData(ShmupBrickShape.T, 1)]
    [InlineData(ShmupBrickShape.T, 2)] [InlineData(ShmupBrickShape.T, 3)]
    public void RotatedCutoutsAreEmptyForAimAndAvatar(ShmupBrickShape shape, int q)
    {
        using var native = new NativeShmupArena(TestSettings.Baseline, Settings(bricks: 1), new(1, 64, 1));
        native.ActivateBrick(0, new(640, 300), shape, q);
        var hole = Rotate(shape == ShmupBrickShape.L ? new(.5, -1) : new(-1, .5), q);
        var direction = Rotate(shape == ShmupBrickShape.L ? new(0, -1) : new(0, 1), q);
        var position = new double2(640, 300) + hole * 36;
        var hit = native.Query(position, direction);
        Assert.True(hit.Distance > 50); // Not ambiguous initial overlap or an adjacent occupied cell.
        native.Step(0, position, position); Assert.False(native.AvatarContact);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MultipartSeamsDamageOnceWhileContinuous(bool bounce)
    {
        using var sim = Sim(Settings(bounce: bounce, speed: 1, descent: 1, bricks: 1));
        sim.SpawnBrick(new(640, 300), ShmupBrickShape.T);
        sim.SpawnBall(new(640, 296.4), new(1, 0));
        for (int i = 0; i < 40; i++) sim.Step();
        Assert.Equal(90, Assert.Single(sim.Snapshot.Bricks.ToArray()).Health);
    }
    [Fact]
    public void CompoundPiercingReentryInLaterIntervalsDamagesAgainAndLifeReuseClearsContacts()
    {
        var basis = Settings(bounce: false, width: 140, speed: 60000, descent: 1, bricks: 1);
        var s = new ShmupSettings(basis.Arena, basis.Avatar,
            new(3, 60000, 12, 10, false, 100, 64), basis.Brick, basis.Difficulty, basis.Noise, basis.Capacity);
        using var sim = Sim(s);
        sim.SpawnBrick(new(640, 300), ShmupBrickShape.T); sim.SpawnBall(new(640, 282), new(1, 0)); sim.Step();
        Assert.InRange(sim.Snapshot.Bricks[0].Health, 10, 80);
        using var native = new NativeShmupArena(TestSettings.Baseline, Settings(bounce: false, speed: 1, descent: 1, bricks: 1), new(1, 64, 1));
        native.ActivateBall(0, new(630, 300), new(1, 0));
        foreach (var shape in new[] { ShmupBrickShape.Square, ShmupBrickShape.L, ShmupBrickShape.T, ShmupBrickShape.Square })
        {
            native.ActivateBrick(0, new(640, 300), shape);
            native.Step(0, new(100, 600), new(100, 600));
            Assert.Equal(1, native.BrickHits[0, 0]);
            native.Step(0, new(100, 600), new(100, 600)); Assert.Equal(0, native.BrickHits[0, 0]);
            native.ClearBrick(0);
        }
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void PiercingSweepsDoNotFillRotatedLCutouts(int turns)
    {
        using var sim = Sim(Settings(bounce: false, speed: 100, descent: 1, bricks: 1));
        sim.SpawnBrick(new(640, 300), ShmupBrickShape.L, turns);
        sim.SpawnBall(new double2(640, 300) + Rotate(new(.5, -1), turns) * 36, Rotate(new(0, -1), turns));
        for (int i = 0; i < 10; i++) sim.Step();
        Assert.Equal(100, sim.Snapshot.Bricks[0].Health);
    }
    [Fact]
    public void RotatedEntireFootprintMustExitBeforeEscapeAndReuseKeepsNativeCounts()
    {
        using var sim = Sim(Settings(descent: 120, bricks: 1));
        sim.SpawnBrick(new(640, 750), ShmupBrickShape.L); sim.Step();
        Assert.Single(sim.Snapshot.Bricks.ToArray());
        for (int i = 0; i < 20; i++) sim.Step();
        Assert.Empty(sim.Snapshot.Bricks.ToArray());
        using var native = new NativeShmupArena(TestSettings.Baseline, Settings(bricks: 1), new(1, 64, 1));
        int bodies = native.BodyCount, fixtures = native.ShapeCount;
        foreach (var shape in new[] { ShmupBrickShape.Square, ShmupBrickShape.L, ShmupBrickShape.T, ShmupBrickShape.Square })
        {
            native.ActivateBrick(0, new(640, 300), shape, 0);
            Assert.Equal(bodies, native.BodyCount); Assert.Equal(fixtures, native.ShapeCount);
            native.ClearBrick(0); Assert.False(native.Query(new(640, 600), new(0, -1)).HasHit);
        }
        native.ActivateBrick(0, new(640, 300));
        Assert.False(native.Query(new(615, 600), new(0, -1)).HasHit); // No leftover L arm.
        Assert.True(native.Query(new(640, 600), new(0, -1)).HasHit);
    }
}
