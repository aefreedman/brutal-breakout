using Brutal.Numerics;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class BreakoutTests
{
    internal static GameplaySettings Game(int balls = 3, int maxBounces = 12, double aimX = 0, double aimY = -1,
        IReadOnlyList<BrickSettings>? bricks = null, double speed = 600) =>
        new(new(1000, 900, 50, 50, 950, 850, 20), new(10, speed, 500, 750, aimX, aimY),
            new(balls, 1.0 / 3, maxBounces, bricks ?? [new(450, 200, 100, 30), new(100, 120, 80, 25)]));

    private static void Until(BallSimulation sim, Func<bool> predicate, int maximum = 1200)
    {
        for (int i = 0; i < maximum && !predicate(); i++) sim.Step();
        Assert.True(predicate(), $"Condition not reached: {sim.RoundPhase}, {sim.State.Phase}, position={sim.State.Position}, remaining={sim.RemainingBricks}, balls={sim.BallsRemaining}.");
    }

    [Fact]
    public void NativeBrickReflectsOnceDisappearsAndNoLongerBlocksCircleCast()
    {
        var game = Game();
        using var sim = new BallSimulation(TestSettings.Baseline, game, new(2, 64, 2));
        var before = sim.Preview();
        Assert.True(before.HasHit);
        Assert.InRange(before.Position.Y, 239.49, 240.51);
        sim.Step(new(PrimaryPressed: true));
        Until(sim, () => sim.RemainingBricks == 1);
        Assert.True(sim.State.Velocity.Y > 0);
        Assert.False(sim.Bricks[0].Alive);
        Assert.True(sim.Bricks[1].Alive);
        Assert.Equal(0, sim.Bricks[0].Id);
        Assert.Same(game.Breakout!.Bricks[0], sim.Bricks[0].Bounds);
        Assert.Equal(RoundPhase.Playing, sim.RoundPhase);
        var after = sim.Query(game.Ball.Spawn, new(0, -1));
        Assert.True(after.HasHit);
        Assert.InRange(after.Position.Y, 59.49, 60.51);
        Assert.True(after.Distance > before.Distance + 100);
        for (int i = 0; i < 10; i++) sim.Step();
        Assert.Equal(1, sim.RemainingBricks);
    }

    [Fact]
    public void BeginAndHitAreDeduplicatedBeforeNativeBodyDestruction()
    {
        using var native = new NativeArena(TestSettings.Baseline, Game(), 2);
        native.Launch(new(0, -1));
        NativeArena.StepResult result = default;
        for (int i = 0; i < 300 && result.BricksDestroyed == 0; i++) result = native.Step();
        Assert.Equal(1, result.BricksDestroyed);
        Assert.True(result.HasContact);
        Assert.False(native.IsBrickAlive(0));
        Assert.True(native.IsBrickAlive(1));
        for (int i = 0; i < 10; i++) Assert.Equal(0, native.Step().BricksDestroyed);
        native.Freeze(); native.ResetBricks(); native.Reset();
        Assert.True(native.IsBrickAlive(0));
        Assert.True(native.IsBrickAlive(1));
    }

    [Fact]
    public void OneNativeStepCanDestroyBothBricksAtASeam()
    {
        using var native = new NativeArena(TestSettings.Baseline,
            Game(bricks: [new(450, 200, 50, 30), new(500, 200, 50, 30), new(100, 120, 80, 25)]), 2);
        native.Launch(new(0, -1));
        NativeArena.StepResult result = default;
        for (int i = 0; i < 300 && result.BricksDestroyed == 0; i++) result = native.Step();
        Assert.Equal(2, result.BricksDestroyed);
        Assert.False(native.IsBrickAlive(0)); Assert.False(native.IsBrickAlive(1));
        Assert.True(native.IsBrickAlive(2));
    }

    [Fact]
    public void MustLeaveAndHitThenReturnBelowLineBeforeCatching()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, Game(), new(2, 64, 2));
        Assert.Equal(600, sim.CatchLineY);
        sim.Step(new(PrimaryPressed: true));
        Until(sim, () => sim.State.Position.Y < sim.CatchLineY);
        Assert.Equal(2, sim.RemainingBricks); // Left the zone, but no contact yet.
        var beforeBlockedCatch = sim.State.Position;
        sim.Step(new(PrimaryPressed: true));
        Assert.Equal(BallPhase.Flying, sim.State.Phase);
        Assert.NotEqual(beforeBlockedCatch, sim.State.Position);
        Assert.False(sim.State.CatchEligible);
        Until(sim, () => sim.RemainingBricks == 1);
        Assert.True(sim.State.Position.Y < sim.CatchLineY); // A contact above the line is insufficient.
        Assert.False(sim.State.CatchEligible);
        sim.Step(new(PrimaryPressed: true));
        Assert.Equal(BallPhase.Flying, sim.State.Phase);
        Until(sim, () => sim.State.CatchEligible);
        Assert.True(sim.State.Position.Y >= sim.CatchLineY);
        var caughtAt = sim.State.Position;
        sim.Step(new(PrimaryPressed: true));
        Assert.Equal(BallPhase.Aiming, sim.State.Phase);
        Assert.Equal(caughtAt, sim.State.Position);
        Assert.Equal(default, sim.State.Velocity);
        for (int i = 0; i < 20; i++) sim.Step();
        Assert.Equal(caughtAt, sim.State.Position);
        // A separate edge relaunches; staying below and hitting a wall cannot reuse old facts.
        sim.Step(new(new double2(1, 0), PrimaryPressed: true));
        Assert.Equal(BallPhase.Flying, sim.State.Phase);
        Until(sim, () => sim.BouncesWithoutBrick > 0);
        Assert.False(sim.State.CatchEligible);
        sim.Step(new(PrimaryPressed: true));
        Assert.Equal(BallPhase.Flying, sim.State.Phase);
        Assert.Equal(1, sim.RemainingBricks);
    }

    [Fact]
    public void CatchLineBoundaryIsInclusiveAndCenterBased()
    {
        var game = Game();
        double line = game.CatchLineY;
        Assert.False(game.IsInCatchZone(Math.BitDecrement(line)));
        Assert.True(game.IsInCatchZone(line));
        Assert.True(game.IsInCatchZone(Math.BitIncrement(line)));
        Assert.False(game.IsInCatchZone(line - game.Ball.Radius / 2));
        Assert.False(game.IsInCatchZone(double.NaN));
        Assert.False(game.IsInCatchZone(double.PositiveInfinity));
        Assert.False(TestSettings.Gameplay.IsInCatchZone(750));
    }

    [Fact]
    public void OpenBottomMissConsumesBallAndPreservesDestroyedBricksUntilRestart()
    {
        var game = Game();
        using var sim = new BallSimulation(TestSettings.Baseline, game, new(2, 64, 2));
        Assert.False(sim.Query(game.Ball.Spawn, new(0, 1)).HasHit);
        sim.Step(new(PrimaryPressed: true));
        Until(sim, () => sim.State.CatchEligible);
        sim.Step(new(PrimaryPressed: true));
        Assert.Equal(1, sim.RemainingBricks);
        for (int remaining = 2; remaining >= 0; remaining--)
        {
            sim.Step(new(new double2(0, 1), PrimaryPressed: true));
            Until(sim, () => sim.BallsRemaining == remaining);
            Assert.Equal(1, sim.RemainingBricks);
            Assert.False(sim.Bricks[0].Alive);
            Assert.False(sim.State.CatchEligible);
            if (remaining > 0)
            {
                Assert.Equal(RoundPhase.Playing, sim.RoundPhase);
                Assert.Equal(game.Ball.Spawn, sim.State.Position);
                Assert.Equal(game.Ball.InitialAim, sim.State.AimDirection);
                Assert.Equal(BallPhase.Aiming, sim.State.Phase);
            }
        }
        Assert.Equal(RoundPhase.Lost, sim.RoundPhase);
        Assert.True(sim.State.Position.Y - game.Ball.Radius > game.Arena.Bottom);
        var terminal = sim.State;
        sim.Step(new(new double2(1, 0), true));
        Assert.Equal(terminal, sim.State);
        sim.Step(new(new double2(1, 0), true, true));
        Assert.Equal(RoundPhase.Playing, sim.RoundPhase);
        Assert.Equal(3, sim.BallsRemaining);
        Assert.Equal(2, sim.RemainingBricks);
        Assert.True(sim.Bricks[0].Alive);
        Assert.Equal(game.Ball.Spawn, sim.State.Position);
        Assert.Equal(0, sim.BouncesWithoutBrick);
        Assert.InRange(sim.Preview().Position.Y, 239.49, 240.51);
    }

    [Fact]
    public void LastBrickWinsFreezesAndOnlyRestartResumes()
    {
        var game = Game(bricks: [new(450, 200, 100, 30)]);
        using var sim = new BallSimulation(TestSettings.Baseline, game, new(2, 64, 2));
        sim.Step(new(PrimaryPressed: true));
        Until(sim, () => sim.RoundPhase == RoundPhase.Won);
        Assert.Equal(0, sim.RemainingBricks);
        Assert.Equal(3, sim.BallsRemaining);
        Assert.Equal(BallPhase.Aiming, sim.State.Phase);
        Assert.Equal(default, sim.State.Velocity);
        Assert.False(sim.State.CatchEligible);
        var terminal = sim.State;
        for (int i = 0; i < 20; i++) sim.Step(new(new double2(1, 0), true));
        Assert.Equal(terminal, sim.State);
        sim.Step(new(RestartPressed: true));
        Assert.Equal(RoundPhase.Playing, sim.RoundPhase);
        Assert.Equal(1, sim.RemainingBricks);
        Assert.True(sim.Bricks[0].Alive);
        Assert.Equal(game.Ball.Spawn, sim.State.Position);
    }

    [Fact]
    public void WallBounceExpiryUsesUniqueContactsAndConfiguredLimit()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, Game(maxBounces: 1, aimX: 1, aimY: 0), new(2, 64, 2));
        sim.Step(new(PrimaryPressed: true));
        Until(sim, () => sim.BouncesWithoutBrick > 0);
        Assert.Equal(1, sim.BouncesWithoutBrick);
        Assert.Equal(3, sim.BallsRemaining);
        sim.Step();
        Assert.Equal(1, sim.BouncesWithoutBrick); // No second count for a begin+hit or stale contact.
        Until(sim, () => sim.BallsRemaining == 2);
        Assert.Equal(Game().Ball.Spawn, sim.State.Position);
        Assert.Equal(0, sim.BouncesWithoutBrick);
    }

    [Fact]
    public void ZeroDisablesBounceExpiryAndBelowLineWallHitsCannotEnableCatch()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, Game(maxBounces: 0, aimX: 1, aimY: 0), new(2, 64, 2));
        sim.Step(new(PrimaryPressed: true));
        for (int i = 0; i < 1600; i++)
        {
            sim.Step();
            Assert.False(sim.State.CatchEligible);
        }
        Assert.True(sim.BouncesWithoutBrick > 5);
        Assert.Equal(3, sim.BallsRemaining);
        Assert.Equal(RoundPhase.Playing, sim.RoundPhase);
    }

    [Fact]
    public void BrickHitResetsWallOnlyBounceBudget()
    {
        using var sim = new BallSimulation(TestSettings.Baseline,
            Game(aimX: 1, aimY: -1, bricks: [new(720, 100, 100, 25), new(100, 100, 70, 25)]), new(2, 64, 2));
        sim.Step(new(PrimaryPressed: true));
        Until(sim, () => sim.BouncesWithoutBrick == 1);
        Assert.Equal(2, sim.RemainingBricks);
        Until(sim, () => sim.RemainingBricks == 1);
        Assert.Equal(0, sim.BouncesWithoutBrick);
    }

    [Fact]
    public void AimingDoesNotDestroyBricksAndRestartClearsShotFacts()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, Game(), new(2, 64, 2));
        for (int i = 0; i < 50; i++) sim.Step();
        Assert.Equal(2, sim.RemainingBricks);
        sim.Step(new(PrimaryPressed: true));
        Until(sim, () => sim.State.CatchEligible);
        sim.Step(new(RestartPressed: true));
        Assert.False(sim.State.CatchEligible);
        Assert.Equal(2, sim.RemainingBricks);
        sim.Step(new(new double2(1, 0), true));
        Until(sim, () => sim.BouncesWithoutBrick > 0);
        Assert.False(sim.State.CatchEligible);
    }

    [Fact]
    public void RepeatedDestroyRestartAndDisposeKeepsIndependentWorldsValid()
    {
        using var held = new BallSimulation(TestSettings.Baseline, Game(), new(2, 64, 2));
        var original = held.Preview();
        for (int cycle = 0; cycle < 20; cycle++)
        {
            using var sim = new BallSimulation(TestSettings.Baseline, Game(bricks: [new(450, 200, 100, 30)]), new(2, 64, 2));
            for (int i = 0; i < 3; i++)
            {
                sim.Step(new(PrimaryPressed: true));
                Until(sim, () => sim.RoundPhase == RoundPhase.Won);
                sim.Step(new(RestartPressed: true));
            }
            sim.Dispose(); sim.Dispose();
            Assert.Equal(original, held.Preview());
        }
    }

    [Theory]
    [InlineData(30, 1, 7, 350)] [InlineData(60, 2, 7, 350)]
    [InlineData(120, 4, 7, 350)] [InlineData(240, 8, 7, 350)]
    [InlineData(30, 1, 23, 950)] [InlineData(60, 2, 23, 950)]
    [InlineData(120, 4, 23, 950)] [InlineData(240, 8, 23, 950)]
    public void CustomArenaRadiusSpeedAndPhysicsStillUseRealBrickCatchAndReset(double rate, int substeps, double radius, double speed)
    {
        var game = new GameplaySettings(new(1800, 1000, 80, 120, 1600, 850, 24), new(radius, speed, 420, 750, 0, -1),
            new(2, 1.0 / 3, 12, [new(360, 200, 120, 40), new(1000, 200, 120, 40)]));
        using var sim = new BallSimulation(new(rate, substeps, 8, 120, 1), game, new(2, 64, 2));
        sim.Step(new(PrimaryPressed: true));
        Until(sim, () => sim.RemainingBricks == 1);
        Assert.InRange(double2.Distance(default, sim.State.Velocity), speed - .1, speed + .1);
        Assert.False(sim.State.CatchEligible);
        Until(sim, () => sim.State.CatchEligible);
        var position = sim.State.Position;
        Assert.True(game.IsInCatchZone(position.Y));
        sim.Step(new(PrimaryPressed: true));
        Assert.Equal(position, sim.State.Position);
        Assert.Equal(BallPhase.Aiming, sim.State.Phase);
        sim.Step(new(RestartPressed: true));
        Assert.Equal(game.Ball.Spawn, sim.State.Position);
        Assert.Equal(game.Ball.InitialAim, sim.State.AimDirection);
        Assert.Equal(2, sim.BallsRemaining);
        Assert.Equal(2, sim.RemainingBricks);
    }

    [Fact]
    public void RepeatedCatchRelaunchRequiresNewContactAndKeepsCurrentPose()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, Game(), new(2, 64, 2));
        for (int cycle = 0; cycle < 5; cycle++)
        {
            sim.Step(new(new double2(0, -1), true));
            Assert.False(sim.State.CatchEligible);
            Until(sim, () => sim.State.CatchEligible);
            Assert.Equal(1, sim.RemainingBricks);
            var caughtAt = sim.State.Position;
            sim.Step(new(PrimaryPressed: true));
            Assert.Equal(BallPhase.Aiming, sim.State.Phase);
            Assert.Equal(caughtAt, sim.State.Position);
            for (int i = 0; i < 5; i++) sim.Step();
            Assert.Equal(caughtAt, sim.State.Position);
        }
        Assert.Equal(3, sim.BallsRemaining);
    }

    [Fact]
    public void BreakoutTicksCanBeSubmittedFromAnotherThread()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, Game(), new(2, 64, 2));
        Exception? error = null;
        var other = new Thread(() => error = Record.Exception(() => sim.Step(new(RestartPressed: true))));
        other.Start();
        Assert.True(other.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(error);
        Assert.Equal(SimulationEvents.Restart, sim.Events);
        Assert.Equal(Game().Breakout!.Bricks.Count, sim.RemainingBricks);
    }
}
