using System.Text.Json;
using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using BrutalBreakout.Editor;
using Xunit;

public sealed class ConfigurationRevisionTests
{
    private static ConfigurationWorkspace.Document Doc(ConfigurationWorkspace w, string section) => ConfigurationWorkspaceTests.Doc(w, section);
    private static void Read(ConfigurationWorkspace w)
    {
        foreach (var d in w.Documents) { _ = d.Draft; _ = d.Text; _ = d.Pending; _ = d.Unsaved; }
        _ = w.PendingResetFields; _ = w.PendingRelaunchFields; _ = w.Status;
    }
    private static void AssertIdle(ConfigurationWorkspace w)
    {
        w.Process(_ => { }); Read(w);
        var counts = w.Documents.Select(d => (d.SerializationCount, d.ParseCount, d.Revision)).ToArray();
        var work = (w.ChangeRevision, w.ValidationCount, w.MergeCount, w.PendingBuildCount);
        var reset = w.PendingResetFields; var relaunch = w.PendingRelaunchFields;
        for (int frame = 0; frame < 30; frame++) { Read(w); w.Process(_ => throw new Exception("Idle apply")); }
        Assert.Equal(counts, w.Documents.Select(d => (d.SerializationCount, d.ParseCount, d.Revision)).ToArray());
        Assert.Equal(work, (w.ChangeRevision, w.ValidationCount, w.MergeCount, w.PendingBuildCount));
        Assert.Same(reset, w.PendingResetFields); Assert.Same(relaunch, w.PendingRelaunchFields);
    }

    [Theory]
    [InlineData("accent.blue")]
    [InlineData("accent/blue")]
    [InlineData("accent~blue")]
    [InlineData("0")]
    [InlineData("accent.blue/~0")]
    public void WidgetEditHelperPreservesLiteralPaletteKeysThroughAutoApplySaveAndIdleReads(string key)
    {
        ConfigurationWorkspaceTests.WithCopy(root =>
        {
            var paths = ConfigurationWorkspaceTests.Paths(root);
            var original = JsonNode.Parse(File.ReadAllText(paths["colors"]))!;
            original["palette"]![key] = "#123456"; original["roles"]!["ball"] = key;
            File.WriteAllText(paths["colors"], original.ToJsonString());
            string source = File.ReadAllText(paths["colors"]);
            var w = new ConfigurationWorkspace(paths, true); var colors = Doc(w, "colors");
            var retained = colors.Draft; long revision = colors.Revision;
            string[] keys = ["palette", key];
            Assert.True(ConfigurationControls.EditValue(colors, keys, JsonValue.Create("#ABCDEF")));
            Assert.False(ConfigurationControls.EditValue(colors, keys, JsonValue.Create("#ABCDEF")));
            Assert.Equal(revision + 1, colors.Revision);
            keys[1] = "not-the-edited-key"; // edit addresses, like values, are not retained caller aliases
            Assert.True(ConfigurationControls.EditValue(colors, ["roles", "aim"], JsonValue.Create(key)));
            Assert.False(ConfigurationControls.EditValue(colors, ["roles", "aim"], JsonValue.Create(key)));
            Assert.Equal(revision + 2, colors.Revision);
            Assert.Equal("#123456", retained.GetProperty("palette"u8).GetProperty(key).GetString());
            Assert.Equal("#ABCDEF", colors.Draft.GetProperty("palette"u8).GetProperty(key).GetString());
            ConfigurationWorkspace.Candidate? applied = null;
            w.Process(c => applied = c);
            Assert.NotNull(applied); Assert.False(colors.Pending); Assert.True(colors.Unsaved);
            var scheme = applied.Get<LoadedColorSchemeConfiguration>("colors")!.Settings;
            Assert.Equal(scheme.Ball, scheme.Aim);
            Assert.Equal(source, File.ReadAllText(colors.Path)); AssertIdle(w);
            w.Request(ConfigurationAction.Save); w.Process(_ => throw new Exception("Save must not apply"));
            Assert.StartsWith("Saved", w.Status); Assert.False(colors.Unsaved); Assert.False(colors.Pending);
            original["palette"]![key] = "#ABCDEF"; original["roles"]!["aim"] = key;
            Assert.True(JsonNode.DeepEquals(original, JsonNode.Parse(File.ReadAllText(colors.Path))));
            AssertIdle(w);
        });
    }

