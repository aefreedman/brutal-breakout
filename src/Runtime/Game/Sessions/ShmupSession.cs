using Brutal.Numerics;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Presentation;

/// <summary>Aspect-fit mapping of the shmup's own immutable logical canvas; never uses breakout geometry.</summary>
public readonly record struct ShmupViewport(int WindowWidth, int WindowHeight, int Width, int Height, ShmupArenaSettings Arena)
{
    public float Scale => MathF.Min(Width / (float)Arena.Width, Height / (float)Arena.Height);
    public float X => (Width - (float)Arena.Width * Scale) / 2;
    public float Y => (Height - (float)Arena.Height * Scale) / 2;
    public bool TryWorld(double2 cursor, out double2 world)
    {
        world = default;
        if (WindowWidth <= 0 || WindowHeight <= 0 || Width <= 0 || Height <= 0 ||
            !double.IsFinite(cursor.X) || !double.IsFinite(cursor.Y)) return false;
        double x = (cursor.X * Width / WindowWidth - X) / Scale;
        double y = (cursor.Y * Height / WindowHeight - Y) / Scale;
        if (x < 0 || y < 0 || x > Arena.Width || y > Arena.Height) return false;
        world = new(x, y);
        return true;
    }
}

/// <summary>Synchronous fixed ticks. Movement/fire are levels; restart survives render-only frames once.
/// Pausing resets clocks and input baselines, not simulation invulnerability or progression.</summary>
public sealed class ShmupSession : IDisposable
{
    public ShmupSimulation Simulation { get; }
    public ShmupSettings Settings { get; }
    public BallTrailHistory? Trails { get; }
    public ShmupInputRouter Router { get; }
    private readonly FixedStepClock clock;
    private readonly ShmupEvents[] tickEvents;
    private int eventCount;
    /// <summary>Drain immediately after Advance; each tick is consumed once, including catch-up ticks.</summary>
    public ReadOnlySpan<ShmupEvents> DrainEvents()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var events = tickEvents.AsSpan(0, eventCount);
        eventCount = 0;
        return events;
    }
    private bool active, restart, disposed;
    private double2? aim;
    public int TotalTicks { get; private set; }
    public bool Paused { get; private set; }
    public void SetPaused(bool paused)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Paused == paused) return;
        Paused = paused;
        active = restart = false;
        aim = null;
        eventCount = 0;
        clock.Reset();
        Router.Reset();
    }
    public ShmupSession(PhysicsSettings physics, ShmupSettings settings, InputSettings input, ExecutionSettings execution,
        BallTrailSettings? trails = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        // Validate and allocate bounded managed history before creating a native world.
        Trails = trails is null ? null : new(trails, settings.Capacity.MaxBalls);
        Router = new(input);
        clock = new(physics);
        tickEvents = new ShmupEvents[physics.MaxCatchUpSteps];
        Simulation = new(physics, settings, execution);
    }
    public int Advance(InputSnapshot snapshot, double seconds, ShmupViewport viewport)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(viewport.Arena, Settings.Arena)) throw new ArgumentException("Viewport must use this session's settings.", nameof(viewport));
        eventCount = 0;
        if (Paused) { Router.Reset(); return 0; }
        Trails?.Synchronize(Simulation.Snapshot);
        var input = Router.Route(snapshot);
        if (!input.Active)
        {
            active = restart = false; aim = null;
            clock.Reset(); Router.Reset(); return 0;
        }
        if (!active) { seconds = 0; active = true; }
        if (input.Aim is { } stick) aim = new(stick.X, -stick.Y);
        else if (input.Pointer is { } cursor && viewport.TryWorld(cursor, out var target))
            aim = target - Simulation.Snapshot.PlayerPosition;
        restart |= input.RestartPressed;
        int ticks = clock.Advance(seconds);
        bool fire = !restart && input.FireHeld;
        for (int i = 0; i < ticks; i++)
        {
            Simulation.Step(new(new(input.Move.X, -input.Move.Y), aim, fire, restart));
            tickEvents[eventCount++] = Simulation.Events;
            if (restart) { aim = null; Router.Reset(); Trails?.Clear(); }
            Trails?.Capture(Simulation.Snapshot);
            restart = false;
            TotalTicks++;
        }
        return ticks;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        eventCount = 0;
        Trails?.Clear();
        Simulation.Dispose();
    }
}

