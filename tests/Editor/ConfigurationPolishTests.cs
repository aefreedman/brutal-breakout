using System.Text.Json;
using System.Text.Json.Nodes;
using BrutalBreakout.Editor;
using CrushKit.Brutal.ImGui;
using Xunit;

public sealed class ConfigurationPolishTests
{
    [Theory]
    [InlineData("accent.blue/~0")]
    [InlineData("0")]
    public void SourceRestoreUsesLiteralKeysOnlyAndNeverSavesOrResets(string key)
    {
        ConfigurationWorkspaceTests.WithCopy(root =>
        {
            var paths = ConfigurationWorkspaceTests.Paths(root);
            var node = JsonNode.Parse(File.ReadAllText(paths["colors"]))!; node["palette"]![key] = "#123456";
            File.WriteAllText(paths["colors"], node.ToJsonString());
            var workspace = new ConfigurationWorkspace(paths, true);
            var colors = ConfigurationWorkspaceTests.Doc(workspace, "colors");
            string disk = File.ReadAllText(colors.Path); long sourceRevision = colors.SourceRevision;
            var retained = colors.Draft;
            colors.Set(["palette", key], JsonValue.Create("#ABCDEF"));
            colors.Set(["roles", "aim"], JsonValue.Create("white"));
            string unrelated = colors.Draft.GetProperty("roles"u8).GetProperty("aim"u8).GetString()!;
            Assert.True(colors.CanRestoreSource(["palette", key]));
            Assert.True(colors.RestoreSource(["palette", key]));
            Assert.False(colors.RestoreSource(["palette", key]));
            Assert.False(colors.RestoreSource(["palette", "missing"]));
            Assert.Equal("#123456", colors.Draft.GetProperty("palette"u8).GetProperty(key).GetString());
            Assert.Equal(unrelated, colors.Draft.GetProperty("roles"u8).GetProperty("aim"u8).GetString());
            Assert.Equal("#123456", retained.GetProperty("palette"u8).GetProperty(key).GetString());
            Assert.Equal(disk, File.ReadAllText(colors.Path)); Assert.Equal(sourceRevision, colors.SourceRevision);
            Assert.Null(workspace.LastOutcome);
        });
    }

    [Fact]
    public void SourceRestoreSupportsAtomicArraysAndUpdatedSavedAndReloadedSnapshots()
    {
        ConfigurationWorkspaceTests.WithCopy(root =>
        {
            var workspace = new ConfigurationWorkspace(ConfigurationWorkspaceTests.Paths(root), true);
            var d = ConfigurationWorkspaceTests.Doc(workspace, "shmup");
            int original = d.Draft.GetProperty("brick"u8).GetProperty("quarterTurns"u8)[0].GetInt32();
            d.Set(["brick", "quarterTurns", "0"], JsonValue.Create(2));
            Assert.True(d.RestoreSource(["brick", "quarterTurns", "0"]));
            Assert.Equal(original, d.Draft.GetProperty("brick"u8).GetProperty("quarterTurns"u8)[0].GetInt32());
            d.Set("ball.fireRate", 12.5); long sourceRevision = d.SourceRevision;
            workspace.Request(ConfigurationAction.Save); workspace.Process(_ => throw new Exception("Save must not apply"));
            Assert.True(d.SourceRevision > sourceRevision);
            d.Set("ball.fireRate", 60); Assert.True(d.RestoreSource(["ball", "fireRate"]));
            Assert.Equal(12.5, d.Draft.GetProperty("ball"u8).GetProperty("fireRate"u8).GetDouble());
            var disk = JsonNode.Parse(File.ReadAllText(d.Path))!; disk["ball"]!["fireRate"] = 15.5;
            File.WriteAllText(d.Path, disk.ToJsonString()); sourceRevision = d.SourceRevision;
            workspace.Request(ConfigurationAction.Reload); workspace.Process(_ => { });
            Assert.True(d.SourceRevision > sourceRevision);
            d.Set("ball.fireRate", 90); Assert.True(d.RestoreSource(["ball", "fireRate"]));
            Assert.Equal(15.5, d.Draft.GetProperty("ball"u8).GetProperty("fireRate"u8).GetDouble());
        });
    }

