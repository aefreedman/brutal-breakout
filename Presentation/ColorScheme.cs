using BrutalBreakout.Rendering.Shapes;

namespace BrutalBreakout.Presentation;

/// <summary>Immutable semantic colors, already converted from sRGB to opaque linear RGB.</summary>
public sealed class ColorScheme
{
    public string Name { get; }
    public LinearColor Background { get; }
    public LinearColor BackgroundBottom { get; }
    public LinearColor HeldRing { get; }
    public LinearColor Walls { get; }
    public LinearColor Ball { get; }
    public LinearColor CatchReady { get; }
    public LinearColor CatchLine { get; }
    public LinearColor Aim { get; }
    public LinearColor BrickPrimary { get; }
    public LinearColor BrickSecondary { get; }
    public LinearColor BrickAccent { get; }
    public LinearColor Hud { get; }
    public LinearColor Win { get; }
    public LinearColor Loss { get; }

    public ColorScheme(string name, LinearColor background, LinearColor walls, LinearColor ball,
        LinearColor catchReady, LinearColor catchLine, LinearColor aim, LinearColor brickPrimary,
        LinearColor brickSecondary, LinearColor brickAccent, LinearColor hud, LinearColor win, LinearColor loss, LinearColor backgroundBottom, LinearColor heldRing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Any(char.IsControl)) throw new ArgumentException("Scheme name cannot contain control characters.", nameof(name));
        foreach (var color in new[] { background, walls, ball, catchReady, catchLine, aim,
            brickPrimary, brickSecondary, brickAccent, hud, win, loss, backgroundBottom, heldRing })
        {
            if (!float.IsFinite(color.R) || !float.IsFinite(color.G) || !float.IsFinite(color.B) ||
                color.R < 0 || color.R > 1 || color.G < 0 || color.G > 1 || color.B < 0 || color.B > 1 || color.A != 1)
                throw new ArgumentException("Scheme colors must be opaque finite linear RGB in [0,1].");
        }
        Name = name;
        BackgroundBottom = backgroundBottom; HeldRing = heldRing;
        Background = background; Walls = walls; Ball = ball; CatchReady = catchReady;
        CatchLine = catchLine; Aim = aim; BrickPrimary = brickPrimary; BrickSecondary = brickSecondary;
        BrickAccent = brickAccent; Hud = hud; Win = win; Loss = loss;
    }
}
