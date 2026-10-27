using System.Text;
using System.Text.Json.Nodes;
using BrutalBreakout.Editor;
using Xunit;

public sealed class ConfigurationRecoveryTests
{
    [Fact]
    public void RestoringRejectedLiveValueToAppliedSourceClearsOnlyLiveFailureWithoutAnotherEvent()
    {
        var workspace = ConfigurationWorkspaceTests.Create();
        var document = ConfigurationWorkspaceTests.Doc(workspace, "shmup");
        document.Set("ball.speed", -1);
        workspace.Process(_ => throw new Exception("Invalid speed must not apply."));
        var rejected = workspace.LastOutcome!;
        Assert.Equal(ConfigurationOutcomeOrigin.LiveApply, rejected.Origin);
        Assert.Equal(ConfigurationAction.None, rejected.Action); Assert.False(rejected.Succeeded);
        Assert.StartsWith("Rejected:", workspace.Status);
        Assert.True(document.RestoreSource(["ball", "speed"]));
        workspace.Process(_ => throw new Exception("Already-applied recovery must not reapply."));
        Assert.DoesNotContain("Rejected:", workspace.Status); Assert.Same(rejected, workspace.LastOutcome);
        for (int i = 0; i < 20; i++) workspace.Process(_ => throw new Exception("Idle apply"));
        Assert.Same(rejected, workspace.LastOutcome);
    }

    [Fact]
    public void LiveRecoveryAndUnrelatedApplyDoNotHideAnUnresolvedSaveConflict()
    {
        ConfigurationWorkspaceTests.WithCopy(root =>
        {
            var workspace = new ConfigurationWorkspace(ConfigurationWorkspaceTests.Paths(root), true);
            var input = ConfigurationWorkspaceTests.Doc(workspace, "input");
            var shmup = ConfigurationWorkspaceTests.Doc(workspace, "shmup");
            File.WriteAllText(input.Path, "{\"stickDeadzone\":0.21}");
            input.Set("stickDeadzone", .23);
            workspace.Request(ConfigurationAction.Save); workspace.Process(_ => { });
            Assert.Equal(ConfigurationAction.Save, workspace.LastOutcome!.Action);
            Assert.Equal(ConfigurationOutcomeOrigin.UserOperation, workspace.LastOutcome.Origin);
            Assert.False(workspace.LastOutcome.Succeeded); Assert.Contains("Source changed externally", workspace.Status);
            shmup.Set("ball.speed", -1); workspace.Process(_ => { });
            var liveAttempt = workspace.LastOutcome;
            Assert.Equal(ConfigurationOutcomeOrigin.LiveApply, liveAttempt!.Origin);
            workspace.Request(ConfigurationAction.Apply); workspace.Process(_ => throw new Exception("Invalid draft must not apply"));
            Assert.False(workspace.LastOutcome!.Succeeded);
            Assert.Equal(ConfigurationAction.Apply, workspace.LastOutcome.Action);
            liveAttempt = workspace.LastOutcome;
            Assert.Contains("Source changed externally", workspace.Status); // Failed Apply must not replace failed Save.
            shmup.RestoreSource(["ball", "speed"]); workspace.Process(_ => throw new Exception("Recovery apply"));
            Assert.Same(liveAttempt, workspace.LastOutcome);
            Assert.Contains("Source changed externally", workspace.Status);
            Assert.DoesNotContain("live values pending", workspace.Status);
            workspace.Request(ConfigurationAction.Apply); workspace.Process(_ => { });
            Assert.True(workspace.LastOutcome!.Succeeded); Assert.Equal(ConfigurationAction.Apply, workspace.LastOutcome.Action);
            Assert.Contains("Source changed externally", workspace.Status); // Outcome describes this Apply, not the stale Save failure.
            workspace.Request(ConfigurationAction.Reload); workspace.Process(_ => { });
            Assert.True(workspace.LastOutcome.Succeeded); Assert.DoesNotContain("Rejected:", workspace.Status);
        });
    }

    [Fact]
    public void ExternalFailureSurvivesLiveRecoveryAndClearsOnlyWhenItsOwnerReportsRecovery()
    {
        var workspace = ConfigurationWorkspaceTests.Create();
        workspace.ReportFailure(new IOException("Clipboard unavailable"));
        Assert.Equal(ConfigurationOutcomeOrigin.External, workspace.LastOutcome!.Origin);
        var d = ConfigurationWorkspaceTests.Doc(workspace, "shmup");
        d.Set("ball.speed", -1); workspace.Process(_ => { });
        var last = workspace.LastOutcome;
        d.RestoreSource(["ball", "speed"]); workspace.Process(_ => throw new Exception("No apply needed"));
        Assert.Contains("Clipboard unavailable", workspace.Status); Assert.Same(last, workspace.LastOutcome);
        workspace.ClearReportedFailure(); Assert.DoesNotContain("Rejected:", workspace.Status);
        workspace.Process(_ => throw new Exception("Idle apply")); Assert.Same(last, workspace.LastOutcome);
    }

    [Theory]
    [InlineData(ConfigurationAction.Save)]
    [InlineData(ConfigurationAction.Apply)]
    public void ExplicitFailureOutcomeRetainsRequestedAction(ConfigurationAction action)
    {
        var workspace = ConfigurationWorkspaceTests.Create();
        ConfigurationWorkspaceTests.Doc(workspace, "shmup").Set("ball.speed", -1);
        workspace.Request(action); workspace.Process(_ => throw new Exception("Invalid draft must not apply"));
        Assert.Equal(action, workspace.LastOutcome!.Action);
        Assert.Equal(ConfigurationOutcomeOrigin.UserOperation, workspace.LastOutcome.Origin);
        Assert.False(workspace.LastOutcome.Succeeded);
    }

    [Fact]
    public void InvalidCommandCannotGrowFailureStorageOrReplacePendingCommand()
    {
        var workspace = ConfigurationWorkspaceTests.Create();
        workspace.Request(ConfigurationAction.Discard);
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.Request((ConfigurationAction)int.MaxValue));
        workspace.Process(_ => { });
        Assert.Equal(ConfigurationAction.Discard, workspace.LastOutcome!.Action);
        Assert.True(workspace.LastOutcome.Succeeded);
    }

    [Fact]
    public void DiagnosticPresentationSanitizesFullTextButKeepsHeaderConstantSized()
    {
        string text = "Rejected: Source changed externally: " + string.Concat(Enumerable.Repeat("😀\0\uD800x\uDC00", 4000)) + "TAIL";
        var presentation = new ConfigurationDiagnosticPresentation(text);
        Assert.Equal("Source conflict — open Help.", presentation.Summary);
        Assert.InRange(presentation.Summary.Length, 1, 32);
        Assert.DoesNotContain('\0', presentation.Details); Assert.EndsWith("TAIL", presentation.Details);
        Assert.Contains("😀", presentation.Details); Assert.Contains('\u2400', presentation.Details);
        Assert.True(new UTF8Encoding(false, true).GetByteCount(presentation.Details) > 0);
        Assert.True(presentation.Details.Length > 20000);
    }
}
