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
        Assert.NotEqual(loaded.Settings.FontPath, inspector.Settings.FontPath);
        Assert.Equal(loaded.Settings.FontSize, inspector.Settings.FontSize);
        // No GLFW, device or ImGui context is initialized by inspector construction/disposal.
        Assert.False(configured.Settings.Visible);
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
        Assert.True(File.Exists(settings.ImagePath));
    }

    [Theory]
    [InlineData("unknown", "1")]
    [InlineData("fontPath", "null")]
    [InlineData("fontPath", "\"missing.ttf\"")]
    [InlineData("imagePath", "\"missing.png\"")]
    [InlineData("fontSize", "0")]
    [InlineData("imageSize", "0")]
    [InlineData("maxTextureDimension", "8")]
    [InlineData("maxEncodedImageBytes", "1")]
    public void InvalidInspectorConfigurationIsRejected(string property, string value)
    {
        WithConfig(n => n[property] = JsonNode.Parse(value), path =>
            Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(path)));
    }

    [Fact]
    public void RequiredAndDuplicatePropertiesAreRejected()
    {
        WithConfig(n => n.Remove("fontPath"), path =>
            Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(path)));
        WithConfig(_ => { }, path =>
        {
            File.WriteAllText(path, File.ReadAllText(path).TrimEnd().TrimEnd('}') + ",\"visible\":true}");
            Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(path));
        });
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
            node["imagePath"] = settings.ImagePath;
            mutate(node);
            File.WriteAllText(path, node.ToJsonString());
            test(path);
        }
        finally { Directory.Delete(directory, true); }
    }
}
