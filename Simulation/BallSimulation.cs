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

/// <summary>One owned native world and synchronous configured-frequency gameplay. No paddle.</summary>
public sealed class BallSimulation : IDisposable
{
    public PhysicsSettings Settings { get; }
    public double StepSeconds => Settings.StepSeconds;
    private readonly NativeArena native;
    private readonly BrickState[] bricks;
    private bool leftCatchZone, contactSinceLaunch;
    public GameplaySettings Gameplay { get; }
    public ReadOnlySpan<BrickState> Bricks => bricks;
    public int RemainingBricks { get; private set; }
    /// <summary>Includes the active ball, matching Unity's total permitted spawns.</summary>
    public int BallsRemaining { get; private set; }
    public RoundPhase RoundPhase { get; private set; } = RoundPhase.Playing;
    public long BouncesWithoutBrick { get; private set; }
    /// <summary>Y-down canvas coordinate; NaN in explicit physics-reference mode.</summary>
    public double CatchLineY => Gameplay.CatchLineY;

    public BallSimulation(PhysicsSettings physics, GameplaySettings gameplay)
    {
        ArgumentNullException.ThrowIfNull(physics);
        ArgumentNullException.ThrowIfNull(gameplay);
        Settings = physics;
        Gameplay = gameplay;
        bricks = new BrickState[gameplay.Breakout?.Bricks.Count ?? 0];
        RestoreRoundState();
        native = new(physics, gameplay);
    }
    public BallState State { get; private set; }

    private BallState InitialState() => new(Gameplay.Ball.Spawn, default, Gameplay.Ball.InitialAim, BallPhase.Aiming, false);
    private void ClearShotFacts()
    {
        leftCatchZone = contactSinceLaunch = false;
        BouncesWithoutBrick = 0;
    }
    private void RestoreRoundState()
    {
        for (int i = 0; i < bricks.Length; i++) bricks[i] = new(i, Gameplay.Breakout!.Bricks[i], true);
        RemainingBricks = bricks.Length;
        BallsRemaining = Gameplay.Breakout?.StartingBalls ?? 0;
        RoundPhase = RoundPhase.Playing;
        ClearShotFacts();
        State = InitialState();
    }

    public WallContact Preview() => Query(State.Position, State.AimDirection);
    public WallContact Query(double2 origin, double2 direction) => native.Query(origin, direction);
    public void Dispose() => native.Dispose();

    private void FinishRound(RoundPhase phase, double2 position, double2 aim)
    {
        native.Freeze();
        RoundPhase = phase;
        State = new(position, default, aim, BallPhase.Aiming, false);
    }
    private void LoseBall(double2 position, double2 aim)
    {
        BallsRemaining--;
        ClearShotFacts();
        if (BallsRemaining == 0) FinishRound(RoundPhase.Lost, position, aim);
        else
        {
            native.Reset();
            State = InitialState();
        }
    }

    /// <summary>Commands apply before movement. Launch moves during this tick; catch and restart do not.</summary>
    public void Step(SimulationCommand command = default)
    {
        native.Guard();
        if (command.RestartPressed)
        {
            native.Reset();
            native.ResetBricks();
            RestoreRoundState();
            return;
        }
        if (RoundPhase != RoundPhase.Playing) return;

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
                ClearShotFacts();
                native.Launch(aim);
                phase = BallPhase.Flying;
                eligible = false;
            }
            else if (eligible)
            {
                native.Freeze();
                ClearShotFacts();
                State = new(state.Position, default, aim, BallPhase.Aiming, false);
                return; // A catch never fires or advances native motion on the same edge.
            }
        }

        if (phase != BallPhase.Flying)
        {
            State = new(state.Position, state.Velocity, aim, phase, eligible);
            return;
        }

        var result = native.Step();
        var position = native.Position;
        var velocity = native.Velocity;
        if (Gameplay.Breakout is not { } rules)
        {
            // Explicit physics-reference mode preserves the existing four-wall fixture behavior.
            State = new(position, velocity, aim, phase, eligible || result.HasContact);
            return;
        }

        leftCatchZone |= state.Position.Y < CatchLineY || position.Y < CatchLineY;
        contactSinceLaunch |= result.HasContact;
        if (result.BricksDestroyed > 0)
        {
            for (int i = 0; i < bricks.Length; i++)
                if (bricks[i].Alive && !native.IsBrickAlive(i)) bricks[i] = bricks[i] with { Alive = false };
            RemainingBricks -= result.BricksDestroyed;
            BouncesWithoutBrick = 0;
            if (RemainingBricks == 0)
            {
                FinishRound(RoundPhase.Won, position, aim);
                return;
            }
        }
        else BouncesWithoutBrick += result.WallContacts;

        // Like Unity, the restriction is center-based and applies to the BALL, not the cursor.
        eligible = leftCatchZone && contactSinceLaunch && Gameplay.IsInCatchZone(position.Y);
        if (position.Y - Gameplay.Ball.Radius > Gameplay.Arena.Bottom ||
            (rules.MaxBouncesWithoutBrick > 0 && BouncesWithoutBrick > rules.MaxBouncesWithoutBrick))
        {
            LoseBall(position, aim);
            return;
        }
        State = new(position, velocity, aim, phase, eligible);
    }
}