    [Fact]
    public void ExplicitIdenticalOutcomesAreEventsButSuccessfulDragsAndIdleFramesStayQuiet()
    {
        ConfigurationWorkspaceTests.WithCopy(root =>
        {
            var workspace = new ConfigurationWorkspace(ConfigurationWorkspaceTests.Paths(root), true);
            var preferences = new InspectorFeedbackSettings(4, 128, 1, 2);
            var feedback = new ConfigurationFeedback(preferences);
            workspace.Request(ConfigurationAction.Save); workspace.Process(_ => { });
            var first = workspace.LastOutcome!; feedback.Update(0, first);
            Assert.Single(Enumerable.Range(0, feedback.Count));
            workspace.Request(ConfigurationAction.Save); workspace.Process(_ => { });
            Assert.Equal(first.Message, workspace.LastOutcome!.Message);
            Assert.True(workspace.LastOutcome.Sequence > first.Sequence);
            feedback.Update(.1, workspace.LastOutcome); Assert.Equal(1, feedback.Count);
            long sequence = workspace.LastOutcome.Sequence;
            ConfigurationWorkspaceTests.Doc(workspace, "input").Set("stickDeadzone", .23);
            workspace.Process(_ => { });
            for (int i = 0; i < 10; i++) { workspace.Process(_ => throw new Exception("Idle apply")); feedback.Update(.2, workspace.LastOutcome); }
            Assert.Equal(sequence, workspace.LastOutcome.Sequence); Assert.Equal(1, feedback.Count);
            feedback.Update(3, workspace.LastOutcome); Assert.Equal(0, feedback.Count);
            Assert.Same(preferences, feedback.Settings);
        });
    }

    [Fact]
    public void SmallFeedbackLimitNeverTurnsLongFailureReportingIntoAnotherFailure()
    {
        var workspace = ConfigurationWorkspaceTests.Create();
        string detail = string.Concat(Enumerable.Repeat("Long diagnostic 😀\0", 1000));
        workspace.ReportFailure(new InvalidOperationException(detail));
        var feedback = new ConfigurationFeedback(new(2, 128, 1, 2));
        feedback.Update(0, workspace.LastOutcome);
        Assert.Equal(1, feedback.Count); Assert.Contains(detail, workspace.Status);
        Assert.True(feedback.Buffer[0].Id.Length + feedback.Buffer[0].Title.Length + feedback.Buffer[0].Detail.Length <= 128);
        Assert.DoesNotContain('\0', feedback.Buffer[0].Detail);
        feedback.Update(3, workspace.LastOutcome);
        Assert.Equal(0, feedback.Count); Assert.StartsWith("Rejected:", workspace.Status);
    }

    [Fact]
    public void PresentationIsExplicitAndUnclampedPreciseDraftsRemainUnchanged()
    {
        var speed = new ConfigurationFieldPresentation("shmup.ball.speed", "Speed");
        var damage = new ConfigurationFieldPresentation("shmup.ball.damage", "Damage");
        var unknown = new ConfigurationFieldPresentation("unknown.wholeLookingValue", "Value");
        Assert.Null(speed.Integer); Assert.NotNull(damage.Integer); Assert.Null(unknown.Integer);
        Assert.Equal(ToolNumberMode.Drag, speed.Real.Mode);
        Assert.Equal("units/s", speed.UnitSuffix); Assert.Equal("units/s", speed.Real.Suffix);
        Assert.Empty(speed.Property.Units); Assert.Empty(speed.Property.Effect);
        Assert.True(speed.Property.MetadataUtf8.IsEmpty);
        Assert.DoesNotContain("LIVE", speed.Property.Label);
        var controls = new ConfigurationControls(ConfigurationWorkspaceTests.Create(), new(0, 0, 900, 600));
        controls.SetPresentationFacts(0, true, "ImmediateKHR");
        Assert.Contains("Unlimited", controls.PresentationSummary); Assert.Contains("pending", controls.PresentationSummary);
        Assert.Contains("does not disable VSync", controls.PresentationSummary);
        controls.SetPresentationFacts(120, false, "ImmediateKHR");
        Assert.Contains("120 fps", controls.PresentationSummary); Assert.DoesNotContain("pending", controls.PresentationSummary);
    }

