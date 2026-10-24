using BrutalBreakout.Simulation;

namespace Simulation.Tests;

internal static class TestSettings
{
    // Explicit historical fixture, independent of application configuration files.
    internal static PhysicsSettings Baseline { get; } = new(120, 4, 8, 120, 1);
}
