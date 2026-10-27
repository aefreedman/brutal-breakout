using Brutal.ImGuiApi;
using Brutal.Numerics;
using BrutalBreakout.Editor;
using CrushKit.Brutal.ImGui;
using Xunit;
using Ui = Brutal.ImGuiApi.ImGui;

[Collection("Native inspector font")]
public sealed class ConfigurationPolishNativeTests
{
    [Fact]
    public void NativeIntegerEntryKeepsValuesBeyondDoublePrecisionWithoutCastingTheDraft()
    {
        var workspace = ConfigurationWorkspaceTests.Create(); var document = ConfigurationWorkspaceTests.Doc(workspace, "shmup");
        var controls = new ConfigurationControls(workspace, new(16, 16, 900, 650)) { Visible = true };
        controls.SetGroupOpen("shmup.ball", false); controls.SetGroupOpen("shmup.noise", true);
        Context(() =>
        {
            Frame(controls); Frame(controls);
            Assert.Equal("shmup.noise.seed", controls.FirstVisibleField!.Value.Path);
            var field = controls.FirstVisibleField.Value;
            Click(controls, (field.Minimum + field.Maximum) / 2);
            SelectAll(controls);
            Ui.GetIO().AddInputCharactersUTF8("9007199254740993"u8); Frame(controls);
            Assert.Equal(9007199254740993L, document.Draft.GetProperty("noise"u8).GetProperty("seed"u8).GetInt64());
            Ui.GetIO().AddKeyEvent(ImGuiKey.Enter, true); Frame(controls);
            Ui.GetIO().AddKeyEvent(ImGuiKey.Enter, false); Frame(controls);
            Assert.Equal(9007199254740993L, document.Draft.GetProperty("noise"u8).GetProperty("seed"u8).GetInt64());
        });
        workspace.Process(_ => throw new Exception("Invalid Int32 schema draft must not apply."));
        Assert.StartsWith("Rejected:", workspace.Status);
    }

    [Fact]
    public void NativeWindowInputCommitsOnlyOnEnterAndVisibleActionRestoresJustTheField()
    {
        var workspace = new ConfigurationWorkspace(new Dictionary<string, string> { ["window"] = ConfigurationWorkspaceTests.Paths()["window"] }, true);
        var document = ConfigurationWorkspaceTests.Doc(workspace, "window");
        long sourceWidth = document.Draft.GetProperty("width"u8).GetInt64();
        var controls = new ConfigurationControls(workspace, new(16, 16, 900, 650)) { Visible = true };
        Context(() =>
        {
            Frame(controls); Frame(controls);
            var field = controls.FirstVisibleField!.Value; Assert.Equal("window.width", field.Path);
            Click(controls, (field.Minimum + field.Maximum) / 2); SelectAll(controls);
            Ui.GetIO().AddInputCharactersUTF8("900"u8); Frame(controls);
            Assert.Equal(sourceWidth, document.Draft.GetProperty("width"u8).GetInt64());
            Ui.GetIO().AddKeyEvent(ImGuiKey.Enter, true); Frame(controls);
            Ui.GetIO().AddKeyEvent(ImGuiKey.Enter, false); Frame(controls);
            Assert.Equal(900, document.Draft.GetProperty("width"u8).GetInt64());
            document.Set("width", 900.5); Frame(controls); Frame(controls);
            Assert.Equal(900.5, document.Draft.GetProperty("width"u8).GetDouble());
            field = controls.FirstVisibleField!.Value; Click(controls, (field.Minimum + field.Maximum) / 2); SelectAll(controls);
            Ui.GetIO().AddInputCharactersUTF8("901"u8); Frame(controls);
            Ui.GetIO().AddKeyEvent(ImGuiKey.Enter, true); Frame(controls);
            Ui.GetIO().AddKeyEvent(ImGuiKey.Enter, false); Frame(controls);
            Assert.Equal(901, document.Draft.GetProperty("width"u8).GetDouble());
            document.Set("height", 555);
            Frame(controls); var action = controls.FirstVisibleAction!.Value;
            Assert.Equal("window.width", action.Path);
            Click(controls, (action.Minimum + action.Maximum) / 2); Frame(controls);
            Assert.True(Ui.GetCurrentContext().OpenPopupStack.Count > 0);
            var popup = Ui.GetCurrentContext().OpenPopupStack[0].Window;
            var restore = popup.Pos + Ui.GetStyle().WindowPadding + new float2(50, Ui.GetFontSize() / 2);
            Click(controls, restore); Frame(controls);
            Assert.Equal(sourceWidth, document.Draft.GetProperty("width"u8).GetInt64());
            Assert.Equal(555, document.Draft.GetProperty("height"u8).GetInt64());
            Assert.Null(workspace.LastOutcome);
        });
    }

