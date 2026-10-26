using BrutalBreakout.Configuration;

namespace BrutalBreakout.Presentation;

/// <summary>Owner-thread render pacing only. Each actual frame starts a fresh budget; no catch-up debt.</summary>
public sealed class RenderFrameBudget(Func<double> seconds, Action<double> waitEvents, Func<bool> shouldClose)
{
    public static double RemainingSeconds(double frameStart, double now, int limit) =>
        limit == 0 ? 0 : Math.Max(0, 1d / limit - (now - frameStart));

    // Settings are the same immutable publication held by the host, not copied defaults.
    public void Wait(double frameStart, WindowSettings settings)
    {
        if (settings.FrameRateLimit == 0) return;
        while (!shouldClose())
        {
            double remaining = RemainingSeconds(frameStart, seconds(), settings.FrameRateLimit);
            if (remaining <= 0) return;
            waitEvents(Math.Min(.010, remaining)); // Pump close/input events, never spin or migrate threads.
        }
    }
}
