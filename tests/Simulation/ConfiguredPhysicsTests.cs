using BrutalBreakout.Platform.Input;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class ConfiguredPhysicsTests
{
    public static IEnumerable<object[]> RatesAndPartitions()
    {
        foreach (var (hz, substeps) in new[]
        {
            (30, 1), (30, 4), (60, 2), (60, 4),
            (120, 4), (144, 3), (240, 4), (240, 8)
        })
        foreach (int frames in new[] { 30, 60, 144, 1000 })
            yield return new object[] { hz, frames, substeps, 11 };
    }

    [Theory]
    [MemberData(nameof(RatesAndPartitions))]
    public void OneNativeSecondAgreesWithClockAndDirectSteps(int hz, int frames, int substeps, int cap)
    {
        var settings = new PhysicsSettings(hz, substeps, cap, 90, .7);
        var clock = new FixedStepClock(settings);
        using var simulation = new BallSimulation(settings, TestSettings.Gameplay, new(2, 64, 2));
        using var reference = new BallSimulation(settings, TestSettings.Gameplay, new(2, 64, 2));
        Assert.Same(settings, simulation.Settings);
        Assert.Same(settings, clock.Settings);
        int ticks = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            int count = clock.Advance(1.0 / frames);
            for (int i = 0; i < count; i++) simulation.Step(new(PrimaryPressed: ticks++ == 0));
        }
        for (int i = 0; i < hz; i++) reference.Step(new(PrimaryPressed: i == 0));
        Assert.Equal(hz, ticks);
        Assert.Equal(reference.State, simulation.State);
        Assert.InRange(clock.RemainderSeconds, 0, 1e-12);
    }

    [Theory]
    [InlineData(30, 1)] [InlineData(60, 2)] [InlineData(120, 4)]
    [InlineData(144, 3)] [InlineData(240, 8)]
    public void NativeFirstDisplacementUsesConfiguredStepAndSubsteps(int hz, int substeps)
    {
        var settings = new PhysicsSettings(hz, substeps, 2, 60, .5);
        using var simulation = new BallSimulation(settings, TestSettings.Gameplay, new(2, 64, 2));
        double start = simulation.State.Position.Y;
        simulation.Step(new(PrimaryPressed: true));
        Assert.InRange(Math.Abs((start - simulation.State.Position.Y) - 600 * settings.StepSeconds), 0, .001);
        // Independently step the published native API with the requested substeps, not the wrapper.
        var definition = Brutal.Box2DApi.B2WorldDef.Default;
        definition.Gravity = default;
        definition.ContactHertz = settings.ContactHertz;
        definition.ContactDampingRatio = settings.ContactDampingRatio;
        using var world = Brutal.Box2DApi.Box2D.CreateWorld(in definition);
        var bodyDefinition = Brutal.Box2DApi.B2BodyDef.Default;
        bodyDefinition.Type = Brutal.Box2DApi.B2BodyType.Dynamic;
        bodyDefinition.Position = new(6.4f, 6.12f);
        bodyDefinition.LinearVelocity = new(0, -6);
        var body = world.CreateBody(in bodyDefinition);
        world.Step((float)settings.StepSeconds, substeps);
        Assert.Equal((double)body.GetPosition().Y * 100, simulation.State.Position.Y);
    }

    [Theory]
    [InlineData(30, 1)] [InlineData(60, 3)] [InlineData(144, 17)] [InlineData(240, int.MaxValue)]
    public void HugeElapsedPreservesFractionWithCustomAndLargestCaps(int hz, int cap)
    {
        var clock = new FixedStepClock(new(hz, 2, cap, 0, 0));
        Assert.Equal(0, clock.Advance(clock.StepSeconds / 4));
        double remainder = clock.RemainderSeconds;
        Assert.Equal(cap, clock.Advance(double.MaxValue));
        Assert.Equal(remainder, clock.RemainderSeconds);
        Assert.Equal(0, clock.Advance(0));
        Assert.Equal(1, clock.Advance(clock.StepSeconds * .75));
        Assert.InRange(clock.RemainderSeconds, 0, 1e-12);
        clock.Reset();
        Assert.Equal(0, clock.RemainderSeconds);
    }

    [Fact]
    public void LargestCapNearBoundaryDoesNotOverflowIntegerConversion()
    {
        var clock = new FixedStepClock(new(144, 1, int.MaxValue, 0, 0));
        clock.Advance(clock.StepSeconds / 2);
        Assert.Equal(int.MaxValue, clock.Advance((int.MaxValue - .25) * clock.StepSeconds));
        Assert.InRange(clock.RemainderSeconds / clock.StepSeconds, .249999, .250001);
        Assert.Equal(0, clock.Advance(0));
    }

    [Theory]
    [InlineData(30, 1)] [InlineData(60, 3)] [InlineData(144, 7)] [InlineData(240, 2)]
    public void SessionLatchesUntilTickConsumesOnceAndClearsOnPause(int hz, int cap)
    {
        var settings = new PhysicsSettings(hz, 2, cap, 60, 1);
        using var session = new BallSandboxSession(settings, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
        Assert.Same(settings, session.Settings);
        var viewport = new ArenaViewport(1280, 720, 1280, 720, TestSettings.Gameplay.Arena);
        var idle = new InputSnapshot { Focused = true };
        var press = idle with { KeyboardPrimary = true };
        Assert.Equal(0, session.Advance(idle, 10, viewport)); // Activation discards time.
        session.Simulation.Step(new(PrimaryPressed: true));
        for (int i = 0; i < hz * 3 && !session.Simulation.State.CatchEligible; i++) session.Simulation.Step();
        Assert.True(session.Simulation.State.CatchEligible);
        Assert.Equal(0, session.Advance(press, settings.StepSeconds / 4, viewport));
        Assert.Equal(cap, session.Advance(idle, 10, viewport));
        Assert.Equal(BallPhase.Aiming, session.Simulation.State.Phase); // No repeated edge relaunch.
        Assert.Equal(0, session.Advance(press, 0, viewport));
        Assert.Equal(0, session.Advance(default, 10, viewport));
        Assert.Equal(0, session.Advance(idle, 10, viewport));
        Assert.Equal(0, session.Advance(idle, settings.StepSeconds / 2, viewport));
        Assert.Equal(1, session.Advance(idle, settings.StepSeconds / 2, viewport));
        Assert.Equal(BallPhase.Aiming, session.Simulation.State.Phase); // Pending press discarded by pause.
    }

    [Theory]
    [InlineData(30, 1)] [InlineData(144, 3)] [InlineData(240, 8)]
    public void ConfiguredWarmedClockDoesNotAllocate(int hz, int substeps)
    {
        var settings = new PhysicsSettings(hz, substeps, 3, 60, 1);
        var clock = new FixedStepClock(settings);
        // The accumulator remains allocation-free. Dependency graphs, tasks and retained
        // immutable snapshots intentionally allocate; measure that path separately.
        for (int i = 0; i < 1000; i++) clock.Advance(settings.StepSeconds);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) clock.Advance(settings.StepSeconds);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
