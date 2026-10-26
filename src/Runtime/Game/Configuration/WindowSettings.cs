namespace BrutalBreakout.Configuration;

/// <summary>Physical window dimensions (startup or owner-thread live resize), independent of the logical arena.</summary>
public sealed class WindowSettings
{
    public int Width { get; }
    public int Height { get; }

    public bool VSync { get; }
    public int FrameRateLimit { get; }

    public WindowSettings(int width, int height, bool vSync, int frameRateLimit)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Window dimensions must be positive integers.");
        if (frameRateLimit is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(frameRateLimit), "Frame rate limit must be an integer from 0 (unlimited) to 1000.");
        VSync = vSync;
        FrameRateLimit = frameRateLimit;
        Width = width;
        Height = height;
    }
}
