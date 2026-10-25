using Brutal.ImGuiApi;
using Brutal.Numerics;
using BrutalBreakout.Editor;
using CrushKit.Brutal.ImGui;
using Xunit;
using Ui = Brutal.ImGuiApi.ImGui;

namespace BrutalBreakout.Media.Tests;

/// <summary>Actual native CPU frames only; no platform backend, OS window or GPU.</summary>
[Collection("Native inspector font")]
public sealed class GameplayControlsNativeTests
{
    [Theory]
    [InlineData(960, 540, 1f)]
    [InlineData(1280, 720, 1f)]
    [InlineData(960, 540, 2f)]
    public void BoundsSurviveHiddenFramesContextRecreationAndDpi(int width, int height, float scale)
    {
        var loaded = InspectorConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "inspector.json"));
        var controls = new GameplayControls(loaded.GameplayWindow);
        WithContext(width, height, 1, () =>
        {
            var initial = Frame(controls);
            Assert.Equal(new float2(16), initial.Position);
            Assert.Equal(new float2(360, 420), initial.Size);
            // Native window APIs stand in for dragging/resizing; no private managed layout state is changed.
            Frame(controls, () => { Ui.SetWindowPos(new float2(90, 45)); Ui.SetWindowSize(new float2(300, 360)); });
            Frame(controls); // observes the native interaction
            Ui.NewFrame(); Ui.EndFrame(); // hidden: callback is not invoked
            var reopened = Frame(controls);
            Assert.Equal(new float2(90, 45), reopened.Position);
            Assert.Equal(new float2(300, 360), reopened.Size);
            var io = Ui.GetIO();
            io.DisplayFramebufferScale = new float2(2);
            io.DisplaySize = new float2(width / 2f, height / 2f);
            var dpiChanged = Frame(controls);
            Assert.Equal(new float2(300, 360), dpiChanged.Size * 2);
            io.DisplayFramebufferScale = new float2(1);
            io.DisplaySize = new float2(width, height);
            Frame(controls);
        });
        WithContext(width, height, scale, () =>
        {
            var recreated = Frame(controls);
            // Native ImGui rounds logical coordinates to whole pixels at a DPI transition.
            Assert.InRange(Math.Abs(recreated.Position.X * scale - 90), 0, scale);
            Assert.InRange(Math.Abs(recreated.Position.Y * scale - 45), 0, scale);
            Assert.Equal(new float2(300, 360), recreated.Size * scale);
            Frame(controls, () => { Ui.SetWindowPos(new float2(-100, 900)); Ui.SetWindowSize(new float2(2000)); });
            var clamped = Frame(controls);
            Assert.True(clamped.Position.X >= 0 && clamped.Position.Y >= 0);
            Assert.True(clamped.Position.X + clamped.Size.X <= width / scale + .01f);
            Assert.True(clamped.Position.Y + clamped.Size.Y <= height / scale + .01f);
        });
    }

    [Fact]
    public void ActualRadioClickSelectsModeAndSelectedClickDoesNotRestart()
    {
        WithContext(960, 540, 1, () =>
        {
            var controls = new GameplayControls(new(16, 16, 360, 420));
            controls.Synchronize(BrutalBreakout.Presentation.GameplayMode.Breakout, 432, new BrutalBreakout.Simulation.ShmupArenaSettings(1280, 720, 432));
            Frame(controls); Frame(controls);
            var point = FindClick(controls, r => r.Mode == BrutalBreakout.Presentation.GameplayMode.Shmup);
            Assert.Equal(BrutalBreakout.Presentation.GameplayMode.Breakout, controls.Mode);
            controls.Synchronize(BrutalBreakout.Presentation.GameplayMode.Shmup, 432, new BrutalBreakout.Simulation.ShmupArenaSettings(1280, 720, 432));
            Click(controls, point);
            var selected = controls.Drain();
            Assert.Null(selected.Mode);
            Assert.False(selected.Restart);
        });
    }

    [Fact]
    public void PausedSliderCannotQueueWidthButResumeCanAndNarrowLayoutWraps()
    {
        WithContext(960, 540, 1, () =>
        {
            var controls = new GameplayControls(new(16, 16, 360, 420));
            controls.Synchronize(BrutalBreakout.Presentation.GameplayMode.Shmup, 432, new BrutalBreakout.Simulation.ShmupArenaSettings(1280, 720, 432));
            Frame(controls); Frame(controls);
            var point = FindClick(controls, r => r.Width is > 432);
            controls.Synchronize(BrutalBreakout.Presentation.GameplayMode.Shmup, 432, new BrutalBreakout.Simulation.ShmupArenaSettings(1280, 720, 432), true);
            Click(controls, point);
            Assert.Null(controls.Drain().Width);
            controls.Synchronize(BrutalBreakout.Presentation.GameplayMode.Shmup, 432, new BrutalBreakout.Simulation.ShmupArenaSettings(1280, 720, 432));
            Click(controls, point);
            Assert.NotNull(controls.Drain().Width);

            var arena = new BrutalBreakout.Simulation.ShmupArenaSettings(1280, 720, 432);
            controls.Synchronize(BrutalBreakout.Presentation.GameplayMode.Shmup, 432, arena);
            Assert.Same(arena, controls.Arena);
            var io = Ui.GetIO();
            io.AddMousePosEvent(200, point.Y); Frame(controls);
            io.AddMouseButtonEvent(0, true); Frame(controls);
            double requested = Assert.IsType<double>(controls.Drain().Width);
            Assert.InRange(requested, 433, 1279);
            // Host applies the request while the same pointer remains held: the slider's
            // immutable domain must not ratchet a stationary target toward the maximum.
            controls.Synchronize(BrutalBreakout.Presentation.GameplayMode.Shmup, requested, arena);
            for (int i = 0; i < 5; i++)
            {
                Frame(controls);
                var next = controls.Drain().Width;
                Assert.True(next is null || next <= requested + .01);
            }
            io.AddMouseButtonEvent(0, false); Frame(controls);
            controls.Synchronize(BrutalBreakout.Presentation.GameplayMode.Shmup, arena.Width, arena);
            Click(controls, point);
            Assert.Null(controls.Drain().Width);
            float wideBottom = Frame(controls).ContentBottom;
            Frame(controls, () => Ui.SetWindowSize(new float2(180, 420)));
            float narrowBottom = Frame(controls).ContentBottom;
            Assert.True(narrowBottom > wideBottom, "Narrow controls/help must wrap, not extend horizontally.");
        });
    }

    private static float2 FindClick(GameplayControls controls,
        Func<(BrutalBreakout.Presentation.GameplayMode? Mode, double? Width, bool Restart, bool TogglePause), bool> matches)
    {
        // Discover native hit targets rather than freezing pixel coordinates to font metrics.
        for (int y = 60; y < 390; y += 9)
            for (int x = 30; x < 350; x += 18)
            {
                var point = new float2(x, y);
                Click(controls, point);
                if (matches(controls.Drain())) return point;
            }
        throw new InvalidOperationException("Native widget was not clickable.");
    }

    private static void Click(GameplayControls controls, float2 point)
    {
        var io = Ui.GetIO();
        io.AddMousePosEvent(point.X, point.Y);
        Frame(controls);
        io.AddMouseButtonEvent(0, true); Frame(controls);
        io.AddMouseButtonEvent(0, false); Frame(controls);
    }

    private static (float2 Position, float2 Size, float ContentBottom) Frame(GameplayControls controls, Action? edit = null)
    {
        Ui.NewFrame();
        int before = Ui.GetCurrentContext().CurrentWindowStack.Count;
        controls.Draw();
        Assert.Equal(before, Ui.GetCurrentContext().CurrentWindowStack.Count);
        Assert.True(Ui.GetCurrentContext().WithinFrameScope);
        // Re-enter only to inspect the same native window and optionally simulate a user move/resize.
        Ui.Begin("Gameplay"u8);
        var result = (Ui.GetWindowPos(), Ui.GetWindowSize(), Ui.GetCursorScreenPos().Y);
        try { edit?.Invoke(); }
        finally { Ui.End(); }
        Ui.Render();
        Assert.False(Ui.GetCurrentContext().WithinFrameScope);
        return result;
    }

    private static void WithContext(int width, int height, float scale, Action test)
    {
        var previous = Ui.GetCurrentContext();
        var context = Ui.CreateContext();
        try
        {
            var io = Ui.GetIO(); io.IniFilename = default; io.LogFilename = default;
            io.DisplaySize = new float2(width / scale, height / scale);
            io.DisplayFramebufferScale = new float2(scale); io.DeltaTime = 1f / 60;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var settings = InspectorConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "inspector.json")).Settings;
            InspectorFont.Load(io, settings.FontPath, settings.FontSize);
            Ui.GetStyle().FontSizeBase = InspectorFont.LogicalSize(settings.FontSize, io.DisplayFramebufferScale);
            test();
        }
        finally { Ui.DestroyContext(context); Ui.SetCurrentContext(previous); }
    }
}
