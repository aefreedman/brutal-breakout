namespace BrutalBreakout.Configuration;

/// <summary>Initial physical window dimensions, independent of the logical arena.</summary>
public sealed class WindowSettings
{
    public int Width { get; }
    public int Height { get; }

    public WindowSettings(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Window dimensions must be positive integers.");
        Width = width;
        Height = height;
    }
}