    [Theory]
    [InlineData("shmup.ball.radius", "units", false)]
    [InlineData("shmup.ball.fireRate", "shots/s", false)]
    [InlineData("shmup.avatar.invulnerabilitySeconds", "s", false)]
    [InlineData("physics.updateHz", "Hz", false)]
    [InlineData("window.width", "px", true)]
    [InlineData("window.frameRateLimit", "fps", true)]
    public void UnitsBelongToNumericValuesNotPropertySubtitles(string id, string units, bool integer)
    {
        var field = new ConfigurationFieldPresentation(id, "Value");
        Assert.Equal(units, field.UnitSuffix); Assert.Equal(units, field.Real.Suffix);
        if (integer) Assert.Equal(units, field.Integer!.Suffix);
        Assert.True(field.Property.MetadataUtf8.IsEmpty);
        Assert.Equal("Value", field.Property.Label);
    }

    [Fact]
    public void ResetMarkerTracksRunningValuesAndSurvivesSaveAndSourceReload()
    {
        ConfigurationWorkspaceTests.WithCopy(root =>
        {
            var workspace = new ConfigurationWorkspace(ConfigurationWorkspaceTests.Paths(root), true);
            var document = ConfigurationWorkspaceTests.Doc(workspace, "physics");
            var field = new ConfigurationFieldPresentation("physics.updateHz", "Update Hz");
            string[] keys = ["updateHz"];
            double original = document.Draft.GetProperty("updateHz"u8).GetDouble();
            Assert.Same(field.Property, field.ForDocument(document, keys));
            document.Set("updateHz", original + 1);
            var marked = field.ForDocument(document, keys);
            Assert.Equal("Update Hz*", marked.Label); Assert.Empty(marked.Effect);
            Assert.Equal(field.Property.Id, marked.Id);
            workspace.Process(_ => throw new Exception("Reset-only edit must not autoapply"));
            Assert.Same(marked, field.ForDocument(document, keys));
            workspace.Request(ConfigurationAction.Save); workspace.Process(_ => { });
            Assert.True(workspace.LastOutcome!.Succeeded); Assert.False(document.Unsaved);
            Assert.Same(marked, field.ForDocument(document, keys)); // Saved is not applied.
            workspace.Request(ConfigurationAction.Reload); workspace.Process(_ => { });
            Assert.Same(marked, field.ForDocument(document, keys)); // Source Reload is not a game reset.
            long revision = document.Revision, applied = document.AppliedRevision;
            workspace.Request(ConfigurationAction.Apply); workspace.Process(_ => { });
            Assert.True(workspace.LastOutcome.Succeeded);
            Assert.Equal(revision, document.Revision); Assert.True(document.AppliedRevision > applied);
            Assert.Same(field.Property, field.ForDocument(document, keys));
            document.Set("updateHz", original + 2); Assert.Same(marked, field.ForDocument(document, keys));
            document.Set("updateHz", original + 1); Assert.Same(field.Property, field.ForDocument(document, keys));
        });
    }

