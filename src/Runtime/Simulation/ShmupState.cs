using Brutal.Numerics;

namespace BrutalBreakout.Simulation;

/// <summary>Coalesced gameplay facts for one synchronous command, independent of audio/presentation.</summary>
[Flags]
public enum ShmupEvents
{
    None = 0, Fire = 1, BrickHit = 2, BrickDestroyed = 4,
    PlayerDamaged = 8, Defeat = 16, Restart = 32
}

/// <summary>Movement is analog, clamped to unit length. Neutral aim retains last valid aim.
/// Restart wins over other input. Commands are consumed synchronously.</summary>
public readonly record struct ShmupCommand(double2 MoveDirection = default, double2? AimDirection = null,
    bool FireHeld = false, bool RestartPressed = false);

/// <summary>Query-only instantaneous projectile path. A miss with positive Distance ends at the screen exit;
/// a zero-distance miss is the conservative ambiguous/overlap fallback.</summary>
public readonly record struct AimPreviewState(WallContact Hit, WallContact NextHit);

public readonly record struct ShmupBallState(int Id, long Generation, double2 Position, double2 Velocity, double Radius, int WallHits);
public readonly record struct ShmupBrickState(int Id, long Generation, double2 Position, double Size, int Health,
    ShmupBrickShape Shape = ShmupBrickShape.Square, int QuarterTurns = 0, int InitialHealth = 0);

/// <summary>Owned immutable publication. Id+Generation identifies a life; ids alone may be reused.</summary>
public sealed class ShmupSnapshot
{
    private readonly ShmupBallState[] balls;
    private readonly ShmupBrickState[] bricks;
    public double Width { get; }
    public double Height { get; }
    public double2 PlayerPosition { get; }
    public double2 AimDirection { get; }
    public AimPreviewState AimPreview { get; }
    public int PlayerHealth { get; }
    public int PlayerMaximumHealth { get; }
    public bool IsDefeated => PlayerHealth == 0;
    public double TimeSeconds { get; }
    public double BallSpeed { get; }
    public double DescentSpeed { get; }
    public double RowInterval { get; }
    public double InvulnerabilityRemaining { get; }
    public long Tick { get; }
    public long RowsSpawned { get; }
    public long DroppedBalls { get; }
    public long DroppedBricks { get; }
    public int EntityCount { get; }
    public int BodyCount { get; }
    public ReadOnlySpan<ShmupBallState> Balls => balls;
    public ReadOnlySpan<ShmupBrickState> Bricks => bricks;
    internal ShmupSnapshot(double width, double height, double2 playerPosition, double2 aimDirection, int playerHealth,
        double timeSeconds, double ballSpeed, double descentSpeed, double rowInterval, double invulnerabilityRemaining,
        long tick, long rowsSpawned, long droppedBalls, long droppedBricks, int entityCount, int bodyCount,
        ShmupBallState[] balls, ShmupBrickState[] bricks, AimPreviewState aimPreview = default, int playerMaximumHealth = 0)
    {
        PlayerMaximumHealth = playerMaximumHealth > 0 ? playerMaximumHealth : Math.Max(1, playerHealth);
        AimPreview = aimPreview;
        (Width, Height, PlayerPosition, AimDirection, PlayerHealth, TimeSeconds, BallSpeed, DescentSpeed, RowInterval,
            InvulnerabilityRemaining, Tick, RowsSpawned, DroppedBalls, DroppedBricks, EntityCount, BodyCount, this.balls, this.bricks) =
            (width, height, playerPosition, aimDirection, playerHealth, timeSeconds, ballSpeed, descentSpeed, rowInterval,
            invulnerabilityRemaining, tick, rowsSpawned, droppedBalls, droppedBricks, entityCount, bodyCount, balls, bricks);
    }
}

internal struct ShmupBallComponent
{
    internal bool Active;
    internal long Generation;
    internal int WallHits;
}
internal struct ShmupBrickComponent
{
    internal ShmupBrickShape Shape;
    internal int QuarterTurns;
    internal bool Active;
    internal long Generation;
    internal int Health, InitialHealth;
}
