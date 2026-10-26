using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using Ui = Brutal.ImGuiApi.ImGui;

namespace BrutalBreakout.Editor;

/// <summary>Draws inside the inspector-owned ImGui frame. Requests are applied by the host next update.</summary>
public sealed class GameplayControls
{
    public GameplayWindowSettings Settings { get; }
    private float2 physicalPosition, physicalSize, lastScale;

    public GameplayControls(GameplayWindowSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        physicalPosition = new(settings.Left, settings.Top);
        physicalSize = new(settings.Width, settings.Height);
    }

    public GameplayMode Mode { get; private set; }
    public double Width { get; private set; }
    public ShmupArenaSettings? Arena { get; private set; }
    public double MaximumWidth => Arena?.Width ?? 0;
    public int DrawCount { get; private set; }
    public bool Paused { get; private set; }
    private bool requestedRestart, requestedPause;
    public void RequestRestart() => requestedRestart = true;
    public void RequestPauseToggle() => requestedPause = true;
    private GameplayMode? requestedMode;
    private double? requestedWidth;
    public void Synchronize(GameplayMode mode, double width, ShmupArenaSettings arena, bool paused = false)
    {
        ArgumentNullException.ThrowIfNull(arena);
        (Mode, Width, Arena, Paused) = (mode, width, arena, paused);
    }
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
        if (mode == Mode) return; // A selected mode is not a restart command.
        requestedMode = mode;
        requestedWidth = null;
    }
    public void RequestWiden(double width)
    {
        if (Paused) throw new InvalidOperationException("Resume to widen.");
        if (Mode != GameplayMode.Shmup || !double.IsFinite(width) || width < Width || width > MaximumWidth)
            throw new ArgumentOutOfRangeException(nameof(width));
        requestedWidth = width;
    }
    public Action? OpenConfiguration { get; set; }
    public void Draw()
    {
        DrawCount++;
        var io = Ui.GetIO();
        var scale = io.DisplayFramebufferScale;
        var viewport = Ui.GetMainViewport();
        // Once is native per-window lifetime, so it also seeds a recreated context (even if its
        // address is reused). Normal dragging/resizing is never overwritten with stale bounds.
        var condition = lastScale != default && lastScale != scale ? ImGuiCond.Always : ImGuiCond.Once;
        lastScale = scale;
        Ui.SetNextWindowSizeConstraints(new float2(0), viewport.Size);
        Ui.SetNextWindowPos(viewport.Pos + physicalPosition / scale, condition);
        Ui.SetNextWindowSize(physicalSize / scale, condition);
        bool expanded = Ui.Begin("Gameplay"u8, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse);
        try
        {
            var size = Ui.GetWindowSize();
            var position = Ui.GetWindowPos();
            var clampedSize = new float2(Math.Min(size.X, viewport.Size.X), Math.Min(size.Y, viewport.Size.Y));
            var clampedPosition = new float2(
                Math.Clamp(position.X, viewport.Pos.X, viewport.Pos.X + Math.Max(0, viewport.Size.X - clampedSize.X)),
                Math.Clamp(position.Y, viewport.Pos.Y, viewport.Pos.Y + Math.Max(0, viewport.Size.Y - clampedSize.Y)));
            if (size != clampedSize) Ui.SetWindowSize(clampedSize);
            if (position != clampedPosition) Ui.SetWindowPos(clampedPosition);
            physicalPosition = (clampedPosition - viewport.Pos) * scale;
            physicalSize = clampedSize * scale;
            if (!expanded) return;

            Ui.AlignTextToFramePadding();
            Ui.Text(Paused ? "Paused"u8 : "Running"u8);
            SameLineIfFits(Paused ? "Resume"u8 : "Pause"u8);
            if (Ui.Button(Paused ? "Resume"u8 : "Pause"u8)) RequestPauseToggle();
            SameLineIfFits("Restart"u8);
            if (Ui.Button("Restart"u8)) RequestRestart();
            Ui.SetItemTooltip("Restart resets this run; pause is kept."u8);

            if (OpenConfiguration is not null && Ui.Button("Configuration..."u8)) OpenConfiguration();

            Ui.SeparatorText("Mode"u8);
            if (Ui.RadioButton("Breakout"u8, Mode == GameplayMode.Breakout)) RequestMode(GameplayMode.Breakout);
            SameLineIfFits("Shmup"u8, Ui.GetFrameHeight());
            if (Ui.RadioButton("Shmup"u8, Mode == GameplayMode.Shmup)) RequestMode(GameplayMode.Shmup);
            Ui.TextWrapped("Changing mode starts a new run."u8);
            if (Mode == GameplayMode.Shmup)
            {
                Ui.SeparatorText("Play area"u8);
                Ui.Text(Encoding.UTF8.GetBytes($"Current width: {Width:F0}"));
                Ui.Text(Encoding.UTF8.GetBytes($"Maximum width: {MaximumWidth:F0}"));
                Ui.BeginDisabled(Paused || Width >= MaximumWidth);
                try
                {
                    float width = (float)Width;
                    Ui.SetNextItemWidth(-1);
                    if (Ui.SliderFloat("##Widen area"u8, ref width, (float)Arena!.InitialPlayAreaWidth, (float)MaximumWidth))
                    {
                        double requested = Math.Clamp(width, Width, MaximumWidth);
                        if (requested > Width) RequestWiden(requested);
                    }
                }
                finally { Ui.EndDisabled(); }
                Ui.TextWrapped(Paused ? "Resume to widen"u8 : Width >= MaximumWidth
                    ? "Maximum width reached; restart resets width."u8 : "Widen only; restart resets width."u8);
            }
        }
        finally { Ui.End(); }
    }

    internal static void SameLineIfFits(ReadOnlySpan<byte> label, float extraWidth = 0)
    {
        var style = Ui.GetStyle();
        float required = Ui.CalcTextSize(label).X + style.FramePadding.X * 2 + extraWidth;
        // Available region at the previous item's right edge, not the next line's left edge.
        float right = Ui.GetCursorScreenPos().X + Ui.GetContentRegionAvail().X;
        if (Ui.GetItemRectMax().X + style.ItemSpacing.X + required <= right)
            Ui.SameLine();
    }
}
