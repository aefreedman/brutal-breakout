using BrutalBreakout.Configuration;
using BrutalBreakout.Editor;
using BrutalBreakout.Presentation;
using Xunit;

public sealed class ConfigurationFailurePathTests
{
    private static GameplaySessionHost CreateHost(ConfigurationWorkspace.Candidate candidate, GameplayMode mode) => new(
        candidate.Get<LoadedPhysicsConfiguration>("physics")!.Settings,
        candidate.Get<LoadedGameplayConfiguration>("gameplay")!.Settings,
        candidate.Get<LoadedShmupConfiguration>("shmup")!.Settings,
        candidate.Get<LoadedInputConfiguration>("input")!.Settings,
        candidate.Get<LoadedExecutionConfiguration>("execution")!.Settings, mode,
        candidate.Get<LoadedBallTrailConfiguration>("trails")!.Settings);

    [Theory]
    [InlineData(GameplayMode.Breakout)] [InlineData(GameplayMode.Shmup)]
    public void CommittedRetirementFailureCompletesLoadedAndAppliedPublication(GameplayMode mode)
    {
        var workspace = ConfigurationWorkspaceTests.Create();
        ConfigurationWorkspaceTests.Doc(workspace, "shmup").Draft["capacity"]!["maxBalls"] = 16;
        ConfigurationWorkspaceTests.Doc(workspace, "shmup").Draft["capacity"]!["maxBricks"] = 32;
        ConfigurationWorkspaceTests.Doc(workspace, "execution").Draft["workerCount"] = 1;
        ConfigurationWorkspaceTests.Doc(workspace, "execution").Draft["physicsWorkerCount"] = 1;
        GameplaySessionHost? created = null;
        LoadedGameplayConfiguration? publishedGameplay = null;
        LoadedShmupConfiguration? publishedShmup = null;
        workspace.Request(ConfigurationAction.Apply);
        workspace.Process(candidate =>
        {
            created = CreateHost(candidate, mode);
            publishedGameplay = candidate.Get<LoadedGameplayConfiguration>("gameplay");
            publishedShmup = candidate.Get<LoadedShmupConfiguration>("shmup");
        });
        using var host = Assert.IsType<GameplaySessionHost>(created);
        host.TogglePause();
        var oldBreakout = host.Breakout; var oldShmup = host.Shmup;
        Exception? wrongThread = null;
        var thread = new Thread(() => wrongThread = Record.Exception(() => host.Switch(mode)));
        thread.Start(); thread.Join();
        Assert.IsType<InvalidOperationException>(wrongThread);
        Assert.Same(oldBreakout, host.Breakout); Assert.Same(oldShmup, host.Shmup);
        int retired = 0;
        host.RetireSession = resource =>
        {
            resource.Dispose(); // fail AFTER real cleanup, never leave a test native world behind
            retired++;
            throw new IOException("injected retirement failure " + new string('x', 2000));
        };
        var gameplay = ConfigurationWorkspaceTests.Doc(workspace, "gameplay");
        var shmup = ConfigurationWorkspaceTests.Doc(workspace, "shmup");
        gameplay.Draft["ball"]!["speed"] = publishedGameplay!.Settings.Ball.Speed + 1;
        shmup.Draft["ball"]!["speed"] = 600.5;
        workspace.Request(ConfigurationAction.Apply);
        workspace.Process(candidate =>
        {
            var nextPhysics = candidate.Get<LoadedPhysicsConfiguration>("physics")!;
            var nextGameplay = candidate.Get<LoadedGameplayConfiguration>("gameplay")!;
            var nextShmup = candidate.Get<LoadedShmupConfiguration>("shmup")!;
            // Same commit-then-publication ordering as Program: this call must not throw after commit.
            host.Reconfigure(nextPhysics.Settings, nextGameplay.Settings, nextShmup.Settings,
                candidate.Get<LoadedInputConfiguration>("input")!.Settings,
                candidate.Get<LoadedExecutionConfiguration>("execution")!.Settings,
                candidate.Get<LoadedBallTrailConfiguration>("trails")!.Settings);
            publishedGameplay = nextGameplay; publishedShmup = nextShmup;
            workspace.RuntimeDiagnostic = host.RetirementDiagnostic;
        });
        Assert.Equal(1, retired);
        Assert.StartsWith("Runtime applied", workspace.Status);
        Assert.False(gameplay.Pending); Assert.False(shmup.Pending);
        Assert.True(gameplay.Unsaved); Assert.True(shmup.Unsaved);
        Assert.Equal(mode, host.Mode); Assert.True(host.Paused);
        Assert.Contains("retirement cleanup warning", workspace.RuntimeDiagnostic);
        Assert.Contains("injected retirement failure", workspace.RuntimeDiagnostic);
        Assert.True(workspace.RuntimeDiagnostic!.Length < 650);
        if (mode == GameplayMode.Breakout)
        {
            Assert.NotSame(oldBreakout, host.Breakout);
            Assert.Same(publishedGameplay!.Settings, host.Breakout!.Simulation.Gameplay);
            host.Breakout.Advance(default, 0, new(1280, 720, 1280, 720, publishedGameplay.Settings.Arena));
        }
        else
        {
            Assert.NotSame(oldShmup, host.Shmup);
            Assert.Same(publishedShmup!.Settings, host.Shmup!.Settings);
            host.Shmup.Advance(default, 0, new(1280, 720, 1280, 720, publishedShmup.Settings.Arena));
        }
    }

