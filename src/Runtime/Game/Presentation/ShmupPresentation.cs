using Brutal.Numerics;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;
using CrushKit.Brutal.Vulkan;

namespace BrutalBreakout.Presentation;

/// <summary>Only side boundaries are rendered. Logical geometry shares the collision settings and snapshot.</summary>
public static class ShmupPresentation
{
    public const string Controls = "WASD/LS: move | mouse/RS: aim | Space/click/RT: fire | R/Start: restart";
    public static ShapeFrame Build(ShapeDrawList draw, ShmupSnapshot snapshot, ShmupSettings settings,
        ColorScheme colors, PresentationStyle style, int width, int height, BallTrailHistory? trails = null)
        => Build(draw, snapshot, settings, colors, style, width, height, out _, trails);

    public readonly record struct GeometryEvidence(int AimIndices, int TrailIndices);
    public static ShapeFrame Build(ShapeDrawList draw, ShmupSnapshot snapshot, ShmupSettings settings,
        ColorScheme colors, PresentationStyle style, int width, int height, out GeometryEvidence evidence,
        BallTrailHistory? trails = null)
    {
        var viewport = new ShmupViewport(width, height, width, height, settings.Arena);
        draw.BeginFrame(width, height);
        draw.FillVerticalGradientRect(new(0), new(width, height), colors.Background, colors.BackgroundBottom);
        draw.PushClip(new((int)MathF.Ceiling(viewport.X), (int)MathF.Ceiling(viewport.Y),
            (int)MathF.Floor(viewport.X + (float)settings.Arena.Width * viewport.Scale),
            (int)MathF.Floor(viewport.Y + (float)settings.Arena.Height * viewport.Scale)));
        draw.SetTransform(new float3x2(viewport.Scale, 0, 0, viewport.Scale, viewport.X, viewport.Y));
        float left = (float)((settings.Arena.Width - snapshot.Width) / 2);
        float right = (float)(left + snapshot.Width), h = (float)settings.Arena.Height;
        // Stroke thickness is a presentation detail, not an additional collision inset.
        float wall = (float)style.Hud.BarOutlineWidth;
        draw.FillRect(new(left, 0), new(wall, h), colors.Walls);
        draw.FillRect(new(right - wall, 0), new(wall, h), colors.Walls);
        foreach (var brick in snapshot.Bricks)
            draw.FillRect(new((float)brick.Position.X - (float)brick.Size / 2, (float)brick.Position.Y - (float)brick.Size / 2),
                new((float)brick.Size),
                brick.Health * 2 > settings.Brick.Health ? colors.BrickPrimary : colors.BrickAccent);
        int aimStart = draw.IndexCount;
        if (!snapshot.IsDefeated)
            AimGeometry.Draw(draw, snapshot.PlayerPosition, snapshot.AimDirection, settings.Ball.Radius,
                snapshot.AimPreview.Hit, snapshot.AimPreview.NextHit, style, colors.Aim, drawOpenMiss: true);
        int aimCount = draw.IndexCount - aimStart;
        int trailCount = trails?.Draw(draw, snapshot, colors.Ball) ?? 0;
        evidence = new(aimCount, trailCount);
        foreach (var ball in snapshot.Balls)
            draw.FillDisc(new((float)ball.Position.X, (float)ball.Position.Y), (float)ball.Radius, colors.Ball);
        var player = new float2((float)snapshot.PlayerPosition.X, (float)snapshot.PlayerPosition.Y);
        float radius = (float)settings.Avatar.Radius;
        draw.FillDisc(player, radius, snapshot.IsDefeated ? colors.Loss :
            snapshot.InvulnerabilityRemaining > 0 ? colors.HeldRing : colors.Ball);
        float barWidth = (float)Math.Min(style.Hud.BarMaxWidth, snapshot.Width * style.Hud.BarMaxArenaWidthFraction);
        float barHeight = (float)style.Hud.BarHeight;
        draw.StrokeRect(new(left, h - barHeight), new(barWidth, barHeight), (float)style.Hud.BarOutlineWidth, colors.Hud);
        draw.FillRect(new(left, h - barHeight), new(barWidth * snapshot.PlayerHealth / settings.Avatar.Health, barHeight), colors.Hud);
        draw.PopClip();
        return draw.Finish();
    }
    public static OverlayFrame Overlay(ShmupSnapshot snapshot, LinearColor tint) =>
        new(new OverlayLabel[]
        {
            new($"HP: {snapshot.PlayerHealth} | Balls: {snapshot.Balls.Length} | Bricks: {snapshot.Bricks.Length}", tint),
            new(snapshot.IsDefeated ? "GAME OVER - R/Start to restart" : $"Shmup | Time: {snapshot.TimeSeconds:F1} | Width: {snapshot.Width:F0}", tint),
            new(Controls, tint)
        }, new(snapshot.IsDefeated ? "Defeated" : "Shmup",
            $"Entities {snapshot.EntityCount} Bodies {snapshot.BodyCount} Dropped {snapshot.DroppedBalls}/{snapshot.DroppedBricks}", 0, 0, 0, 0));
}
