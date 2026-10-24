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

/// <summary>One synchronous, allocation-free 120Hz gameplay system. No wall time or platform dependencies.</summary>
public sealed class BallSimulation
{
    public const double StepSeconds = 1.0 / 120;
    public BallState State { get; private set; } = InitialState();

    private static BallState InitialState() => new(new(640, 612), default, new(0, -1), BallPhase.Aiming, false);

    public WallContact Preview() => Arena.QueryUnit(State.Position, State.AimDirection);

    /// <summary>Commands apply before movement. Launch moves during this tick; catch and restart do not.</summary>
    public void Step(SimulationCommand command = default)
    {
        if (command.RestartPressed)
        {
            State = InitialState();
            return;
        }

        var state = State;
        double2 aim = state.AimDirection;
        if (command.AimDirection is { } direction && Arena.Finite(direction) && (direction.X != 0 || direction.Y != 0))
            aim = Arena.Normalize(direction);

        var phase = state.Phase;
        bool eligible = state.CatchEligible;
        double2 velocity = state.Velocity;
        if (command.PrimaryPressed)
        {
            if (phase == BallPhase.Aiming)
            {
                phase = BallPhase.Flying;
                velocity = aim * Arena.Speed;
                eligible = false;
            }
            else if (eligible)
            {
                State = new(state.Position, default, aim, BallPhase.Aiming, false);
                return; // A catch never fires on the same edge.
            }
        }

        double2 position = state.Position;
        if (phase == BallPhase.Flying)
        {
            double remaining = Arena.Speed * StepSeconds;
            double2 travel = velocity / Arena.Speed;
            // Tick distance is 5 units; inset spans are >=600. At most two distinct
            // immediate/near-corner contacts plus final travel fit in one tick.
            for (int i = 0; i < 4 && remaining > 0; i++)
            {
                var hit = Arena.QueryUnit(position, travel);
                if (hit.Distance > remaining)
                {
                    position += travel * remaining;
                    remaining = 0;
                }
                else
                {
                    position = hit.Position;
                    remaining -= hit.Distance;
                    travel = hit.ReflectedDirection;
                    eligible = true;
                }
            }
            velocity = travel * Arena.Speed;
        }
        State = new(position, velocity, aim, phase, eligible);
    }
}
