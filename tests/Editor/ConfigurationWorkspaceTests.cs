using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using BrutalBreakout.Editor;
using Xunit;

public sealed class ConfigurationWorkspaceTests
{
    internal static Dictionary<string, string> Paths(string? root = null) => new[]
    {
        "window", "physics", "execution", "gameplay", "shmup", "input", "trails", "colors", "style", "audio", "overlay", "inspector"
    }.ToDictionary(s => s, s => Path.Combine(root ?? AppContext.BaseDirectory, "Config", s + ".json"));
    internal static ConfigurationWorkspace Create() => new(Paths(), true);
    internal static ConfigurationWorkspace.Document Doc(ConfigurationWorkspace workspace, string section) => workspace.Documents.Single(d => d.Section == section);
    private static void Process(ConfigurationWorkspace workspace, ConfigurationAction action, Action<ConfigurationWorkspace.Candidate>? apply = null)
    {
        workspace.Request(action); workspace.Process(apply ?? (_ => throw new InvalidOperationException("Unexpected Apply")));
    }

    [Fact]
    public void DraftsStartFromSelectedSourcesAndQueuedApplyDoesNotSaveOrMutateCapturedSettings()
    {
        var workspace = Create(); var shmup = Doc(workspace, "shmup");
        Assert.All(workspace.Documents, d => { Assert.False(d.Pending); Assert.False(d.Unsaved); Assert.True(Path.IsPathFullyQualified(d.Path)); });
        string source = File.ReadAllText(shmup.Path);
        shmup.Draft["ball"]!["speed"] = 600.5; // lexical integer in the source is really a double schema field
        Assert.True(shmup.Pending); Assert.True(shmup.Unsaved);
        ConfigurationWorkspace.Candidate? captured = null;
        workspace.Request(ConfigurationAction.Apply);
        Assert.Null(captured);
        workspace.Process(c => captured = c);
        Assert.Equal(600.5, captured!.Get<LoadedShmupConfiguration>("shmup")!.Settings.Ball.Speed);
        Assert.False(shmup.Pending); Assert.True(shmup.Unsaved);
        Assert.Equal(source, File.ReadAllText(shmup.Path));
        shmup.Draft["ball"]!["speed"] = 610.5;
        Assert.Equal(600.5, captured.Get<LoadedShmupConfiguration>("shmup")!.Settings.Ball.Speed);
        Process(workspace, ConfigurationAction.Discard);
        Assert.False(shmup.Unsaved); Assert.True(shmup.Pending); // source and applied snapshots intentionally differ
    }

    [Theory]
    [InlineData("speed", -1)] [InlineData("damage", 1.5)]
    public void InvalidDraftNeverCallsApplyAndRemainsDirty(string key, double value)
    {
        var workspace = Create(); var d = Doc(workspace, "shmup"); d.Draft["ball"]![key] = value;
        Process(workspace, ConfigurationAction.Apply);
        Assert.StartsWith("Rejected:", workspace.Status); Assert.True(d.Pending); Assert.True(d.Unsaved);
    }

    [Fact]
    public void CrossFileTrailCapacityAndPhysicsValidationAreIdenticalToStartup()
    {
        var workspace = Create();
        Doc(workspace, "trails").Draft["maxRenderedSegments"] = 1;
        Process(workspace, ConfigurationAction.Apply);
        Assert.StartsWith("Rejected:", workspace.Status);
        Process(workspace, ConfigurationAction.Discard);
        Doc(workspace, "physics").Draft["updateHz"] = 1;
        Process(workspace, ConfigurationAction.Apply);
        Assert.StartsWith("Rejected:", workspace.Status);
    }

    [Fact]
    public void RelaunchOnlyDraftsAreNotReportedAppliedAndDoNotLeakIntoRuntimeCandidate()
    {
        var workspace = Create(); var window = Doc(workspace, "window");
        int original = window.Draft["width"]!.GetValue<int>(); window.Draft["width"] = original + 1;
        Process(workspace, ConfigurationAction.Apply, c => Assert.Equal(original, c.Get<LoadedWindowConfiguration>("window")!.Settings.Width));
        Assert.True(window.Pending); Assert.True(window.Unsaved); Assert.Contains("NOT applied", workspace.Status);
    }

    [Fact]
    public void CandidatePrepareFailureLeavesAppliedSnapshotsUntouched()
    {
        var workspace = Create(); var d = Doc(workspace, "shmup"); d.Draft["ball"]!["fireRate"] = 13;
        Process(workspace, ConfigurationAction.Apply, _ => throw new InvalidOperationException("injected prepare failure"));
        Assert.Contains("injected prepare failure", workspace.Status); Assert.True(d.Pending);
    }

    [Fact]
    public void AllShapesAndQuarterTurnsPassProductionValidation()
    {
        var workspace = Create();
        Process(workspace, ConfigurationAction.Apply, c =>
        {
            var brick = c.Get<LoadedShmupConfiguration>("shmup")!.Settings.Brick;
            Assert.Equal(5, brick.Shapes.Length); Assert.Equal(new[] { 0, 1, 2, 3 }, brick.QuarterTurns);
        });
        Assert.StartsWith("Runtime applied", workspace.Status);
    }

