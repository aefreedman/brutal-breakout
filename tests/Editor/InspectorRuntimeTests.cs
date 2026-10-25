using CrushKit.Brutal.Vulkan;
using CrushKit.Brutal.ImGui;
using Brutal.Numerics;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Presentation.Overlay;
using Xunit;

namespace BrutalBreakout.Media.Tests;

public sealed class InspectorRuntimeTests
{
    private static InputSnapshot Sample(bool down = false, bool focused = true, bool minimized = false) =>
        new() { Focused = focused, Minimized = minimized, KeyboardDebugToggle = down };

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RuntimeF1UsesPressEdgesAndDoesNotRepeatWhileHeld(bool seed)
    {
        var toggle = new InspectorToggle(seed);
        Assert.Equal(seed, toggle.Update(Sample()));
        Assert.Equal(!seed, toggle.Update(Sample(true)));
        Assert.Equal(!seed, toggle.Update(Sample(true)));
        Assert.Equal(!seed, toggle.Update(Sample()));
        Assert.Equal(seed, toggle.Update(Sample(true)));
    }

    [Fact]
    public void HeldF1AtStartupFocusRegainAndRestoreIsBaselined()
    {
        var toggle = new InspectorToggle(false);
        Assert.False(toggle.Update(Sample(true)));
        Assert.False(toggle.Update(Sample(true)));
        Assert.False(toggle.Update(Sample(false, focused: false)));
        Assert.False(toggle.Update(Sample(true)));
        Assert.False(toggle.Update(Sample(false, minimized: true)));
        Assert.False(toggle.Update(Sample(true)));
        Assert.False(toggle.Update(Sample()));
        Assert.True(toggle.Update(Sample(true)));
    }

    [Fact]
    public void F1IsHostInputEvenWhenInspectorCapturesKeyboard()
    {
        var toggle = new InspectorToggle(false);
        var captured = new OverlayCapture(true, true, false);
        toggle.Update(Sample());
        var sample = Sample(true) with { KeyboardPrimary = true, KeyboardRestart = true, PadConnected = true, PadPrimary = true };
        Assert.True(toggle.Update(sample)); // Raw input is processed BEFORE filtering, not a gameplay action.
        var filtered = OverlayInputRouting.Filter(sample, captured);
        Assert.True(filtered.KeyboardDebugToggle);
        Assert.False(filtered.KeyboardPrimary);
        Assert.False(filtered.KeyboardRestart);
        Assert.True(filtered.PadPrimary);
        toggle.Update(Sample());
        Assert.False(toggle.Update(sample)); // Captured keyboard can close it too.
    }

    [Fact]
    public void SelectAndStartRemainIndependentWhenPanelCapturesBothSources()
    {
        var toggle = new InspectorToggle(false);
        var host = new HostControlRouter();
        var idle = Sample() with { PadConnected = true };
        toggle.Update(idle); host.Route(idle);
        var both = idle with { PadPanelToggle = true, PadPause = true };
        Assert.True(toggle.Update(both));
        Assert.Equal(new HostControlIntent(true, true), host.Route(both));
        var filtered = OverlayInputRouting.Filter(both, new(true, true, true));
        Assert.True(filtered.PadPanelToggle); Assert.True(filtered.PadPause);
        Assert.True(toggle.Update(both));
        Assert.Equal(default, host.Route(both));
        toggle.Update(idle); host.Route(idle);
        Assert.False(toggle.Update(both));
        Assert.Equal(new HostControlIntent(true, true), host.Route(both));
    }

    [Theory]
    [InlineData(1f)] [InlineData(1.25f)] [InlineData(1.5f)] [InlineData(2f)]
    public void PhysicalFontSizeIsConvertedOnceAtTheUiBoundary(float scale)
    {
        float logical = InspectorFont.LogicalSize(22, new float2(scale));
        Assert.InRange(logical * scale, 21.99999f, 22.00001f);
    }

    [Fact]
    public void FontPolicyHandlesScaleChangesAndNativeMinimumWithoutAUserDefault()
    {
        Assert.Equal(22, InspectorFont.LogicalSize(22, new(1)));
        Assert.Equal(11, InspectorFont.LogicalSize(22, new(2)));
        Assert.Equal(1, InspectorFont.LogicalSize(1, new(2))); // Dear ImGui minimum one logical pixel.
        foreach (var scale in new[] { new float2(0), new float2(float.NaN, 1), new float2(1, float.PositiveInfinity) })
            Assert.Throws<ArgumentException>(() => InspectorFont.LogicalSize(22, scale));
    }
}
