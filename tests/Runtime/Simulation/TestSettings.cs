using BrutalBreakout.Simulation;

namespace Simulation.Tests;

internal static class TestSettings
{
    internal static BrutalBreakout.Presentation.PresentationStyle Style { get; } = new(.2, .02, 16, .5, 2, .25, .1, 2, 4, .1, .01, 3, 3, new(120, .25, 6, 1, 4, .5, 28, .125, 4));
    internal static BrutalBreakout.Presentation.ColorScheme Colors { get; } = new("test",
        new(1, 1, 1), CrushKit.Brutal.Draw2D.LinearColor.FromSrgb(.25f, .38f, .48f),
        CrushKit.Brutal.Draw2D.LinearColor.FromSrgb(1, .75f, .25f),
        CrushKit.Brutal.Draw2D.LinearColor.FromSrgb(.2f, .9f, .85f), new(0, 0, 1),
        CrushKit.Brutal.Draw2D.LinearColor.FromSrgb(.9f, .94f, 1),
        new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(0, 0, 1), new(0, 1, 0), new(1, 0, 0), new(.1f, .2f, .3f), new(.8f, .7f, .6f), new(1, 1, 1));
    internal static GameplaySettings Gameplay { get; } = new(new(1280, 720, 48, 48, 1232, 672, 16), new(12, 600, 640, 612, 0, -1), null);
    internal static BrutalBreakout.Platform.Input.InputSettings Input { get; } = new(.2);
    // Explicit historical fixture, independent of application configuration files.
    internal static PhysicsSettings Baseline { get; } = new(120, 4, 8, 120, 1);
}
