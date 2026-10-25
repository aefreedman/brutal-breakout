using BrutalBreakout.Presentation.Overlay;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Vulkan;

namespace BrutalBreakout.Editor;

/// <summary>Game-specific read-only inspector facts and its control hint; never part of runtime HUD code.</summary>
public static class InspectorHost
{
    public static void Validate(OverlaySettings settings)
    {
        if (settings.MaxLabelCharacters < (OverlayHost.Controls + " | F1: debug").Length)
            throw new ArgumentException("Overlay maxLabelCharacters cannot fit the editor control hint.");
    }

    public static OverlayFrame Compose(OverlayFrame frame, BallSimulation? game, double milliseconds,
        int shapeFrames, long vertices, long indices)
    {
        var labels = frame.Labels.ToArray();
        labels[2] = labels[2] with { Text = OverlayHost.Controls + " | F1: debug" };
        return new(labels, new(game?.RoundPhase.ToString() ?? "Showcase",
            $"Balls {game?.BallsRemaining ?? 0}  Bricks {game?.RemainingBricks ?? 0}",
            milliseconds, shapeFrames, vertices, indices));
    }
}
