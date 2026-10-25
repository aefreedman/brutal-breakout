using System.Diagnostics;
using BrutalBreakout.Audio;
using BrutalBreakout.Presentation.Overlay;
using BrutalBreakout.Configuration;
using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalBreakout.Rendering;
using BrutalBreakout.Rendering.Shapes;
using BrutalBreakout.Presentation;
using BrutalBreakout.Platform.Input;

var gameplaySmokeTest = args.Contains("--gameplay-smoke-test");
var showcase = args.Contains("--showcase");
var debugUi = args.Contains("--debug-ui");
var overlaySmokeTest = args.Contains("--overlay-smoke-test");
var muteAudio = args.Contains("--mute");
var hiddenSmokeTest = args.Contains("--smoke-test");
var lifecycleSmokeTest = args.Contains("--lifecycle-smoke-test");
var resizeStressTest = args.Contains("--visible-resize-stress");
if (new[] { hiddenSmokeTest, gameplaySmokeTest, lifecycleSmokeTest, resizeStressTest, showcase }.Count(enabled => enabled) > 1)
    throw new ArgumentException("Select only one GPU test mode.");
var smokeTest = hiddenSmokeTest || lifecycleSmokeTest;
var gpuTest = smokeTest || resizeStressTest || gameplaySmokeTest;
var gallery = showcase || smokeTest || resizeStressTest;
if (overlaySmokeTest && !hiddenSmokeTest && !gameplaySmokeTest)
    throw new ArgumentException("--overlay-smoke-test requires a hidden GPU test mode.");
