using CrushKit.Brutal.Vulkan;
using CrushKit.Brutal.Audio;
#if BREAKOUT_EDITOR
using CrushKit.Brutal.ImGui;
using BrutalBreakout.Editor;
#endif
using System.Diagnostics;
using BrutalBreakout.Audio;
using BrutalBreakout.Presentation.Overlay;
using BrutalBreakout.Configuration;
using Brutal.GlfwApi;
using Brutal.Numerics;
using CrushKit.Brutal.Draw2D;
using BrutalBreakout.Presentation;
using BrutalBreakout.Platform.Input;

var shmupSmokeTest = args.Contains("--shmup-smoke-test");
var startShmup = args.Contains("--shmup") || shmupSmokeTest;
var gameplaySmokeTest = args.Contains("--gameplay-smoke-test");
var showcase = args.Contains("--showcase");
var debugUi = args.Contains("--debug-ui");
var overlaySmokeTest = args.Contains("--overlay-smoke-test");
var muteAudio = args.Contains("--mute") || shmupSmokeTest;
var hiddenSmokeTest = args.Contains("--smoke-test");
var lifecycleSmokeTest = args.Contains("--lifecycle-smoke-test");
var resizeStressTest = args.Contains("--visible-resize-stress");
if (new[] { hiddenSmokeTest, gameplaySmokeTest, shmupSmokeTest, lifecycleSmokeTest, resizeStressTest, showcase }.Count(enabled => enabled) > 1)
    throw new ArgumentException("Select only one GPU test mode.");
var smokeTest = hiddenSmokeTest || lifecycleSmokeTest;
var gpuTest = smokeTest || resizeStressTest || gameplaySmokeTest || shmupSmokeTest;
var gallery = showcase || smokeTest || resizeStressTest;
if (overlaySmokeTest && !hiddenSmokeTest && !gameplaySmokeTest)
    throw new ArgumentException("--overlay-smoke-test requires a hidden GPU test mode.");
#if !BREAKOUT_EDITOR
if (debugUi || overlaySmokeTest || args.Any(a => a.StartsWith("--inspector-config", StringComparison.Ordinal)))
    throw new ArgumentException("Inspector options require a build with EnableEditor=true.");
#endif
var cleanupSelfTest = args.Contains("--cleanup-self-test");
if (smokeTest || cleanupSelfTest) VerifyCleanupFailureHandling();
if (cleanupSelfTest) return;
LoadedWindowConfiguration windowConfiguration;
LoadedRenderingConfiguration renderingConfiguration;
LoadedPhysicsConfiguration? physics;
LoadedExecutionConfiguration? execution;
LoadedGameplayConfiguration? gameplay;
LoadedShmupConfiguration? shmup;
LoadedBallTrailConfiguration? trails;
LoadedInputConfiguration? input;
LoadedColorSchemeConfiguration? colors;
LoadedPresentationStyleConfiguration? style;
LoadedAudioConfiguration? audioConfiguration;
LoadedOverlayConfiguration? overlays;
#if BREAKOUT_EDITOR
LoadedInspectorConfiguration? inspectorConfiguration;
InspectorSettings uiSettings;
#endif
try
{
    windowConfiguration = WindowConfiguration.LoadForMode(args, AppContext.BaseDirectory, Environment.CurrentDirectory);
    renderingConfiguration = RenderingConfiguration.LoadForMode(args, AppContext.BaseDirectory, Environment.CurrentDirectory);
    physics = PhysicsConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    execution = ExecutionConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    gameplay = GameplayConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    shmup = ShmupConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    trails = BallTrailConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    input = InputConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    colors = ColorSchemeConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    style = PresentationStyleConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    audioConfiguration = AudioConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    // HUD and optional inspector settings are validated before native initialization.
#if BREAKOUT_EDITOR
    inspectorConfiguration = InspectorConfiguration.LoadForMode(args, true, AppContext.BaseDirectory, Environment.CurrentDirectory);
    uiSettings = inspectorConfiguration!.Settings;
    if (overlaySmokeTest)
    {
        // Explicit hidden-test fixture, validated before native startup; never a normal consumer requirement.
        var preview = new InspectorPreviewSettings(Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "debug.png"),
            imageSize: 96, maxTextureDimension: 2048, maxEncodedImageBytes: 1048576);
        uiSettings = new InspectorSettings(uiSettings.FontPath, uiSettings.FontSize, uiSettings.Visible,
            uiSettings.Left, uiSettings.Top, uiSettings.Width, uiSettings.Height, diagnosticsVisible: true, preview: preview);
    }
#endif
    overlays = OverlayConfiguration.LoadForMode(args, true, AppContext.BaseDirectory, Environment.CurrentDirectory);
#if BREAKOUT_EDITOR
    if (overlays is not null) InspectorHost.Validate(overlays.Settings);
#endif
    if (overlaySmokeTest && overlays?.Settings.Enabled != true)
        throw new ArgumentException("Overlay smoke requires enabled overlays.");
    ConfigurationValidation.Validate(physics, gameplay, shmup, trails, style, overlays, renderingConfiguration);
    Console.WriteLine(windowConfiguration.Describe());
    if (physics is not null) Console.WriteLine(physics.Describe());
    if (execution is not null) Console.WriteLine(execution.Describe());
    if (gameplay is not null) Console.WriteLine(gameplay.Describe());
    if (shmup is not null) Console.WriteLine(shmup.Describe());
    if (trails is not null) Console.WriteLine($"Trail configuration: enabled={trails.Settings.Enabled}; maxPointsPerBall={trails.Settings.MaxPointsPerBall}; durationSeconds={trails.Settings.DurationSeconds}; path={trails.Path}");
    if (input is not null) Console.WriteLine(input.Describe());
    if (colors is not null) Console.WriteLine(colors.Describe());
    if (style is not null) Console.WriteLine(style.Describe());
    if (audioConfiguration is not null) Console.WriteLine(FormattableString.Invariant($"Audio configuration: enabled={audioConfiguration.Settings.Enabled}; masterGain={audioConfiguration.Settings.MasterGain}; maxVoices={audioConfiguration.Settings.MaxVoices}; path={audioConfiguration.Path}"));
    if (overlays is not null) Console.WriteLine(FormattableString.Invariant($"Overlay configuration: enabled={overlays.Settings.Enabled}; fontSize={overlays.Settings.FontSize}; path={overlays.Path}"));
#if BREAKOUT_EDITOR
    Console.WriteLine($"Inspector configuration: visible={inspectorConfiguration!.Settings.Visible}; path={inspectorConfiguration.Path}");
#endif
}
catch (Exception error) when (error is InvalidDataException or ArgumentException)
{
    Console.Error.WriteLine(error.Message);
    Environment.ExitCode = 1;
    return; // Configuration failure must precede GLFW initialization or native world creation.
}
#if DEBUG
var validation = !args.Contains("--no-validation");
#else
var validation = args.Contains("--validation");
#endif
if (gpuTest) validation = true;

if (!Glfw.Init())
    throw new InvalidOperationException("Failed to initialize GLFW.");

