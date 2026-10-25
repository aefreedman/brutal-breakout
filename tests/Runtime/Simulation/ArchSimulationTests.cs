using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class ArchSimulationTests
{
    [Fact]
    public async Task QueuedTicksAreOrderedAndEachReturnsItsOwnImmutablePublication()
    {
        using var simulation = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        var initial = simulation.Snapshot;
        var first = simulation.StepAsync(new(PrimaryPressed: true));
        var ticks = Enumerable.Range(0, 40).Select(_ => simulation.StepAsync()).ToArray();
        var launched = await first.WaitAsync(TimeSpan.FromSeconds(10));
        var snapshots = await Task.WhenAll(ticks).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, initial.Tick);
        Assert.Equal(BallPhase.Aiming, initial.State.Phase);
        Assert.Equal(1, launched.Tick);
        Assert.Equal(SimulationEvents.Launch, launched.Events);
        Assert.Equal(BallPhase.Flying, launched.State.Phase);
        for (int i = 0; i < snapshots.Length; i++) Assert.Equal(i + 2, snapshots[i].Tick);
        Assert.Same(snapshots[^1], simulation.Snapshot);
        Assert.NotEqual(initial.State.Position, snapshots[^1].State.Position);
        Assert.Equal(BallPhase.Aiming, initial.State.Phase); // A retained publication never changes.
    }

    [Fact]
    public async Task ConcurrentCallersQueueTicksAndDisposalDrainsAcceptedWork()
    {
        var simulation = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        await simulation.StepAsync(new(PrimaryPressed: true));
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            Task.Run(() => simulation.StepAsync()))).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(32, results.Select(x => x.Tick).Distinct().Count());
        var pending = Enumerable.Range(0, 16).Select(_ => simulation.StepAsync()).ToArray();
        await Task.Run(simulation.Dispose).WaitAsync(TimeSpan.FromSeconds(10));
        await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(49, simulation.Snapshot.Tick);
        Assert.Throws<ObjectDisposedException>(() => { _ = simulation.StepAsync(); });
        simulation.Dispose();
    }

    [Fact]
    public async Task IndependentWorldsCanAdvanceConcurrentlyWithoutSharingStateOrSchedulers()
    {
        using var first = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        using var second = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        await Task.WhenAll(first.StepAsync(new(PrimaryPressed: true)), second.StepAsync());
        Assert.Equal(BallPhase.Flying, first.State.Phase);
        Assert.Equal(BallPhase.Aiming, second.State.Phase);
        first.Dispose();
        await second.StepAsync(new(PrimaryPressed: true));
        Assert.Equal(BallPhase.Flying, second.State.Phase);
    }

    [Fact]
    public async Task BrickPublicationsRemainStableAcrossDestructionAndRestart()
    {
        var reference = TestSettings.Gameplay;
        var gameplay = new GameplaySettings(reference.Arena, reference.Ball,
            new(3, 1.0 / 3, 0, [new(590, 200, 100, 30), new(200, 200, 100, 30)]));
        using var simulation = new BallSimulation(TestSettings.Baseline, gameplay, new(2, 64, 2));
        var original = simulation.Snapshot;
        await simulation.StepAsync(new(PrimaryPressed: true));
        for (int i = 0; i < 300 && simulation.RemainingBricks == 2; i++) await simulation.StepAsync();
        var destroyed = simulation.Snapshot;
        Assert.Equal(1, destroyed.RemainingBricks);
        Assert.False(destroyed.Bricks[0].Alive);
        var restarted = await simulation.StepAsync(new(RestartPressed: true));
        Assert.Equal(2, restarted.RemainingBricks);
        Assert.True(restarted.Bricks[0].Alive);
        Assert.True(original.Bricks[0].Alive);
        Assert.False(destroyed.Bricks[0].Alive);
    }

    [Fact]
    public async Task QueriesWaitForAcceptedTicksAndInvalidQueriesDoNotStrandTheQueue()
    {
        using var simulation = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        var pending = simulation.StepAsync(new(PrimaryPressed: true));
        var hit = simulation.Preview(); // Must query the new pose, after the publication barrier.
        var state = await pending;
        var expected = simulation.Query(state.State.Position, state.State.AimDirection);
        Assert.Equal(expected.Position, hit.Position);
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Query(new(-1, 0), new(1, 0)));
        var next = await simulation.StepAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, next.Tick);
    }

    [Fact]
    public async Task WorkerBudgetsPreservePhysicsContactsAndGameFacts()
    {
        using var serial = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(1, 64, 1));
        using var parallel = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        var launch = new SimulationCommand(new Brutal.Numerics.double2(1, -.731), true);
        await Task.WhenAll(serial.StepAsync(launch), parallel.StepAsync(launch));
        bool contact = false;
        for (int i = 0; i < 500; i++)
        {
            var states = await Task.WhenAll(serial.StepAsync(), parallel.StepAsync());
            ArenaTests.Near(states[0].State.Position, states[1].State.Position, .001);
            ArenaTests.Near(states[0].State.Velocity, states[1].State.Velocity, .001);
            Assert.Equal(states[0].State.CatchEligible, states[1].State.CatchEligible);
            Assert.Equal(states[0].Events, states[1].Events);
            contact |= states[1].State.CatchEligible;
        }
        Assert.True(contact);
    }

    [Fact]
    public void InvalidBudgetsFailBeforeCreatingWorkersOrNativeWorlds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionSettings(-1, 64, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionSettings(2, 0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionSettings(2, 64, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionSettings(2, 64, 65));
        Assert.Throws<ArgumentNullException>(() => new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, null!));
    }
}
