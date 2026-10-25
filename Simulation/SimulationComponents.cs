namespace BrutalBreakout.Simulation;

// Gameplay data belongs to Breakout. The shared host knows nothing about these components.
internal struct BallComponent
{
    internal BallState State;
    internal bool LeftCatchZone;
    internal bool ContactSinceLaunch;
}

internal struct RoundComponent
{
    internal SimulationEvents Events;
    internal int RemainingBricks;
    internal int BallsRemaining;
    internal RoundPhase Phase;
    internal long BouncesWithoutBrick;
}

internal struct BrickComponent
{
    internal BrickState State;
}

/// <summary>An owned, immutable publication. Safe to retain while another tick runs.</summary>
public sealed class SimulationSnapshot
{
    private readonly BrickState[] bricks;
    public BallState State { get; }
    public SimulationEvents Events { get; }
    public int RemainingBricks { get; }
    public int BallsRemaining { get; }
    public RoundPhase RoundPhase { get; }
    public long BouncesWithoutBrick { get; }
    public long Tick { get; }
    public ReadOnlySpan<BrickState> Bricks => bricks;

    internal SimulationSnapshot(BallState state, SimulationEvents events, int remainingBricks,
        int ballsRemaining, RoundPhase roundPhase, long bouncesWithoutBrick, long tick, BrickState[] bricks)
        => (State, Events, RemainingBricks, BallsRemaining, RoundPhase, BouncesWithoutBrick, Tick, this.bricks) =
            (state, events, remainingBricks, ballsRemaining, roundPhase, bouncesWithoutBrick, tick, bricks);
}