try
{
    // Vulkan owns presentation; GLFW must not create an OpenGL context.
    Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
    if (gpuTest)
    {
        // Published BRUTAL GLFW hints take integer GLFW_FALSE (0).
        Glfw.WindowHint(GlfwWindowHint.Focused, 0);
        Glfw.WindowHint(GlfwWindowHint.FocusOnShow, 0);
        Glfw.WindowHint(GlfwWindowHint.Visible, hiddenSmokeTest || gameplaySmokeTest || shmupSmokeTest ? 0 : 1);
    }
    var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
    {
        Title = gallery ? "Brutal Breakout - primitive showcase" :
            "Brutal Breakout - Space / click / A: launch or catch below line; R: restart; Start: pause; Select/Share: panel; WASD / arrows / stick / mouse: aim",
        Size = new int2(windowConfiguration.Settings.Width, windowConfiguration.Settings.Height),
    });
    if (window.IsNull())
        throw new InvalidOperationException("Failed to create the game window.");

    Console.WriteLine($"Window created: {window.Size.X}x{window.Size.Y}; framebuffer={window.FramebufferSize.X}x{window.FramebufferSize.Y}.");
    var renderer = VulkanRenderer.Create(window, validation, windowConfiguration.Settings.VSync);
    ResourceLifetime.Run(() =>
    {
#if BREAKOUT_EDITOR
        var gameplayControls = new GameplayControls(inspectorConfiguration!.GameplayWindow, inspectorConfiguration.Theme);
        var configurationPaths = new Dictionary<string, string> { ["window"] = windowConfiguration.Path, ["rendering"] = renderingConfiguration.Path,
            ["inspector"] = inspectorConfiguration.Path };
        if (overlays is not null) configurationPaths["overlay"] = overlays.Path;
        if (physics is not null) configurationPaths["physics"] = physics.Path;
        if (execution is not null) configurationPaths["execution"] = execution.Path;
        if (gameplay is not null) configurationPaths["gameplay"] = gameplay.Path;
        if (shmup is not null) configurationPaths["shmup"] = shmup.Path;
        if (input is not null) configurationPaths["input"] = input.Path;
        if (trails is not null) configurationPaths["trails"] = trails.Path;
        if (colors is not null) configurationPaths["colors"] = colors.Path;
        if (style is not null) configurationPaths["style"] = style.Path;
        if (audioConfiguration is not null) configurationPaths["audio"] = audioConfiguration.Path;
        var configurationWorkspace = new ConfigurationWorkspace(configurationPaths, !gallery && !gameplaySmokeTest);
        var configurationFeedback = new ConfigurationFeedback(inspectorConfiguration.Feedback, inspectorConfiguration.Theme);
        var configurationControls = new ConfigurationControls(configurationWorkspace, inspectorConfiguration.ConfigurationWindow,
            inspectorConfiguration.Theme) { Feedback = configurationFeedback };
        string? audioApplyDiagnostic = null;
        var callbackFailure = new InvalidOperationException("Injected hidden-smoke inspector callback failure.");
        bool callbackFailureInjected = false, callbackFailureRecovered = false;
        var frameHistory = new FrameTimeHistory(inspectorConfiguration.HistoryCapacity);
        var uiInspector = new ImGuiInspector(uiSettings, () =>
        {
            gameplayControls.Draw();
            configurationControls.Draw();
            if (shmupSmokeTest && !callbackFailureInjected)
            {
                callbackFailureInjected = true;
                throw callbackFailure; // Balanced UI scopes; failure must occur before Vulkan image acquisition.
            }
        }, overlaySmokeTest ? null : frameHistory, inspectorConfiguration.Theme); // Same validated immutable recipe, including after retirement.
        gameplayControls.OpenConfiguration = () => { configurationControls.Visible = true; uiInspector.DiagnosticsVisible = false; };
        gameplayControls.ToggleDiagnostics = () =>
        {
            uiInspector.DiagnosticsVisible = !uiInspector.DiagnosticsVisible;
            if (uiInspector.DiagnosticsVisible) configurationControls.Visible = false;
        };
        if (overlays is not null) renderer.ConfigureOverlay(overlays.Settings, uiInspector);
#else
        if (overlays is not null) renderer.ConfigureOverlay(overlays.Settings);
#endif
        using var shapes = new ShapeDrawList();
        using var shmupRendering = new ShmupRendering(renderingConfiguration.Settings);
        Console.WriteLine(hiddenSmokeTest ? "Smoke: hidden GPU shape/resize validation (no minimize/restore)." :
            gallery ? "Close the window to exit. Expect twelve panels of Vulkan primitives (see README)." :
            "Breakout: leave the lower zone, hit something, return below the line to catch; release/repress to relaunch. R restarts. See README.");
        using var modes = !gallery && !gameplaySmokeTest ? new GameplaySessionHost(physics!.Settings, gameplay!.Settings,
            shmup!.Settings, input!.Settings, execution!.Settings, startShmup ? GameplayMode.Shmup : GameplayMode.Breakout, trails!.Settings) : null;
        if (modes?.Mode == GameplayMode.Shmup) Console.WriteLine(ShmupPresentation.Controls);
        // Hidden/showcase paths never create an audio device, even with enabled audio.json.
        string? audioDiagnostic = null;
        using var audio = modes is null ? null : new GameAudio(audioConfiguration!.Settings,
            muteAudio ? new NullAudioOutput<AudioCue>() : SdlMixerOutput<AudioCue>.Create(audioConfiguration.Settings.Output, out audioDiagnostic));
        if (audioDiagnostic is not null) Console.Error.WriteLine(audioDiagnostic);
        Console.WriteLine($"Audio host: nativeRequested={modes is not null && !muteAudio && audioConfiguration!.Settings.Enabled}; muted={muteAudio || modes is null}.");
        var poller = new GlfwInputPoller(window);
#if BREAKOUT_EDITOR
        var inspector = new InspectorToggle(shmupSmokeTest || debugUi || inspectorConfiguration!.Settings.Visible);
        Console.WriteLine("F1 / Select / Share: toggle panel. Start: pause/resume simulation. R / panel Restart: restart.");
#endif
        using var gameplayScript = gameplaySmokeTest ? new BallSandboxSmoke(physics!.Settings, gameplay!.Settings, input!.Settings, execution!.Settings) : null;
        string? lastGameTitle = null;
        var gameplayViewports = new HashSet<(int Width, int Height)>();
        var gameplayFrames = 0;
        var shmupFrames = 0;
        long pausedSmokeTick = -1;
        var shmupSmokeStage = 0;
        var shmupViewports = new HashSet<(int Width, int Height)>();
        var shmupTrailViewports = new HashSet<(int Width, int Height)>();
        bool shmupSawBalls = false, shmupSawBricks = false, shmupSawAim = false, shmupSawTrails = false;
#if BREAKOUT_EDITOR
        configurationWorkspace.PreflightLive = candidate =>
        {
            renderer.ValidateVSync(candidate.Get<LoadedWindowConfiguration>("window")!.Settings.VSync);
            if (modes is null) return;
            modes.ValidateTuning(candidate.Get<LoadedPhysicsConfiguration>("physics")!.Settings,
                candidate.Get<LoadedGameplayConfiguration>("gameplay")!.Settings,
                candidate.Get<LoadedShmupConfiguration>("shmup")!.Settings,
                candidate.Get<LoadedInputConfiguration>("input")!.Settings,
                candidate.Get<LoadedExecutionConfiguration>("execution")!.Settings,
                candidate.Get<LoadedBallTrailConfiguration>("trails")!.Settings);
        };
        ShmupSession? configurationSmokeOldRun = null;
        BrutalBreakout.Simulation.ShmupSnapshot? configurationSmokeSnapshot = null;
        bool configurationSmokeReset = false;
        bool configurationSmokePaused = false, configurationSmokeApplied = false, configurationSmokeRejected = false;
        ShmupSession? presentationSmokeRun = null;
        long presentationSmokeTick = -1;
        bool presentationSmokeImmediate = false, presentationSmokeFifo = false;
        bool meshSmokeDefault = false, meshSmokeTriangles = false, meshSmokeShared = false;
#endif
        var staleFrameRegressionPassed = false;
        var resizeRequested = false;
        var lastElapsed = TimeSpan.Zero;
        var shapeFrames = 0;
        var presentedGeometryFrames = 0; // All modes, not just smoke-test stage counters.
        var shapeViewports = new HashSet<(int Width, int Height)>();
        var overlayViewports = new Dictionary<(int Width, int Height), OverlayPresentationEvidence>();
        var hudOnlyViewports = new HashSet<(int Width, int Height)>();
        if (smokeTest) Console.WriteLine($"Smoke showcase: expectedShapes={ShapeShowcase.ShapeCount}; " +
            "filled=5; outlines=14; patterned=8; transforms=4; clipped=5; blend=6; panels=12; markers=2; decorativeAngled=4; decorativeChevron=4.");
        var timer = Stopwatch.StartNew();
        var frameBudget = new RenderFrameBudget(() => timer.Elapsed.TotalSeconds, Glfw.WaitEventsTimeout, () => window.ShouldClose);
        var phase = SmokePhase.Warmup;
        var phaseStarted = TimeSpan.Zero;
        var framesAtTransition = 0;
        var completed = false;
#if BREAKOUT_EDITOR
        long elapsedObservationId = 0;
        double? previousElapsedObservation = null;
        (int Cap, bool VSync, Brutal.VulkanApi.VkPresentModeKHR? Mode)? lastPresentationFacts = null;
#endif
        while (!window.ShouldClose)
        {
            // Keep window operations and graphics on this thread. Async work will later
            // need an explicit main-thread scheduler, not an await Task.Delay frame loop.
            var frameStart = timer.Elapsed.TotalSeconds;
#if BREAKOUT_EDITOR
            // Host-loop elapsed observations, not simulation steps or UI/presentation attempts.
            // Collection precedes UI creation/cancellation; a callback retry never resubmits an observation ID.
            if (previousElapsedObservation is { } previous)
                frameHistory.Observe(elapsedObservationId++, (frameStart - previous) * 1000);
            previousElapsedObservation = frameStart;
#endif
            Glfw.PollEvents();
            if (window.ShouldClose) break;
            // Hidden acceptance opens/closes the inspector without desktop input, including after resize.
            var wasPaused = modes?.Paused ?? false;
            var raw = poller.Poll(); // Host controls precede capture and run while paused.
            if (shmupSmokeTest)
                raw = new InputSnapshot { Focused = true, PadConnected = true,
                    PadPanelToggle = shmupFrames is 2 or 4, PadPause = shmupFrames is 8 or 12 };
#if BREAKOUT_EDITOR
            if (shmupSmokeTest && shmupFrames == 14) gameplayControls.RequestRestart();
#endif
#if BREAKOUT_EDITOR
            bool? inspectorOverride = overlaySmokeTest ? renderer.PresentedFrames % 6 is >= 2 and <= 4 : inspector.Update(raw);
#else
            bool? inspectorOverride = false;
#endif
#if BREAKOUT_EDITOR
            if (shmupSmokeTest && shmupFrames is 9 or 10 or 11)
            {
                presentationSmokeRun = modes!.Shmup;
                presentationSmokeTick = modes.Shmup!.Simulation.Snapshot.Tick;
                var detailDocument = configurationWorkspace.Documents.Single(d => d.Section == "rendering");
                detailDocument.Set("circleDetail.mode", shmupFrames == 11 ? "automatic" : "fixedSideCount");
                detailDocument.Set("circleDetail.fixedSides", shmupFrames == 10 ? 16 : 12);
            }
            if (shmupSmokeTest && shmupFrames is 9 or 11)
            {
                presentationSmokeRun = modes!.Shmup;
                presentationSmokeTick = modes.Shmup!.Simulation.Snapshot.Tick;
                var document = configurationWorkspace.Documents.Single(d => d.Section == "window");
                document.Set("vsync", shmupFrames == 11);
                document.Set("frameRateLimit", shmupFrames == 9 ? 60 : 0);
                configurationWorkspace.Documents.Single(d => d.Section == "rendering").Set("path",
                    shmupFrames == 9 ? "cpuBuiltTriangles" : "sharedMeshes");
            }
            if (shmupSmokeTest && shmupFrames == 15)
            {
                configurationControls.Visible = true;
                configurationControls.SelectCategory("shmup");
                configurationControls.SetGroupOpen("shmup.ball", true);
                configurationControls.SetGroupOpen("rendering", true);
                configurationControls.SetGroupOpen("rendering.circleDetail", true);
                configurationControls.SetGroupOpen("shmup.brick", true);
            }
            if (shmupSmokeTest && shmupFrames is 16 or 18)
            {
                configurationSmokeOldRun = modes!.Shmup;
                configurationSmokeSnapshot = modes.Shmup!.Simulation.Snapshot;
                configurationSmokePaused = modes.Paused;
                configurationWorkspace.Documents.Single(d => d.Section == "shmup").Set("ball.speed",
                    shmupFrames == 16 ? 600.5 : -1);
            }
            if (shmupSmokeTest && shmupFrames == 19) configurationWorkspace.Request(ConfigurationAction.Discard);
            if (shmupSmokeTest && shmupFrames == 20)
            {
                configurationSmokeOldRun = modes!.Shmup;
                configurationWorkspace.Documents.Single(d => d.Section == "shmup").Set("capacity.maxBalls", shmup!.Settings.Capacity.MaxBalls + 1);
                configurationWorkspace.Request(ConfigurationAction.Apply);
            }
            if (configurationControls.DrainClipboardText() is { } clipboardText)
            {
                try { Glfw.SetClipboard(clipboardText); configurationFeedback.Copied(frameStart); }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { configurationWorkspace.ReportFailure(error); }
            }
            configurationWorkspace.InactiveModeSection = modes?.Mode == GameplayMode.Shmup ? "gameplay" : modes?.Mode == GameplayMode.Breakout ? "shmup" : null;
            configurationWorkspace.SetLiveContext((object?)modes?.Shmup ?? modes?.Breakout,
                modes?.Shmup?.Simulation.Snapshot.Width ?? 0);
            configurationWorkspace.Process(candidate =>
            {
                // Explicit structural Apply bypasses live-world preflight; reject unsupported presentation before any mutation.
                renderer.ValidateVSync(candidate.Get<LoadedWindowConfiguration>("window")!.Settings.VSync);
                if (modes is null) throw new InvalidOperationException("No runtime host.");
                var nextPhysics = candidate.Get<LoadedPhysicsConfiguration>("physics")!;
                var nextExecution = candidate.Get<LoadedExecutionConfiguration>("execution")!;
                var nextGameplay = candidate.Get<LoadedGameplayConfiguration>("gameplay")!;
                var nextShmup = candidate.Get<LoadedShmupConfiguration>("shmup")!;
                var nextInput = candidate.Get<LoadedInputConfiguration>("input")!;
                var nextTrails = candidate.Get<LoadedBallTrailConfiguration>("trails")!;
                if (candidate.ResetRequired)
                    modes.Reconfigure(nextPhysics.Settings, nextGameplay.Settings, nextShmup.Settings,
                        nextInput.Settings, nextExecution.Settings, nextTrails.Settings);
                else if (new[] { "physics", "execution", "gameplay", "shmup", "input", "trails" }
                    .Any(section => configurationWorkspace.IsChanged(candidate, section)))
                    modes.ApplyTuning(nextPhysics.Settings, nextGameplay.Settings, nextShmup.Settings,
                        nextInput.Settings, nextExecution.Settings, nextTrails.Settings);
                if (configurationWorkspace.IsChanged(candidate, "rendering"))
                    shmupRendering.Apply(candidate.Get<LoadedRenderingConfiguration>("rendering")!.Settings);
                if (configurationWorkspace.IsChanged(candidate, "window"))
                {
                    var nextWindow = candidate.Get<LoadedWindowConfiguration>("window")!;
                    renderer.SetVSync(nextWindow.Settings.VSync);
                    if (nextWindow.Settings.Width != windowConfiguration.Settings.Width || nextWindow.Settings.Height != windowConfiguration.Settings.Height)
                        window.Size = new int2(nextWindow.Settings.Width, nextWindow.Settings.Height);
                    windowConfiguration = nextWindow;
                }
                if (configurationWorkspace.IsChanged(candidate, "audio"))
                {
                    var nextAudio = candidate.Get<LoadedAudioConfiguration>("audio")!;
                    var diagnostic = audio!.Reconfigure(nextAudio.Settings, muteAudio);
                    audioConfiguration = nextAudio;
                    audioApplyDiagnostic = diagnostic ?? (muteAudio ? "Audio: silent output (--mute / hidden smoke)." : "Audio: applied.");
                    if (diagnostic is not null) Console.Error.WriteLine(diagnostic);
                }
                configurationWorkspace.RuntimeDiagnostic = modes.RetirementDiagnostic is null ? audioApplyDiagnostic
                    : string.Join(" ", new[] { modes.RetirementDiagnostic, audioApplyDiagnostic }.Where(message => message is not null));
                (physics, execution, gameplay, shmup, input, trails, colors, style) =
                    (nextPhysics, nextExecution, nextGameplay, nextShmup, nextInput, nextTrails,
                        candidate.Get<LoadedColorSchemeConfiguration>("colors"), candidate.Get<LoadedPresentationStyleConfiguration>("style"));
                if (candidate.ResetRequired)
                {
                    audio?.SetSuppressed(muteAudio, hasFocus: false);
                    gameplayControls.Drain(); // commands target the retired run only on explicit structural reset
                    lastElapsed = timer.Elapsed;
                }
            });
            configurationFeedback.Update(frameStart, configurationWorkspace.LastOutcome);
            var presentationFacts = (windowConfiguration.Settings.FrameRateLimit, renderer.VSync, renderer.EffectivePresentMode);
            if (lastPresentationFacts != presentationFacts)
            {
                configurationControls.SetPresentationFacts(presentationFacts.FrameRateLimit, presentationFacts.VSync,
                    presentationFacts.EffectivePresentMode?.ToString());
                lastPresentationFacts = presentationFacts;
            }
            if (shmupSmokeTest && shmupFrames is 9 or 11)
            {
                if (!ReferenceEquals(presentationSmokeRun, modes!.Shmup) ||
                    presentationSmokeTick != modes.Shmup!.Simulation.Snapshot.Tick || !modes.Paused ||
                    windowConfiguration.Settings.VSync != (shmupFrames == 11) ||
                    windowConfiguration.Settings.FrameRateLimit != (shmupFrames == 9 ? 60 : 0) ||
                    renderer.VSync != windowConfiguration.Settings.VSync ||
                    shmupRendering.Path != (shmupFrames == 9 ? ShmupRenderingPath.CpuBuiltTriangles : ShmupRenderingPath.SharedMeshes))
                    throw new InvalidOperationException("Live presentation policy replaced/advanced the paused run or failed publication.");
            }
            if (shmupSmokeTest && shmupFrames is 9 or 10 or 11)
            {
                var expectedMode = shmupFrames == 11 ? CircleDetailMode.Automatic : CircleDetailMode.FixedSideCount;
                if (!ReferenceEquals(presentationSmokeRun, modes!.Shmup) || !modes.Paused ||
                    presentationSmokeTick != modes.Shmup!.Simulation.Snapshot.Tick ||
                    shmupRendering.CircleDetail.Mode != expectedMode ||
                    shmupRendering.CircleDetail.FixedSides != (shmupFrames == 10 ? 16 : 12) ||
                    !ReferenceEquals(shmupRendering.Limits, renderingConfiguration.Settings.Limits))
                    throw new InvalidOperationException("Live circle detail changed the paused run or failed policy publication.");
                Console.WriteLine($"Circle detail receipt: mode={expectedMode}; sides={shmupRendering.CircleDetail.FixedSides}; path={shmupRendering.Path}; sameRun=True; paused=True; noSave=True.");
            }
            if (shmupSmokeTest && shmupFrames == 16)
            {
                configurationSmokeApplied = ReferenceEquals(modes!.Shmup, configurationSmokeOldRun) &&
                    ReferenceEquals(modes.Shmup!.Settings, shmup!.Settings) && shmup.Settings.Ball.Speed == 600.5 &&
                    modes.Mode == GameplayMode.Shmup && modes.Paused == configurationSmokePaused &&
                    modes.Shmup.Simulation.Snapshot.Tick == configurationSmokeSnapshot!.Tick &&
                    modes.Shmup.Simulation.Snapshot.TimeSeconds == configurationSmokeSnapshot.TimeSeconds &&
                    modes.Shmup.Simulation.Snapshot.PlayerHealth == configurationSmokeSnapshot.PlayerHealth &&
                    modes.Shmup.Simulation.Snapshot.PlayerPosition == configurationSmokeSnapshot.PlayerPosition &&
                    modes.Shmup.Simulation.Snapshot.Balls.ToArray().Select(b => (b.Id, b.Generation, b.Position, b.WallHits))
                        .SequenceEqual(configurationSmokeSnapshot.Balls.ToArray().Select(b => (b.Id, b.Generation, b.Position, b.WallHits))) &&
                    modes.Shmup.Simulation.Snapshot.Bricks.ToArray().SequenceEqual(configurationSmokeSnapshot.Bricks.ToArray());
                if (!configurationSmokeApplied) throw new InvalidOperationException("Automatic live tuning did not retain settings/run safely.");
            }
            if (shmupSmokeTest && shmupFrames == 18)
            {
                configurationSmokeRejected = ReferenceEquals(modes!.Shmup, configurationSmokeOldRun) &&
                    shmup!.Settings.Ball.Speed == 600.5 && configurationWorkspace.Status.StartsWith("Rejected:", StringComparison.Ordinal);
                if (!configurationSmokeRejected) throw new InvalidOperationException("Invalid configuration draft changed the live run.");
            }
            if (shmupSmokeTest && shmupFrames == 20)
            {
                configurationSmokeReset = !ReferenceEquals(modes!.Shmup, configurationSmokeOldRun) &&
                    modes.Shmup!.Simulation.Snapshot.Tick == 0 &&
                    modes.Shmup.Simulation.Snapshot.Balls.Length == 0;
                if (!configurationSmokeReset) throw new InvalidOperationException("Explicit structural reset did not replace the run.");
            }
            var requests = gameplayControls.Drain();
            if (modes is not null && requests.Mode is { } requestedMode)
            {
                modes.Switch(requestedMode);
                audio!.SetSuppressed(muteAudio, hasFocus: false);
            }
            if (requests.Restart)
            {
                modes?.RequestRestart();
                audio?.SetSuppressed(muteAudio, hasFocus: false);
            }
            if (requests.TogglePause) modes?.TogglePause();
            if (modes?.Shmup is { } widening && !modes.Paused && !requests.Restart && requests.Mode is null && requests.Width is { } requestedWidth)
                widening.Simulation.Widen(Math.Clamp(requestedWidth, widening.Simulation.Snapshot.Width, widening.Settings.Arena.Width));
#endif
            modes?.UpdateControls(raw); // Raw Start is processed even when UI captures or simulation is paused.
#if BREAKOUT_EDITOR
            if (modes is not null) gameplayControls.Synchronize(modes.Mode, modes.Shmup?.Simulation.Snapshot.Width ?? 0, shmup!.Settings.Arena, modes.Paused, modes.Shmup?.RunMinimumWidth);
#endif
            if (modes is not null && (wasPaused != modes.Paused || modes.Paused))
                audio!.SetSuppressed(muteAudio, hasFocus: false);
            if (shmupSmokeTest)
            {
                if (timer.Elapsed.TotalSeconds > 20) throw new InvalidOperationException("Shmup smoke timed out.");
                if (shmupFrames >= 30 && shmupSmokeStage == 0)
                {
                    modes!.Shmup!.Simulation.Widen(shmup!.Settings.Arena.Width);
                    window.Size = new int2(960, 540);
                    shmupSmokeStage = 1;
                }
                if (shmupFrames >= 36 && shmupSmokeStage == 1 && shmupViewports.Contains((960, 540)))
                {
                    modes!.Switch(GameplayMode.Breakout);
                    shmupSmokeStage = 2;
                }
                if (shmupFrames >= 40 && shmupSmokeStage == 2)
                {
                    modes!.Switch(GameplayMode.Shmup);
                    if (modes.Shmup!.Simulation.Snapshot.Width != shmup!.Settings.Arena.InitialPlayAreaWidth ||
                        modes.Shmup.Simulation.Snapshot.Balls.Length != 0 || modes.Shmup.Simulation.Snapshot.Tick != 0)
                        throw new InvalidOperationException("Shmup mode switch retained run state.");
                    shmupSmokeStage = 3;
                }
                if (shmupFrames >= 70 && shmupSmokeStage == 3)
                {
#if BREAKOUT_EDITOR
                    if (gameplayControls.DrawCount < 2 || !callbackFailureRecovered || !configurationSmokeApplied || !configurationSmokeRejected || !configurationSmokeReset || !presentationSmokeImmediate || !presentationSmokeFifo || !meshSmokeDefault || !meshSmokeTriangles || !meshSmokeShared || configurationControls.DrawCount < 2 || configurationControls.ScalarDrawCount == 0 || configurationControls.FirstVisibleField is null)
                        throw new InvalidOperationException("Shmup smoke did not recover and redraw after the inspector callback failure.");
#endif
                    if (shmupViewports.Count < 2 || !shmupSawBalls || !shmupSawBricks || !shmupSawAim || (trails!.Settings.Enabled && (!shmupSawTrails || shmupTrailViewports.Count < 2)))
                        throw new InvalidOperationException("Shmup smoke missing resized, fired-ball or spawned-brick geometry.");
                    Console.WriteLine($"Shmup smoke PASS: hidden input, Select panel, Start pause/resume, queued editor restart, staged configuration Apply/reject (no saves), widening, resize, isolated mode switches, fresh reset and indexed presentation; aim={shmupSawAim}; trails={shmupSawTrails}.");
                    break;
                }
            }
            var session = modes?.Breakout;
            var shmupSession = modes?.Shmup;
            OverlayCapture capture;
#if BREAKOUT_EDITOR
            try { capture = overlays is null ? default : renderer.BeginOverlayFrame(inspectorOverride); }
            catch (InvalidOperationException error) when (shmupSmokeTest && ReferenceEquals(error, callbackFailure))
            {
                if (renderer.PresentedFrames != 0)
                    throw new InvalidOperationException("Inspector callback failure was not injected before the first acquisition.", error);
                callbackFailureRecovered = true;
                Console.WriteLine("Shmup inspector callback failure caught before acquisition; retrying with a fresh UI frame.");
                continue;
            }
#else
            capture = overlays is null ? default : renderer.BeginOverlayFrame(inspectorOverride);
#endif
#if BREAKOUT_EDITOR
            if (shmupSmokeTest && shmupFrames is 2 or 4 &&
                renderer.InspectorActive != (shmupFrames == 4))
                throw new InvalidOperationException("Synthetic Select did not toggle inspector.");
#endif
            if (overlaySmokeTest && inspectorOverride == false &&
                (renderer.InspectorActive || capture.Mouse || capture.Keyboard))
                throw new InvalidOperationException("Closed inspector retained native UI state or captured input.");
            var now = timer.Elapsed;
            var elapsedSeconds = (now - lastElapsed).TotalSeconds;
            lastElapsed = now;
            var size = window.Size;
            var pixels = window.FramebufferSize;
            if (!gallery)
            {
                var viewport = new ArenaViewport(size.X, size.Y, pixels.X, pixels.Y, gameplay!.Settings.Arena);
                if (gameplayScript is not null)
                {
                    if (now.TotalSeconds > 15) throw new InvalidOperationException("Gameplay smoke timed out.");
                    if (gameplayScript.ResizeRequested && !resizeRequested)
                    {
                        window.Size = new int2(960, 540);
                        resizeRequested = true;
                        Console.WriteLine("Gameplay: resize requested; waiting for resumed presentations.");
                    }
                    if (gameplayScript.Completed) break;
                    gameplayScript.Advance(viewport);
                }
                else
                {
                    if (capture.ResetBaselines)
                    {
                        audio!.SetSuppressed(muteAudio, hasFocus: false);
                        if (session is not null) session.Advance(raw with { Focused = false }, 0, viewport);
                        if (shmupSession is not null) shmupSession.Advance(raw with { Focused = false }, 0,
                            new(size.X, size.Y, pixels.X, pixels.Y, shmupSession.Settings.Arena));
                    }
#if BREAKOUT_EDITOR
                    var filtered = OverlayInputRouting.Filter(raw, capture);
#else
                    var filtered = raw;
#endif
                    if (shmupSmokeTest)
                    {
                        filtered = new InputSnapshot { Focused = true, PadConnected = true, PadStick = new(.5f, 0),
                            PadRightStick = new(0, 1), PadRightTrigger = shmupFrames % 4 == 0 ? 0 : 1,
                            Cursor = new(double.NaN, double.NaN) };
                        elapsedSeconds = physics!.Settings.StepSeconds * physics.Settings.MaxCatchUpSteps;
                    }
                    audio!.SetSuppressed(muteAudio, filtered.Focused && !filtered.Minimized && !modes!.Paused);
                    if (shmupSession is not null)
                    {
                        shmupSession.Advance(filtered, elapsedSeconds, new(size.X, size.Y, pixels.X, pixels.Y, shmupSession.Settings.Arena));
                        audio.ConsumeShmup(shmupSession.DrainEvents());
                        if (shmupSmokeTest && shmupFrames is >= 8 and < 12)
                        {
                            if (!modes!.Paused) throw new InvalidOperationException("Synthetic Start did not pause.");
                            if (shmupFrames == 8) pausedSmokeTick = shmupSession.Simulation.Snapshot.Tick;
                            if (pausedSmokeTick != shmupSession.Simulation.Snapshot.Tick)
                                throw new InvalidOperationException("Paused shmup advanced.");
                        }
                        if (shmupSmokeTest && shmupFrames == 12 && modes!.Paused)
                            throw new InvalidOperationException("Synthetic Start did not resume.");
#if BREAKOUT_EDITOR
                        if (shmupSmokeTest && shmupFrames == 14 && shmupSession.Simulation.Snapshot.Tick != 0)
                            throw new InvalidOperationException("Queued inspector restart did not reset run.");
#endif
                        window.Title = $"Brutal Breakout | Shmup | HP: {shmupSession.Simulation.Snapshot.PlayerHealth}";
                        lastGameTitle = null;
                    }
                    else
                    {
                        session!.Advance(filtered, elapsedSeconds, viewport);
                        audio.Consume(session.DrainEvents());
                        var game = session.Simulation;
                        string title = $"Brutal Breakout | Balls: {game.BallsRemaining} | Bricks: {game.RemainingBricks} | " +
                            (game.RoundPhase == BrutalBreakout.Simulation.RoundPhase.Won ? "CLEARED - R to restart" :
                             game.RoundPhase == BrutalBreakout.Simulation.RoundPhase.Lost ? "GAME OVER - R to restart" :
                             "Space/click/A: launch or catch below line | R: restart");
                        if (title != lastGameTitle) { window.Title = title; lastGameTitle = title; }
                    }
                }
            }
            if (smokeTest)
            {
                if (timer.Elapsed.TotalSeconds > 15)
                    throw new InvalidOperationException($"Smoke timed out in {phase}; frames={renderer.PresentedFrames}, " +
                        $"iconified={window.IsIconified}, size={window.Size}, framebuffer={window.FramebufferSize}.");
                switch (phase)
                {
                    case SmokePhase.Warmup when renderer.PresentedFrames >= 3:
                        window.Size = new int2(960, 540);
                        phase = SmokePhase.ResizedState;
                        Console.WriteLine("Smoke: resize requested.");
                        break;
                    case SmokePhase.ResizedState when window.Size.X == 960 && window.Size.Y == 540:
                        framesAtTransition = renderer.PresentedFrames;
                        phase = SmokePhase.ResizedFrames;
                        Console.WriteLine("Smoke: resized window observed; waiting for new presentations.");
                        break;
                    case SmokePhase.ResizedFrames when hiddenSmokeTest && renderer.PresentedFrames >= framesAtTransition + 6:
                        completed = true;
                        break;
                    case SmokePhase.ResizedFrames when lifecycleSmokeTest && renderer.PresentedFrames >= framesAtTransition + 3:
                        window.Iconify();
                        phase = SmokePhase.IconifiedState;
                        Console.WriteLine("Smoke: presented after resize; minimize requested.");
                        break;
                    case SmokePhase.IconifiedState when window.IsIconified:
                        framesAtTransition = renderer.PresentedFrames;
                        phaseStarted = timer.Elapsed;
                        phase = SmokePhase.HoldMinimized;
                        Console.WriteLine("Smoke: iconified state observed.");
                        break;
                    case SmokePhase.HoldMinimized:
                        if (!window.IsIconified || renderer.PresentedFrames != framesAtTransition)
                            throw new InvalidOperationException("Smoke: minimized interval did not remain idle and iconified.");
                        if (timer.Elapsed - phaseStarted >= TimeSpan.FromMilliseconds(300))
                        {
                            window.Restore();
                            phase = SmokePhase.RestoredState;
                            Console.WriteLine("Smoke: no presentations while minimized; restore requested.");
                        }
                        break;
                    case SmokePhase.RestoredState when !window.IsIconified && window.FramebufferSize.X > 0 && window.FramebufferSize.Y > 0:
                        framesAtTransition = renderer.PresentedFrames;
                        phase = SmokePhase.RestoredFrames;
                        Console.WriteLine("Smoke: restored state observed; waiting for new presentations.");
                        break;
                    case SmokePhase.RestoredFrames when renderer.PresentedFrames >= framesAtTransition + 3:
                        completed = true;
                        break;
                }
                if (completed) break;
            }
            var wasIconified = window.IsIconified;
            var framesBeforeDraw = renderer.PresentedFrames;
            var framebuffer = window.FramebufferSize;
            ShapeFrame? frame = null;
            OrderedShapeFrame? orderedFrame = null;
            BallSandbox.GeometryEvidence gameplayGeometry = default;
            ShmupPresentation.GeometryEvidence shmupGeometry = default;
            ShapeShowcase.DecorativeEvidence decorative = default;
            var injectStaleFrame = gameplayScript is not null && !staleFrameRegressionPassed;
            if (!wasIconified && framebuffer.X > 0 && framebuffer.Y > 0)
            {
                frame = gallery ? ShapeShowcase.Build(shapes, framebuffer.X, framebuffer.Y, out decorative) :
                    shmupSession is not null ? shmupRendering.Build(shapes, shmupSession.Simulation.Snapshot, shmupSession.Settings,
                        colors!.Settings, style!.Settings, framebuffer.X, framebuffer.Y, out shmupGeometry, out orderedFrame, shmupSession.Trails) :
                    BallSandbox.Build(shapes, (gameplayScript?.Session ?? session!).Simulation, colors!.Settings, style!.Settings, now.TotalSeconds,
                        framebuffer.X + (injectStaleFrame ? 1 : 0), framebuffer.Y, out gameplayGeometry, shmupRendering.CircleDetail);
                if (smokeTest && (frame.Value.Vertices.Length < ShapeShowcase.ShapeCount * 3 ||
                    frame.Value.Indices.Length < ShapeShowcase.ShapeCount * 3 || frame.Value.Commands.Length < 5))
                    throw new InvalidOperationException("Smoke: showcase lost shapes or nested clip commands.");
                if (smokeTest && (decorative.Angled != 4 || decorative.Chevron != 4 ||
                    decorative.AngledIndices < decorative.Angled * 3 || decorative.ChevronIndices < decorative.Chevron * 3))
                    throw new InvalidOperationException("Smoke: decorative calls lost indexed geometry.");
            }
            // Consumes borrowed CPU buffers before the next BeginFrame; only a true receipt
            // proves this frame's indexed geometry survived extent validation and presentation.
            if (overlays is not null)
            {
                var overlayFrame = shmupSession is not null ? ShmupPresentation.Overlay(shmupSession.Simulation.Snapshot, colors!.Settings.Hud) :
                    OverlayHost.Build((gameplayScript?.Session ?? session)?.Simulation,
                    colors?.Settings.Hud ?? LinearColor.FromSrgb(1, 1, 1));
#if BREAKOUT_EDITOR
                overlayFrame = InspectorHost.Compose(overlayFrame, (gameplayScript?.Session ?? session)?.Simulation,
                    elapsedSeconds * 1000, presentedGeometryFrames, frame?.Vertices.Length ?? 0, frame?.Indices.Length ?? 0,
                    shmupSession?.Simulation.Snapshot, modes?.Paused ?? false);
                if (gpuTest && overlayFrame.Diagnostics.PresentedShapeFrames != presentedGeometryFrames)
                    throw new InvalidOperationException("Inspector lost the prior successful geometry presentation count.");
                if (gpuTest && presentedGeometryFrames == 1)
                    Console.WriteLine($"Inspector geometry receipt: priorSuccessfulGeometryFrames=1; diagnosticsShapeFrames={overlayFrame.Diagnostics.PresentedShapeFrames}.");
#endif
                // Ordinary gallery keeps its original artwork; F1 can still inspect it. GPU fixtures exercise labels too.
                if (gallery && !gpuTest) overlayFrame = overlayFrame with { Labels = Array.Empty<OverlayLabel>() };
                renderer.SetOverlayFrame(overlayFrame);
            }
            var previousOverlay = renderer.OverlayEvidence;
            var geometryPresented = orderedFrame is { } sharedFrame
                ? renderer.Draw(sharedFrame, colors?.Settings.Background ?? ShapeShowcase.Background)
                : renderer.Draw(frame, colors?.Settings.Background ?? ShapeShowcase.Background);
            if (geometryPresented) presentedGeometryFrames++;
#if BREAKOUT_EDITOR
            if (shmupSmokeTest && !meshSmokeDefault && shmupFrames < 9 && geometryPresented && orderedFrame is { } initialMeshes && initialMeshes.Instances.Length > 0)
            {
                meshSmokeDefault = true;
                Console.WriteLine($"Shared mesh default receipt: instances={initialMeshes.Instances.Length}; sameLimits={ReferenceEquals(initialMeshes.Limits, renderingConfiguration.Settings.Limits)}.");
            }
            if (shmupSmokeTest && shmupFrames is 9 or 11)
            {
                if (!geometryPresented || (shmupFrames == 9 ? orderedFrame is not null : orderedFrame is null || orderedFrame.Value.Instances.Length == 0))
                    throw new InvalidOperationException("Live rendering path did not submit the requested geometry representation.");
                if (shmupFrames == 9) meshSmokeTriangles = true; else meshSmokeShared = true;
                Console.WriteLine($"Rendering path receipt: path={shmupRendering.Path}; instances={orderedFrame?.Instances.Length ?? 0}; sameRun=True; paused=True; tick={presentationSmokeTick}; noSave=True.");
                var expected = shmupFrames == 9 ? Brutal.VulkanApi.VkPresentModeKHR.ImmediateKHR : Brutal.VulkanApi.VkPresentModeKHR.FifoKHR;
                if (renderer.EffectivePresentMode != expected)
                    throw new InvalidOperationException("Swapchain did not apply the explicit present mode.");
                if (shmupFrames == 9) presentationSmokeImmediate = true; else presentationSmokeFifo = true;
                Console.WriteLine($"Presentation policy receipt: effective={renderer.EffectivePresentMode}; cap={windowConfiguration.Settings.FrameRateLimit}; sameRun=True; paused=True; tick={presentationSmokeTick}; noSave=True.");
            }
#endif
            // The host owns event waiting; the reusable renderer must not pump the OS.
            // Keep processing pause/capture transitions above, then avoid spinning while no framebuffer is available.
            if (wasIconified || framebuffer.X <= 0 || framebuffer.Y <= 0)
                Glfw.WaitEventsTimeout(0.05);
            var overlayReceipt = renderer.OverlayEvidence;
            if (overlaySmokeTest && inspectorOverride == false && overlayReceipt.TextFrames > previousOverlay.TextFrames)
            {
                if (overlayReceipt.ImageFrames != previousOverlay.ImageFrames || overlayReceipt.InspectorFrames != previousOverlay.InspectorFrames ||
                    overlayReceipt.DrawIndices - previousOverlay.DrawIndices != 6 * (overlayReceipt.LabelQuads - previousOverlay.LabelQuads))
                    throw new InvalidOperationException("HUD-only frame used inspector geometry or lost direct textured quads.");
                if (hudOnlyViewports.Add((overlayReceipt.Width, overlayReceipt.Height)))
                    Console.WriteLine($"HUD-only viewport: {overlayReceipt.Width}x{overlayReceipt.Height}; inspectorActive=False; capture=False; directQuadIndices=True.");
            }
            if (overlaySmokeTest && overlayReceipt.TextFrames > previousOverlay.TextFrames &&
                overlayReceipt.ImageFrames > previousOverlay.ImageFrames && overlayReceipt.InspectorFrames > previousOverlay.InspectorFrames)
            {
                var key = (overlayReceipt.Width, overlayReceipt.Height);
                if (!overlayViewports.ContainsKey(key))
                    Console.WriteLine($"Overlay viewport: {key.Item1}x{key.Item2}; textFrames={overlayReceipt.TextFrames}; imageFrames={overlayReceipt.ImageFrames}; inspectorFrames={overlayReceipt.InspectorFrames}; labelQuads={overlayReceipt.LabelQuads}; drawIndices={overlayReceipt.DrawIndices}; generation={overlayReceipt.SwapchainGeneration}.");
                overlayViewports[key] = overlayReceipt;
            }
            if (shmupSmokeTest && geometryPresented && frame.HasValue)
            {
                if (frame.Value.Indices.Length == 0) throw new InvalidOperationException("Shmup smoke presented empty geometry.");
                shmupFrames++;
                shmupSawAim |= shmupGeometry.AimIndices > 0;
                shmupSawTrails |= shmupGeometry.TrailIndices > 0;
                if (shmupGeometry.TrailIndices > 0 && shmupTrailViewports.Add((framebuffer.X, framebuffer.Y)))
                    Console.WriteLine($"Shmup trails viewport: {framebuffer.X}x{framebuffer.Y}; trailIndices={shmupGeometry.TrailIndices}; historyPoints={shmupSession?.Trails?.PointCount ?? 0}.");
                if (shmupViewports.Add((framebuffer.X, framebuffer.Y)))
                    Console.WriteLine($"Shmup viewport: {framebuffer.X}x{framebuffer.Y}; vertices={frame.Value.Vertices.Length}; indices={frame.Value.Indices.Length}; aimIndices={shmupGeometry.AimIndices}; trailIndices={shmupGeometry.TrailIndices}; historyPoints={shmupSession?.Trails?.PointCount ?? 0}.");
                shmupSawBalls |= shmupSession?.Simulation.Snapshot.Balls.Length > 0;
                shmupSawBricks |= shmupSession?.Simulation.Snapshot.Bricks.Length > 0;
            }
            if (smokeTest && geometryPresented && frame.HasValue)
            {
                shapeFrames++;
                if (shapeViewports.Add((framebuffer.X, framebuffer.Y)))
                    Console.WriteLine($"Smoke viewport: {framebuffer.X}x{framebuffer.Y}; " +
                        $"vertices={frame.Value.Vertices.Length}; indices={frame.Value.Indices.Length}; commands={frame.Value.Commands.Length}. " +
                        $"decorativeAngled={decorative.Angled}; decorativeChevron={decorative.Chevron}; " +
                        $"decorativeAngledIndices={decorative.AngledIndices}; decorativeChevronIndices={decorative.ChevronIndices}.");
            }
            if (gameplayScript is not null && geometryPresented && frame.HasValue)
            {
                gameplayFrames++;
                if (gameplayViewports.Add((framebuffer.X, framebuffer.Y)))
                    Console.WriteLine($"Gameplay viewport: {framebuffer.X}x{framebuffer.Y}; indices={frame.Value.Indices.Length}; arenaIndices={gameplayGeometry.ArenaIndices}; ballIndices={gameplayGeometry.BallIndices}; aimIndices={gameplayGeometry.AimIndices}; brickIndices={gameplayGeometry.BrickIndices}; catchLineIndices={gameplayGeometry.CatchLineIndices}; hudIndices={gameplayGeometry.HudIndices}; backgroundIndices={gameplayGeometry.BackgroundIndices}; heldRingIndices={gameplayGeometry.HeldRingIndices}.");
                if (gameplayFrames == 1)
                    Console.WriteLine(FormattableString.Invariant($"Gameplay HUD geometry: viewport={framebuffer.X}x{framebuffer.Y}; barOuterWidth={gameplayGeometry.HudBarOuterWidth:R}; barOuterHeight={gameplayGeometry.HudBarOuterHeight:R}; barCenterY={gameplayGeometry.HudBarCenterY:R}."));
                gameplayScript.Presented(framebuffer.X, framebuffer.Y, gameplayGeometry);
            }
            if (injectStaleFrame && frame.HasValue && renderer.PresentedFrames > framesBeforeDraw)
            {
                if (geometryPresented || presentedGeometryFrames != 0 || gameplayFrames != 0 || gameplayViewports.Count != 0 || !gameplayScript!.IsInitial)
                    throw new InvalidOperationException("Gameplay: stale frame advanced geometry evidence or stage.");
                staleFrameRegressionPassed = true;
                Console.WriteLine("Gameplay receipt regression PASS: clearOnlyPresented=True; geometryPresented=False; nonemptyPresentedFrames=0; viewports=0; stage=Initial.");
            }
            if (smokeTest && wasIconified && renderer.PresentedFrames != framesBeforeDraw)
                throw new InvalidOperationException("Smoke: Draw presented while iconified.");
            if (!wasIconified && framebuffer.X > 0 && framebuffer.Y > 0)
                frameBudget.Wait(frameStart, windowConfiguration.Settings);
        }
        if (shmupSmokeTest && shmupFrames < 70) throw new InvalidOperationException("Shmup smoke interrupted.");
        if (gameplayScript is not null)
        {
            if (!staleFrameRegressionPassed || !gameplayScript.Completed || gameplayViewports.Count < 2 || gameplayFrames < 9)
                throw new InvalidOperationException("Gameplay smoke interrupted or missing resized geometry.");
            Console.WriteLine($"Gameplay: launch wall catch hold release relaunch reset passed; ticks={gameplayScript.Session.TotalTicks}; nonemptyPresentedFrames={gameplayFrames}; hidden resize and resumed presentation passed.");
        }
        if (overlaySmokeTest)
        {
            if (!hudOnlyViewports.Contains((1280, 720)) || !hudOnlyViewports.Contains((960, 540)) ||
                !overlayViewports.TryGetValue((1280, 720), out var initialOverlay) ||
                !overlayViewports.TryGetValue((960, 540), out var resizedOverlay) ||
                resizedOverlay.SwapchainGeneration <= initialOverlay.SwapchainGeneration ||
                resizedOverlay.DrawIndices <= initialOverlay.DrawIndices || resizedOverlay.LabelQuads <= initialOverlay.LabelQuads)
                throw new InvalidOperationException("Overlay smoke missing actual text/image/inspector presentation across resize.");
            Console.WriteLine("Overlay smoke PASS: text, Stb image and readonly inspector presented at both extents; generation advanced; audio device disabled.");
        }
        Console.WriteLine($"Presented frames: {renderer.PresentedFrames}.");
        if (smokeTest && !completed)
            throw new InvalidOperationException($"Smoke interrupted before completing test stages in {phase}.");
        if (smokeTest && (shapeFrames < 9 || shapeViewports.Count < 2))
            throw new InvalidOperationException("Smoke: insufficient nonempty shape frames or distinct viewports.");
        if (smokeTest) Console.WriteLine($"Smoke: {ShapeShowcase.ShapeCount} shapes/frame, {shapeFrames} nonempty frames; " +
            (hiddenSmokeTest ? "hidden resize and resumed presentation passed (no minimize/restore coverage)." :
                "visible resize, idle minimize, restore and resumed presentation passed."));
    }, renderer.Dispose); // Dispose GPU resources before GLFW destroys the native window.
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}
finally
{
    Glfw.Terminate();
    Console.WriteLine($"Validation: {VulkanRenderer.ValidationErrors} errors, {VulkanRenderer.ValidationWarnings} warnings.");
    if (VulkanRenderer.ValidationErrors != 0)
        Environment.ExitCode = 1;
}

