using CrushKit.Brutal.Input;

namespace BrutalBreakout.Platform.Input;

public readonly record struct HostControlIntent(bool TogglePanel, bool TogglePause);

/// <summary>Raw host controls, independent of UI capture and gameplay pause. Device identity and
/// focus changes baseline held buttons using the same published edges as gameplay.</summary>
public sealed class HostControlRouter
{
    private readonly ButtonEdges panel = new(), pause = new();
    public HostControlIntent Route(InputSnapshot sample)
    {
        bool active = sample.Focused && !sample.Minimized;
        var panels = new Dictionary<string, bool> { ["keyboard"] = sample.KeyboardDebugToggle };
        var pauses = new Dictionary<string, bool>();
        if (sample.PadConnected)
        {
            panels[$"pad:{sample.PadId}"] = sample.PadPanelToggle;
            pauses[$"pad:{sample.PadId}"] = sample.PadPause;
        }
        return new(panel.Update(active, panels), pause.Update(active, pauses));
    }
}
