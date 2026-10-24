using BrutalBreakout.Simulation;
using BrutalBreakout.Platform.Input;

internal static class TestSettings
{
    // Explicit deterministic fixture, independent of the user's editable production file.
    internal static PhysicsSettings Baseline { get; } = new(120, 4, 8, 120, 1);
    internal static GameplaySettings Gameplay { get; } = new(
        new(1280, 720, 48, 48, 1232, 672, 16), new(12, 600, 640, 612, 0, -1));
    internal static InputSettings Input { get; } = new(.2);
}
