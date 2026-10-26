using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using BrutalBreakout.Editor;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Presentation;
using Xunit;

public sealed class ConfigurationLiveTests
{
    private static ConfigurationWorkspace.Document Doc(ConfigurationWorkspace w, string section) => ConfigurationWorkspaceTests.Doc(w, section);
    private static void WithHost(Action<ConfigurationWorkspace, GameplaySessionHost> test) => ConfigurationWorkspaceTests.WithCopy(root =>
    {
        var paths = ConfigurationWorkspaceTests.Paths(root);
        var json = JsonNode.Parse(File.ReadAllText(paths["shmup"]))!;
        json["capacity"]!["maxBalls"] = 8; json["capacity"]!["maxBricks"] = 16;
        File.WriteAllText(paths["shmup"], json.ToJsonString());
        var workspace = new ConfigurationWorkspace(paths, true);
        using var host = new GameplaySessionHost(PhysicsConfiguration.Load(paths["physics"]).Settings,
            GameplayConfiguration.Load(paths["gameplay"]).Settings, ShmupConfiguration.Load(paths["shmup"]).Settings,
            InputConfiguration.Load(paths["input"]).Settings, ExecutionConfiguration.Load(paths["execution"]).Settings,
            GameplayMode.Shmup, BallTrailConfiguration.Load(paths["trails"]).Settings);
        workspace.PreflightLive = c => host.ValidateTuning(c.Get<LoadedPhysicsConfiguration>("physics")!.Settings,
            c.Get<LoadedGameplayConfiguration>("gameplay")!.Settings, c.Get<LoadedShmupConfiguration>("shmup")!.Settings,
            c.Get<LoadedInputConfiguration>("input")!.Settings, c.Get<LoadedExecutionConfiguration>("execution")!.Settings,
            c.Get<LoadedBallTrailConfiguration>("trails")!.Settings);
        test(workspace, host);
    });
    private static void Apply(GameplaySessionHost host, ConfigurationWorkspace.Candidate c)
    {
        var p = c.Get<LoadedPhysicsConfiguration>("physics")!.Settings;
        var g = c.Get<LoadedGameplayConfiguration>("gameplay")!.Settings;
        var s = c.Get<LoadedShmupConfiguration>("shmup")!.Settings;
        var i = c.Get<LoadedInputConfiguration>("input")!.Settings;
        var e = c.Get<LoadedExecutionConfiguration>("execution")!.Settings;
        var t = c.Get<LoadedBallTrailConfiguration>("trails")!.Settings;
        if (c.ResetRequired) host.Reconfigure(p, g, s, i, e, t);
        else host.ApplyTuning(p, g, s, i, e, t);
    }

    [Fact]
    public void AutomaticPausedMixedLiveFutureResetAndRelaunchRetainsRunHistoryAndExactSettingsReferences()
    {
        WithHost((w, host) =>
        {
            var session = host.Shmup!; var simulation = session.Simulation; var history = session.Trails!;
            var view = new ShmupViewport(1280, 720, 1280, 720, session.Settings.Arena);
            var released = new InputSnapshot { Focused = true };
            session.Advance(released, 0, view);
            for (int i = 0; i < 12; i++) session.Advance(released with { KeyboardPrimary = true }, .01, view);
            Assert.NotEmpty(simulation.Snapshot.Balls.ToArray()); Assert.True(history.PointCount > 1);
            host.TogglePause();
            var before = simulation.Snapshot; int points = history.PointCount, ticks = session.TotalTicks;
            var shmup = Doc(w, "shmup");
            shmup.Set("ball.speed", 800);
            shmup.Set("capacity.maxBalls", 10);
            shmup.Set("avatar.health", 50);
            shmup.Set("arena.initialPlayAreaWidth", 500);
            Doc(w, "trails").Set("widthRadiusRatio", 2);
            Doc(w, "input").Set("stickDeadzone", .3);
            Doc(w, "overlay").Set("fontSize", 23);
            Doc(w, "window").Set("width", 960);
            Doc(w, "window").Set("vsync", false);
            Doc(w, "window").Set("frameRateLimit", 144);
            ConfigurationWorkspace.Candidate? candidate = null;
            w.Process(c => { candidate = c; Apply(host, c); });
            Assert.NotNull(candidate); Assert.False(candidate.ResetRequired);
            Assert.Same(session, host.Shmup); Assert.Same(simulation, host.Shmup!.Simulation); Assert.Same(history, session.Trails);
            Assert.True(host.Paused); Assert.Equal(ticks, session.TotalTicks);
            Assert.Equal(before.Tick, simulation.Snapshot.Tick); Assert.Equal(before.TimeSeconds, simulation.Snapshot.TimeSeconds);
            Assert.Equal(before.PlayerPosition, simulation.Snapshot.PlayerPosition); Assert.Equal(50, simulation.Snapshot.PlayerHealth);
            Assert.Equal(points, history.PointCount); Assert.Equal(800, session.Settings.Ball.Speed);
            Assert.Equal(8, session.Settings.Capacity.MaxBalls); Assert.Equal(432, simulation.Snapshot.Width); Assert.Equal(432, session.RunMinimumWidth);
            Assert.Equal(500, session.Settings.Arena.InitialPlayAreaWidth);
            Assert.Same(candidate.Get<LoadedShmupConfiguration>("shmup")!.Settings, session.Settings);
            Assert.Same(session.Settings, simulation.Settings);
            Assert.Same(candidate.Get<LoadedInputConfiguration>("input")!.Settings, session.Router.Settings);
            Assert.Same(candidate.Get<LoadedExecutionConfiguration>("execution")!.Settings, simulation.Execution);
            Assert.Same(candidate.Get<LoadedBallTrailConfiguration>("trails")!.Settings, history.Settings);
            Assert.Equal(960, candidate.Get<LoadedWindowConfiguration>("window")!.Settings.Width);
            Assert.False(candidate.Get<LoadedWindowConfiguration>("window")!.Settings.VSync);
            Assert.Equal(144, candidate.Get<LoadedWindowConfiguration>("window")!.Settings.FrameRateLimit);
            Assert.Single(w.PendingResetFields); Assert.Single(w.PendingRelaunchFields);
            Assert.True(shmup.Pending); Assert.True(shmup.Unsaved);
            w.Request(ConfigurationAction.Apply); w.Process(c => Apply(host, c));
            Assert.NotSame(session, host.Shmup); Assert.True(host.Paused);
            Assert.Equal(10, host.Shmup!.Settings.Capacity.MaxBalls); Assert.Equal(0, host.Shmup.Simulation.Snapshot.Tick);
            Assert.Empty(w.PendingResetFields); Assert.Single(w.PendingRelaunchFields);
        });
    }

