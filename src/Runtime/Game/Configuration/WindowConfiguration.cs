namespace BrutalBreakout.Configuration;

public sealed record LoadedWindowConfiguration(string Path, WindowSettings Settings)
{
    public string Describe() => FormattableString.Invariant(
        $"Window configuration: width={Settings.Width}; height={Settings.Height}; vsync={Settings.VSync}; frameRateLimit={Settings.FrameRateLimit}; path={Path}");
}

/// <summary>Required initial window size. Defaults exist only in window.json.</summary>
public static class WindowConfiguration
{
    private sealed record WindowFile(int Width, int Height, bool Vsync, int FrameRateLimit);

    public static LoadedWindowConfiguration Load(string path) => Load(path, null);

    public static LoadedWindowConfiguration Load(string path, string? jsonText)
    {
        var loaded = ConfigurationFile.Load<WindowFile, WindowSettings>(path, "window", data => new(data.Width, data.Height, data.Vsync, data.FrameRateLimit), jsonText);
        return new(loaded.Path, loaded.Settings);
    }

    public static LoadedWindowConfiguration LoadForMode(IReadOnlyList<string> args,
        string baseDirectory, string currentDirectory) =>
        Load(ConfigurationFile.SelectPath(args, true, "window", baseDirectory, currentDirectory)!);
}
