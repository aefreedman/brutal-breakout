using System.Diagnostics;
using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalBreakout.Rendering;
using BrutalBreakout.Rendering.Shapes;
using BrutalBreakout.Presentation;

var hiddenSmokeTest = args.Contains("--smoke-test");
var lifecycleSmokeTest = args.Contains("--lifecycle-smoke-test");
var resizeStressTest = args.Contains("--visible-resize-stress");
if (new[] { hiddenSmokeTest, lifecycleSmokeTest, resizeStressTest }.Count(enabled => enabled) > 1)
    throw new ArgumentException("Select only one GPU test mode.");
var smokeTest = hiddenSmokeTest || lifecycleSmokeTest;
var gpuTest = smokeTest || resizeStressTest;
var cleanupSelfTest = args.Contains("--cleanup-self-test");
if (smokeTest || cleanupSelfTest) VerifyCleanupFailureHandling();
if (cleanupSelfTest) return;
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
        Glfw.WindowHint(GlfwWindowHint.Visible, hiddenSmokeTest ? 0 : 1);
    }
    var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
    {
        Title = "Brutal Breakout",
        Size = new int2(1280, 720),
    });
    if (window.IsNull())
        throw new InvalidOperationException("Failed to create the game window.");

    var renderer = VulkanRenderer.Create(window, validation);
    ResourceLifetime.Run(() =>
    {
        using var shapes = new ShapeDrawList();
        Console.WriteLine(hiddenSmokeTest ? "Smoke: hidden GPU shape/resize validation (no minimize/restore)." :
            "Close the window to exit. Expect six panels of Vulkan primitives (see README).");
        var shapeFrames = 0;
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
            if (!wasIconified && framebuffer.X > 0 && framebuffer.Y > 0)
            {
                frame = ShapeShowcase.Build(shapes, framebuffer.X, framebuffer.Y);
                if (smokeTest && (frame.Value.Vertices.Length < ShapeShowcase.ShapeCount * 3 ||
                    frame.Value.Indices.Length < ShapeShowcase.ShapeCount * 3 || frame.Value.Commands.Length < 5))
                    throw new InvalidOperationException("Smoke: showcase lost shapes or nested clip commands.");
                shapeFrames++;
            }
            renderer.Draw(frame); // Consumes borrowed CPU buffers before the next BeginFrame.
            if (smokeTest && wasIconified && renderer.PresentedFrames != framesBeforeDraw)
                throw new InvalidOperationException("Smoke: Draw presented while iconified.");
        }
        Console.WriteLine($"Presented frames: {renderer.PresentedFrames}.");
        if (smokeTest && !completed)
            throw new InvalidOperationException($"Smoke interrupted before completing test stages in {phase}.");
        if (smokeTest && shapeFrames < 9)
            throw new InvalidOperationException("Smoke: insufficient nonempty shape frames.");
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