// Injection checks for the same error-handling helper used by renderer creation and shutdown.
// This does not simulate an actual GPU device loss.
static void VerifyCleanupFailureHandling()
{
    var original = new InvalidOperationException("Injected rendering failure");
    var idleFailure = new InvalidOperationException("Injected idle-wait failure");
    var destroyFailure = new InvalidOperationException("Injected destruction failure");
    var released = 0;
    try
    {
        ResourceLifetime.Run(() => throw original, () => ResourceLifetime.ReleaseAll(
            () => throw idleFailure,
            () => released++,
            () => throw destroyFailure,
            () => released++));
        throw new InvalidOperationException("Cleanup self-test failed to propagate errors.");
    }
    catch (AggregateException error) when (error.InnerExceptions[0] == original &&
        error.InnerExceptions[1] is AggregateException cleanup &&
        cleanup.InnerExceptions.SequenceEqual(new[] { idleFailure, destroyFailure }) && released == 2)
    {
        Console.WriteLine("Cleanup self-test: original failure preserved; all releases attempted after injected failures.");
    }
    try
    {
        ResourceLifetime.Run(() => { }, () => throw idleFailure);
        throw new InvalidOperationException("Cleanup self-test hid a standalone shutdown failure.");
    }
    catch (InvalidOperationException error) when (error == idleFailure) { }
    try
    {
        ResourceLifetime.OnFailure(() => throw original, () => released++);
        throw new InvalidOperationException("Cleanup self-test hid an initialization failure.");
    }
    catch (InvalidOperationException error) when (error == original && released == 3) { }
}

enum SmokePhase
{
    Warmup, ResizedState, ResizedFrames, IconifiedState, HoldMinimized, RestoredState, RestoredFrames
}
