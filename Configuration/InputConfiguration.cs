using BrutalBreakout.Platform.Input;

namespace BrutalBreakout.Configuration;

public sealed record LoadedInputConfiguration(string Path, InputSettings Settings)
{
    public string Describe() => FormattableString.Invariant(
        $"Input configuration: stickDeadzone={Settings.StickDeadzone:R}; path={Path}");
}

/// <summary>Strict input tuning. Defaults exist only in input.json.</summary>
public static class InputConfiguration
{
    private sealed record InputFile(double StickDeadzone);

    public static LoadedInputConfiguration Load(string path)
    {
        var loaded = ConfigurationFile.Load<InputFile, InputSettings>(path, "input", data => new(data.StickDeadzone));
        return new(loaded.Path, loaded.Settings);
    }

    public static LoadedInputConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "input", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }
}
