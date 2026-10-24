using Brutal.Numerics;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Presentation;

/// <summary>Deterministic fake snapshots, never desktop input. Stages only advance after a presentation.</summary>
internal sealed class BallSandboxSmoke : IDisposable
{
    public BallSandboxSession Session { get; } = new();
    private enum Stage { Initial, Launch, Travel, Catch, Hold, Release, Relaunch, Reset, Resized, Done }
    private Stage stage;
    private double2 caught;
    private int holds, resizedFrames;
    public bool IsInitial => stage == Stage.Initial;
    public bool ResizeRequested => stage == Stage.Resized;
    public bool Completed => stage == Stage.Done;

    public void Dispose() => Session.Dispose();

    public void Advance(ArenaViewport viewport)
    {
        bool primary = stage is Stage.Launch or Stage.Catch or Stage.Hold or Stage.Relaunch;
        Session.Advance(new InputSnapshot
        {
            Focused = true, KeyboardPrimary = primary, KeyboardRestart = stage == Stage.Reset,
            KeyboardDirection = stage is Stage.Release or Stage.Relaunch ? new(0, -1) : default
        }, 1.0 / 60, viewport);
    }

    public void Presented(int width, int height, BallSandbox.GeometryEvidence geometry)
    {
        if (geometry.ArenaIndices <= 0 || geometry.BallIndices <= 0)
            throw new InvalidOperationException("Gameplay: empty arena or ball.");
        var s = Session.Simulation.State;
        const double NativeContactAllowance = 2;
        const double CoordinateRoundoff = .001;
        const double boundsAllowance = NativeContactAllowance + CoordinateRoundoff;
        if (s.Position.X < Arena.MinX - boundsAllowance || s.Position.X > Arena.MaxX + boundsAllowance ||
            s.Position.Y < Arena.MinY - boundsAllowance || s.Position.Y > Arena.MaxY + boundsAllowance)
            throw new InvalidOperationException("Gameplay: escaped native contact allowance.");
        if (s.Phase == BallPhase.Aiming && geometry.AimIndices <= 0)
            throw new InvalidOperationException("Gameplay: missing aiming preview.");
        bool ready = true;
        switch (stage)
        {
            case Stage.Initial:
                Require(s.Phase == BallPhase.Aiming && s.AimDirection.Y == -1, "initial aim"); break;
            case Stage.Launch:
                Require(s.Phase == BallPhase.Flying && !s.CatchEligible, "launch"); break;
            case Stage.Travel:
                ready = s.CatchEligible;
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
                // This measured straight-up scenario has no new native begin/hit event on relaunch.
                // Other contact-point relaunches can legitimately regain eligibility immediately.
                Require(s.Phase == BallPhase.Flying && !s.CatchEligible && s.Position != caught, "separate repress relaunch"); break;
            case Stage.Reset:
                Require(s.Phase == BallPhase.Aiming && s.Position == new double2(640, 612) &&
                    s.AimDirection == new double2(0, -1), "reset"); break;
            case Stage.Resized:
                Require(s.Phase == BallPhase.Aiming && s.Position == new double2(640, 612), "resized reset hold");
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
