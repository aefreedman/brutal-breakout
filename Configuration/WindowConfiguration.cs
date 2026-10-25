namespace BrutalBreakout.Configuration;

public sealed record LoadedWindowConfiguration(string Path, WindowSettings Settings)
{
    public string Describe() => FormattableString.Invariant(
        $"Window configuration: width={Settings.Width}; height={Settings.Height}; path={Path}");
}

/// <summary>Required initial window size. Defaults exist only in window.json.</summary>
public static class WindowConfiguration
{
    private sealed record WindowFile(int Width, int Height);

    public static LoadedWindowConfiguration Load(string path)
    {
        var loaded = ConfigurationFile.Load<WindowFile, WindowSettings>(path, "window", data => new(data.Width, data.Height));
        return new(loaded.Path, loaded.Settings);
    }

    public static LoadedWindowConfiguration LoadForMode(IReadOnlyList<string> args,
        string baseDirectory, string currentDirectory) =>
        Load(ConfigurationFile.SelectPath(args, true, "window", baseDirectory, currentDirectory)!);
}
