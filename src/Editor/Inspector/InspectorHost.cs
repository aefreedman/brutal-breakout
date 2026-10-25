using BrutalBreakout.Presentation;
using BrutalBreakout.Presentation.Overlay;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Vulkan;

namespace BrutalBreakout.Editor;

/// <summary>Game-specific read-only inspector facts and its control hint; never part of runtime HUD code.</summary>
public static class InspectorHost
{
    public static void Validate(OverlaySettings settings)
    {
        if (settings.MaxLabelCharacters < Math.Max(OverlayHost.Controls.Length, ShmupPresentation.Controls.Length) + " | F1: debug".Length)
            throw new ArgumentException("Overlay maxLabelCharacters cannot fit the editor control hint.");
    }

    public static OverlayFrame Compose(OverlayFrame frame, BallSimulation? game, double milliseconds,
        int shapeFrames, long vertices, long indices, ShmupSnapshot? shmup = null, bool paused = false)
    {
        var labels = frame.Labels.ToArray();
        labels[2] = labels[2] with { Text = (shmup is null ? OverlayHost.Controls : ShmupPresentation.Controls) + " | F1: debug" };
        string state = shmup is not null ? $"Shmup / {(shmup.IsDefeated ? "Defeated" : paused ? "Paused" : "Running")}" :
            game is not null ? $"Breakout / {game.RoundPhase}" : "Showcase";
        if (paused && (shmup is null || shmup.IsDefeated)) state += " / Paused";
        string detail = shmup is not null
            ? $"HP {shmup.PlayerHealth}  Balls {shmup.Balls.Length}  Bricks {shmup.Bricks.Length} | " +
                $"Entities {shmup.EntityCount} Bodies {shmup.BodyCount} Dropped {shmup.DroppedBalls}/{shmup.DroppedBricks}"
            : $"Balls {game?.BallsRemaining ?? 0}  Bricks {game?.RemainingBricks ?? 0}";
        return new(labels, new(state, detail,
            milliseconds, shapeFrames, vertices, indices));
    }
}
