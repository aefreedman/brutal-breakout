using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using BrutalBreakout.Editor;
using BrutalBreakout.Presentation.Overlay;
using CrushKit.Brutal.Vulkan;
using Xunit;

namespace BrutalBreakout.Media.Tests;

public sealed class InspectorConfigurationTests
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "Config", "overlay.json");
    private static string InspectorPath => Path.Combine(AppContext.BaseDirectory, "Config", "inspector.json");

    [Fact]
    public void SeparateSettingsAreSharedWithTheirConsumers()
    {
        var loaded = OverlayConfiguration.Load(ConfigPath);
        var configured = InspectorConfiguration.Load(InspectorPath);
        using var text = new OverlayText(loaded.Settings);
        using var inspector = new CrushKit.Brutal.ImGui.ImGuiInspector(configured.Settings);
        Assert.Same(configured.Settings, inspector.Settings);
        var controls = new GameplayControls(configured.GameplayWindow);
        Assert.Same(configured.GameplayWindow, controls.Settings);
        Assert.Equal(16, controls.Settings.Left);
        Assert.Equal(16, controls.Settings.Top);
        Assert.Equal(360, controls.Settings.Width);
        Assert.Equal(420, controls.Settings.Height);
        Assert.Equal(392, configured.Settings.Left);
        Assert.Equal(440, configured.Settings.Width);
        Assert.Equal(480, configured.Settings.Height);
        Assert.NotEqual(loaded.Settings.FontPath, inspector.Settings.FontPath);
        Assert.Equal(loaded.Settings.FontSize, inspector.Settings.FontSize);
        // No GLFW, device or ImGui context is initialized by inspector construction/disposal.
        Assert.False(configured.Settings.Visible);
        Assert.False(configured.Settings.DiagnosticsVisible);
        Assert.Null(configured.Settings.Preview);
        Assert.Equal(240, configured.HistoryCapacity);
    }

    [Fact]
    public void CapturedSourcesDoNotReplayHeldEdgesOrStealGamepad()
    {
        var tracker = new OverlayCaptureTracker();
        var router = new BrutalBreakout.Platform.Input.InputRouter(new(0.2)); // Explicit test fixture, not a runtime default.
        var held = new BrutalBreakout.Platform.Input.InputSnapshot
        {
            Focused = true, KeyboardPrimary = true, MousePrimary = true, KeyboardRestart = true,
            Cursor = new(10, 20), PadConnected = true, PadId = 0, PadPrimary = true, PadStick = new(1, 0)
        };
        var captured = tracker.Update(true, true, true);
        var filtered = OverlayInputRouting.Filter(held, captured);
        Assert.False(filtered.KeyboardPrimary); Assert.False(filtered.KeyboardRestart); Assert.False(filtered.MousePrimary);
        Assert.True(double.IsNaN(filtered.Cursor.X)); Assert.True(filtered.PadPrimary); Assert.Equal(held.PadStick, filtered.PadStick);
        Assert.False(router.Route(filtered).PrimaryPressed);
        var closed = tracker.Update(false, true, true);
        Assert.True(closed.ResetBaselines);
        router.Reset(); // Host additionally flushes the session's latched edges via one inactive Advance.
        Assert.False(router.Route(OverlayInputRouting.Filter(held, closed)).PrimaryPressed);
        Assert.False(router.Route(held).PrimaryPressed);
        router.Route(held with { KeyboardPrimary = false, MousePrimary = false, PadPrimary = false });
        Assert.True(router.Route(held).PrimaryPressed);
    }

    [Fact]
    public void EditorOwnsDiagnosticsAndItsControlHint()
    {
        var settings = OverlayConfiguration.Load(ConfigPath).Settings;
        InspectorHost.Validate(settings);
        var hud = OverlayHost.Build(null, new(1, 1, 1));
        var frame = InspectorHost.Compose(hud, null, 2.5, 7, 8, 9);
        Assert.DoesNotContain("F1", hud.Labels[2].Text);
        Assert.Contains("F1", frame.Labels[2].Text);
        Assert.Equal("Showcase", frame.Diagnostics.State);
        Assert.Equal(2.5, frame.Diagnostics.FrameMilliseconds);
        Assert.Equal(7, frame.Diagnostics.PresentedShapeFrames);
        Assert.Equal(8, frame.Diagnostics.ShapeVertices);
        Assert.Equal(9, frame.Diagnostics.ShapeIndices);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ShmupDiagnosticsIncludeLiveTimingFactsAndPauseWithoutReplacingHud(bool paused)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Config");
        var physics = PhysicsConfiguration.Load(Path.Combine(directory, "physics.json")).Settings;
        var shmup = ShmupConfiguration.Load(Path.Combine(directory, "shmup.json")).Settings;
        var execution = ExecutionConfiguration.Load(Path.Combine(directory, "execution.json")).Settings;
        using var simulation = new BrutalBreakout.Simulation.ShmupSimulation(physics, shmup, execution);
        simulation.Step(new(FireHeld: true));
        var snapshot = simulation.Snapshot;
        var hud = BrutalBreakout.Presentation.ShmupPresentation.Overlay(snapshot, new(1, 1, 1));
        var result = InspectorHost.Compose(hud, null, 3.5, 17, 180, 240, snapshot, paused);
        Assert.Equal(hud.Labels[0], result.Labels[0]);
        Assert.Equal(hud.Labels[1], result.Labels[1]);
        Assert.StartsWith(hud.Labels[2].Text, result.Labels[2].Text);
        Assert.Contains("WASD/LS", result.Labels[2].Text);
        Assert.Contains("Shmup", result.Diagnostics.State);
        Assert.Contains(paused ? "Paused" : "Running", result.Diagnostics.State);
        Assert.Contains($"HP {snapshot.PlayerHealth}", result.Diagnostics.Detail);
        Assert.Contains($"Balls {snapshot.Balls.Length}", result.Diagnostics.Detail);
        Assert.Contains($"Bricks {snapshot.Bricks.Length}", result.Diagnostics.Detail);
        Assert.Contains(hud.Diagnostics.Detail, result.Diagnostics.Detail);
        Assert.Equal(3.5, result.Diagnostics.FrameMilliseconds);
        Assert.Equal(17, result.Diagnostics.PresentedShapeFrames);
        Assert.Equal(180, result.Diagnostics.ShapeVertices);
        Assert.Equal(240, result.Diagnostics.ShapeIndices);
    }

    [Fact]
    public void EditorPackageNoticesAreDeployed()
    {
        foreach (var package in new[] { "ImGui", "ImGui.Backends", "ImGui.Native", "Localization", "OpenGl", "OpenGl.Native" })
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Licenses", "Media", package);
            Assert.True(File.Exists(Path.Combine(directory, "LICENSE.txt")), package);
            Assert.True(File.Exists(Path.Combine(directory, "NOTICE.txt")), package);
            Assert.NotEmpty(Directory.GetFiles(directory, "*THIRD-PARTY-NOTICES.txt", SearchOption.AllDirectories));
        }
    }

    [Fact]
    public void InspectorAssetsResolveRelativeToTheirOwnConfiguration()
    {
        var settings = InspectorConfiguration.Load(InspectorPath).Settings;
        Assert.Equal("Iosevka-Regular.ttf", Path.GetFileName(settings.FontPath));
        Assert.True(File.Exists(settings.FontPath));
        Assert.Null(settings.Preview); // Ordinary consumers require no diagnostic image.
    }

    [Theory]
    [InlineData("unknown", "1")]
    [InlineData("fontPath", "null")]
    [InlineData("fontPath", "\"missing.ttf\"")]
    [InlineData("fontSize", "0")]
    [InlineData("diagnosticsVisible", "null")]
    [InlineData("historyCapacity", "0")]
    [InlineData("historyCapacity", "65537")]
    [InlineData("historyCapacity", "1.5")]
    [InlineData("imagePath", "\"legacy.png\"")] // Legacy preview fields are no longer production schema.
    public void InvalidInspectorConfigurationIsRejected(string property, string value)
    {
        WithConfig(n => n[property] = JsonNode.Parse(value), path =>
            Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(path)));
    }

    [Fact]
    public void RequiredAndDuplicatePropertiesAreRejected()
    {
        foreach (string property in new[] { "fontPath", "diagnosticsVisible", "historyCapacity" })
            WithConfig(n => n.Remove(property), path =>
                Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(path)));
        WithConfig(_ => { }, path =>
        {
            File.WriteAllText(path, File.ReadAllText(path).TrimEnd().TrimEnd('}') + ",\"visible\":true}");
            Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(path));
        });
    }

    [Theory]
    [InlineData("left", "-1")] [InlineData("top", "65537")]
    [InlineData("width", "0")] [InlineData("width", "8193")]
    [InlineData("height", "0")] [InlineData("height", "8193")]
    [InlineData("unknown", "1")] [InlineData("width", "null")]
    public void InvalidNestedWindowIsRejected(string property, string value)
    {
        WithConfig(n => n["gameplayWindow"]![property] = JsonNode.Parse(value), path =>
            Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(path)));
    }

    [Theory]
    [InlineData("left")] [InlineData("top")] [InlineData("width")] [InlineData("height")]
    public void AllNestedWindowPropertiesAreRequired(string property)
    {
        WithConfig(n => n["gameplayWindow"]!.AsObject().Remove(property), path =>
            Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(path)));
    }

    [Fact]
    public void NestedWindowCannotBeMissingNullOrHaveDuplicates()
    {
        WithConfig(n => n.Remove("gameplayWindow"), path =>
            Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(path)));
        WithConfig(n => n["gameplayWindow"] = null, path =>
            Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(path)));
        WithConfig(_ => { }, path =>
        {
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"gameplayWindow\":{", "\"gameplayWindow\":{\"left\":2,"));
            Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(path));
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameplayWindowSettings(float.NaN, 0, 100, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameplayWindowSettings(0, 0, float.PositiveInfinity, 100));
        Assert.Throws<ArgumentNullException>(() => new GameplayControls(null!));
    }

    [Fact]
    public void SelectPathHonorsInspectorOverrideIndependently()
    {
        Assert.Null(InspectorConfiguration.LoadForMode([], false, "unused", "unused"));
        Assert.Equal(InspectorPath, InspectorConfiguration.LoadForMode([], true, AppContext.BaseDirectory, "unused")!.Path);
        Assert.NotNull(InspectorConfiguration.LoadForMode(["--inspector-config", "Config/inspector.json"], false, "unused", AppContext.BaseDirectory));
        foreach (string[] args in new[] { new[] { "--inspector-config" }, new[] { "--inspector-config=x" }, new[] { "--inspector-config", "x", "--inspector-config", "y" } })
            Assert.Throws<ArgumentException>(() => InspectorConfiguration.LoadForMode(args, false, "unused", "unused"));
    }

    private static void WithConfig(Action<JsonObject> mutate, Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "inspector-config-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "inspector.json");
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(InspectorPath))!.AsObject();
            var settings = InspectorConfiguration.Load(InspectorPath).Settings;
            node["fontPath"] = settings.FontPath;
            mutate(node);
            File.WriteAllText(path, node.ToJsonString());
            test(path);
        }
        finally { Directory.Delete(directory, true); }
    }
}
