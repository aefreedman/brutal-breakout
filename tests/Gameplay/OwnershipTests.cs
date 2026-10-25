using BrutalBreakout.Platform.Input;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using Xunit;

public class OwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostScopeDisposesWorldBeforeOuterDependencyOnNormalAndExceptionalExit(bool fail)
    {
        BallSandboxSession? session = null;
        bool outerCleanup = false;
        var original = new InvalidOperationException("host failure");
        void Run()
        {
            try
            {
                using var owner = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
                session = owner;
                owner.Simulation.Step(new(PrimaryPressed: true));
                if (fail) throw original;
            }
            finally
            {
                Assert.NotNull(session);
                Assert.Throws<ObjectDisposedException>(() => session.Simulation.Step());
                outerCleanup = true;
            }
        }
        if (fail) Assert.Same(original, Assert.Throws<InvalidOperationException>(Run));
        else Run();
        Assert.True(outerCleanup);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void DisposedSessionRejectsAdvanceEvenWithoutNativeTick(bool focused, double seconds)
    {
        using var session = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
        var snapshot = session.Simulation.State;
        session.Dispose();
        session.Dispose();
        Assert.Equal(snapshot.Position, session.Simulation.State.Position);
        Assert.Throws<ObjectDisposedException>(() => session.Advance(
            new InputSnapshot { Focused = focused }, seconds, new(1280, 720, 1280, 720, TestSettings.Gameplay.Arena)));
        Assert.Throws<ObjectDisposedException>(() => session.Simulation.Step());
        Assert.Throws<ObjectDisposedException>(() => session.Simulation.Preview());
        Assert.Throws<ObjectDisposedException>(() => session.Simulation.Query(snapshot.Position, snapshot.AimDirection));
    }

    [Fact]
    public void WrongThreadCannotAdvanceOrDisposeAndOwnerCanContinue()
    {
        using var session = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
        Exception? advance = null, dispose = null;
        var thread = new Thread(() =>
        {
            advance = Record.Exception(() => session.Advance(default, 0, default));
            dispose = Record.Exception(session.Dispose);
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(advance);
        Assert.IsType<InvalidOperationException>(dispose);
        session.Simulation.Step(new(PrimaryPressed: true));
        Assert.Equal(BallPhase.Flying, session.Simulation.State.Phase);
    }

    [Fact]
    public void FailedSimulationDrainStillClosesTheHostInputSession()
    {
        var session = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
        try
        {
            // Inject only the bridge's recorded managed fault, not a failing native callback.
            // This exercises the real shutdown queue and native cleanup without corrupting physics.
            static object Field(object owner, string name) => owner.GetType()
                .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(owner)!;
            var core = Field(session.Simulation, "simulation");
            var bridge = Field(Field(core, "native"), "tasks");
            var failure = new InvalidOperationException("injected recorded physics fault");
            bridge.GetType().GetField("fault", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(bridge, System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure));
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(session.Dispose));
            Assert.Throws<ObjectDisposedException>(() => session.Advance(default, 0, default));
            Assert.Throws<ObjectDisposedException>(() => { _ = session.DrainEvents(); });
            Assert.Throws<ObjectDisposedException>(() => session.Simulation.Step());
        }
        finally { _ = Record.Exception(session.Dispose); }
    }

    [Fact]
    public void RepeatedIndependentHostLifetimesDoNotInvalidateHeldWorld()
    {
        using var held = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
        for (int i = 0; i < 50; i++)
        {
            using var temporary = new BallSandboxSession(TestSettings.Baseline, TestSettings.Gameplay, TestSettings.Input, new(2, 64, 2));
            temporary.Simulation.Step(new(PrimaryPressed: true));
            Assert.True(temporary.Simulation.Preview().HasHit);
        }
        held.Simulation.Step(new(PrimaryPressed: true));
        Assert.Equal(BallPhase.Flying, held.Simulation.State.Phase);
        Assert.True(held.Simulation.Preview().HasHit);
    }
}
