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
