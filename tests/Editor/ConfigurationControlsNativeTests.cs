using Brutal.ImGuiApi;
using Brutal.Numerics;
using BrutalBreakout.Editor;
using CrushKit.Brutal.ImGui;
using Xunit;
using Ui = Brutal.ImGuiApi.ImGui;

[Collection("Native inspector font")]
public sealed class ConfigurationControlsNativeTests
{
    [Theory]
    [InlineData(960, 540, 1f)] [InlineData(960, 540, 2f)] [InlineData(1280, 720, 1f)]
    public void AllPublishedWidgetsDrawWithBalancedScopesScrollAndRetainBoundsAcrossContexts(int width, int height, float scale)
    {
        var workspace = ConfigurationWorkspaceTests.Create();
        var settings = InspectorConfiguration.Load(ConfigurationWorkspaceTests.Paths()["inspector"]);
        var controls = new ConfigurationControls(workspace, settings.ConfigurationWindow) { Visible = true };
        foreach (var document in workspace.Documents)
        {
            controls.SetGroupOpen(document.Section, true);
            OpenChildren(controls, document.Draft, document.Section);
        }
        Context(width, height, 1, () =>
        {
            Frame(controls); var initial = Frame(controls);
            Assert.True(initial.ScrollMax > 0);
            Assert.Equal(new float2(settings.ConfigurationWindow.Width, settings.ConfigurationWindow.Height), initial.Size);
            Frame(controls, () => { Ui.SetWindowPos(new float2(90, 45)); Ui.SetWindowSize(new float2(300, 360)); });
            Frame(controls);
            controls.Visible = false; Frame(controls); controls.Visible = true;
            var reopened = Frame(controls); Assert.Equal(new float2(90, 45), reopened.Position);
            Frame(controls, () => Ui.SetWindowSize(new float2(180, 240)));
            Assert.True(Frame(controls).ScrollMax > 0);
            Frame(controls, () => Ui.SetWindowSize(new float2(300, 360))); Frame(controls);
        });
        Context(width, height, scale, () =>
        {
            var recreated = Frame(controls); Frame(controls);
            Assert.Equal(new float2(300, 360), recreated.Size * scale);
            Assert.InRange(Math.Abs(recreated.Position.X * scale - 90), 0, scale);
            Assert.True(Frame(controls).ScrollMax > 0); // managed group choices also survived context retirement
        });
        Assert.All(workspace.Documents, d => { Assert.False(d.Pending); Assert.False(d.Unsaved); });
    }

    [Fact]
    public void ShippedWindowShowsAnActualBallFieldInsideNativeClipViewportAt960x540()
    {
        var settings = InspectorConfiguration.Load(ConfigurationWorkspaceTests.Paths()["inspector"]);
        var controls = new ConfigurationControls(ConfigurationWorkspaceTests.Create(), settings.ConfigurationWindow) { Visible = true };
        controls.SetGroupOpen("shmup", true); controls.SetGroupOpen("shmup.ball", true);
        Context(960, 540, 1, () =>
        {
            Frame(controls); var window = Frame(controls);
            var field = Assert.IsType<(string Path, float2 Minimum, float2 Maximum)>(controls.FirstVisibleField);
            Assert.StartsWith("shmup.ball.", field.Path);
            // Recorded only when native IsItemVisible() confirms intersection with the actual clipping viewport.
            Assert.True(field.Minimum.X < window.Position.X + window.Size.X && field.Maximum.X > window.Position.X);
            Assert.True(field.Minimum.Y < window.Position.Y + window.Size.Y && field.Maximum.Y > window.Position.Y + Ui.GetFrameHeight());
            Assert.True(field.Maximum.X > field.Minimum.X && field.Maximum.Y > field.Minimum.Y);
        });
    }

