using BrutalBreakout.Simulation;

namespace Simulation.Tests;

internal static class TestSettings
{
    internal static GameplaySettings Gameplay { get; } = new(new(1280, 720, 48, 48, 1232, 672, 16), new(12, 600, 640, 612, 0, -1), null);
    internal static BrutalBreakout.Platform.Input.InputSettings Input { get; } = new(.2);
    // Explicit historical fixture, independent of application configuration files.
    internal static PhysicsSettings Baseline { get; } = new(120, 4, 8, 120, 1);
}
