using CrushKit.Brutal.Vulkan;
using BrutalBreakout.Configuration;
using CrushKit.Brutal.Draw2D;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Presentation.Overlay;

/// <summary>The host's fixed labels and limits are checked before native initialization.</summary>
public static class OverlayHost
{
    public const string Controls = "Space/click/A: launch/catch | R: restart | Start: pause | Back: panel";
    private const string GalleryTitle = "Primitive showcase";
    private const string GalleryDetail = "BRUTAL text / image";

    public static void Validate(OverlaySettings settings, GameplaySettings? gameplay)
    {
        ArgumentNullException.ThrowIfNull(settings);
        int longest = new[] { Controls.Length, GalleryTitle.Length, GalleryDetail.Length,
            $"Balls: {gameplay?.Breakout?.StartingBalls ?? 0} | Bricks: {gameplay?.Breakout?.Bricks.Count ?? 0}".Length,
            "Round: Playing".Length }.Max();
        if (settings.MaxLabels < 3 || settings.MaxLabelCharacters < longest)
            throw new ArgumentException($"Overlay host requires maxLabels >= 3 and maxLabelCharacters >= {longest}.");
    }

    public static OverlayFrame Build(BallSimulation? game, LinearColor tint)
    {
        string state = game?.RoundPhase.ToString() ?? "Showcase";
        return new(new OverlayLabel[]
        {
            new(game is null ? GalleryTitle : $"Balls: {game.BallsRemaining} | Bricks: {game.RemainingBricks}", tint),
            new(game is null ? GalleryDetail : $"Round: {state}", tint),
            new(Controls, tint)
        }, new("", "", 0, 0, 0, 0)); // Inspector diagnostics are supplied by the optional editor assembly.
    }
}
