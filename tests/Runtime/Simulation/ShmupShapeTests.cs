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
            new(28.8, 32.4, 100, 10, shapes ? Enum.GetValues<ShmupBrickShape>() : [ShmupBrickShape.Square], shapes ? [0, 1, 2, 3] : [0]),
            b.Difficulty, b.Noise, b.Capacity);
    }
    private static double2 Rotate(double2 p, int q)
    { for (int i = 0; i < q; i++) p = new(-p.Y, p.X); return p; }
    private static ShmupSimulation Sim(ShmupSettings s) => new(TestSettings.Baseline, s, new(1, 64, 1));

    [Fact]
    public void GeometryAllRotationsHasExactAreaBoundsAndNoInternalOverlap()
    {
        foreach (var shape in Enum.GetValues<ShmupBrickShape>()) for (int q = 0; q < 4; q++)
        {
            var rects = ShmupBrickGeometry.Rectangles(shape, q).ToArray();
            var size = ShmupBrickGeometry.BoundsSize(shape, q);
            Assert.Equal(-size.X / 2, rects.Min(r => r.Center.X - r.HalfExtents.X));
            Assert.Equal(size.X / 2, rects.Max(r => r.Center.X + r.HalfExtents.X));
            Assert.Equal(-size.Y / 2, rects.Min(r => r.Center.Y - r.HalfExtents.Y));
            Assert.Equal(size.Y / 2, rects.Max(r => r.Center.Y + r.HalfExtents.Y));
            Assert.Equal(shape == ShmupBrickShape.Square ? 1 : shape == ShmupBrickShape.TwoByOne ? 2 : 4,
                rects.Sum(r => 4 * r.HalfExtents.X * r.HalfExtents.Y));
            for (int i = 0; i < rects.Length; i++) for (int j = i + 1; j < rects.Length; j++)
                Assert.True(Math.Abs(rects[i].Center.X - rects[j].Center.X) >= rects[i].HalfExtents.X + rects[j].HalfExtents.X ||
                    Math.Abs(rects[i].Center.Y - rects[j].Center.Y) >= rects[i].HalfExtents.Y + rects[j].HalfExtents.Y);
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
                Assert.InRange(Math.Abs((brick.Position.X - 640) / 32.4 - Math.Round((brick.Position.X - 640) / 32.4)), 0, 1e-5);
            var old = narrow.Snapshot.Bricks.ToArray(); narrow.Widen(1000);
            Assert.Equal(old, narrow.Snapshot.Bricks.ToArray());
        }
    }
    [Fact]
    public void SeededSpawningIncludesAllShapesAndRotationsWithoutBoundingOverlap()
    {
        using var a = Sim(Settings(threshold: 0)); using var b = Sim(Settings(threshold: 0));
        var seen = new HashSet<ShmupBrickShape>(); var turns = new HashSet<int>();
        for (int tick = 0; tick < 800; tick++)
        {
            a.Step(); b.Step(); Assert.Equal(a.Snapshot.Bricks.ToArray(), b.Snapshot.Bricks.ToArray());
            var live = a.Snapshot.Bricks.ToArray();
            for (int i = 0; i < live.Length; i++)
            {
                seen.Add(live[i].Shape); turns.Add(live[i].QuarterTurns);
                var s = ShmupBrickGeometry.BoundsSize(live[i].Shape, live[i].QuarterTurns) * live[i].Size;
                Assert.InRange(live[i].Position.X, s.X / 2 - .001, 1280 - s.X / 2 + .001);
                for (int j = i + 1; j < live.Length; j++)
                {
                    var t = ShmupBrickGeometry.BoundsSize(live[j].Shape, live[j].QuarterTurns) * live[j].Size;
                    var d = live[i].Position - live[j].Position;
                    Assert.True(Math.Abs(d.X) >= (s.X + t.X) / 2 - .002 || Math.Abs(d.Y) >= (s.Y + t.Y) / 2 - .002);
                }
            }
        }
        Assert.Equal(5, seen.Count); Assert.Equal(4, turns.Count);
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
        var position = new double2(640, 300) + hole * 28.8;
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
        sim.SpawnBall(new(640, 285.6), new(1, 0));
        for (int i = 0; i < 40; i++) sim.Step();
        Assert.Equal(90, Assert.Single(sim.Snapshot.Bricks.ToArray()).Health);
    }
    [Fact]
    public void CompoundPiercingReentryInLaterIntervalsDamagesAgainAndLifeReuseClearsContacts()
    {
        var basis = Settings(bounce: false, width: 140, speed: 60000, descent: 1, bricks: 1);
        var s = new ShmupSettings(basis.Arena, basis.Avatar,
            new(3, 60000, 12, 10, false, 100), basis.Brick, basis.Difficulty, basis.Noise, basis.Capacity);
        using var sim = Sim(s);
        sim.SpawnBrick(new(640, 300), ShmupBrickShape.T); sim.SpawnBall(new(640, 285.6), new(1, 0)); sim.Step();
        Assert.InRange(sim.Snapshot.Bricks[0].Health, 10, 80);
        using var native = new NativeShmupArena(TestSettings.Baseline, Settings(bounce: false, speed: 1, descent: 1, bricks: 1), new(1, 64, 1));
        native.ActivateBall(0, new(640, 300), new(1, 0));
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
        sim.SpawnBall(new double2(640, 300) + Rotate(new(.5, -1), turns) * 28.8, Rotate(new(0, -1), turns));
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