    [Fact]
    public void SaveIsExplicitExactPathRelativeAssetsAndAppliedStateRemainSeparate()
    {
        WithCopy(root =>
        {
            var workspace = new ConfigurationWorkspace(Paths(root), true);
            var audio = Doc(workspace, "audio"); string originalCue = audio.Draft["cues"]!["launch"]!.GetValue<string>();
            audio.Draft["masterGain"] = .31;
            var input = Doc(workspace, "input"); input.Draft["stickDeadzone"] = .23;
            Process(workspace, ConfigurationAction.Save);
            Assert.StartsWith("Saved", workspace.Status);
            Assert.All(workspace.Documents, d => Assert.False(d.Unsaved));
            Assert.True(audio.Pending); Assert.True(input.Pending);
            Assert.Equal(originalCue, JsonNode.Parse(File.ReadAllText(audio.Path))!["cues"]!["launch"]!.GetValue<string>());
            Assert.Equal(.23f, InputConfiguration.Load(input.Path).Settings.StickDeadzone);
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        });
    }

    [Fact]
    public void ExternalChangesConflictAndValidatedReloadResolvesWithoutApplying()
    {
        WithCopy(root =>
        {
            var workspace = new ConfigurationWorkspace(Paths(root), true); var input = Doc(workspace, "input");
            input.Draft["stickDeadzone"] = .23;
            File.WriteAllText(input.Path, "{\"stickDeadzone\":0.24}");
            Process(workspace, ConfigurationAction.Save);
            Assert.Contains("changed externally", workspace.Status); Assert.True(input.Unsaved);
            Assert.Equal(.24f, InputConfiguration.Load(input.Path).Settings.StickDeadzone);
            Process(workspace, ConfigurationAction.Reload);
            Assert.False(input.Unsaved); Assert.True(input.Pending); Assert.Equal(.24, input.Draft["stickDeadzone"]!.GetValue<double>());
        });
    }

    [Fact]
    public void InvalidCrossFileSaveNeverWritesAnyDocument()
    {
        WithCopy(root =>
        {
            var workspace = new ConfigurationWorkspace(Paths(root), true);
            var original = workspace.Documents.ToDictionary(d => d.Path, d => File.ReadAllText(d.Path));
            Doc(workspace, "input").Draft["stickDeadzone"] = .23;
            Doc(workspace, "physics").Draft["updateHz"] = 1;
            Process(workspace, ConfigurationAction.Save);
            Assert.StartsWith("Rejected:", workspace.Status);
            Assert.All(original, p => Assert.Equal(p.Value, File.ReadAllText(p.Key)));
        });
    }

    [Fact]
    public void AtomicReplacementFailureRollsBackAlreadyReplacedFilesAndCleansTemps()
    {
        WithCopy(root =>
        {
            var workspace = new ConfigurationWorkspace(Paths(root), true);
            var original = workspace.Documents.ToDictionary(d => d.Path, d => File.ReadAllText(d.Path));
            Doc(workspace, "input").Draft["stickDeadzone"] = .23;
            Doc(workspace, "trails").Draft["opacity"] = .31;
            using (File.Open(Doc(workspace, "trails").Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Process(workspace, ConfigurationAction.Save);
            Assert.StartsWith("Rejected:", workspace.Status);
            Assert.All(original, p => Assert.Equal(p.Value, File.ReadAllText(p.Key)));
            Assert.True(Doc(workspace, "input").Unsaved); Assert.True(Doc(workspace, "trails").Unsaved);
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        });
    }

    [Fact]
    public void ApplyDoesNotReadOrWriteSelectedJsonFiles()
    {
        WithCopy(root =>
        {
            var workspace = new ConfigurationWorkspace(Paths(root), true);
            foreach (var d in workspace.Documents) File.Delete(d.Path);
            Doc(workspace, "shmup").Draft["ball"]!["speed"] = 600.5;
            bool applied = false; Process(workspace, ConfigurationAction.Apply, _ => applied = true);
            Assert.True(applied); Assert.Empty(Directory.GetFiles(Path.Combine(root, "Config")));
        });
    }

    [Fact]
    public void ReloadRejectsInvalidDiskBundleWithoutLosingDrafts()
    {
        WithCopy(root =>
        {
            var workspace = new ConfigurationWorkspace(Paths(root), true);
            var input = Doc(workspace, "input"); input.Draft["stickDeadzone"] = .23;
            File.WriteAllText(input.Path, "{}");
            Process(workspace, ConfigurationAction.Reload);
            Assert.StartsWith("Rejected:", workspace.Status); Assert.Equal(.23, input.Draft["stickDeadzone"]!.GetValue<double>());
        });
    }

    internal static void WithCopy(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "configuration-workspace-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string directory in new[] { "Config", "Assets" })
                foreach (string source in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, directory), "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(root, Path.GetRelativePath(AppContext.BaseDirectory, source));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
                }
            test(root);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
