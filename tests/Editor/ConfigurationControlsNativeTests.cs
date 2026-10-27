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
            foreach (string category in new[] { "shmup", "breakout", "rendering", "appearance", "audio", "input", "physics", "window", "tools" })
            {
                controls.SelectCategory(category); Frame(controls); Frame(controls);
            }
            controls.SelectCategory("shmup");
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
        var jsonWork = workspace.Documents.Select(d => (d.SerializationCount, d.ParseCount, d.Revision)).ToArray();
        int pendingBuilds = workspace.PendingBuildCount;
        Context(width, height, scale, () =>
        {
            var recreated = Frame(controls); Frame(controls);
            Assert.Equal(new float2(300, 360), recreated.Size * scale);
            Assert.InRange(Math.Abs(recreated.Position.X * scale - 90), 0, scale);
            Assert.True(Frame(controls).ScrollMax > 0); // managed group choices also survived context retirement
        });
        Assert.Equal(jsonWork, workspace.Documents.Select(d => (d.SerializationCount, d.ParseCount, d.Revision)).ToArray());
        Assert.Equal(pendingBuilds, workspace.PendingBuildCount);
        Assert.All(workspace.Documents, d => { Assert.False(d.Pending); Assert.False(d.Unsaved); });
    }

    [Fact]
    public void ShippedWindowShowsAnActualBallFieldInsideNativeClipViewportAt960x540()
    {
        var settings = InspectorConfiguration.Load(ConfigurationWorkspaceTests.Paths()["inspector"]);
        var controls = new ConfigurationControls(ConfigurationWorkspaceTests.Create(), settings.ConfigurationWindow) { Visible = true };
        controls.SelectCategory("shmup"); controls.SetGroupOpen("shmup.ball", true);
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
                controls.SelectCategory("shmup"); controls.SetGroupOpen("shmup.brick", true);
                controls.SetGroupOpen("shmup.brick.shapes", true); controls.SetGroupOpen("shmup.brick.quarterTurns", true);
            }
            Prepare(); Frame(controls); Frame(controls);
            Frame(controls);
            bool ClickUntil(string key, int count)
            {
                for (int y = (int)controls.BodyMinimum.Y + 12; y < controls.BodyMaximum.Y - 12; y += 8)
                {
                    Prepare();
                    float x = controls.BodyMinimum.X + Ui.GetStyle().WindowPadding.X + Ui.GetStyle().IndentSpacing + 8;
                    var io = Ui.GetIO(); io.AddMousePosEvent(x, y); Frame(controls);
                    io.AddMouseButtonEvent(0, true); Frame(controls);
                    io.AddMouseButtonEvent(0, false); Frame(controls);
                    if (document.Draft.GetProperty("brick"u8).GetProperty(key).GetArrayLength() < count) return true;
                }
                return false;
            }
            Assert.True(ClickUntil("shapes", 5), "Shape checkbox must be a working published widget.");
            Assert.True(ClickUntil("quarterTurns", 4), "Quarter-turn checkbox must be a working published widget.");
            Assert.True(document.Pending); Assert.True(document.Unsaved);
        });
    }

    [Fact]
    public void CategoriesAndActionsStayOutsideScrollableBodyAndSelectionSurvivesRecreation()
    {
        var workspace = ConfigurationWorkspaceTests.Create();
        var controls = new ConfigurationControls(workspace, new(16, 16, 900, 600)) { Visible = true };
        foreach (var document in workspace.Documents) OpenChildren(controls, document.Draft, document.Section);
        Context(1280, 720, 1, () =>
        {
            Frame(controls); Frame(controls);
            Assert.True(controls.CategorySidebarVisible);
            Assert.True(controls.BodyScrollMax > 0);
            var bodyTop = controls.BodyMinimum;
            var io = Ui.GetIO();
            io.AddMousePosEvent(bodyTop.X + 30, bodyTop.Y + 80); Frame(controls);
            io.AddMouseWheelEvent(0, -10); Frame(controls); Frame(controls);
            Assert.Equal(bodyTop, controls.BodyMinimum);
            Assert.True(controls.BodyScrollY > 0, "The long selected body must really scroll.");
            Assert.Equal(0, Frame(controls).ParentScroll); // Only the child scrolls, never the action/navigation shell.
            // Native hit targets remain available beside the independently scrolled body.
            for (int y = (int)bodyTop.Y; y < bodyTop.Y + 300 && controls.SelectedCategory != "window"; y += 6)
            {
                io.AddMousePosEvent(bodyTop.X - 20, y); Frame(controls);
                io.AddMouseButtonEvent(0, true); Frame(controls);
                io.AddMouseButtonEvent(0, false); Frame(controls);
            }
            Assert.Equal("window", controls.SelectedCategory);
            controls.Visible = false; Frame(controls); controls.Visible = true;
            Assert.Equal("window", controls.SelectedCategory);
            Frame(controls, () => Ui.SetWindowSize(new float2(230, 500))); Frame(controls);
            Assert.False(controls.CategorySidebarVisible);
            Assert.Equal("window", controls.SelectedCategory);
        });
        Context(1280, 720, 1, () =>
        {
            Frame(controls); Frame(controls);
            Assert.Equal("window", controls.SelectedCategory);
            Assert.False(controls.CategorySidebarVisible);
            Assert.StartsWith("window.", controls.FirstVisibleField!.Value.Path);
        });
        Assert.Throws<ArgumentException>(() => controls.SelectCategory("missing"));
    }

    [Theory]
    [InlineData(230)] [InlineData(900)]
    public void LongFieldLabelsStayInSelectedBodyWithoutChangingDrafts(float width)
    {
        var workspace = new ConfigurationWorkspace(new Dictionary<string, string>
        { ["window"] = ConfigurationWorkspaceTests.Paths()["window"] }, true);
        var document = ConfigurationWorkspaceTests.Doc(workspace, "window");
        document.Set("A deliberately long diagnostic field label that must wrap rather than pushing its editor outside the panel", 1);
        var controls = new ConfigurationControls(workspace, new(16, 16, width, 500)) { Visible = true };
        long revision = document.Revision;
        Context(1280, 720, 1, () =>
        {
            Frame(controls); var window = Frame(controls);
            Assert.Equal("window", controls.SelectedCategory);
            Assert.Equal(width >= 900, controls.CategorySidebarVisible);
            Assert.True(controls.BodyMaximum.X <= window.Position.X + window.Size.X);
            Assert.True(controls.BodyMaximum.Y <= window.Position.Y + window.Size.Y);
            Assert.True(controls.ScalarDrawCount >= 5); // The long-name fallback field is actually traversed.
            Assert.Equal(0, window.ParentScroll);
            Assert.Equal(revision, document.Revision);
        });
    }

    private static void OpenChildren(ConfigurationControls controls, System.Text.Json.JsonElement node, string path)
    {
        if (node.ValueKind == System.Text.Json.JsonValueKind.Object)
            foreach (var property in node.EnumerateObject())
            {
                if (property.Value.ValueKind is System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array)
                { controls.SetGroupOpen(path + "." + property.Name, true); OpenChildren(controls, property.Value, path + "." + property.Name); }
            }
        else if (node.ValueKind == System.Text.Json.JsonValueKind.Array)
            for (int i = 0; i < node.GetArrayLength(); i++)
            { controls.SetGroupOpen(path + "." + i, true); OpenChildren(controls, node[i], path + "." + i); }
    }
    private static (float2 Position, float2 Size, float ScrollMax, float ParentScroll) Frame(ConfigurationControls controls, Action? edit = null)
    {
        Ui.NewFrame();
        int before = Ui.GetCurrentContext().CurrentWindowStack.Count;
        controls.Draw();
        Assert.Equal(before, Ui.GetCurrentContext().CurrentWindowStack.Count);
        Ui.Begin("Configuration"u8);
        var result = (Ui.GetWindowPos(), Ui.GetWindowSize(), controls.BodyScrollMax, Ui.GetScrollY());
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
