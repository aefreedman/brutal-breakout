using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.ImGui;
using Ui = Brutal.ImGuiApi.ImGui;

namespace BrutalBreakout.Editor;

/// <summary>Draws inside the inspector-owned ImGui frame. Requests are applied by the host next update.</summary>
public sealed class GameplayControls
{
    public GameplayWindowSettings Settings { get; }
    private float2 physicalPosition, physicalSize, lastScale;

    public InspectorTheme? Theme { get; }
    private ToolDoubleEditor? widthEditor;
    private static readonly ToolHelp WidthHelp = new("Widen play area", "Widen only; restart resets width.", "Slide or Ctrl-click for an exact game-unit width.");
    private static readonly ToolHelp PausedWidthHelp = new("Widen play area", "The current width is retained while paused.", "", "Resume to widen.");
    private static readonly ToolHelp MaximumWidthHelp = new("Widen play area", "The current width is retained.", "", "Maximum width reached; restart resets width.");
    public GameplayControls(GameplayWindowSettings settings) : this(settings, null) { }
    public GameplayControls(GameplayWindowSettings settings, InspectorTheme? theme)
    {
        Theme = theme;
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        physicalPosition = new(settings.Left, settings.Top);
        physicalSize = new(settings.Width, settings.Height);
    }

    public GameplayMode Mode { get; private set; }
    public double Width { get; private set; }
    public ShmupArenaSettings? Arena { get; private set; }
    public double MaximumWidth => Arena?.Width ?? 0;
    public double MinimumWidth { get; private set; }
    public int DrawCount { get; private set; }
    public bool Paused { get; private set; }
    private bool requestedRestart, requestedPause;
    public void RequestRestart() => requestedRestart = true;
    public void RequestPauseToggle() => requestedPause = true;
    private GameplayMode? requestedMode;
    private double? requestedWidth;
    public void Synchronize(GameplayMode mode, double width, ShmupArenaSettings arena, bool paused = false, double? runMinimumWidth = null)
    {
        ArgumentNullException.ThrowIfNull(arena);
        double minimum = runMinimumWidth ?? arena.InitialPlayAreaWidth;
        if (widthEditor is null || MinimumWidth != minimum || MaximumWidth != arena.Width)
            widthEditor = minimum < arena.Width ? ToolDoubleEditor.Slider(minimum, arena.Width, "%.3f") : ToolDoubleEditor.Input("%.17g");
        (Mode, Width, Arena, Paused) = (mode, width, arena, paused);
        MinimumWidth = minimum;
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
    public Action? ToggleDiagnostics { get; set; }
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
            if (Theme is null) Ui.Text(Paused ? "Paused"u8 : "Running"u8);
            else Theme.TextWrapped(Paused ? "Paused"u8 : "Running"u8, Paused ? InspectorTone.Warning : InspectorTone.Success);
            SameLineIfFits(Paused ? "Resume"u8 : "Pause"u8);
            if (Ui.Button(Paused ? "Resume"u8 : "Pause"u8)) RequestPauseToggle();
            SameLineIfFits("Restart"u8);
            if (Ui.Button("Restart"u8)) RequestRestart();
            Ui.SetItemTooltip("Restart resets this run; pause is kept."u8);

            ToolLayout.Heading("Tools"u8);
            if (OpenConfiguration is not null && Ui.Button("Configuration..."u8)) OpenConfiguration();
            if (ToggleDiagnostics is not null)
            {
                SameLineIfFits("Diagnostics"u8);
                if (Ui.Button("Diagnostics"u8)) ToggleDiagnostics();
            }

            ToolLayout.Heading("Session"u8);
            using (var row = ToolLayout.PropertyRow("session.mode"u8, "Mode"u8, Ui.GetFontSize() * 7,
                "Changing mode starts a new run and keeps manual pause."u8))
            {
                if (row.ContentVisible)
                {
                    if (Ui.RadioButton("Breakout"u8, Mode == GameplayMode.Breakout)) RequestMode(GameplayMode.Breakout);
                    SameLineIfFits("Shmup"u8, Ui.GetFrameHeight());
                    if (Ui.RadioButton("Shmup"u8, Mode == GameplayMode.Shmup)) RequestMode(GameplayMode.Shmup);
                }
            }
            Ui.TextWrapped("Changing mode starts a new run."u8);
            if (Mode == GameplayMode.Shmup)
            {
                ToolLayout.Heading("Play area"u8);
                Ui.Text(Encoding.UTF8.GetBytes($"Current width: {Width:F0}"));
                Ui.Text(Encoding.UTF8.GetBytes($"Maximum width: {MaximumWidth:F0}"));
                double width = Width;
                bool enabled = !Paused && Width < MaximumWidth;
                var help = Paused ? PausedWidthHelp : Width >= MaximumWidth ? MaximumWidthHelp : WidthHelp;
                Ui.SetNextItemWidth(-1);
                if (ToolControls.EditDouble("##Widen area"u8, ref width, widthEditor!, enabled, help))
                {
                    double requested = Math.Clamp(width, Width, MaximumWidth);
                    if (requested > Width) RequestWiden(requested);
                }
                if (enabled) Ui.TextWrapped("Widen only; restart resets width."u8);
            }
        }
        finally { Ui.End(); }
    }

    internal static void SameLineIfFits(ReadOnlySpan<byte> label, float extraWidth = 0) =>
        ToolLayout.SameLineIfFits(label, extraWidth);
}
