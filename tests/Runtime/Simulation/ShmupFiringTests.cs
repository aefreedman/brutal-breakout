using Brutal.Numerics;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public sealed class ShmupFiringTests
{
    private static ShmupSimulation Create(double rate, int capacity = 512, double speed = 60) =>
        new(TestSettings.Baseline, ShmupSettingsTests.Settings(threshold: 1, fireRate: rate,
            maxBalls: capacity, maxBricks: 8, speed: speed), new(1, 64, 1));

    [Theory]
    [InlineData(12, 12)] [InlineData(12.5, 13)] [InlineData(100, 100)] [InlineData(300, 300)]
    public void FractionalAndHighRatesPreserveBirthSpacingAndRemainingTravel(double rate, int expected)
    {
        using var sim = Create(rate);
        for (int i = 0; i < 120; i++) sim.Step(new(FireHeld: true));
        var balls = sim.Snapshot.Balls.ToArray();
        Assert.Equal(expected, balls.Length);
        for (int i = 0; i < balls.Length; i++)
        {
            Assert.Equal(1, balls[i].Generation);
            Assert.InRange(Math.Abs(balls[i].Position.Y - (696 - 60 * (1 - i / rate))), 0, .02);
            if (i > 0) Assert.InRange(balls[i].Position.Y - balls[i - 1].Position.Y, 60 / rate - .01, 60 / rate + .01);
        }
        Assert.Equal(120, sim.Snapshot.Tick);
    }

    [Fact]
    public void DefaultCadenceAndExactTickEndBirthAreHalfOpen()
    {
        using var sim = Create(12);
        sim.Step(new(FireHeld: true));
        var retained = sim.Snapshot;
        for (int i = 1; i < 10; i++) sim.Step(new(FireHeld: true));
        Assert.Single(sim.Snapshot.Balls.ToArray());
        sim.Step(new(FireHeld: true));
        Assert.Equal(2, sim.Snapshot.Balls.Length);
        Assert.Single(retained.Balls.ToArray());
        using var boundary = Create(120);
        boundary.Step(new(FireHeld: true));
        Assert.Single(boundary.Snapshot.Balls.ToArray());
        boundary.Step(new(FireHeld: true));
        Assert.Equal(2, boundary.Snapshot.Balls.Length);
    }

    [Fact]
    public void MovingEmitterUsesActualBirthPoseNotEndPose()
    {
        using var sim = Create(300);
        var start = sim.Snapshot.PlayerPosition;
        sim.Step(new(new(1, 0), new(0, -1), true));
        var balls = sim.Snapshot.Balls.ToArray();
        Assert.Equal(3, balls.Length);
        for (int i = 0; i < balls.Length; i++)
        {
            Assert.InRange(Math.Abs(balls[i].Position.X - (start.X + 250 * i / 300d)), 0, .002);
            Assert.InRange(Math.Abs(balls[i].Position.Y - (start.Y - 60 * (sim.StepSeconds - i / 300d))), 0, .002);
        }
        Assert.True(balls[0].Position.X < sim.Snapshot.PlayerPosition.X);
    }

    [Fact]
    public void IdleReleaseAndFullPoolConsumeDeadlinesWithoutBanking()
    {
        using var sim = Create(300, capacity: 1);
        sim.Step(new(FireHeld: true));
        Assert.Equal(2, sim.Snapshot.DroppedBalls);
        sim.Step(new(FireHeld: true));
        Assert.Equal(4, sim.Snapshot.DroppedBalls);
        for (int i = 0; i < 120; i++) sim.Step();
        long dropped = sim.Snapshot.DroppedBalls;
        sim.Step(new(FireHeld: true));
        Assert.Equal(dropped + 3, sim.Snapshot.DroppedBalls);
        Assert.Equal(1, sim.Snapshot.Balls[0].Generation);
    }

    [Fact]
    public void LiveRateChangePreservesFractionalProgressBelowOneTick()
    {
        using var sim = Create(100);
        sim.Step(new(FireHeld: true)); // remaining .001666..., 1/6 of a period
        var old = sim.Snapshot;
        var settings = sim.Settings;
        var next = new ShmupSettings(settings.Arena, settings.Avatar,
            new(settings.Ball.Radius, settings.Ball.Speed, 300, settings.Ball.Damage,
                settings.Ball.BounceOnBricks, settings.Ball.MaxWallHits, settings.Ball.MaxShotsPerTick),
            settings.Brick, settings.Difficulty, settings.Noise, settings.Capacity);
        sim.ApplyTuning(sim.Physics, next);
        Assert.Equal(old.Tick, sim.Snapshot.Tick);
        Assert.Equal(old.Balls.ToArray(), sim.Snapshot.Balls.ToArray());
        sim.Step(new(FireHeld: true));
        Assert.Equal(4, sim.Snapshot.Balls.Length);
        Assert.InRange(696 - sim.Snapshot.Balls[1].Position.Y, .465, .468);
    }

    [Fact]
    public void SchedulerHasBoundedAttemptsAndNoIdleDebtAtSupportedCeiling()
    {
        Span<double> births = stackalloc double[64];
        double remaining = 0, dt = 1 / 120d, period = dt / 64;
        Assert.Equal(64, ShmupFireSchedule.Fill(births, ref remaining, dt, period, true));
        Assert.InRange(remaining, 0, 1e-15);
        Assert.Equal(0, births[0]);
        Assert.True(births[63] < dt);
        Assert.Equal(0, ShmupFireSchedule.Fill(births, ref remaining, dt, period, false));
        Assert.Equal(64, ShmupFireSchedule.Fill(births, ref remaining, dt, period, true));
    }

    [Theory]
    [InlineData(0)] [InlineData(65)]
    public void InvalidBudgetsRejectBeforeNativeAllocation(int budget) =>
        Assert.ThrowsAny<ArgumentException>(() => ShmupSettingsTests.Settings(shotBudget: budget));

    [Fact]
    public void BudgetAndUnrepresentablePeriodRejectButHighRatesAreSupported()
    {
        ShmupSettingsTests.Settings(fireRate: 7680).ValidatePhysics(TestSettings.Baseline);
        Assert.Throws<ArgumentException>(() => ShmupSettingsTests.Settings(fireRate: 7681).ValidatePhysics(TestSettings.Baseline));
        Assert.Throws<ArgumentException>(() => ShmupSettingsTests.Settings(fireRate: double.Epsilon).ValidatePhysics(TestSettings.Baseline));
        var tinyTick = new PhysicsSettings(1e30, 4, 8, 120, 1);
        Assert.Throws<ArgumentException>(() => ShmupSettingsTests.Settings().ValidatePhysics(tinyTick));
    }

    [Fact]
    public void LateBirthCannotCollideWithEarlierBrickPath()
    {
        var settings = ShmupSettingsTests.Settings(threshold: 1, bounce: false, speed: 1, descent: 12000);
        using var native = new NativeShmupArena(TestSettings.Baseline, settings, new(1, 64, 1));
        native.ActivateBrick(0, new(640, 250));
        native.Step(12000, new(640, 696), new(640, 696), [.006],
            _ => native.ActivateBall(0, new(640, 300), new(0, -1)));
        Assert.Equal(0, native.BrickHits[0, 0]);
        Assert.Empty(native.HitPairs.ToArray());
        Assert.InRange(native.BallPosition(0).Y, 299.997, 300);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NewbornBrickHitBeforeLaterExpirySurvivesTickFacts(bool bounce)
    {
        var settings = ShmupSettingsTests.Settings(threshold: 1, bounce: bounce, width: 100,
            speed: 60000, descent: 1, wallHits: 1);
        using var native = new NativeShmupArena(TestSettings.Baseline, settings, new(1, 64, 1));
        native.ActivateBrick(0, new(640, 300));
        native.Step(1, new(640, 696), new(640, 696), [.004],
            _ => native.ActivateBall(0, new(640, 300), new(1, 0)));
        Assert.True(native.Expired[0]);
        Assert.True(native.BrickHits[0, 0] >= 1);
        Assert.Single(native.HitPairs.ToArray());
    }

    [Theory]
    [InlineData(ShmupBrickShape.T)] [InlineData(ShmupBrickShape.L)]
    public void ExtraBirthBoundariesPreserveCompoundTouchWithoutDuplicateEntry(ShmupBrickShape shape)
    {
        var settings = ShmupSettingsTests.Settings(threshold: 1, bounce: false, speed: 1, descent: 1);
        using var native = new NativeShmupArena(TestSettings.Baseline, settings, new(1, 64, 1));
        native.ActivateBrick(0, new(640, 300), shape, 0);
        // L joins at x=-4,y=40; T joins at x=0,y=-4 for size32/pitch40.
        native.ActivateBall(0, shape == ShmupBrickShape.L ? new(636, 340) : new(640, 296), new(1, 0));
        native.Step(1, new(640, 696), new(640, 696), [0, .002, .004, .006], _ => { });
        Assert.Equal(1, native.BrickHits[0, 0]);
        Assert.InRange(native.LastIntervalCount, 1, 5);
        native.Step(1, new(640, 696), new(640, 696), [.003], _ => { });
        Assert.Equal(0, native.BrickHits[0, 0]);
    }
}