    [Fact]
    public void LiteralFieldAddressesAlsoKeepIndependentPaletteEditsLiveWhenAnotherColorFieldIsInvalid()
    {
        var w = ConfigurationWorkspaceTests.Create(); var colors = Doc(w, "colors");
        const string key = "accent.blue/~0";
        Assert.True(ConfigurationControls.EditValue(colors, ["palette", key], JsonValue.Create("#123456")));
        Assert.True(ConfigurationControls.EditValue(colors, ["roles", "ball"], JsonValue.Create("undefined")));
        ConfigurationWorkspace.Candidate? applied = null;
        w.Process(c => applied = c);
        Assert.NotNull(applied); Assert.StartsWith("Rejected:", w.Status);
        var accepted = JsonNode.Parse(applied.Texts["colors"])!;
        Assert.Equal("#123456", accepted["palette"]![key]!.GetValue<string>());
        Assert.NotEqual("undefined", accepted["roles"]!["ball"]!.GetValue<string>());
        Assert.True(colors.Pending); AssertIdle(w);
    }

    [Fact]
    public void SegmentEditsDistinguishArrayIndicesAndNumericObjectKeysAndSupportDetachedElements()
    {
        var w = ConfigurationWorkspaceTests.Create(); var shmup = Doc(w, "shmup");
        long revision = shmup.Revision;
        Assert.True(shmup.Set(["ball", "speed"], 701)); Assert.False(shmup.Set("ball.speed", 701));
        Assert.True(shmup.Set(["brick", "quarterTurns", "0"], 2));
        Assert.False(shmup.Set("brick.quarterTurns.0", 2));
        Assert.Equal(revision + 2, shmup.Revision);
        var colors = Doc(w, "colors");
        using (var incoming = JsonDocument.Parse("\"#123456\"")) Assert.True(colors.Set(["palette", "0"], incoming.RootElement));
        Assert.Equal("#123456", colors.Draft.GetProperty("palette"u8).GetProperty("0"u8).GetString());
        Assert.False(colors.Set(["palette", "0"], JsonValue.Create("#123456")));
        Assert.Throws<ArgumentException>(() => colors.Set(Array.Empty<string>(), JsonValue.Create(0)));
        AssertIdle(w);
    }