    [Fact]
    public void PausedPresentationLiveDiscardSaveReloadAndInvalidCapRetainLastValidPublication()
    {
        WithHost((w, host) =>
        {
            host.TogglePause();
            var session = host.Shmup!;
            long tick = session.Simulation.Snapshot.Tick;
            var document = Doc(w, "window");
            string source = File.ReadAllText(document.Path);
            WindowSettings current = WindowConfiguration.Load(document.Path).Settings;
            void Commit(ConfigurationWorkspace.Candidate c)
            {
                Apply(host, c);
                current = c.Get<LoadedWindowConfiguration>("window")!.Settings;
                Assert.Same(session, host.Shmup);
                Assert.True(host.Paused); Assert.Equal(tick, session.Simulation.Snapshot.Tick);
                Assert.False(c.ResetRequired);
            }
            Assert.Equal(ConfigurationEffect.Live, ConfigurationImpact.For("window.vsync").Effect);
            Assert.Equal(ConfigurationEffect.Live, ConfigurationImpact.For("window.frameRateLimit").Effect);
            document.Set("vsync", false); document.Set("frameRateLimit", 144);
            w.Process(Commit);
            Assert.False(current.VSync); Assert.Equal(144, current.FrameRateLimit);
            Assert.Equal(source, File.ReadAllText(document.Path));
            foreach (double invalid in new[] { -1d, 1.5, 1001 })
            {
                var before = current;
                document.Set("frameRateLimit", invalid); w.Process(Commit);
                Assert.Same(before, current); Assert.False(current.VSync); Assert.Equal(144, current.FrameRateLimit);
                Assert.StartsWith("Rejected:", w.Status);
            }
            w.Request(ConfigurationAction.Discard); w.Process(Commit);
            Assert.True(current.VSync); Assert.Equal(0, current.FrameRateLimit);
            document.Set("vsync", false); document.Set("frameRateLimit", 60); w.Process(Commit);
            w.Request(ConfigurationAction.Save); w.Process(Commit);
            var saved = WindowConfiguration.Load(document.Path).Settings;
            Assert.False(saved.VSync); Assert.Equal(60, saved.FrameRateLimit);
            document.Set("vsync", true); document.Set("frameRateLimit", 0); w.Process(Commit);
            w.Request(ConfigurationAction.Reload); w.Process(Commit);
            Assert.False(current.VSync); Assert.Equal(60, current.FrameRateLimit);
            Assert.Empty(w.PendingResetFields); Assert.Empty(w.PendingRelaunchFields);
        });
    }

