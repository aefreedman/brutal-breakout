using Brutal.Numerics;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class ShmupSimulationTests
{
    private static ShmupSimulation Create(ShmupSettings? settings = null) => new(TestSettings.Baseline,
        settings ?? ShmupSettingsTests.Settings(threshold: 1), new(1, 64, 1));
    private static void Advance(ShmupSimulation sim, int ticks, ShmupCommand command = default)
    { for (int i = 0; i < ticks; i++) sim.Step(command); }

    [Fact]
    public void MovementIsAnalogClampedAndAimRetainedWithoutGpu()
    {
        var settings = ShmupSettingsTests.Settings(threshold: 1);
        using var sim = Create(settings);
        Assert.Same(settings, sim.Settings);
        var initial = sim.Snapshot;
        sim.Step(new(new(.5, 0), new(1, -1)));
        Assert.InRange(sim.Snapshot.PlayerPosition.X - initial.PlayerPosition.X, 1.04, 1.05);
        double2 aim = sim.Snapshot.AimDirection;
        sim.Step(new(new(double.NaN, 0), new(0, 0)));
        Assert.Equal(aim, sim.Snapshot.AimDirection);
        sim.Step(new(AimDirection: new(double.PositiveInfinity, 0)));
        Assert.Equal(aim, sim.Snapshot.AimDirection);
        Advance(sim, 1000, new(new(double.MaxValue, -double.MaxValue)));
        Assert.Equal(928, sim.Snapshot.PlayerPosition.X);
        Assert.Equal(12, sim.Snapshot.PlayerPosition.Y);
        Assert.Equal(new double2(640, 696), initial.PlayerPosition);
        Assert.Empty(initial.Balls.ToArray());
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void NativeMovingBrickReceivesSingleEntryDamageAndBounceToggleControlsResponse(bool bounce)
    {
        using var sim = Create(ShmupSettingsTests.Settings(bounce: bounce, threshold: 1));
        sim.SpawnBrick(new(640, 300)); sim.SpawnBall(new(640, 350), new(0, -1));
        Advance(sim, 20);
        Assert.Equal(90, Assert.Single(sim.Snapshot.Bricks.ToArray()).Health);
        var ball = Assert.Single(sim.Snapshot.Balls.ToArray());
        Assert.True(bounce ? ball.Velocity.Y > 0 : ball.Velocity.Y < 0);
        Assert.InRange(double2.Distance(default, ball.Velocity), 599.99, 600.01);
        Assert.True(sim.Snapshot.Bricks[0].Position.Y > 304);
    }
    [Fact]
    public void FastPiercingSweepsEveryCrossedMovingBrickAndDamageIsPerPair()
    {
        using var sim = Create(ShmupSettingsTests.Settings(bounce: false, threshold: 1, speed: 20000));
        sim.SpawnBrick(new(640, 500)); sim.SpawnBrick(new(640, 450));
        sim.SpawnBall(new(640, 600), new(0, -1)); sim.SpawnBall(new(640, 590), new(0, -1));
        sim.Step();
        Assert.Equal(2, sim.Snapshot.Bricks.Length);
        Assert.All(sim.Snapshot.Bricks.ToArray(), b => Assert.Equal(80, b.Health));
        Assert.Equal(2, sim.Snapshot.Balls.Length);
        sim.Step();
        Assert.All(sim.Snapshot.Bricks.ToArray(), b => Assert.Equal(80, b.Health));
    }
    [Fact]
    public void MultipleEntriesOfSamePiercingPairWithinOneTickEachDealDamage()
    {
        using var sim = Create(ShmupSettingsTests.Settings(bounce: false, threshold: 1, width: 100, speed: 60000, descent: 1, wallHits: 100));
        sim.SpawnBrick(new(640, 200)); sim.SpawnBall(new(640, 200), new(1, 0));
        sim.Step();
        Assert.Equal(40, sim.Snapshot.Bricks[0].Health);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void WallExpiryStopsLaterBrickInteractionsWithinTheSameTick(bool bounce)
    {
        using var sim = Create(ShmupSettingsTests.Settings(bounce: bounce, threshold: 1, width: 100, speed: 60000, descent: 1, wallHits: 1));
        sim.SpawnBrick(new(640, 200)); sim.SpawnBall(new(670, 200), new(1, 0));
        sim.Step();
        Assert.Equal(100, sim.Snapshot.Bricks[0].Health);
        Assert.Empty(sim.Snapshot.Balls.ToArray());
    }
    [Fact]
    public void InitialPiercingEntryDoesNotRescueBallFromALaterExpiryWithinTheTick()
    {
        using var sim = Create(ShmupSettingsTests.Settings(bounce: false, threshold: 1, width: 100, speed: 60000, descent: 1, wallHits: 1));
        sim.SpawnBrick(new(640, 200)); sim.SpawnBall(new(640, 200), new(1, 0)); sim.Step();
        Assert.Equal(90, sim.Snapshot.Bricks[0].Health); Assert.Empty(sim.Snapshot.Balls.ToArray());
    }
    [Fact]
    public void MultipleBouncingContactsWithSameBrickWithinOneTickEachDealDamage()
    {
        using var sim = Create(ShmupSettingsTests.Settings(threshold: 1, width: 200, speed: 60000, descent: 1, wallHits: 100));
        sim.SpawnBrick(new(640, 200)); sim.SpawnBall(new(670, 200), new(1, 0)); sim.Step();
        Assert.InRange(sim.Snapshot.Bricks[0].Health, 50, 70);
    }
    [Fact]
    public void MultipleEntryDamageMultiplicationCannotOverflow()
    {
        var basis = ShmupSettingsTests.Settings(bounce: false, threshold: 1, width: 100, speed: 60000, descent: 1, wallHits: 100, brickHealth: int.MaxValue);
        var settings = new ShmupSettings(basis.Arena, basis.Avatar,
            new(basis.Ball.Radius, basis.Ball.Speed, basis.Ball.FireRate, int.MaxValue, false, basis.Ball.MaxWallHits),
            basis.Brick, basis.Difficulty, basis.Noise, basis.Capacity);
        using var sim = Create(settings);
        sim.SpawnBrick(new(640, 200)); sim.SpawnBall(new(640, 200), new(1, 0)); sim.Step();
        Assert.Empty(sim.Snapshot.Bricks.ToArray());
    }
    [Fact]
    public void PersistentPiercingOverlapIsDamagedOnlyOnEntry()
    {
        using var sim = Create(ShmupSettingsTests.Settings(bounce: false, threshold: 1, speed: 1, descent: 1));
        sim.SpawnBrick(new(640, 300)); sim.SpawnBall(new(640, 300), new(1, 0));
        Advance(sim, 100);
        Assert.Equal(90, Assert.Single(sim.Snapshot.Bricks.ToArray()).Health);
    }
    [Fact]
    public void BrickDeathRemovesNativeObstacleAndBallSurvives()
    {
        using var sim = Create(ShmupSettingsTests.Settings(bounce: false, threshold: 1, brickHealth: 10));
        sim.SpawnBrick(new(640, 300)); sim.SpawnBall(new(640, 350), new(0, -1));
        Advance(sim, 20);
        Assert.Empty(sim.Snapshot.Bricks.ToArray()); Assert.Single(sim.Snapshot.Balls.ToArray());
    }
    [Fact]
    public void NativeSideWallExpiresOnConfiguredNthCollisionNotTheNext()
    {
        using var sim = Create(ShmupSettingsTests.Settings(threshold: 1, width: 100, wallHits: 3));
        sim.SpawnBall(new(640, 100), new(1, 0));
        bool sawTwo = false;
        for (int i = 0; i < 100 && (i == 0 || sim.Snapshot.Balls.Length > 0); i++)
        {
            sim.Step();
            if (sim.Snapshot.Balls.Length > 0) { Assert.InRange(sim.Snapshot.Balls[0].WallHits, 0, 2); sawTwo |= sim.Snapshot.Balls[0].WallHits == 2; }
        }
        Assert.True(sawTwo); Assert.Empty(sim.Snapshot.Balls.ToArray());
    }
    [Fact]
    public void BrickInteractionResetsWallStreak()
    {
        using var sim = Create(ShmupSettingsTests.Settings(threshold: 1, width: 200));
        sim.SpawnBall(new(640, 200), new(1, 0));
        for (int i = 0; i < 100 && (sim.Snapshot.Balls.Length == 0 || sim.Snapshot.Balls[0].WallHits < 1); i++) sim.Step();
        Assert.Equal(1, sim.Snapshot.Balls[0].WallHits);
        var ball = sim.Snapshot.Balls[0];
        sim.SpawnBrick(ball.Position + new double2(-50, 0));
        bool hit = false;
        for (int i = 0; i < 20; i++)
        {
            sim.Step();
            if (sim.Snapshot.Bricks[0].Health < 100) { Assert.Equal(0, sim.Snapshot.Balls[0].WallHits); hit = true; break; }
        }
        Assert.True(hit);
    }
    [Fact]
    public void AvatarOngoingContactRecursAfterInvulnerabilityAndRestartClearsIt()
    {
        using var sim = Create(ShmupSettingsTests.Settings(threshold: 1, descent: 1));
        sim.SpawnBrick(sim.Snapshot.PlayerPosition);
        sim.Step(); Assert.Equal(90, sim.Snapshot.PlayerHealth);
        Advance(sim, 88); Assert.Equal(90, sim.Snapshot.PlayerHealth);
        Advance(sim, 3); Assert.Equal(80, sim.Snapshot.PlayerHealth);
        Assert.Single(sim.Snapshot.Bricks.ToArray());
        sim.Restart();
        Assert.Equal(100, sim.Snapshot.PlayerHealth); Assert.Equal(0, sim.Snapshot.InvulnerabilityRemaining);
        sim.SpawnBrick(sim.Snapshot.PlayerPosition); sim.Step(); Assert.Equal(90, sim.Snapshot.PlayerHealth);
    }
    [Fact]
    public void EscapedBrickDamageOccursExactlyOnceAndKilledBrickDoesNotEscape()
    {
        using var sim = Create();
        sim.SpawnBrick(new(400, 740)); sim.Step();
        Assert.Equal(90, sim.Snapshot.PlayerHealth); Assert.Empty(sim.Snapshot.Bricks.ToArray());
        Advance(sim, 20); Assert.Equal(90, sim.Snapshot.PlayerHealth);
        using var killed = Create(ShmupSettingsTests.Settings(bounce: false, threshold: 1, brickHealth: 10));
        killed.SpawnBrick(new(400, 740)); killed.SpawnBall(new(400, 740), new(1, 0)); killed.Step();
        Assert.Equal(100, killed.Snapshot.PlayerHealth); Assert.Empty(killed.Snapshot.Bricks.ToArray());
    }
    [Fact]
    public void DefeatFreezesClocksInputSpawnsAndPublicationUntilExplicitRestart()
    {
        using var sim = Create(ShmupSettingsTests.Settings(health: 10, threshold: 1));
        sim.SpawnBrick(new(400, 740)); sim.Step();
        var defeated = sim.Snapshot; Assert.True(defeated.IsDefeated);
        Advance(sim, 100, new(new(1, 1), new(1, 0), true));
        Assert.Same(defeated, sim.Snapshot);
        sim.Step(new(new(1, 1), new(1, 0), true, true));
        Assert.False(sim.Snapshot.IsDefeated); Assert.Equal(0, sim.Snapshot.Tick);
        Assert.Empty(sim.Snapshot.Balls.ToArray()); Assert.Equal(new double2(0, -1), sim.Snapshot.AimDirection);
    }
    [Fact]
    public void BothScreenEdgesAreOpenForBalls()
    {
        using var sim = Create();
        sim.SpawnBall(new(640, 1), new(0, -1)); sim.SpawnBall(new(640, 719), new(0, 1));
        Advance(sim, 3); Assert.Empty(sim.Snapshot.Balls.ToArray());
    }
    [Fact]
    public void WidenMovesNativeWallsSymmetricallyPreservesBallsAndSpawnsRowsInsideCurrentWidth()
    {
        using var sim = Create(ShmupSettingsTests.Settings(threshold: 0, width: 200));
        sim.SpawnBall(new(730, 200), new(1, 0));
        sim.Widen(600); sim.Step();
        Assert.True(sim.Snapshot.Balls[0].Velocity.X > 0);
        Assert.All(sim.Snapshot.Bricks.ToArray(), b => Assert.InRange(b.Position.X, 340 + b.Size / 2, 940 - b.Size / 2));
        bool bounced = false;
        for (int i = 0; i < 100; i++)
        {
            sim.Step(); if (sim.Snapshot.Balls[0].Velocity.X < 0) { Assert.InRange(sim.Snapshot.Balls[0].Position.X, 930, 940); bounced = true; break; }
        }
        Assert.True(bounced); Assert.Equal(600, sim.Snapshot.Width);
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Widen(599));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Widen(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Widen(1281));
        sim.Restart(); Assert.Equal(200, sim.Snapshot.Width);
    }
    [Fact]
    public void SeededRowsRepeatAfterRestartAndDifferentWidthsHaveCenteredColumns()
    {
        using var sim = Create(ShmupSettingsTests.Settings(threshold: .35));
        sim.Step(); var first = sim.Snapshot.Bricks.ToArray();
        Assert.NotEmpty(first); Assert.InRange(first.Length, 1, 15);
        sim.Restart(); sim.Step();
        Assert.Equal(first.Select(b => b.Position), sim.Snapshot.Bricks.ToArray().Select(b => b.Position));
        Assert.All(first, b => Assert.InRange(Math.Abs((b.Position.X - 360) / 40 - Math.Round((b.Position.X - 360) / 40)), 0, .00001));
        Assert.All(first, b => Assert.Equal(100, b.Health));
    }
    [Fact]
    public void DifficultyScalesAndCapsEvenOverLongRuns()
    {
        using var sim = Create();
        Advance(sim, 7200);
        Assert.InRange(sim.Snapshot.DescentSpeed, 32.999, 33.001);
        Assert.InRange(sim.Snapshot.RowInterval, 1.34999, 1.35001);
        Advance(sim, 250000);
        Assert.Equal(120, sim.Snapshot.DescentSpeed); Assert.Equal(.35, sim.Snapshot.RowInterval);
    }
    [Fact]
    public void ReusedSlotsResetHpStreakContactsAndGenerationWithoutChangingEntityBodyCounts()
    {
        using var sim = Create(ShmupSettingsTests.Settings(bounce: false, maxBalls: 1, maxBricks: 1, threshold: 1, brickHealth: 10));
        sim.SpawnBrick(new(640, 300)); sim.SpawnBall(new(640, 300), new(1, 0)); sim.Step();
        var old = sim.Snapshot; Assert.Empty(old.Bricks.ToArray());
        sim.Restart(); sim.SpawnBrick(new(640, 300)); sim.SpawnBall(new(640, 300), new(1, 0)); sim.Step();
        Assert.Empty(sim.Snapshot.Bricks.ToArray()); // Pair entry damage was not suppressed by old life.
        Assert.True(sim.Snapshot.Balls[0].Generation > old.Balls[0].Generation);
        Assert.Equal(0, sim.Snapshot.Balls[0].WallHits); Assert.Equal(2, sim.Snapshot.EntityCount); Assert.Equal(4, sim.Snapshot.BodyCount);
        Assert.Equal(1, old.Balls[0].Generation);
    }
    [Fact]
    public void SaturationDropsNewSpawnsWithoutDebtAndSustainedChurnStaysBounded()
    {
        using var sim = Create(ShmupSettingsTests.Settings(maxBalls: 2, maxBricks: 2, threshold: 0, rowInterval: .02, health: int.MaxValue));
        int bodies = sim.Snapshot.BodyCount, entities = sim.Snapshot.EntityCount;
        Advance(sim, 12000, new(FireHeld: true));
        Assert.True(sim.Snapshot.DroppedBalls > 0); Assert.True(sim.Snapshot.DroppedBricks > 0);
        Assert.InRange(sim.Snapshot.Balls.Length, 0, 2); Assert.InRange(sim.Snapshot.Bricks.Length, 0, 2);
        Assert.Equal(bodies, sim.Snapshot.BodyCount); Assert.Equal(entities, sim.Snapshot.EntityCount);
        long dropped = sim.Snapshot.DroppedBalls;
        Advance(sim, 300); Assert.Equal(dropped, sim.Snapshot.DroppedBalls); Assert.Empty(sim.Snapshot.Balls.ToArray());
    }
    [Fact]
    public void SimultaneousNativeWallAndBrickInteractionUsesBrickResetPrecedence()
    {
        var settings = ShmupSettingsTests.Settings(threshold: 1, width: 100, wallHits: 1);
        using var native = new NativeShmupArena(TestSettings.Baseline, settings, new(1, 64, 1));
        native.ActivateBrick(0, new(674, 200)); native.ActivateBall(0, new(687, 180), Arena.Normalize(new(1, 1)));
        native.Step(30, new(640, 696), new(640, 696));
        Assert.True(native.WallHits[0] > 0); Assert.Equal(1, native.BrickHits[0, 0]);
        using var sim = Create(settings);
        sim.SpawnBrick(new(674, 200)); sim.SpawnBall(new(687, 180), new(1, 1)); sim.Step();
        Assert.Equal(0, Assert.Single(sim.Snapshot.Balls.ToArray()).WallHits);
        Assert.Equal(90, sim.Snapshot.Bricks[0].Health);
    }
    [Fact]
    public void AvatarSweepDetectsFullCrossingAndLargeRadiusResetStaysInsideScreen()
    {
        var basis = ShmupSettingsTests.Settings(threshold: 1);
        var fast = new ShmupSettings(basis.Arena, new(12, 60000, 100, 10, .75), basis.Ball,
            basis.Brick, basis.Difficulty, basis.Noise, basis.Capacity);
        using var sim = Create(fast);
        sim.SpawnBrick(new(640, 450));
        sim.Step(new(new(0, -1)));
        Assert.True(sim.Snapshot.PlayerPosition.Y < 450 - 32);
        Assert.Equal(90, sim.Snapshot.PlayerHealth);
        var large = new ShmupSettings(new(1280, 720, 1000), new(300, 250, 100, 10, .75), basis.Ball,
            basis.Brick, basis.Difficulty, basis.Noise, basis.Capacity);
        using var largeAvatar = Create(large);
        Assert.InRange(largeAvatar.Snapshot.PlayerPosition.Y, 300, 420);
    }
    [Fact]
    public void PiercingExitThenReentryDamagesAgainAndPooledBrickRestoresFullHealth()
    {
        using var sim = Create(ShmupSettingsTests.Settings(bounce: false, threshold: 1, width: 100, descent: 1, maxBricks: 1));
        sim.SpawnBrick(new(640, 200)); sim.SpawnBall(new(640, 200), new(1, 0));
        sim.Step(); Assert.Equal(90, sim.Snapshot.Bricks[0].Health);
        Advance(sim, 20); Assert.True(sim.Snapshot.Bricks[0].Health <= 80);
        var prior = sim.Snapshot.Bricks[0];
        sim.Restart(); sim.SpawnBrick(new(640, 300)); sim.Step();
        Assert.Equal(100, sim.Snapshot.Bricks[0].Health);
        Assert.True(sim.Snapshot.Bricks[0].Generation > prior.Generation);
    }
    [Fact]
    public void DefaultCapacitySteadyNativeTicksUseFourBoundedIntervals()
    {
        var settings = ShmupSettingsTests.Settings(maxBalls: 512, maxBricks: 2048, threshold: 1);
        using var native = new NativeShmupArena(TestSettings.Baseline, settings, new(1, 64, 1));
        for (int i = 0; i < 64; i++) native.ActivateBall(i, new(640, 100 + i * 7), new(1, 0));
        for (int i = 0; i < 32; i++) native.ActivateBrick(i, new(400 + i % 8 * 40, -40 - i / 8 * 40));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 120; i++)
        {
            native.Step(30, new(640, 696), new(640, 696));
            Assert.Equal(4, native.LastIntervalCount); Assert.Equal(2562, native.BodyCount);
        }
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"120 default-capacity ticks took {clock.Elapsed}.");
    }
    [Fact]
    public void DefaultCapacityNativeStartupAndEightyThousandActorLivesRemainBounded()
    {
        using var sim = Create(ShmupSettingsTests.Settings(maxBalls: 512, maxBricks: 2048, threshold: 1, health: int.MaxValue));
        Assert.Equal(2560, sim.Snapshot.EntityCount); Assert.Equal(2562, sim.Snapshot.BodyCount);
        long previousBallGeneration = 0, previousBrickGeneration = 0;
        for (int life = 0; life < 32; life++)
        {
            for (int i = 0; i < 512; i++) Assert.Equal(i, sim.SpawnBall(new(640, 1), new(0, -1)));
            for (int i = 0; i < 2048; i++) Assert.Equal(i, sim.SpawnBrick(new(400, 740)));
            sim.Step();
            Assert.Empty(sim.Snapshot.Balls.ToArray()); Assert.Empty(sim.Snapshot.Bricks.ToArray());
            Assert.Equal(2560, sim.Snapshot.EntityCount); Assert.Equal(2562, sim.Snapshot.BodyCount);
            previousBallGeneration++; previousBrickGeneration++;
        }
        sim.SpawnBall(new(640, 300), new(1, 0)); sim.SpawnBrick(new(400, 300)); sim.Step();
        Assert.Equal(previousBallGeneration + 1, sim.Snapshot.Balls[0].Generation);
        Assert.Equal(previousBrickGeneration + 1, sim.Snapshot.Bricks[0].Generation);
    }
    [Fact]
    public void DisposeIsIdempotentAndAllMutationRejectsDisposedWorld()
    {
        var sim = Create(); sim.Dispose(); sim.Dispose();
        Assert.Throws<ObjectDisposedException>(() => sim.Step());
        Assert.Throws<ObjectDisposedException>(() => sim.Restart());
        Assert.Throws<ObjectDisposedException>(() => sim.Widen(1000));
    }
}