    [Theory]
    [InlineData(230)] [InlineData(900)]
    public void ThemeMetadataAndFractionalIntegerDraftsStayReadableAndUnchanged(float width)
    {
        var workspace = ConfigurationWorkspaceTests.Create(); var d = ConfigurationWorkspaceTests.Doc(workspace, "shmup");
        d.Set("ball.damage", 1.5); d.Set("ball.speed", 123.45678901234567);
        var settings = InspectorConfiguration.Load(ConfigurationWorkspaceTests.Paths()["inspector"]);
        var controls = new ConfigurationControls(workspace, new(16, 16, width, 600), settings.Theme) { Visible = true };
        Context(() =>
        {
            settings.Theme.Apply(settings.Settings.FontSize);
            for (int i = 0; i < 4; i++) Frame(controls);
            Assert.Equal(1.5, d.Draft.GetProperty("ball"u8).GetProperty("damage"u8).GetDouble());
            Assert.Equal(123.45678901234567, d.Draft.GetProperty("ball"u8).GetProperty("speed"u8).GetDouble());
            Assert.NotNull(controls.FirstVisibleAction); Assert.True(controls.BodyScrollMax > 0);
        });
    }
    [Fact]
    public void IdleMetadataReusesDescriptorsAndSourceRevisionInvalidatesThem()
    {
        var workspace = ConfigurationWorkspaceTests.Create();
        var document = ConfigurationWorkspaceTests.Doc(workspace, "shmup");
        var controls = new ConfigurationControls(workspace, new(16, 16, 900, 650)) { Visible = true };
        Context(() =>
        {
            Frame(controls); Frame(controls);
            var cache = (Dictionary<string, ConfigurationFieldPresentation>)typeof(ConfigurationControls)
                .GetField("presentation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(controls)!;
            var retained = cache["shmup.ball.speed"];
            var work = (document.ParseCount, document.SerializationCount, document.Revision);
            for (int i = 0; i < 12; i++) Frame(controls);
            Assert.Same(retained, cache["shmup.ball.speed"]);
            Assert.Equal(work, (document.ParseCount, document.SerializationCount, document.Revision));
            document.Set("ball.speed", 650.5);
            document.Source = document.Text; // Simulate the owner Save boundary without file I/O.
            Frame(controls);
            Assert.NotSame(retained, cache["shmup.ball.speed"]);
        });
    }
    private static void SelectAll(ConfigurationControls controls)
    {
        var io = Ui.GetIO(); io.AddKeyEvent(ImGuiKey.Mod_Ctrl, true); io.AddKeyEvent(ImGuiKey.A, true); Frame(controls);
        io.AddKeyEvent(ImGuiKey.A, false); io.AddKeyEvent(ImGuiKey.Mod_Ctrl, false); Frame(controls);
    }
    private static void Click(ConfigurationControls controls, float2 point)
    {
        var io = Ui.GetIO(); io.AddMousePosEvent(point.X, point.Y); Frame(controls);
        io.AddMouseButtonEvent(0, true); Frame(controls);
        io.AddMouseButtonEvent(0, false); Frame(controls);
    }
    private static void Frame(ConfigurationControls controls)
    {
        Ui.NewFrame(); int windows = Ui.GetCurrentContext().CurrentWindowStack.Count;
        controls.Draw(); Assert.Equal(windows, Ui.GetCurrentContext().CurrentWindowStack.Count);
        Assert.True(Ui.GetCurrentContext().CurrentTable.IsNull()); Ui.Render();
    }
    private static void Context(Action test)
    {
        var previous = Ui.GetCurrentContext(); var context = Ui.CreateContext();
        try
        {
            var io = Ui.GetIO(); io.IniFilename = default; io.LogFilename = default;
            io.DisplaySize = new float2(1280, 720); io.DeltaTime = 1f / 60;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var settings = InspectorConfiguration.Load(ConfigurationWorkspaceTests.Paths()["inspector"]).Settings;
            InspectorFont.Load(io, settings.FontPath, settings.FontSize);
            Ui.GetStyle().FontSizeBase = settings.FontSize;
            test();
        }
        finally { Ui.DestroyContext(context); Ui.SetCurrentContext(previous); }
    }
}
