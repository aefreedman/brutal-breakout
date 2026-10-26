using BrutalBreakout.Configuration;
using CrushKit.Brutal.ImGui;

namespace BrutalBreakout.Editor;

public sealed record LoadedInspectorConfiguration(string Path, InspectorSettings Settings, GameplayWindowSettings GameplayWindow, GameplayWindowSettings ConfigurationWindow);

public static class InspectorConfiguration
{
    public static LoadedInspectorConfiguration Load(string path) => Load(path, null);

    public static LoadedInspectorConfiguration Load(string path, string? jsonText)
    {
        var loaded = ConfigurationFile.Load<InspectorFile, (InspectorSettings Inspector, GameplayWindowSettings GameplayWindow, GameplayWindowSettings ConfigurationWindow)>(path, "inspector", d =>
        {
            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
            ArgumentNullException.ThrowIfNull(d.GameplayWindow);
            var window = new GameplayWindowSettings(d.GameplayWindow.Left, d.GameplayWindow.Top,
                d.GameplayWindow.Width, d.GameplayWindow.Height);
            var inspector = new InspectorSettings(System.IO.Path.GetFullPath(d.FontPath, directory),
                System.IO.Path.GetFullPath(d.ImagePath, directory), d.FontSize, d.Visible, d.Left, d.Top,
                d.Width, d.Height, d.ImageSize, d.MaxTextureDimension, d.MaxEncodedImageBytes);
            ArgumentNullException.ThrowIfNull(d.ConfigurationWindow);
            var configurationWindow = new GameplayWindowSettings(d.ConfigurationWindow.Left, d.ConfigurationWindow.Top,
                d.ConfigurationWindow.Width, d.ConfigurationWindow.Height);
            return (inspector, window, configurationWindow);
        }, jsonText);
        return new(loaded.Path, loaded.Settings.Inspector, loaded.Settings.GameplayWindow, loaded.Settings.ConfigurationWindow);
    }

    public static LoadedInspectorConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "inspector", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }

    private sealed record InspectorFile(string FontPath, string ImagePath, float FontSize, bool Visible,
        float Left, float Top, float Width, float Height, float ImageSize, int MaxTextureDimension, int MaxEncodedImageBytes,
        GameplayWindowFile GameplayWindow, GameplayWindowFile ConfigurationWindow);
    private sealed record GameplayWindowFile(float Left, float Top, float Width, float Height);
}
