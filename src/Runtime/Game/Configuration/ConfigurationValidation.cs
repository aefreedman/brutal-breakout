using BrutalBreakout.Presentation;
using BrutalBreakout.Presentation.Overlay;

namespace BrutalBreakout.Configuration;

/// <summary>The cross-file rules shared by startup and staged replacement runs.</summary>
public static class ConfigurationValidation
{
    public static void Validate(LoadedPhysicsConfiguration? physics, LoadedGameplayConfiguration? gameplay,
        LoadedShmupConfiguration? shmup, LoadedBallTrailConfiguration? trails,
        LoadedPresentationStyleConfiguration? style, LoadedOverlayConfiguration? overlay)
    {
        if (trails is not null && shmup is not null) BallTrailConfiguration.ValidateCapacity(trails, shmup);
        if (style is not null && gameplay is not null) style.Settings.Validate(gameplay.Settings);
        if (physics is not null && gameplay is not null) GameplayConfiguration.ValidatePhysics(gameplay, physics);
        if (physics is not null && shmup is not null) ShmupConfiguration.ValidatePhysics(shmup, physics);
        if (overlay is not null) OverlayHost.Validate(overlay.Settings, gameplay?.Settings);
        if (overlay is not null && shmup is not null && overlay.Settings.MaxLabelCharacters < ShmupPresentation.Controls.Length)
            throw new ArgumentException("Overlay maxLabelCharacters cannot fit shmup controls.");
    }
}
