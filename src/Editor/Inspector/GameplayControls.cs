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
    public bool Paused { get; private set; }
    private bool requestedRestart, requestedPause;
    public void RequestRestart() => requestedRestart = true;
    public void RequestPauseToggle() => requestedPause = true;
    private GameplayMode? requestedMode;
    private double? requestedWidth;
    public void Synchronize(GameplayMode mode, double width, double maximumWidth, bool paused = false) =>
        (Mode, Width, MaximumWidth, Paused) = (mode, width, maximumWidth, paused);
    public (GameplayMode? Mode, double? Width, bool Restart, bool TogglePause) Drain()
    {
        var result = (requestedMode, requestedWidth, requestedRestart, requestedPause);
        requestedMode = null; requestedWidth = null;
        requestedRestart = requestedPause = false;
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
            if (Ui.Button("Restart"u8)) RequestRestart();
            if (Ui.Button(Paused ? "Resume"u8 : "Pause"u8)) RequestPauseToggle();
            Ui.Text(Paused ? "Paused"u8 : "Running"u8);
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