    [Fact]
    public void RelaunchMarkersSurviveRuntimeApplyWhileLiveAndFutureValuesHaveNoPolicyLabelsOrStars()
    {
        var workspace = ConfigurationWorkspaceTests.Create();
        var rendering = ConfigurationWorkspaceTests.Doc(workspace, "rendering");
        var field = new ConfigurationFieldPresentation("rendering.limits.instances", "Instances");
        string[] keys = ["limits", "instances"];
        long original = rendering.Draft.GetProperty("limits"u8).GetProperty("instances"u8).GetInt64();
        rendering.Set(keys, JsonValue.Create(original + 1));
        var marked = field.ForDocument(rendering, keys);
        Assert.Equal("Instances*", marked.Label);
        workspace.Request(ConfigurationAction.Apply); workspace.Process(_ => { });
        Assert.True(workspace.LastOutcome!.Succeeded); Assert.Same(marked, field.ForDocument(rendering, keys));
        Assert.True(rendering.RestoreSource(keys));
        Assert.Same(field.Property, field.ForDocument(rendering, keys));
        var shmup = ConfigurationWorkspaceTests.Doc(workspace, "shmup");
        foreach (var (id, label, path, value) in new[]
        {
            ("shmup.ball.speed", "Speed", "ball.speed", 650d),
            ("shmup.avatar.health", "Health", "avatar.health", 4d)
        })
        {
            var immediate = new ConfigurationFieldPresentation(id, label);
            shmup.Set(path, JsonValue.Create(value));
            var property = immediate.ForDocument(shmup, path.Split('.'));
            Assert.Equal(label, property.Label); Assert.Empty(property.Effect);
        }
    }

    [Fact]
    public void AtomicArrayAndLiteralKeyPendingComparisonsUseRunningRatherThanSourceValues()
    {
        var workspace = ConfigurationWorkspaceTests.Create();
        var document = ConfigurationWorkspaceTests.Doc(workspace, "gameplay");
        var field = new ConfigurationFieldPresentation("gameplay.breakout.bricks", "Bricks");
        string[] array = ["breakout", "bricks"];
        var original = document.Draft.GetProperty("breakout"u8).GetProperty("bricks"u8)[0].GetProperty("width"u8).GetDouble();
        document.Set(["breakout", "bricks", "0", "width"], JsonValue.Create(original + 1));
        Assert.Equal("Bricks*", field.ForDocument(document, array).Label);
        Assert.True(document.RestoreSource(array)); Assert.Equal("Bricks", field.ForDocument(document, array).Label);
        document.Set(["ball", "literal.key"], JsonValue.Create(1));
        Assert.True(document.IsUnapplied(["ball", "literal.key"]));
        Assert.False(document.IsUnapplied(["ball", "radius"]));
    }

    [Fact]
    public void CopyAndPresetsAreApplicationOwnedRequestsAndSourceRestoreOnlyChangesTheField()
    {
        var workspace = ConfigurationWorkspaceTests.Create(); var d = ConfigurationWorkspaceTests.Doc(workspace, "shmup");
        var controls = new ConfigurationControls(workspace, new(0, 0, 900, 600));
        var description = new ConfigurationFieldPresentation("shmup.ball.fireRate", "Fire rate");
        var value = d.Draft.GetProperty("ball"u8).GetProperty("fireRate"u8);
        controls.ExecuteFieldAction(d, ["ball", "fireRate"], value, description, new(ToolFieldActionKind.Copy));
        Assert.Equal(value.GetRawText(), controls.DrainClipboardText()); Assert.Null(controls.DrainClipboardText());
        Assert.False(d.Unsaved); Assert.Null(workspace.LastOutcome);
        d.Set("ball.speed", 650.5);
        controls.ExecuteFieldAction(d, ["ball", "fireRate"], value, description, new(ToolFieldActionKind.Preset, 0));
        Assert.Equal(12.5, d.Draft.GetProperty("ball"u8).GetProperty("fireRate"u8).GetDouble());
        controls.ExecuteFieldAction(d, ["ball", "fireRate"], value, description, new(ToolFieldActionKind.Restore));
        Assert.Equal(value.GetDouble(), d.Draft.GetProperty("ball"u8).GetProperty("fireRate"u8).GetDouble());
        Assert.Equal(650.5, d.Draft.GetProperty("ball"u8).GetProperty("speed"u8).GetDouble());
    }
}
