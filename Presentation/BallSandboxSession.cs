using Brutal.Numerics;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Presentation;

/// <summary>Uniform physical framebuffer fit; cursor coordinates originate in logical window units.</summary>
public readonly record struct ArenaViewport(int WindowWidth, int WindowHeight, int Width, int Height)
{
    public float Scale => MathF.Min(Width / (float)Arena.Width, Height / (float)Arena.Height);
    public float X => (Width - (float)Arena.Width * Scale) / 2;
    public float Y => (Height - (float)Arena.Height * Scale) / 2;
    public bool TryWorld(float2 pointer, out double2 world)
    {
        world = default;
        if (WindowWidth <= 0 || WindowHeight <= 0 || Width <= 0 || Height <= 0 ||
            !float.IsFinite(pointer.X) || !float.IsFinite(pointer.Y)) return false;
        double x = ((double)pointer.X * Width / WindowWidth - X) / Scale;
        double y = ((double)pointer.Y * Height / WindowHeight - Y) / Scale;
        if (x < 0 || x > Arena.Width || y < 0 || y > Arena.Height) return false;
        world = new(x, y);
        return true;
    }
}

/// <summary>Single-threaded host bridge: input edges survive render-only frames, never catch-up ticks.</summary>
public sealed class BallSandboxSession : IDisposable
{
    public BallSimulation Simulation { get; }
    public PhysicsSettings Settings => Simulation.Settings;
    public InputRouter Router { get; } = new();
    private readonly FixedStepClock clock;

    public BallSandboxSession(PhysicsSettings settings)
    {
        clock = new(settings); // Validate managed dependencies before creating the native world.
        Simulation = new(settings);
    }
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private bool disposed;
    private bool active, primary, restart;
    private double2? aim;
    public int TotalTicks { get; private set; }

    public void Dispose()
    {
        Simulation.Dispose(); // Enforces owner-thread destruction, including repeated disposal.
        disposed = true;
    }

    public int Advance(InputSnapshot snapshot, double seconds, ArenaViewport viewport)
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("The session must be advanced on its owner thread.");
        ObjectDisposedException.ThrowIf(disposed, this);
        var input = Router.Route(snapshot);
        if (!input.Active)
        {
            active = primary = restart = false;
            aim = null;
            clock.Reset();
            Router.Reset();
            return 0;
        }
        if (!active) { seconds = 0; active = true; }
        if (input.AimKind == AimKind.Direction) aim = new(input.Aim.X, -input.Aim.Y);
        else if (input.AimKind == AimKind.Pointer && viewport.TryWorld(input.Aim, out var target))
            aim = target - Simulation.State.Position;
        restart |= input.RestartPressed;
        primary = !restart && (primary || input.PrimaryPressed);
        int ticks = clock.Advance(seconds);
        for (int i = 0; i < ticks; i++)
        {
            Simulation.Step(new(aim, primary, restart));
            if (restart) aim = null;
            primary = restart = false;
            TotalTicks++;
        }
        return ticks;
    }
}