public enum GameplayMode { Breakout, Shmup }

/// <summary>Exactly one live gameplay world. Switching disposes the old run and creates a fresh isolated run.</summary>
public sealed class GameplaySessionHost : IDisposable
{
    private PhysicsSettings physics;
    private GameplaySettings gameplay;
    private ShmupSettings shmup;
    private InputSettings input;
    private ExecutionSettings execution;
    private BallTrailSettings? trails;
    private readonly HostControlRouter controls = new();
    private readonly int owner = Environment.CurrentManagedThreadId;
    private bool restartRequested;
    public bool Paused { get; private set; }
    public GameplayMode Mode { get; private set; }
    public void RequestRestart()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        restartRequested = true;
    }
    public void TogglePause()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Paused = !Paused;
        Breakout?.SetPaused(Paused);
        Shmup?.SetPaused(Paused);
    }
    /// <summary>Call once before UI capture and gameplay update. Restarts replace the run safely
    /// on the host thread, discard pending actions, and retain manual pause and immutable settings.</summary>
    public void UpdateControls(InputSnapshot raw)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (controls.Route(raw).TogglePause) TogglePause();
        if (!restartRequested) return;
        restartRequested = false;
        Switch(Mode);
    }
    public BallSandboxSession? Breakout { get; private set; }
    public ShmupSession? Shmup { get; private set; }
    private bool disposed;
    public GameplaySessionHost(PhysicsSettings physics, GameplaySettings gameplay, ShmupSettings shmup,
        InputSettings input, ExecutionSettings execution, GameplayMode initialMode, BallTrailSettings? trails = null)
    {
        trails?.ValidateCapacity(shmup.Capacity.MaxBalls);
        (this.physics, this.gameplay, this.shmup, this.input, this.execution, this.trails) = (physics, gameplay, shmup, input, execution, trails);
        Switch(initialMode);
    }
    public void Switch(GameplayMode mode)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Replace(mode, physics, gameplay, shmup, input, execution, trails);
    }

    /// <summary>Prepare before commit. Raw host-control latches survive replacement (held Start cannot toggle twice).</summary>
    public void Reconfigure(PhysicsSettings physics, GameplaySettings gameplay, ShmupSettings shmup,
        InputSettings input, ExecutionSettings execution, BallTrailSettings? trails)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("Reconfiguration belongs to the host thread.");
        gameplay.ValidatePhysics(physics);
        shmup.ValidatePhysics(physics);
        trails?.ValidateCapacity(shmup.Capacity.MaxBalls);
        Replace(Mode, physics, gameplay, shmup, input, execution, trails);
    }

    private void Replace(GameplayMode mode, PhysicsSettings physics, GameplaySettings gameplay, ShmupSettings shmup,
        InputSettings input, ExecutionSettings execution, BallTrailSettings? trails)
    {
        BallSandboxSession? nextBreakout = null;
        ShmupSession? nextShmup = null;
        try
        {
            if (mode == GameplayMode.Breakout) nextBreakout = new(physics, gameplay, input, execution);
            else nextShmup = new(physics, shmup, input, execution, trails);
            nextBreakout?.SetPaused(Paused);
            nextShmup?.SetPaused(Paused);
        }
        catch
        {
            nextBreakout?.Dispose(); nextShmup?.Dispose();
            throw;
        }
        var oldBreakout = Breakout; var oldShmup = Shmup;
        (this.physics, this.gameplay, this.shmup, this.input, this.execution, this.trails) =
            (physics, gameplay, shmup, input, execution, trails);
        (Mode, Breakout, Shmup, restartRequested) = (mode, nextBreakout, nextShmup, false);
        try { oldBreakout?.Dispose(); } finally { oldShmup?.Dispose(); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { Breakout?.Dispose(); }
        finally { Shmup?.Dispose(); Breakout = null; Shmup = null; }
    }
}
