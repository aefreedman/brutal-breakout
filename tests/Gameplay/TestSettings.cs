using BrutalBreakout.Simulation;
using BrutalBreakout.Platform.Input;

internal static class TestSettings
{
    internal static BrutalBreakout.Presentation.ColorScheme Colors { get; } = new("test",
        new(1, 1, 1), BrutalBreakout.Rendering.Shapes.LinearColor.FromSrgb(.25f, .38f, .48f),
        BrutalBreakout.Rendering.Shapes.LinearColor.FromSrgb(1, .75f, .25f),
        BrutalBreakout.Rendering.Shapes.LinearColor.FromSrgb(.2f, .9f, .85f), new(0, 0, 1),
        BrutalBreakout.Rendering.Shapes.LinearColor.FromSrgb(.9f, .94f, 1),
        new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(0, 0, 1), new(0, 1, 0), new(1, 0, 0));
    // Explicit deterministic fixture, independent of the user's editable production file.
    internal static PhysicsSettings Baseline { get; } = new(120, 4, 8, 120, 1);
    internal static GameplaySettings Gameplay { get; } = new(
        new(1280, 720, 48, 48, 1232, 672, 16), new(12, 600, 640, 612, 0, -1), null);
    internal static InputSettings Input { get; } = new(.2);
}
