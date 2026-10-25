using BrutalBreakout.Platform.Input;

namespace BrutalBreakout.Presentation.Overlay;

/// <summary>Raw host panel controls; focus and device changes baseline held buttons.</summary>
public sealed class InspectorToggle(bool initiallyVisible)
{
    private readonly HostControlRouter router = new();
    public bool Visible { get; private set; } = initiallyVisible;
    public bool Update(InputSnapshot sample)
    {
        if (router.Route(sample).TogglePanel) Visible = !Visible;
        return Visible;
    }
}
