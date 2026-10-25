using Brutal.Numerics;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Presentation;

/// <summary>Deterministic fake snapshots, never desktop input. Stages only advance after a presentation.</summary>
internal sealed class BallSandboxSmoke : IDisposable
{
    public BallSandboxSession Session { get; }
    public BallSandboxSmoke(PhysicsSettings physics, GameplaySettings gameplay, InputSettings input, ExecutionSettings execution)
    {
        Session = new(physics, gameplay, input, execution);
        try { VerifyConfiguredDeadzone(); }
        catch { Session.Dispose(); throw; }
    }

    private void VerifyConfiguredDeadzone()
    {
        float deadzone = Session.Router.Settings.StickDeadzone;
        var sample = new InputSnapshot { Focused = true, PadConnected = true, PadId = 0 };
        Session.Router.Route(sample);
        var inside = Session.Router.Route(sample with { PadStick = new(deadzone / 2, 0) });
        var outside = Session.Router.Route(sample with { PadStick = new((1 + deadzone) / 2, 0) });
        Require(inside.AimKind == AimKind.None && outside.AimKind == AimKind.Direction &&
            outside.Aim.X == 1 && outside.Aim.Y == 0, "configured input deadzone");
        Session.Router.Reset(); // The probe never advances the world or changes input baselines for the scenario.
        Console.WriteLine(FormattableString.Invariant($"Gameplay input probe PASS: stickDeadzone={deadzone:R}; inside=None; outside=Direction."));
    }
    private enum Stage { Initial, Launch, Travel, Catch, Hold, Release, Relaunch, Reset, Resized, Done }
    private Stage stage;
    private double2 caught;
    private int holds, resizedFrames;
    private bool blockedCatchProved;
    public bool IsInitial => stage == Stage.Initial;
    public bool ResizeRequested => stage == Stage.Resized;
    public bool Completed => stage == Stage.Done;

    public void Dispose() => Session.Dispose();

    public void Advance(ArenaViewport viewport)
    {
        bool blockedProbe = stage == Stage.Travel && !blockedCatchProved &&
            Session.Simulation.State.Position.Y < Session.Simulation.CatchLineY;
        bool primary = blockedProbe || stage is Stage.Launch or Stage.Catch or Stage.Hold or Stage.Relaunch;
        var towardSpawn = Session.Simulation.Gameplay.Ball.Spawn - caught;
        if (towardSpawn == default) towardSpawn = -Session.Simulation.Gameplay.Ball.InitialAim;
        Session.Advance(new InputSnapshot
        {
            Focused = true, KeyboardPrimary = primary, KeyboardRestart = stage == Stage.Reset,
            KeyboardDirection = stage is Stage.Release or Stage.Relaunch ? new((float)towardSpawn.X, (float)-towardSpawn.Y) : default
        }, Session.Settings.StepSeconds * 2, viewport);
        if (blockedProbe)
        {
            Require(Session.Simulation.State.Phase == BallPhase.Flying, "catch blocked above line");
            blockedCatchProved = true;
            Console.WriteLine("Gameplay catch-line blocked PASS: primary above line did not catch.");
        }
    }

