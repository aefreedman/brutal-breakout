using CrushKit.Brutal.Vulkan;
using BrutalBreakout.Configuration;
using CrushKit.Brutal.Draw2D;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Presentation.Overlay;

/// <summary>The host's fixed labels and limits are checked before native initialization.</summary>
public static class OverlayHost
{
    public const string Controls = "Space/click/A: launch/catch | R/Start: restart | F1: debug";
    private const string GalleryTitle = "Primitive showcase";
    private const string GalleryDetail = "BRUTAL text / Stb image / ImGui";

    public static void Validate(OverlaySettings settings, GameplaySettings? gameplay)
    {
        ArgumentNullException.ThrowIfNull(settings);
        int longest = new[] { Controls.Length, GalleryTitle.Length, GalleryDetail.Length,
            $"Balls: {gameplay?.Breakout?.StartingBalls ?? 0} | Bricks: {gameplay?.Breakout?.Bricks.Count ?? 0}".Length,
            "Round: Playing".Length }.Max();
        if (settings.MaxLabels < 3 || settings.MaxLabelCharacters < longest)
            throw new ArgumentException($"Overlay host requires maxLabels >= 3 and maxLabelCharacters >= {longest}.");
    }

    public static OverlayFrame Build(BallSimulation? game, LinearColor tint, double milliseconds,
        int shapeFrames, long vertices, long indices)
    {
        string state = game?.RoundPhase.ToString() ?? "Showcase";
        return new(new OverlayLabel[]
        {
            new(game is null ? GalleryTitle : $"Balls: {game.BallsRemaining} | Bricks: {game.RemainingBricks}", tint),
            new(game is null ? GalleryDetail : $"Round: {state}", tint),
            new(Controls, tint)
        }, new(state, $"Balls {game?.BallsRemaining ?? 0}  Bricks {game?.RemainingBricks ?? 0}",
            milliseconds, shapeFrames, vertices, indices));
    }
}
