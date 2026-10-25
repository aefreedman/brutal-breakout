using System.Text;
using BrutalBreakout.Presentation;
using Ui = Brutal.ImGuiApi.ImGui;

namespace BrutalBreakout.Editor;

/// <summary>Draws inside the inspector-owned ImGui frame. Requests are applied by the host next update.</summary>
public sealed class GameplayControls
{
    public GameplayMode Mode { get; private set; }
    public double Width { get; private set; }
    public double MaximumWidth { get; private set; }
    public int DrawCount { get; private set; }
    private GameplayMode? requestedMode;
    private double? requestedWidth;
    public void Synchronize(GameplayMode mode, double width, double maximumWidth) =>
        (Mode, Width, MaximumWidth) = (mode, width, maximumWidth);
    public (GameplayMode? Mode, double? Width) Drain()
    {
        var result = (requestedMode, requestedWidth);
        requestedMode = null; requestedWidth = null;
        return result;
    }
    public void RequestMode(GameplayMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        requestedMode = mode;
        requestedWidth = null;
    }
    public void RequestWiden(double width)
    {
        if (Mode != GameplayMode.Shmup || !double.IsFinite(width) || width < Width || width > MaximumWidth)
            throw new ArgumentOutOfRangeException(nameof(width));
        requestedWidth = width;
    }
    public void Draw()
    {
        DrawCount++;
        if (Ui.Begin("Gameplay"u8))
        {
            if (Ui.Button("Breakout"u8)) RequestMode(GameplayMode.Breakout);
            if (Ui.Button("Shmup"u8)) RequestMode(GameplayMode.Shmup);
            if (Mode == GameplayMode.Shmup)
            {
                float width = (float)Width;
                if (Ui.SliderFloat("Widen area"u8, ref width, (float)Width, (float)MaximumWidth))
                    RequestWiden(Math.Clamp(width, Width, MaximumWidth));
                Ui.Text(Encoding.UTF8.GetBytes($"Width {Width:F1} / {MaximumWidth:F1}; restart resets width"));
            }
        }
        Ui.End();
    }
}
