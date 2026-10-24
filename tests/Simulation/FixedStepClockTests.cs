using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class FixedStepClockTests
{
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)] [InlineData(144)] [InlineData(1000)]
    public void TimePartitionProducesSameTicksAndGameplay(int frames)
    {
        var clock = new FixedStepClock();
        var sim = new BallSimulation();
        bool launchPending = true;
        int steps = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            int count = clock.Advance(1.0 / frames);
            steps += count;
            for (int tick = 0; tick < count; tick++)
            {
                sim.Step(new(PrimaryPressed: launchPending));
                launchPending = false;
            }
        }
        var reference = new BallSimulation();
        reference.Step(new(PrimaryPressed: true));
        for (int tick = 1; tick < 120; tick++) reference.Step();
        Assert.Equal(120, steps);
        Assert.Equal(reference.State, sim.State);
        Assert.InRange(clock.RemainderSeconds, 0, 1e-12);
    }

    [Theory]
    [InlineData(1)] [InlineData(1000000)] [InlineData(double.MaxValue)]
    public void LargeElapsedIsBoundedAndDoesNotLeaveDebt(double elapsed)
    {
        var clock = new FixedStepClock();
        Assert.Equal(8, clock.Advance(elapsed));
        Assert.Equal(0, clock.Advance(0));
        Assert.Equal(1, clock.Advance(FixedStepClock.StepSeconds));
    }

    [Fact]
    public void CapPreservesPreviousFractionAndResetDropsIt()
    {
        var clock = new FixedStepClock();
        Assert.Equal(0, clock.Advance(FixedStepClock.StepSeconds / 2));
        Assert.Equal(8, clock.Advance(100));
        Assert.Equal(1, clock.Advance(FixedStepClock.StepSeconds / 2));
        clock.Advance(FixedStepClock.StepSeconds / 2);
        clock.Reset();
        Assert.Equal(0, clock.RemainderSeconds);
        Assert.Equal(0, clock.Advance(FixedStepClock.StepSeconds / 2));
    }

    [Theory]
    [InlineData(-1)] [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void InvalidElapsedThrowsWithoutChangingAccumulator(double elapsed)
    {
        var clock = new FixedStepClock();
        clock.Advance(FixedStepClock.StepSeconds / 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(elapsed));
        Assert.Equal(1, clock.Advance(FixedStepClock.StepSeconds / 2));
    }

    [Fact]
    public void MultiTickCallerConsumesPrimaryOnlyOnce()
    {
        var sim = new BallSimulation();
        sim.Step(new(PrimaryPressed: true));
        while (!sim.State.CatchEligible) sim.Step();
        var position = sim.State.Position;
        var clock = new FixedStepClock();
        int steps = clock.Advance(1);
        for (int i = 0; i < steps; i++) sim.Step(new(PrimaryPressed: i == 0));
        Assert.Equal(BallPhase.Aiming, sim.State.Phase);
        Assert.Equal(position, sim.State.Position);
    }
}
