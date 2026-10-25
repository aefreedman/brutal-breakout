using CrushKit.Brutal.Vulkan;
using Brutal.Numerics;
using BrutalBreakout.Platform.Input;

namespace BrutalBreakout.Presentation.Overlay;

/// <summary>Suppress only captured sources. Host must flush pending session edges when ResetBaselines is true.</summary>
public static class OverlayInputRouting
{
    public static InputSnapshot Filter(InputSnapshot sample, OverlayCapture capture) => sample with
    {
        KeyboardDirection = capture.Keyboard ? default : sample.KeyboardDirection,
        KeyboardPrimary = !capture.Keyboard && sample.KeyboardPrimary,
        KeyboardRestart = !capture.Keyboard && sample.KeyboardRestart,
        MousePrimary = !capture.Mouse && sample.MousePrimary,
        Cursor = capture.Mouse ? new double2(double.NaN, double.NaN) : sample.Cursor,
    };
}
