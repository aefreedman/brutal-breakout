using Brutal.Numerics;
using BrutalBreakout.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Simulation.Tests;

public class LifetimeAndCycleTests(ITestOutputHelper output)
{
    [Fact]
    public void DisposeIsIdempotentAndAllNativeOperationsRejectUseAfterDispose()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        sim.Dispose();
        sim.Dispose();
        Assert.Throws<ObjectDisposedException>(() => sim.Step());
        Assert.Throws<ObjectDisposedException>(() => sim.Preview());
        Assert.Throws<ObjectDisposedException>(() => sim.Query(new(640, 360), new(1, 0)));
        // State is a managed snapshot, intentionally still readable.
        Assert.Equal(new double2(640, 612), sim.State.Position);
    }

    [Fact]
    public void AnyCallerThreadCanSubmitOrderedPhysicsOperations()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        Exception? step = null, query = null, preview = null, dispose = null;
        var thread = new Thread(() =>
        {
            step = Record.Exception(() => sim.Step());
            query = Record.Exception(() => sim.Query(new(640, 360), new(1, 0)));
            preview = Record.Exception(() => sim.Preview());
            dispose = Record.Exception(() => sim.Dispose());
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(step);
        Assert.Null(query);
        Assert.Null(preview);
        Assert.Null(dispose);
        Assert.Throws<ObjectDisposedException>(() => sim.Step());
    }

    [Fact]
    public void IndependentWorldsAndRepeatedDestructionDoNotShareGameplay()
    {
        using var still = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        for (int cycle = 0; cycle < 100; cycle++)
        {
            using var moving = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
            moving.Step(new(new double2(1, 0), true));
            for (int tick = 0; tick < 130; tick++) moving.Step();
            Assert.True(moving.State.CatchEligible);
            Assert.Equal(BallPhase.Aiming, still.State.Phase);
            Assert.Equal(new double2(640, 612), still.State.Position);
            Assert.True(still.Preview().HasHit);
        }
    }

    [Fact]
    public void RepeatedCatchHoldRelaunchRestartKeepsCurrentPoseAndSeparateEdges()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        double penetration = 0, drift = 0;
        for (int cycle = 0; cycle < 50; cycle++)
        {
            sim.Step(new(new double2(cycle % 2 == 0 ? 1 : -1, -.731), true));
            Assert.False(sim.State.CatchEligible); // initial/reset launch is nowhere near a wall
            for (int tick = 0; tick < 400 && !sim.State.CatchEligible; tick++)
            {
                sim.Step();
                Measure();
            }
            Assert.True(sim.State.CatchEligible);
            for (int tick = 0; tick < 20; tick++) { sim.Step(); Measure(); }
            Assert.True(sim.State.CatchEligible); // eligibility persists beyond the event buffer
            var caught = sim.State.Position;
            sim.Step(new(PrimaryPressed: true));
            Assert.Equal(BallPhase.Aiming, sim.State.Phase);
            Assert.Equal(caught, sim.State.Position);
            Assert.Equal(default, sim.State.Velocity);
            Assert.False(sim.State.CatchEligible);
            for (int tick = 0; tick < 10; tick++) sim.Step();
            Assert.Equal(caught, sim.State.Position);
            sim.Step(new(new double2(0, 1), true));
            Assert.Equal(BallPhase.Flying, sim.State.Phase);
            for (int tick = 0; tick < 200; tick++) { sim.Step(); Measure(); }
            sim.Step(new(new double2(1, 0), true, true));
            Assert.Equal(BallPhase.Aiming, sim.State.Phase);
            Assert.Equal(new double2(640, 612), sim.State.Position);
            Assert.Equal(new double2(0, -1), sim.State.AimDirection);
            Assert.False(sim.State.CatchEligible);
        }
        output.WriteLine($"50 catch/relaunch/reset cycles: worst penetration {penetration:F9}, speed drift {drift:F9}");

        void Measure()
        {
            var p = sim.State.Position;
            penetration = Math.Max(penetration, Math.Max(Math.Max(TestSettings.Gameplay.MinX - p.X, p.X - TestSettings.Gameplay.MaxX),
                Math.Max(TestSettings.Gameplay.MinY - p.Y, p.Y - TestSettings.Gameplay.MaxY)));
            drift = Math.Max(drift, Math.Abs(double2.Distance(default, sim.State.Velocity) - TestSettings.Gameplay.Ball.Speed));
            Assert.InRange(penetration, 0, 2);
            Assert.InRange(drift, 0, 1);
        }
    }

    [Fact]
    public void NativeElapsedMovementUsesTheFixedTimestep()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        sim.Step(new(new double2(1, 0), true));
        for (int i = 1; i < 100; i++) sim.Step();
        ArenaTests.Near(new(1140, 612), sim.State.Position, .01);
        Assert.False(sim.State.CatchEligible);
        ArenaTests.Near(new(600, 0), sim.State.Velocity, .001);
    }

    [Fact]
    public void NativeStepAllocationMeasurement()
    {
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        sim.Step(new(new double2(1, -.731), true));
        for (int i = 0; i < 200; i++) sim.Step();
        long totalBefore = GC.GetTotalAllocatedBytes(precise: true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) sim.Step();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        long total = GC.GetTotalAllocatedBytes(precise: true) - totalBefore;
        output.WriteLine($"10000 warmed ECS/job ticks: caller={allocated} managed bytes; process={total} managed bytes (includes workers/harness, excludes native allocations)");
        // Measurement, not a zero-allocation promise: each tick owns its dependency graph/tasks
        // and immutable publication. Process totals include unrelated harness/background activity.
        Assert.Equal(BallPhase.Flying, sim.State.Phase);
    }
}