    public void Presented(int width, int height, BallSandbox.GeometryEvidence geometry)
    {
        if (geometry.BrickIndices <= 0 || geometry.CatchLineIndices <= 0 || geometry.HudIndices <= 0)
            throw new InvalidOperationException("Gameplay: missing bricks, catch line or HUD geometry.");
        if (geometry.ArenaIndices <= 0 || geometry.BallIndices <= 0)
            throw new InvalidOperationException("Gameplay: empty arena or ball.");
        var s = Session.Simulation.State;
        var game = Session.Simulation.Gameplay;
        const double NativeContactAllowance = 2;
        const double CoordinateRoundoff = .001;
        const double boundsAllowance = NativeContactAllowance + CoordinateRoundoff;
        if (s.Position.X < game.MinX - boundsAllowance || s.Position.X > game.MaxX + boundsAllowance ||
            s.Position.Y < game.MinY - boundsAllowance || s.Position.Y > game.MaxY + boundsAllowance)
            throw new InvalidOperationException("Gameplay: escaped native contact allowance.");
        if (geometry.BackgroundIndices != 6 || (s.Phase == BallPhase.Aiming ? geometry.HeldRingIndices <= 0 : geometry.HeldRingIndices != 0))
            throw new InvalidOperationException("Gameplay: missing background or incorrect held-only ring.");
        if (s.Phase == BallPhase.Aiming && geometry.AimIndices <= 0)
            throw new InvalidOperationException("Gameplay: missing aiming preview.");
        bool ready = true;
        switch (stage)
        {
            case Stage.Initial:
                Require(s.Phase == BallPhase.Aiming && s.Position == game.Ball.Spawn &&
                    s.AimDirection == game.Ball.InitialAim, "initial aim"); break;
            case Stage.Launch:
                Require(s.Phase == BallPhase.Flying && s.Position != game.Ball.Spawn, "launch");
                double speed = Math.Sqrt(s.Velocity.X * s.Velocity.X + s.Velocity.Y * s.Velocity.Y);
                Require(Math.Abs(speed - game.Ball.Speed) <= Math.Max(1, game.Ball.Speed * 1e-5), "configured native launch speed");
                Console.WriteLine(FormattableString.Invariant($"Gameplay launch motion: actualSpeed={speed:R}; configuredSpeed={game.Ball.Speed:R}."));
                break;
            case Stage.Travel:
                ready = s.CatchEligible;
                if (ready)
                {
                    Require(blockedCatchProved && s.Position.Y >= Session.Simulation.CatchLineY, "return below catch line");
                    Require(Session.Simulation.RemainingBricks < game.Breakout!.Bricks.Count, "native brick destruction");
                    Console.WriteLine($"Gameplay bricks PASS: remaining={Session.Simulation.RemainingBricks}; initial={game.Breakout.Bricks.Count}; catchLineY={Session.Simulation.CatchLineY}.");
                }
                if (ready) caught = s.Position;
                break;
            case Stage.Catch:
                Require(s.Phase == BallPhase.Aiming && s.Position == caught, "eligible catch position"); break;
            case Stage.Hold:
                Require(s.Phase == BallPhase.Aiming && s.Position == caught, "held catch position");
                ready = ++holds >= 6; break;
            case Stage.Release:
                Require(s.Phase == BallPhase.Aiming && s.Position == caught, "separate release"); break;
            case Stage.Relaunch:
                // Different configured timesteps can catch at a touching pose. A real new native
                // contact may unlock catch again during this launch tick; phase/movement prove relaunch.
                Require(s.Phase == BallPhase.Flying && s.Position != caught, "separate repress relaunch"); break;
            case Stage.Reset:
                Require(s.Phase == BallPhase.Aiming && s.Position == game.Ball.Spawn &&
                    s.AimDirection == game.Ball.InitialAim && Session.Simulation.RemainingBricks == game.Breakout!.Bricks.Count &&
                    Session.Simulation.BallsRemaining == game.Breakout.StartingBalls, "reset"); break;
            case Stage.Resized:
                Require(s.Phase == BallPhase.Aiming && s.Position == game.Ball.Spawn, "resized reset hold");
                ready = width == 960 && height == 540 && ++resizedFrames >= 6; break;
        }
        if (!ready) return;
        Console.WriteLine($"Gameplay stage: {stage}; ticks={Session.TotalTicks}; phase={s.Phase}; eligible={s.CatchEligible}; position={s.Position.X},{s.Position.Y}; presented={width}x{height}.");
        stage++;
    }

    private static void Require(bool condition, string invariant)
    {
        if (!condition) throw new InvalidOperationException($"Gameplay invariant failed: {invariant}.");
    }
}