    [Fact]
    public void PartialSaveAndRollbackFailuresSurviveEveryTemporaryCleanupFailure()
    {
        ConfigurationWorkspaceTests.WithCopy(root =>
        {
            var workspace = new ConfigurationWorkspace(ConfigurationWorkspaceTests.Paths(root), true);
            var input = ConfigurationWorkspaceTests.Doc(workspace, "input"); input.Draft["stickDeadzone"] = .23;
            ConfigurationWorkspaceTests.Doc(workspace, "trails").Draft["opacity"] = .31;
            ConfigurationWorkspaceTests.Doc(workspace, "colors").Draft["name"] = "Changed palette";
            int replacements = 0;
            workspace.ReplaceFile = (temp, target) =>
            {
                replacements++;
                if (replacements is 2 or 3) throw new IOException("replacement fault " + replacements);
                File.Replace(temp, target, null);
            };
            var attempted = new List<string>();
            workspace.DeleteTemporaryFile = path =>
            {
                attempted.Add(path);
                if (attempted.Count <= 2) throw new IOException("cleanup fault " + attempted.Count);
                File.Delete(path);
            };
            workspace.Request(ConfigurationAction.Save); workspace.Process(_ => throw new Exception("Unexpected Apply"));
            Assert.Equal(3, replacements); Assert.Equal(4, attempted.Count); Assert.Equal(4, attempted.Distinct().Count());
            Assert.StartsWith("Rejected: PARTIAL SAVE", workspace.Status);
            Assert.Contains(input.Path, workspace.Status);
            Assert.Contains("replacement fault 2", workspace.Status); Assert.Contains("replacement fault 3", workspace.Status);
            Assert.Contains("cleanup fault 1", workspace.Status); Assert.Contains("cleanup fault 2", workspace.Status);
            Assert.Contains(attempted[0], workspace.Status); Assert.Contains(attempted[1], workspace.Status);
            Assert.True(input.Unsaved); Assert.True(input.Pending);
            Assert.Equal(.23f, InputConfiguration.Load(input.Path).Settings.StickDeadzone); // truthful partial commit
        });
    }

    [Fact]
    public void CommittedSaveWithCleanupWarningStillPublishesSavedButNotAppliedState()
    {
        ConfigurationWorkspaceTests.WithCopy(root =>
        {
            var workspace = new ConfigurationWorkspace(ConfigurationWorkspaceTests.Paths(root), true);
            var input = ConfigurationWorkspaceTests.Doc(workspace, "input"); input.Draft["stickDeadzone"] = .23;
            var trails = ConfigurationWorkspaceTests.Doc(workspace, "trails"); trails.Draft["opacity"] = .31;
            var attempted = new List<string>();
            workspace.DeleteTemporaryFile = path =>
            {
                attempted.Add(path);
                if (attempted.Count == 1) throw new IOException("committed cleanup fault");
                File.Delete(path);
            };
            workspace.Request(ConfigurationAction.Save); workspace.Process(_ => throw new Exception("Unexpected Apply"));
            Assert.Equal(2, attempted.Count);
            Assert.StartsWith("Saved changed sources", workspace.Status);
            Assert.Contains("Temporary cleanup warning", workspace.Status); Assert.Contains("committed cleanup fault", workspace.Status);
            Assert.False(input.Unsaved); Assert.False(trails.Unsaved); Assert.True(input.Pending); Assert.True(trails.Pending);
            Assert.Equal(.23f, InputConfiguration.Load(input.Path).Settings.StickDeadzone);
        });
    }

    [Fact]
    public void RestoredSaveFailureRetainsPrimaryOutcomeWhenAllTemporaryDeletesFail()
    {
        ConfigurationWorkspaceTests.WithCopy(root =>
        {
            var workspace = new ConfigurationWorkspace(ConfigurationWorkspaceTests.Paths(root), true);
            var input = ConfigurationWorkspaceTests.Doc(workspace, "input"); string original = File.ReadAllText(input.Path);
            input.Draft["stickDeadzone"] = .23;
            ConfigurationWorkspaceTests.Doc(workspace, "trails").Draft["opacity"] = .31;
            int replacements = 0, deletes = 0;
            workspace.ReplaceFile = (temp, target) =>
            {
                if (++replacements == 2) throw new IOException("primary replacement failure");
                File.Replace(temp, target, null);
            };
            workspace.DeleteTemporaryFile = _ => { deletes++; throw new IOException("secondary deletion failure"); };
            workspace.Request(ConfigurationAction.Save); workspace.Process(_ => throw new Exception("Unexpected Apply"));
            Assert.Equal(3, deletes);
            Assert.StartsWith("Rejected: Save failed; originals retained/restored.", workspace.Status);
            Assert.Contains("primary replacement failure", workspace.Status); Assert.Contains("secondary deletion failure", workspace.Status);
            Assert.DoesNotContain("PARTIAL SAVE", workspace.Status);
            Assert.Equal(original, File.ReadAllText(input.Path)); Assert.True(input.Unsaved);
        });
    }
}
