using Brutal.Numerics;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public sealed class LiveTuningTests
{
    private static PhysicsSettings Physics => new(120, 4, 8, 120, 1);
    private static ShmupSettings Settings(double speed = 600, int health = 100, double radius = 3, int brickHealth = 100,
        double initialWidth = 432, bool bounce = true) => new(new(1280, 720, initialWidth),
        new(11, 252, health, 10, .75), new(radius, speed, 12, 10, bounce, 20),
        new(28, 36, brickHealth, 10, [ShmupBrickShape.T], [1]), new(1.5, .35, 20, 80, 60, .9, 1.1), new(1, .25, .2, 1), new(4, 8));

    [Fact]
    public void ShmupLiveNativeSpeedAboveOriginalWorldCapAndRadiusRetainLivesTimeAndStreaks()
    {
        using var simulation = new ShmupSimulation(Physics, Settings(), new(1, 16, 1));
        simulation.SpawnBall(new(640, 500), new(0, -1));
        simulation.SpawnBrick(new(700, 80), ShmupBrickShape.L, 2);
        simulation.Step();
        var before = simulation.Snapshot;
        var next = Settings(50000, brickHealth: 250, radius: 4);
        var tunedPhysics = new PhysicsSettings(120, 2, 8, 60, .5);
        simulation.ApplyTuning(tunedPhysics, next);
        Assert.Same(tunedPhysics, simulation.Physics);
        var after = simulation.Snapshot;
        Assert.Same(next, simulation.Settings);
        Assert.Equal(before.Tick, after.Tick); Assert.Equal(before.TimeSeconds, after.TimeSeconds);
        Assert.Equal(before.PlayerHealth, after.PlayerHealth); Assert.Equal(before.PlayerPosition, after.PlayerPosition);
        Assert.Equal(before.Balls[0].Position, after.Balls[0].Position);
        Assert.Equal(before.Balls[0].Generation, after.Balls[0].Generation);
        Assert.Equal(before.Balls[0].WallHits, after.Balls[0].WallHits);
        Assert.Equal(before.Bricks.ToArray(), after.Bricks.ToArray());
        Assert.Equal(100, after.Bricks[0].InitialHealth);
        Assert.Equal(600, before.BallSpeed); // old publication remains immutable
        double y = after.Balls[0].Position.Y;
        simulation.Step();
        Assert.InRange(y - simulation.Snapshot.Balls[0].Position.Y, 416.64, 416.69); // actual native movement > old 400 m/s default cap
        var slower = Settings(300, brickHealth: 250, radius: 4);
        simulation.ApplyTuning(Physics, slower);
        y = simulation.Snapshot.Balls[0].Position.Y;
        simulation.Step();
        Assert.InRange(y - simulation.Snapshot.Balls[0].Position.Y, 2.49, 2.51);
        simulation.SpawnBrick(new(760, 100), ShmupBrickShape.T, 1);
        simulation.Step();
        Assert.Equal(100, simulation.Snapshot.Bricks[0].Health);
        Assert.Equal(250, simulation.Snapshot.Bricks[1].Health);
        Assert.Equal(ShmupBrickShape.L, simulation.Snapshot.Bricks[0].Shape);
        Assert.Equal(2, simulation.Snapshot.Bricks[0].QuarterTurns);
    }

    [Fact]
    public void ExistingNonzeroNativeWallStreakSurvivesLiveTuning()
    {
        using var simulation = new ShmupSimulation(Physics, Settings(), new(1, 16, 1));
        simulation.SpawnBall(new(850, 500), new(1, 0));
        simulation.Step();
        var before = simulation.Snapshot.Balls[0];
        Assert.True(before.WallHits > 0);
        simulation.ApplyTuning(Physics, Settings(900));
        var after = simulation.Snapshot.Balls[0];
        Assert.Equal(before.Id, after.Id); Assert.Equal(before.Generation, after.Generation);
        Assert.Equal(before.Position, after.Position); Assert.Equal(before.WallHits, after.WallHits);
        Assert.True(after.Velocity.X < 0);
        simulation.Step();
        Assert.Equal(before.WallHits, simulation.Snapshot.Balls[0].WallHits); // native streak, not only retained publication
    }

    [Fact]
    public void DifficultyNoiseAndFutureDefaultsDoNotRewindOrCreateSpawnBurstAndHealthNeverHeals()
    {
        var start = Settings(); using var simulation = new ShmupSimulation(Physics, start, new(1, 16, 1));
        for (int i = 0; i < 10; i++) simulation.Step();
        var before = simulation.Snapshot;
        var next = new ShmupSettings(new(1280, 720, 500), new(14, 400, 50, 15, .25), start.Ball, start.Brick,
            new(.1, .02, 30, 90, 20, .8, 1.2), new(4, .3, .4, 0), start.Capacity);
        simulation.ApplyTuning(Physics, next);
        Assert.Equal(before.TimeSeconds, simulation.Snapshot.TimeSeconds);
        Assert.Equal(before.RowsSpawned, simulation.Snapshot.RowsSpawned);
        Assert.Equal(before.Width, simulation.Snapshot.Width);
        Assert.Equal(50, simulation.Snapshot.PlayerHealth);
        simulation.Step();
        Assert.InRange(simulation.Snapshot.RowsSpawned - before.RowsSpawned, 0, 1);
        simulation.ApplyTuning(Physics, Settings(health: 200));
        Assert.Equal(50, simulation.Snapshot.PlayerHealth);
    }

    [Fact]
    public void TopologyOrWrongOwnerRejectsBeforeNativeOrManagedChanges()
    {
        using var simulation = new ShmupSimulation(Physics, Settings(), new(1, 16, 1));
        var publication = simulation.Snapshot;
        Assert.Throws<ArgumentException>(() => simulation.ApplyTuning(Physics, Settings(bounce: false)));
        Assert.Same(publication, simulation.Snapshot);
        Exception? error = null;
        var thread = new Thread(() => error = Record.Exception(() => simulation.ApplyTuning(Physics, Settings(700))));
        thread.Start(); thread.Join();
        Assert.IsType<InvalidOperationException>(error); Assert.Same(publication, simulation.Snapshot);
    }

    [Fact]
    public void FutureInitialWidthDoesNotAllowLiveRadiiOutsideCurrentWalls()
    {
        var initial = Settings(); using var simulation = new ShmupSimulation(Physics, initial, new(1, 16, 1));
        var next = new ShmupSettings(new(1280, 720, 800), new(250, 252, 100, 10, .75),
            initial.Ball, initial.Brick, initial.Difficulty, initial.Noise, initial.Capacity);
        var before = simulation.Snapshot;
        var error = Assert.Throws<ArgumentException>(() => simulation.ApplyTuning(Physics, next));
        Assert.Contains("widen", error.Message); Assert.Same(before, simulation.Snapshot); Assert.Same(initial, simulation.Settings);
        simulation.Widen(800);
        simulation.ApplyTuning(Physics, next);
        Assert.Equal(250, simulation.Settings.Avatar.Radius);
    }

    [Fact]
    public void ClassicLiveCatchPolicyKeepsSeparateCatchAimAndRelaunch()
    {
        var start = BreakoutTests.Game();
        using var simulation = new BallSimulation(Physics, start, new(1, 16, 1));
        simulation.Step(new(PrimaryPressed: true));
        for (int i = 0; i < 400 && !simulation.State.CatchEligible; i++) simulation.Step();
        Assert.True(simulation.State.CatchEligible);
        var next = new GameplaySettings(start.Arena, new(10, 750, 510, 760, 1, -1),
            new(5, .4, 20, start.Breakout!.Bricks));
        var before = simulation.Snapshot;
        simulation.ApplyTuning(Physics, next);
        Assert.Equal(before.State.Position, simulation.State.Position); Assert.Equal(before.State.Phase, simulation.State.Phase);
        Assert.Equal(before.BallsRemaining, simulation.BallsRemaining); Assert.Equal(before.Tick, simulation.Snapshot.Tick);
        Assert.Equal(next.CatchLineY, simulation.CatchLineY);
        simulation.Step(new(PrimaryPressed: true)); Assert.Equal(BallPhase.Aiming, simulation.State.Phase);
        var caught = simulation.State.Position;
        simulation.ApplyTuning(Physics, start);
        Assert.Equal(caught, simulation.State.Position); Assert.Equal(BallPhase.Aiming, simulation.State.Phase);
        simulation.Step(new(AimDirection: new double2(1, -1))); Assert.Equal(BallPhase.Aiming, simulation.State.Phase);
        simulation.Step(new(PrimaryPressed: true)); Assert.Equal(BallPhase.Flying, simulation.State.Phase);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void ClassicLiveTuningNeverResurrectsTerminalRound(bool win)
    {
        var start = BreakoutTests.Game(balls: 1, maxBounces: 1, aimX: win ? 0 : 1, aimY: win ? -1 : 0,
            bricks: [new(450, 200, 100, 30)]);
        using var simulation = new BallSimulation(Physics, start, new(1, 16, 1));
        simulation.Step(new(PrimaryPressed: true));
        for (int i = 0; i < 1000 && simulation.RoundPhase == RoundPhase.Playing; i++) simulation.Step();
        Assert.Equal(win ? RoundPhase.Won : RoundPhase.Lost, simulation.RoundPhase);
        var before = simulation.Snapshot;
        simulation.ApplyTuning(Physics, new(start.Arena, new(12, 900, 500, 750, 1, -1),
            new(8, .4, 50, start.Breakout!.Bricks)));
        Assert.Equal(before.RoundPhase, simulation.RoundPhase); Assert.Equal(before.State.Position, simulation.State.Position);
        Assert.Equal(before.BallsRemaining, simulation.BallsRemaining); Assert.Equal(before.State.Phase, simulation.State.Phase);
        Assert.Equal(default, simulation.State.Velocity); Assert.Equal(before.Tick, simulation.Snapshot.Tick);
    }

    [Fact]
    public void ClassicLiveSpeedUsesExistingExecutionOwnerWithoutChangingCatchPosePhaseOrTerminalFacts()
    {
        var initial = TestSettings.Gameplay;
        using var simulation = new BallSimulation(Physics, initial, new(1, 16, 1));
        simulation.Step(new(PrimaryPressed: true));
        var before = simulation.Snapshot;
        var next = new GameplaySettings(initial.Arena, new(14, 50000, 640, 610, 1, -1), null);
        simulation.ApplyTuning(Physics, next);
        var after = simulation.Snapshot;
        Assert.Equal(before.Tick, after.Tick); Assert.Equal(before.State.Position, after.State.Position);
        Assert.Equal(before.State.Phase, after.State.Phase); Assert.Equal(before.State.AimDirection, after.State.AimDirection);
        Assert.Equal(before.BouncesWithoutBrick, after.BouncesWithoutBrick); Assert.Equal(before.RoundPhase, after.RoundPhase);
        Assert.InRange(Math.Abs(after.State.Velocity.Y), 49999.9, 50000.1);
        double y = after.State.Position.Y; simulation.Step();
        Assert.InRange(y - simulation.State.Position.Y, 416.64, 416.69);
        simulation.ApplyTuning(Physics, new(initial.Arena, new(14, 300, 640, 610, 1, -1), null));
        Assert.InRange(Math.Abs(simulation.State.Velocity.Y), 299.99, 300.01);
    }
}
