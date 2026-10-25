using Arch.Core;
using Brutal.Numerics;

namespace BrutalBreakout.Simulation;

/// <summary>Game-specific ECS systems. The host orders jobs; only PreparePhysics touches the
/// world before the independent ball/round and brick systems run. Only ball/round rules
/// may subsequently access native state; the brick job uses owned flags exclusively.</summary>
internal sealed class BreakoutWorld : IDisposable
{
    private readonly World world;
    private readonly Entity ball;
    private readonly Entity round;
    private readonly NativeArena native;
    private readonly bool[] brickAlive;
    private readonly QueryDescription brickQuery = new QueryDescription().WithAll<BrickComponent>();
    private readonly GameplaySettings gameplay;
    private TickMotion motion;
    private long tick;

    private ref BallComponent Ball => ref world.Get<BallComponent>(ball);
    private ref RoundComponent Round => ref world.Get<RoundComponent>(round);
    private BallState State { get => Ball.State; set => Ball.State = value; }

    internal BreakoutWorld(World world, PhysicsSettings physics, GameplaySettings gameplay, ExecutionSettings execution)
    {
        this.world = world;
        this.gameplay = gameplay;
        brickAlive = new bool[gameplay.Breakout?.Bricks.Count ?? 0];
        ball = world.Create(new BallComponent());
        round = world.Create(new RoundComponent());
        for (int i = 0; i < brickAlive.Length; i++)
            world.Create(new BrickComponent { State = new(i, gameplay.Breakout!.Bricks[i], true) });
        RestoreRoundState();
        native = new(physics, gameplay, execution.PhysicsWorkerCount);
        CaptureBricks();
    }

    private BallState InitialState() => new(gameplay.Ball.Spawn, default, gameplay.Ball.InitialAim, BallPhase.Aiming, false);
    private void ClearShotFacts()
    {
        Ball.LeftCatchZone = Ball.ContactSinceLaunch = false;
        Round.BouncesWithoutBrick = 0;
    }
    private void RestoreRoundState()
    {
        Round.RemainingBricks = brickAlive.Length;
        Round.BallsRemaining = gameplay.Breakout?.StartingBalls ?? 0;
        Round.Phase = RoundPhase.Playing;
        ClearShotFacts();
        State = InitialState();
    }

    internal WallContact Query(double2 origin, double2 direction) => native.Query(origin, direction);
    public void Dispose() => native.Dispose();

    private void FinishRound(RoundPhase phase, double2 position, double2 aim)
    {
        // Called by ResolveGameplay, after physics and concurrently with brick ECS processing.
        // Brick processing never calls native APIs, so this remains exclusive native access.
        native.Freeze();
        Round.Phase = phase;
        Round.Events |= phase == RoundPhase.Won ? SimulationEvents.Win : SimulationEvents.Loss;
        State = new(position, default, aim, BallPhase.Aiming, false);
    }
    private void LoseBall(double2 position, double2 aim)
    {
        Round.Events |= SimulationEvents.Miss;
        Round.BallsRemaining--;
        ClearShotFacts();
        if (Round.BallsRemaining == 0) FinishRound(RoundPhase.Lost, position, aim);
        else
        {
            native.Reset();
            State = InitialState();
        }
    }

    /// <summary>Exclusive native phase. Commands apply before movement; native events are consumed
    /// completely before destroying bodies. No borrowed native buffer escapes this job.</summary>
    internal void PreparePhysics(SimulationCommand command)
    {
        tick++;
        motion = default;
        Round.Events = SimulationEvents.None;
        if (command.RestartPressed)
        {
            native.Reset();
            native.ResetBricks();
            RestoreRoundState();
            Round.Events = SimulationEvents.Restart;
            CaptureBricks();
            return;
        }
        if (Round.Phase != RoundPhase.Playing) return;

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
                Round.Events |= SimulationEvents.Launch;
                phase = BallPhase.Flying;
                eligible = false;
            }
            else if (eligible)
            {
                native.Freeze();
                Round.Events |= SimulationEvents.Catch;
                ClearShotFacts();
                State = new(state.Position, default, aim, BallPhase.Aiming, false);
                return;
            }
        }
        if (phase != BallPhase.Flying)
        {
            State = new(state.Position, state.Velocity, aim, phase, eligible);
            return;
        }
        var result = native.Step();
        motion = new(true, state, native.Position, native.Velocity, aim, eligible, result);
        CaptureBricks();
    }

    private void CaptureBricks()
    {
        for (int i = 0; i < brickAlive.Length; i++) brickAlive[i] = native.IsBrickAlive(i);
    }

    /// <summary>Writes only the ball/round archetypes. Runs alongside SynchronizeBricks.</summary>
    internal void ResolveGameplay()
    {
        if (!motion.Advanced) return;
        var (advanced, previous, position, velocity, aim, eligible, result) = motion;
        if (gameplay.Breakout is not { } rules)
        {
            State = new(position, velocity, aim, BallPhase.Flying, eligible || result.HasContact);
            return;
        }
        Ball.LeftCatchZone |= previous.Position.Y < gameplay.CatchLineY || position.Y < gameplay.CatchLineY;
        Ball.ContactSinceLaunch |= result.HasContact;
        if (result.BricksDestroyed > 0)
        {
            Round.Events |= SimulationEvents.Brick;
            Round.RemainingBricks -= result.BricksDestroyed;
            Round.BouncesWithoutBrick = 0;
            if (Round.RemainingBricks == 0)
            {
                FinishRound(RoundPhase.Won, position, aim);
                return;
            }
        }
        else Round.BouncesWithoutBrick += result.WallContacts;
        eligible = Ball.LeftCatchZone && Ball.ContactSinceLaunch && gameplay.IsInCatchZone(position.Y);
        if (position.Y - gameplay.Ball.Radius > gameplay.Arena.Bottom ||
            (rules.MaxBouncesWithoutBrick > 0 && Round.BouncesWithoutBrick > rules.MaxBouncesWithoutBrick))
        {
            LoseBall(position, aim);
            return;
        }
        State = new(position, velocity, aim, BallPhase.Flying, eligible);
    }

    /// <summary>Arch query over a disjoint brick archetype, concurrent with the ball/round job.
    /// Uses owned flags, not native bodies.
    /// No structural ECS mutation occurs while any query or sibling job is in flight.</summary>
    internal void SynchronizeBricks()
        => world.Query(in brickQuery, (ref BrickComponent brick) =>
            brick.State = brick.State with { Alive = brickAlive[brick.State.Id] });

    internal SimulationSnapshot Publish()
    {
        var bricks = new BrickState[brickAlive.Length];
        world.Query(in brickQuery, (ref BrickComponent brick) => bricks[brick.State.Id] = brick.State);
        return new(State, Round.Events, Round.RemainingBricks, Round.BallsRemaining,
            Round.Phase, Round.BouncesWithoutBrick, tick, bricks);
    }

    private readonly record struct TickMotion(bool Advanced, BallState Previous, double2 Position,
        double2 Velocity, double2 Aim, bool Eligible, NativeArena.StepResult Result);
}
