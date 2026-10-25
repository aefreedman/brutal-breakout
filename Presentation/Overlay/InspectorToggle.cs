using BrutalBreakout.Platform.Input;

namespace BrutalBreakout.Presentation.Overlay;

/// <summary>Host-level raw F1 edge, before ImGui capture. Focus regain baselines held keys.</summary>
public sealed class InspectorToggle(bool initiallyVisible)
{
    private bool initialized, previousDown;
    public bool Visible { get; private set; } = initiallyVisible;

    public bool Update(InputSnapshot sample)
    {
        if (!sample.Focused || sample.Minimized)
        {
            initialized = previousDown = false;
            return Visible;
        }
        bool down = sample.KeyboardDebugToggle;
        if (initialized && down && !previousDown) Visible = !Visible;
        previousDown = down;
        initialized = true;
        return Visible;
    }
}
