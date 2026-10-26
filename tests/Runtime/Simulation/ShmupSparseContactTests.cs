using Brutal.Numerics;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class ShmupSparseContactTests
{
    private static readonly double2 Away = new(100, 600);
    private static void Step(NativeShmupArena native) => native.Step(0, Away, Away);

    [Theory]
    [InlineData(false, 64)] [InlineData(false, 2048)] [InlineData(false, 4096)]
    [InlineData(true, 64)] [InlineData(true, 2048)] [InlineData(true, 4096)]
    public void QuietAndSingleContactWorkDependsOnInteractionsNotReservedPairs(bool bounce, int capacity)
    {
        var settings = ShmupSettingsTests.Settings(bounce: bounce, maxBalls: 512, maxBricks: capacity,
            speed: 1, descent: 1, threshold: 1);
        using var native = new NativeShmupArena(TestSettings.Baseline, settings, new(1, 64, 1));
        // Live populations are identical, including bricks: no collision-filter workaround.
        for (int i = 0; i < 40; i++) native.ActivateBrick(i, new(400 + i % 8 * 40, 100 + i / 8 * 40));
        for (int i = 0; i < 100; i++) native.ActivateBall(i, new(640, 400 + i), new(1, 0));
        Step(native);
        Assert.Equal(0, native.ReconcileVisits); Assert.Equal(0, native.TrackedPairCount);
        Assert.Empty(native.HitPairs.ToArray());
        native.ActivateBall(0, new(400, 100), new(1, 0));
        Step(native);
        Assert.Equal(1, native.ReconcileVisits); Assert.Equal(1, native.TrackedPairCount);
        var pair = Assert.Single(native.HitPairs.ToArray());
        Assert.Equal((0, 0), pair); Assert.Equal(1, native.BrickHits[0, 0]);
        native.ClearBall(0);
        Assert.Equal(0, native.TrackedPairCount); Assert.Empty(native.HitPairs.ToArray());
        Step(native); Assert.Equal(0, native.ReconcileVisits); Assert.Equal(0, native.BrickHits[0, 0]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BothPoolLivesRemoveEveryMembershipBeforeImmediateReuseAndDeferredEnds(bool bounce)
    {
        var settings = ShmupSettingsTests.Settings(bounce: bounce, maxBalls: 2, maxBricks: 64,
            speed: 1, descent: 1, threshold: 1);
        using var native = new NativeShmupArena(TestSettings.Baseline, settings, new(1, 64, 1));
        int bodies = native.BodyCount, fixtures = native.ShapeCount;
        for (int life = 0; life < 80; life++)
        {
            int brick = life % 64, ball = life % 2;
            native.ActivateBrick(brick, new(640, 300), life % 2 == 0 ? ShmupBrickShape.L : ShmupBrickShape.T);
            native.ActivateBall(ball, new(622, 300), new(1, 0));
            Step(native); Assert.Equal(1, native.BrickHits[ball, brick]);
            // Keep the opposite resource alive. Reusing a stable fixture ID is a new logical life.
            native.ClearBall(ball);
            Assert.Empty(native.HitPairs.ToArray()); Assert.Equal(0, native.TrackedPairCount);
            native.ActivateBall(ball, new(622, 300), new(1, 0));
            Step(native); Assert.Equal(1, native.BrickHits[ball, brick]);
            native.ClearBrick(brick);
            Assert.Empty(native.HitPairs.ToArray()); Assert.Equal(0, native.TrackedPairCount);
            native.ActivateBrick(brick, new(640, 300), ShmupBrickShape.L);
            Step(native); Assert.Equal(1, native.BrickHits[ball, brick]);
            native.ClearBrick(brick); native.ClearBall(ball);
            Step(native);
            Assert.Equal(0, native.ReconcileVisits); Assert.Equal(0, native.TrackedPairCount);
            Assert.Equal(bodies, native.BodyCount); Assert.Equal(fixtures, native.ShapeCount);
        }
        native.Reset(); Step(native);
        Assert.Equal(0, native.ReconcileVisits); Assert.Empty(native.HitPairs.ToArray());
    }

    [Theory]
    [InlineData(false, ShmupBrickShape.L)] [InlineData(true, ShmupBrickShape.L)]
    [InlineData(false, ShmupBrickShape.T)] [InlineData(true, ShmupBrickShape.T)]
    public void MultipartSeamKeepsOneLogicalPairAcrossNativeIntervals(bool bounce, ShmupBrickShape shape)
    {
        var settings = ShmupSettingsTests.Settings(bounce: bounce, maxBalls: 1, maxBricks: 1,
            speed: 1, descent: 1, threshold: 1);
        using var native = new NativeShmupArena(TestSettings.Baseline, settings, new(1, 64, 1));
        native.ActivateBrick(0, new(640, 300), shape);
        native.ActivateBall(0, shape == ShmupBrickShape.L ? new(620, 280) : new(640, 296), new(1, 0));
        Step(native); Assert.Equal(1, native.BrickHits[0, 0]); Assert.Single(native.HitPairs.ToArray());
        for (int i = 0; i < 8; i++)
        {
            Step(native); Assert.Equal(0, native.BrickHits[0, 0]); Assert.Empty(native.HitPairs.ToArray());
            Assert.Equal(1, native.TrackedPairCount); Assert.Equal(1, native.ReconcileVisits);
        }
    }

    [Fact]
    public void EarlierHitListSurvivesLaterNativeWallExpiryButNotTheNextTick()
    {
        var settings = ShmupSettingsTests.Settings(bounce: false, maxBalls: 1, maxBricks: 1,
            speed: 60000, descent: 1, threshold: 1, width: 100, wallHits: 1);
        using var native = new NativeShmupArena(TestSettings.Baseline, settings, new(1, 64, 1));
        native.ActivateBrick(0, new(640, 200)); native.ActivateBall(0, new(640, 200), new(1, 0));
        Step(native);
        Assert.True(native.Expired[0]); Assert.Equal(1, native.BrickHits[0, 0]);
        Assert.Equal((0, 0), Assert.Single(native.HitPairs.ToArray()));
        Step(native);
        Assert.Equal(0, native.ReconcileVisits); Assert.Equal(0, native.TrackedPairCount);
        Assert.Empty(native.HitPairs.ToArray()); Assert.Equal(0, native.BrickHits[0, 0]);
    }

    [Fact]
    public void LeavingTouchUnionRemovesHistoricalPairsWithoutDisablingEitherBody()
    {
        var settings = ShmupSettingsTests.Settings(bounce: false, maxBalls: 1, maxBricks: 1,
            speed: 600, descent: 1, threshold: 1);
        using var native = new NativeShmupArena(TestSettings.Baseline, settings, new(1, 64, 1));
        native.ActivateBrick(0, new(640, 300)); native.ActivateBall(0, new(640, 300), new(1, 0));
        Step(native); Assert.Single(native.HitPairs.ToArray());
        for (int i = 0; i < 10; i++) Step(native);
        Assert.Equal(0, native.TrackedPairCount); Assert.Equal(0, native.ReconcileVisits);
        Assert.Empty(native.HitPairs.ToArray()); Assert.Equal(0, native.BrickHits[0, 0]);
    }

    [Theory]
    [InlineData(64)] [InlineData(2048)] [InlineData(4096)]
    public void EcsDamageVisitsOnlyActualHitPairsAndGathersBeforeKillAndEscapeDisposal(int capacity)
    {
        var settings = ShmupSettingsTests.Settings(bounce: false, maxBalls: 3, maxBricks: capacity,
            speed: 1, descent: 1, threshold: 1, brickHealth: 20);
        using var sim = new ShmupSimulation(TestSettings.Baseline, settings, new(1, 64, 1));
        sim.SpawnBrick(new(400, 740));
        for (int i = 0; i < 3; i++) sim.SpawnBall(new(400, 740), new(1, 0));
        sim.Step();
        // Even the third hit must be gathered before ClearBrick removes the hit list.
        Assert.Equal(3, sim.DamagePairVisits); Assert.Empty(sim.Snapshot.Bricks.ToArray());
        Assert.Equal(100, sim.Snapshot.PlayerHealth);
        Assert.True(sim.Events.HasFlag(ShmupEvents.BrickDestroyed));
        sim.Step(); Assert.Equal(0, sim.DamagePairVisits);
    }

    [Fact]
    public void HitScratchHandlesEveryActualPairAndClearsOnlyMarkedCountsNextTick()
    {
        const int balls = 8, bricks = 64;
        var settings = ShmupSettingsTests.Settings(bounce: false, maxBalls: balls, maxBricks: bricks,
            speed: 1, descent: 1, threshold: 1);
        using var native = new NativeShmupArena(TestSettings.Baseline, settings, new(1, 64, 1));
        for (int i = 0; i < balls; i++) native.ActivateBall(i, new(640, 300), new(1, 0));
        for (int i = 0; i < bricks; i++) native.ActivateBrick(i, new(640, 300));
        Step(native);
        Assert.Equal(balls * bricks, native.HitPairs.Length);
        Assert.Equal(balls * bricks, native.ReconcileVisits);
        foreach (var pair in native.HitPairs) Assert.Equal(1, native.BrickHits[pair.Ball, pair.Brick]);
        Step(native);
        Assert.Empty(native.HitPairs.ToArray()); Assert.Equal(balls * bricks, native.TrackedPairCount);
        for (int i = 0; i < balls; i++) for (int j = 0; j < bricks; j++) Assert.Equal(0, native.BrickHits[i, j]);
        native.Reset(); Step(native);
        Assert.Equal(0, native.TrackedPairCount); Assert.Equal(0, native.ReconcileVisits);
    }
}
