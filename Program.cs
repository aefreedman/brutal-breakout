using Brutal.GlfwApi;
using Brutal.Numerics;

// Unlike Unity, we own startup and shutdown rather than implementing MonoBehaviour hooks.
if (!Glfw.Init())
    throw new InvalidOperationException("Failed to initialize GLFW.");

try
{
    // No graphics API yet: this milestone only establishes the native window and event loop.
    Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
    var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
    {
        Title = "Brutal Breakout",
        Size = new int2(1280, 720),
    });

    if (window.IsNull())
        throw new InvalidOperationException("Failed to create the game window.");

    Console.WriteLine("Brutal Breakout started. Close the window to exit.");

    // Block while idle so the empty window doesn't busy-spin.
    // Once we add gameplay, this becomes a timed update/render loop.
    while (!window.ShouldClose)
        Glfw.WaitEvents();
}
finally
{
    // GLFW also destroys any remaining windows here.
    Glfw.Terminate();
}
