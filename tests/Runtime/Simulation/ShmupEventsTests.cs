using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class ShmupEventsTests
{
    private static ShmupSimulation Create(ShmupSettings? settings = null) =>
        new(TestSettings.Baseline, settings ?? ShmupSettingsTests.Settings(threshold: 1), new(1, 64, 1));

    [Fact]
    public void FireFactsDescribeActualSpawnsNotHeldInputOrPoolDrops()
    {
        using var sim = Create();
        Assert.Equal(ShmupEvents.None, sim.Events);
        sim.Step(new(FireHeld: true));
        Assert.Equal(ShmupEvents.Fire, sim.Events);
        sim.Step(new(FireHeld: true));
        Assert.Equal(ShmupEvents.None, sim.Events);
        using var full = Create(ShmupSettingsTests.Settings(threshold: 1, maxBalls: 1));
        full.SpawnBall(new(640, 200), new(1, 0));
        full.Step(new(FireHeld: true));
        Assert.Equal(1, full.Snapshot.DroppedBalls);
        Assert.Equal(ShmupEvents.None, full.Events);
    }

    [Theory]
    [InlineData(100, ShmupEvents.BrickHit)]
    [InlineData(10, ShmupEvents.BrickHit | ShmupEvents.BrickDestroyed)]
    public void HitAndDestructionFactsDoNotRepeatForPersistentOverlap(int health, ShmupEvents expected)
    {
        using var sim = Create(ShmupSettingsTests.Settings(bounce: false, threshold: 1, speed: 1, descent: 1, brickHealth: health));
        sim.SpawnBrick(new(640, 200)); sim.SpawnBall(new(640, 200), new(1, 0));
        sim.Step(); Assert.Equal(expected, sim.Events);
        sim.Step(); Assert.Equal(ShmupEvents.None, sim.Events);
    }

    [Fact]
    public void PlayerDamageOnlyEmitsWhenHealthChangesNotDuringInvulnerability()
    {
        using var sim = Create();
        sim.SpawnBrick(sim.Snapshot.PlayerPosition);
        sim.Step(); Assert.Equal(ShmupEvents.PlayerDamaged, sim.Events);
        sim.Step(); Assert.Equal(ShmupEvents.None, sim.Events);
    }

    [Fact]
    public void EscapeDefeatAndRestartFactsAreOneShotEvenWhileSnapshotIsFrozen()
    {
        using var sim = Create(ShmupSettingsTests.Settings(threshold: 1, health: 10));
        sim.SpawnBrick(new(400, 740));
        sim.Step(); Assert.Equal(ShmupEvents.PlayerDamaged | ShmupEvents.Defeat, sim.Events);
        var defeated = sim.Snapshot;
        sim.Step(new(FireHeld: true));
        Assert.Same(defeated, sim.Snapshot);
        Assert.Equal(ShmupEvents.None, sim.Events);
        sim.Step(new(FireHeld: true, RestartPressed: true));
        Assert.Equal(ShmupEvents.Restart, sim.Events);
        Assert.Empty(sim.Snapshot.Balls.ToArray());
        sim.Step(); Assert.Equal(ShmupEvents.None, sim.Events);
        sim.Restart(); Assert.Equal(ShmupEvents.Restart, sim.Events);
    }
}
