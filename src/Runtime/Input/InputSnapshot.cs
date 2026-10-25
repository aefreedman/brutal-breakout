using Brutal.Numerics;

namespace BrutalBreakout.Platform.Input;

/// <summary>A single synchronous sample. PadConnected means a standardized mapped pad, not a raw joystick.
/// Keyboard/stick directions use +Y up; Cursor is unmodified GLFW logical-window coordinates (+Y down).</summary>
public readonly record struct InputSnapshot
{
    public bool Focused { get; init; }
    public bool Minimized { get; init; }
    public float2 KeyboardDirection { get; init; }
    public bool KeyboardPrimary { get; init; }
    public bool KeyboardRestart { get; init; }
    /// <summary>Raw host-level F1 state; not a gameplay action and never filtered through ImGui capture.</summary>
    public bool KeyboardDebugToggle { get; init; }
    public bool MousePrimary { get; init; }
    public double2 Cursor { get; init; }
    public bool PadConnected { get; init; }
    public int PadId { get; init; }
    public float2 PadStick { get; init; }
    public bool PadPrimary { get; init; }
    public bool PadRestart { get; init; }
}

public enum AimKind { None, Direction, Pointer }

/// <summary>None preserves the game's last aim. Pointer is logical-window coordinates, NOT framebuffer/world.
/// Primary and restart are one-shot edges; integration owns tick latching and restart precedence.</summary>
public readonly record struct InputIntent(AimKind AimKind, float2 Aim, bool PrimaryPressed,
    bool RestartPressed, bool Active, int? PadId);