    [Fact]
    public void PublicRegistryIsOneCachedReadOnlyWrapperAndCannotBypassRevisionTracking()
    {
        var w = ConfigurationWorkspaceTests.Create(); AssertIdle(w);
        var documents = w.Documents; var first = documents[0]; long revision = w.ChangeRevision;
        var list = Assert.IsAssignableFrom<IList<ConfigurationWorkspace.Document>>(documents);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(first));
        Assert.Throws<NotSupportedException>(() => list.Remove(first));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Throws<NotSupportedException>(() => list[0] = documents[1]);
        Assert.Same(documents, w.Documents); Assert.Same(first, w.Documents[0]);
        Assert.Equal(revision, w.ChangeRevision); AssertIdle(w);
    }

    [Fact]
    public void NestedArrayPaletteAndDetachedInputsInvalidateOnceAndRetainedViewsStayUnchanged()
    {
        var w = ConfigurationWorkspaceTests.Create(); var d = Doc(w, "shmup");
        var old = d.Draft; var oldText = old.GetRawText(); long revision = d.Revision, changes = w.ChangeRevision;
        Assert.True(d.Set("ball.speed", 701));
        Assert.Equal(revision + 1, d.Revision); Assert.Equal(changes + 1, w.ChangeRevision);
        Assert.False(d.Set("ball.speed", 701)); Assert.Equal(revision + 1, d.Revision);
        var input = new JsonArray("square", "l");
        Assert.True(d.Set("brick.shapes", input)); input[0] = "t"; input.Add("2x2");
        Assert.Equal("square", d.Draft.GetProperty("brick"u8).GetProperty("shapes"u8)[0].GetString());
        Assert.Equal(2, d.Draft.GetProperty("brick"u8).GetProperty("shapes"u8).GetArrayLength());
        Assert.True(d.Set("brick.shapes.1", "t"));
        Assert.Equal(revision + 3, d.Revision);
        Assert.False(d.Set("brick.shapes", new JsonArray("square", "t")));
        using (var incoming = JsonDocument.Parse("[0,2]")) Assert.True(d.Set("brick.quarterTurns", incoming.RootElement));
        Assert.Equal(2, d.Draft.GetProperty("brick"u8).GetProperty("quarterTurns"u8)[1].GetInt32());
        var colors = Doc(w, "colors"); var palette = JsonNode.Parse(colors.Draft.GetProperty("palette"u8).GetRawText())!.AsObject();
        string key = palette.First().Key; palette[key] = "#123456";
        long colorRevision = colors.Revision;
        Assert.True(colors.Set("palette", palette)); palette[key] = "#FFFFFF";
        Assert.Equal("#123456", colors.Draft.GetProperty("palette"u8).GetProperty(key).GetString());
        Assert.False(colors.Set("palette." + key, "#123456")); Assert.Equal(colorRevision + 1, colors.Revision);
        Assert.Equal(oldText, old.GetRawText());
        AssertIdle(w);
    }

    [Fact]
    public void InvalidAttemptsAreIdleUntilRealEditOrContextChangesIncludingOwnerReplacement()
    {
        var w = ConfigurationWorkspaceTests.Create(); var owner = new object(); w.SetLiveContext(owner, 432);
        bool reject = true; int preflights = 0, applied = 0;
        w.PreflightLive = _ => { preflights++; if (reject) throw new InvalidOperationException("context blocked"); };
        Doc(w, "input").Set("stickDeadzone", .3);
        w.Process(_ => applied++); Assert.Equal(0, applied); Assert.StartsWith("Rejected:", w.Status);
        AssertIdle(w); int attempts = preflights; long revision = w.ChangeRevision;
        w.SetLiveContext(owner, 432); Assert.Equal(revision, w.ChangeRevision);
        w.Process(_ => applied++); Assert.Equal(attempts, preflights);
        w.SetLiveContext(owner, 500); w.Process(_ => applied++); Assert.True(preflights > attempts);
        reject = false; w.SetLiveContext(new object(), 500);
        w.Process(c => { applied++; Assert.Equal(.3f, c.Get<LoadedInputConfiguration>("input")!.Settings.StickDeadzone); });
        Assert.Equal(1, applied); Assert.False(Doc(w, "input").Pending); AssertIdle(w);
        Doc(w, "shmup").Set("ball.speed", -1); w.Process(_ => { }); AssertIdle(w);
        w.LiveContextRevision = "retry"; int validations = w.ValidationCount; w.Process(_ => { }); Assert.True(w.ValidationCount > validations); AssertIdle(w);
    }

    [Fact]
    public void SourceAppliedAndInactiveModeLifecycleCachesRefreshAndThenStayIdle()
    {
        ConfigurationWorkspaceTests.WithCopy(root =>
        {
            var w = new ConfigurationWorkspace(ConfigurationWorkspaceTests.Paths(root), true);
            var input = Doc(w, "input"); AssertIdle(w);
            input.Set("stickDeadzone", .3); w.Process(_ => { }); Assert.False(input.Pending); Assert.True(input.Unsaved); AssertIdle(w);
            w.Request(ConfigurationAction.Save); w.Process(_ => { }); Assert.False(input.Unsaved); AssertIdle(w);
            input.Set("stickDeadzone", .4); w.Process(_ => { });
            w.Request(ConfigurationAction.Discard); w.Process(_ => { }); Assert.Equal(.3, input.Draft.GetProperty("stickDeadzone"u8).GetDouble()); AssertIdle(w);
            var node = JsonNode.Parse(File.ReadAllText(input.Path))!; node["stickDeadzone"] = .2; File.WriteAllText(input.Path, node.ToJsonString());
            w.Request(ConfigurationAction.Reload); w.Process(_ => { }); Assert.False(input.Unsaved); Assert.False(input.Pending); AssertIdle(w);
            Doc(w, "gameplay").Set("arena.wallThickness", 18); Assert.NotEmpty(w.PendingResetFields);
            w.InactiveModeSection = "gameplay"; Assert.Empty(w.PendingResetFields); w.Process(_ => { }); Assert.False(Doc(w, "gameplay").Pending); AssertIdle(w);
        });
    }
}