var cleanupSelfTest = args.Contains("--cleanup-self-test");
if (smokeTest || cleanupSelfTest) VerifyCleanupFailureHandling();
if (cleanupSelfTest) return;
LoadedWindowConfiguration windowConfiguration;
LoadedPhysicsConfiguration? physics;
LoadedGameplayConfiguration? gameplay;
LoadedInputConfiguration? input;
LoadedColorSchemeConfiguration? colors;
LoadedPresentationStyleConfiguration? style;
LoadedAudioConfiguration? audioConfiguration;
LoadedOverlayConfiguration? overlays;
try
{
    windowConfiguration = WindowConfiguration.LoadForMode(args, AppContext.BaseDirectory, Environment.CurrentDirectory);
    physics = PhysicsConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    gameplay = GameplayConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    input = InputConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    colors = ColorSchemeConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    style = PresentationStyleConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    audioConfiguration = AudioConfiguration.LoadForMode(args, !gallery, AppContext.BaseDirectory, Environment.CurrentDirectory);
    // Overlay settings are available in every mode so F1 never requires a startup flag.
    overlays = OverlayConfiguration.LoadForMode(args, true, AppContext.BaseDirectory, Environment.CurrentDirectory);
    if (overlays is not null) OverlayHost.Validate(overlays.Settings, gameplay?.Settings);
    if (overlaySmokeTest && overlays?.Settings.Enabled != true)
        throw new ArgumentException("Overlay smoke requires enabled overlays.");
    if (style is not null && gameplay is not null) style.Settings.Validate(gameplay.Settings);
    if (physics is not null && gameplay is not null) GameplayConfiguration.ValidatePhysics(gameplay, physics);
    Console.WriteLine(windowConfiguration.Describe());
    if (physics is not null) Console.WriteLine(physics.Describe());
    if (gameplay is not null) Console.WriteLine(gameplay.Describe());
    if (input is not null) Console.WriteLine(input.Describe());
    if (colors is not null) Console.WriteLine(colors.Describe());
    if (style is not null) Console.WriteLine(style.Describe());
    if (audioConfiguration is not null) Console.WriteLine(FormattableString.Invariant($"Audio configuration: enabled={audioConfiguration.Settings.Enabled}; masterGain={audioConfiguration.Settings.MasterGain}; maxVoices={audioConfiguration.Settings.MaxVoices}; path={audioConfiguration.Path}"));
    if (overlays is not null) Console.WriteLine(FormattableString.Invariant($"Overlay configuration: enabled={overlays.Settings.Enabled}; fontSize={overlays.Settings.FontSize}; debugVisible={overlays.Settings.DebugVisible}; path={overlays.Path}"));
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
        Glfw.WindowHint(GlfwWindowHint.Visible, hiddenSmokeTest || gameplaySmokeTest ? 0 : 1);
    }
    var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
    {
        Title = gallery ? "Brutal Breakout - primitive showcase" :
            "Brutal Breakout - Space / click / A: launch or catch below line; R / Start: restart; WASD / arrows / stick / mouse: aim",
        Size = new int2(windowConfiguration.Settings.Width, windowConfiguration.Settings.Height),
    });
    if (window.IsNull())
        throw new InvalidOperationException("Failed to create the game window.");

    Console.WriteLine($"Window created: {window.Size.X}x{window.Size.Y}; framebuffer={window.FramebufferSize.X}x{window.FramebufferSize.Y}.");
    var renderer = VulkanRenderer.Create(window, validation);
    ResourceLifetime.Run(() =>
    {
        if (overlays is not null) renderer.ConfigureOverlay(overlays.Settings);
        using var shapes = new ShapeDrawList();
        Console.WriteLine(hiddenSmokeTest ? "Smoke: hidden GPU shape/resize validation (no minimize/restore)." :
            gallery ? "Close the window to exit. Expect twelve panels of Vulkan primitives (see README)." :
            "Breakout: leave the lower zone, hit something, return below the line to catch; release/repress to relaunch. R restarts. See README.");
        using var session = !gallery && !gameplaySmokeTest ? new BallSandboxSession(physics!.Settings, gameplay!.Settings, input!.Settings) : null;
        // Hidden/showcase paths never create an audio device, even with enabled audio.json.
        string? audioDiagnostic = null;
        using var audio = session is null ? null : new GameAudio(audioConfiguration!.Settings,
            muteAudio ? new NullAudioOutput() : SdlMixerOutput.Create(audioConfiguration.Settings, out audioDiagnostic));
        if (audioDiagnostic is not null) Console.Error.WriteLine(audioDiagnostic);
        Console.WriteLine($"Audio host: nativeRequested={session is not null && !muteAudio && audioConfiguration!.Settings.Enabled}; muted={muteAudio || session is null}.");
        var poller = new GlfwInputPoller(window);
        var inspector = new InspectorToggle(debugUi || overlays!.Settings.DebugVisible);
        Console.WriteLine("F1: toggle the read-only diagnostics inspector.");
        using var gameplayScript = gameplaySmokeTest ? new BallSandboxSmoke(physics!.Settings, gameplay!.Settings, input!.Settings) : null;
        string? lastGameTitle = null;
        var gameplayViewports = new HashSet<(int Width, int Height)>();
        var gameplayFrames = 0;
        var staleFrameRegressionPassed = false;
        var resizeRequested = false;
        var lastElapsed = TimeSpan.Zero;
        var shapeFrames = 0;
        var shapeViewports = new HashSet<(int Width, int Height)>();
        var overlayViewports = new Dictionary<(int Width, int Height), OverlayPresentationEvidence>();
        var hudOnlyViewports = new HashSet<(int Width, int Height)>();
        if (smokeTest) Console.WriteLine($"Smoke showcase: expectedShapes={ShapeShowcase.ShapeCount}; " +
            "filled=5; outlines=14; patterned=8; transforms=4; clipped=5; blend=6; panels=12; markers=2; decorativeAngled=4; decorativeChevron=4.");
        var timer = Stopwatch.StartNew();
        var phase = SmokePhase.Warmup;
        var phaseStarted = TimeSpan.Zero;
        var framesAtTransition = 0;
        var completed = false;
        while (!window.ShouldClose)
        {
            // Keep window operations and graphics on this thread. Async work will later
            // need an explicit main-thread scheduler, not an await Task.Delay frame loop.
            Glfw.PollEvents();
            if (window.ShouldClose) break;
            // Hidden acceptance opens/closes the inspector without desktop input, including after resize.
            var raw = poller.Poll(); // Inspect F1 before capture filtering, including while ImGui owns keyboard input.
            bool? inspectorOverride = overlaySmokeTest ? renderer.PresentedFrames % 6 is >= 2 and <= 4 : inspector.Update(raw);
            var capture = overlays is null ? default : renderer.BeginOverlayFrame(inspectorOverride);
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
                        session!.Advance(raw with { Focused = false }, 0, viewport);
                    }
                    var filtered = OverlayInputRouting.Filter(raw, capture);
                    audio!.SetSuppressed(muteAudio, filtered.Focused && !filtered.Minimized);
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
            BallSandbox.GeometryEvidence gameplayGeometry = default;
            ShapeShowcase.DecorativeEvidence decorative = default;
            var injectStaleFrame = gameplayScript is not null && !staleFrameRegressionPassed;
            if (!wasIconified && framebuffer.X > 0 && framebuffer.Y > 0)
            {
                frame = gallery ? ShapeShowcase.Build(shapes, framebuffer.X, framebuffer.Y, out decorative) :
                    BallSandbox.Build(shapes, (gameplayScript?.Session ?? session!).Simulation, colors!.Settings, style!.Settings, now.TotalSeconds,
                        framebuffer.X + (injectStaleFrame ? 1 : 0), framebuffer.Y, out gameplayGeometry);
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
                var overlayFrame = OverlayHost.Build((gameplayScript?.Session ?? session)?.Simulation,
                    colors?.Settings.Hud ?? LinearColor.FromSrgb(1, 1, 1), elapsedSeconds * 1000,
                    gameplayFrames + shapeFrames, frame?.Vertices.Length ?? 0, frame?.Indices.Length ?? 0);
                // Ordinary gallery keeps its original artwork; F1 can still inspect it. GPU fixtures exercise labels too.
                if (gallery && !gpuTest) overlayFrame = overlayFrame with { Labels = Array.Empty<OverlayLabel>() };
                renderer.SetOverlayFrame(overlayFrame);
            }
            var previousOverlay = renderer.OverlayEvidence;
            var geometryPresented = renderer.Draw(frame, colors?.Settings.Background ?? ShapeShowcase.Background);
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
                if (geometryPresented || gameplayFrames != 0 || gameplayViewports.Count != 0 || !gameplayScript!.IsInitial)
                    throw new InvalidOperationException("Gameplay: stale frame advanced geometry evidence or stage.");
                staleFrameRegressionPassed = true;
                Console.WriteLine("Gameplay receipt regression PASS: clearOnlyPresented=True; geometryPresented=False; nonemptyPresentedFrames=0; viewports=0; stage=Initial.");
            }
            if (smokeTest && wasIconified && renderer.PresentedFrames != framesBeforeDraw)
                throw new InvalidOperationException("Smoke: Draw presented while iconified.");
        }
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