    [Fact]
    public void PresentationCapabilityPreflightRejectsBeforeWorldCommit()
    {
        WithHost((w, host) =>
        {
            var session = host.Shmup!;
            w.PreflightLive = c =>
            {
                if (!c.Get<LoadedWindowConfiguration>("window")!.Settings.VSync)
                    throw new NotSupportedException("Immediate unsupported");
            };
            Doc(w, "window").Set("vsync", false);
            int commits = 0;
            w.Process(c => { commits++; Apply(host, c); });
            Assert.Equal(0, commits); Assert.Same(session, host.Shmup);
            Assert.True(Doc(w, "window").Pending);
            Assert.StartsWith("Rejected:", w.Status);
        });
    }

    [Fact]
    public void InvalidLiveIsCachedAndDoesNotBlockOtherValidDocumentOrLeakPendingGeometry()
    {
        WithHost((w, host) =>
        {
            var session = host.Shmup; double speed = session!.Settings.Ball.Speed;
            Doc(w, "shmup").Set("ball.speed", -1);
            Doc(w, "shmup").Set("ball.fireRate", 15);
            Doc(w, "shmup").Set("brick.size", 100);
            Doc(w, "trails").Set("opacity", .25);
            int calls = 0, loads = 0;
            var doc = Doc(w, "shmup"); var load = doc.Load;
            doc.Load = text => { loads++; return load(text); };
            w.Process(c => { calls++; Apply(host, c); });
            Assert.Equal(1, calls); Assert.Same(session, host.Shmup);
            Assert.Equal(speed, session.Settings.Ball.Speed); Assert.Equal(15, session.Settings.Ball.FireRate);
            Assert.Equal(28.8, session.Settings.Brick.Size);
            Assert.Equal(.25, session.Trails!.Settings.Opacity); Assert.StartsWith("Rejected:", w.Status);
            int attemptedLoads = loads;
            for (int frame = 0; frame < 20; frame++) w.Process(_ => calls++);
            Assert.Equal(attemptedLoads, loads); Assert.Equal(1, calls);
            doc.Set("ball.speed", 700);
            w.Process(c => Apply(host, c));
            Assert.Equal(700, session.Settings.Ball.Speed); Assert.Single(w.PendingResetFields);
        });
    }

    [Fact]
    public void LiveRadiusUsesCurrentWidthAndUnrelatedValidTuningStillAppliesThenRetriesAfterWidening()
    {
        WithHost((w, host) =>
        {
            var session = host.Shmup!;
            Doc(w, "shmup").Set("arena.initialPlayAreaWidth", 800);
            Doc(w, "shmup").Set("avatar.radius", 250);
            Doc(w, "trails").Set("opacity", .2);
            w.Process(c => Apply(host, c));
            Assert.Same(session, host.Shmup); Assert.Equal(10.8, session.Settings.Avatar.Radius);
            Assert.Equal(.2, session.Trails!.Settings.Opacity);
            Assert.Contains("widen", w.Status); Assert.Empty(w.PendingResetFields);
            session.Simulation.Widen(800);
            w.LiveContextRevision = "widened";
            w.Process(c => Apply(host, c));
            Assert.Equal(250, session.Settings.Avatar.Radius); Assert.Same(session, host.Shmup);
        });
    }

    [Fact]
    public void InactiveModeGeometryStoresNextSettingsWithoutResettingActiveShmup()
    {
        WithHost((w, host) =>
        {
            var session = host.Shmup; var publication = session!.Simulation.Snapshot;
            w.InactiveModeSection = "gameplay";
            Doc(w, "gameplay").Set("arena.wallThickness", 18);
            w.Process(c => { Assert.False(c.ResetRequired); Apply(host, c); });
            Assert.Same(session, host.Shmup); Assert.Equal(publication.Tick, session.Simulation.Snapshot.Tick);
            Assert.False(Doc(w, "gameplay").Pending); Assert.Empty(w.PendingResetFields);
            host.Switch(GameplayMode.Breakout);
            Assert.Equal(18, host.Breakout!.Simulation.Gameplay.Arena.WallThickness);
        });
    }

    [Fact]
    public void SaveDoesNotApplyStructuralFieldsAndDiscardSourceLiveValuesApplyWithoutReset()
    {
        WithHost((w, host) =>
        {
            var session = host.Shmup!;
            Doc(w, "shmup").Set("ball.speed", 700);
            w.Process(c => Apply(host, c)); Assert.Equal(700, session.Settings.Ball.Speed);
            w.Request(ConfigurationAction.Discard); w.Process(c => Apply(host, c));
            Assert.Equal(576, session.Settings.Ball.Speed); Assert.Same(session, host.Shmup);
            Doc(w, "shmup").Set("capacity.maxBalls", 10);
            w.Request(ConfigurationAction.Save); w.Process(c => Apply(host, c));
            Assert.Same(session, host.Shmup); Assert.Equal(8, session.Settings.Capacity.MaxBalls);
            Assert.False(Doc(w, "shmup").Unsaved); Assert.True(Doc(w, "shmup").Pending);
        });
    }
}