    [Fact]
    public void ActualNativeShapeAndQuarterTurnCheckboxesEditDraftWithoutApplying()
    {
        var workspace = ConfigurationWorkspaceTests.Create();
        var document = ConfigurationWorkspaceTests.Doc(workspace, "shmup");
        var controls = new ConfigurationControls(workspace, new(16, 16, 640, 950)) { Visible = true };
        Context(1280, 1000, 1, () =>
        {
            void Prepare()
            {
                foreach (var d in workspace.Documents) controls.SetGroupOpen(d.Section, false);
                foreach (string group in new[] { "arena", "avatar", "ball", "difficulty", "noise", "capacity" }) controls.SetGroupOpen("shmup." + group, false);
                controls.SetGroupOpen("shmup", true); controls.SetGroupOpen("shmup.brick", true);
                controls.SetGroupOpen("shmup.brick.shapes", true); controls.SetGroupOpen("shmup.brick.quarterTurns", true);
            }
            Prepare(); Frame(controls); Frame(controls);
            Frame(controls, () => Ui.SetScrollY(Ui.GetScrollMaxY())); Frame(controls);
            bool ClickUntil(string key, int count)
            {
                for (int y = 240; y < 940; y += 8)
                {
                    Prepare();
                    var io = Ui.GetIO(); io.AddMousePosEvent(48, y); Frame(controls);
                    io.AddMouseButtonEvent(0, true); Frame(controls);
                    io.AddMouseButtonEvent(0, false); Frame(controls);
                    if (document.Draft["brick"]![key]!.AsArray().Count < count) return true;
                }
                return false;
            }
            Assert.True(ClickUntil("shapes", 5), "Shape checkbox must be a working published widget.");
            Assert.True(ClickUntil("quarterTurns", 4), "Quarter-turn checkbox must be a working published widget.");
            Assert.True(document.Pending); Assert.True(document.Unsaved);
        });
    }

    private static void OpenChildren(ConfigurationControls controls, System.Text.Json.Nodes.JsonNode node, string path)
    {
        if (node is System.Text.Json.Nodes.JsonObject obj)
            foreach (var (key, child) in obj)
            {
                if (child is System.Text.Json.Nodes.JsonObject or System.Text.Json.Nodes.JsonArray)
                { controls.SetGroupOpen(path + "." + key, true); OpenChildren(controls, child, path + "." + key); }
            }
        else if (node is System.Text.Json.Nodes.JsonArray array)
            for (int i = 0; i < array.Count; i++)
                if (array[i] is { } child) { controls.SetGroupOpen(path + "." + i, true); OpenChildren(controls, child, path + "." + i); }
    }
    private static (float2 Position, float2 Size, float ScrollMax) Frame(ConfigurationControls controls, Action? edit = null)
    {
        Ui.NewFrame();
        int before = Ui.GetCurrentContext().CurrentWindowStack.Count;
        controls.Draw();
        Assert.Equal(before, Ui.GetCurrentContext().CurrentWindowStack.Count);
        Ui.Begin("Configuration"u8);
        var result = (Ui.GetWindowPos(), Ui.GetWindowSize(), Ui.GetScrollMaxY());
        try { edit?.Invoke(); } finally { Ui.End(); }
        Ui.Render(); return result;
    }
    private static void Context(int width, int height, float scale, Action test)
    {
        var previous = Ui.GetCurrentContext(); var context = Ui.CreateContext();
        try
        {
            var io = Ui.GetIO(); io.IniFilename = default; io.LogFilename = default;
            io.DisplaySize = new float2(width / scale, height / scale); io.DisplayFramebufferScale = new float2(scale); io.DeltaTime = 1f / 60;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var settings = InspectorConfiguration.Load(ConfigurationWorkspaceTests.Paths()["inspector"]).Settings;
            InspectorFont.Load(io, settings.FontPath, settings.FontSize);
            Ui.GetStyle().FontSizeBase = InspectorFont.LogicalSize(settings.FontSize, io.DisplayFramebufferScale);
            test();
        }
        finally { Ui.DestroyContext(context); Ui.SetCurrentContext(previous); }
    }
}
