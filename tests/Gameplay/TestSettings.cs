using BrutalBreakout.Simulation;

internal static class TestSettings
{
    // Explicit deterministic fixture, independent of the user's editable production file.
    internal static PhysicsSettings Baseline { get; } = new(120, 4, 8, 120, 1);
}
