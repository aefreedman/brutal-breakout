using Brutal.Numerics;

namespace BrutalBreakout.Simulation;

public enum BallPhase { Aiming, Flying }

/// <summary>Input edges belong to the caller. Send each pressed edge on only one Step.
/// Null or zero aim preserves aim; nonfinite aim is ignored. Restart wins over every other field.</summary>
public readonly record struct SimulationCommand(
    double2? AimDirection = null, bool PrimaryPressed = false, bool RestartPressed = false);

public readonly struct BallState
{
    public double2 Position { get; }
    public double2 Velocity { get; }
    public double2 AimDirection { get; }
    public BallPhase Phase { get; }
    public bool CatchEligible { get; }

    internal BallState(double2 position, double2 velocity, double2 aimDirection, BallPhase phase, bool catchEligible)
        => (Position, Velocity, AimDirection, Phase, CatchEligible) = (position, velocity, aimDirection, phase, catchEligible);
}

/// <summary>One owned native world and a synchronous configured-frequency gameplay system.</summary>
public sealed class BallSimulation : IDisposable
{
    public PhysicsSettings Settings { get; }
    public double StepSeconds => Settings.StepSeconds;
    private readonly NativeArena native;

    public BallSimulation(PhysicsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        native = new(settings);
    }
    public BallState State { get; private set; } = InitialState();

    private static BallState InitialState() => new(new(640, 612), default, new(0, -1), BallPhase.Aiming, false);

    public WallContact Preview() => Query(State.Position, State.AimDirection);
    public WallContact Query(double2 origin, double2 direction) => native.Query(origin, direction);
    public void Dispose() => native.Dispose();

    /// <summary>Commands apply before movement. Launch moves during this tick; catch and restart do not.</summary>
    public void Step(SimulationCommand command = default)
    {
        native.Guard();
        if (command.RestartPressed)
        {
            native.Reset();
            State = InitialState();
            return;
        }

        var state = State;
        double2 aim = state.AimDirection;
        if (command.AimDirection is { } direction && Arena.Finite(direction) && (direction.X != 0 || direction.Y != 0))
            aim = Arena.Normalize(direction);

        var phase = state.Phase;
        bool eligible = state.CatchEligible;
        if (command.PrimaryPressed)
        {
            if (phase == BallPhase.Aiming)
            {
                native.Launch(aim);
                phase = BallPhase.Flying;
                eligible = false;
            }
            else if (eligible)
            {
                native.Freeze();
                State = new(state.Position, default, aim, BallPhase.Aiming, false);
                return; // A catch never fires on the same edge.
            }
        }

        if (phase == BallPhase.Flying)
        {
            eligible |= native.Step();
            State = new(native.Position, native.Velocity, aim, phase, eligible);
        }
        else State = new(state.Position, state.Velocity, aim, phase, eligible);
    }
}
