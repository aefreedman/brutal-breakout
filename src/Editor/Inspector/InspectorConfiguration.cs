using BrutalBreakout.Configuration;
using CrushKit.Brutal.ImGui;

namespace BrutalBreakout.Editor;

public sealed record LoadedInspectorConfiguration(string Path, InspectorSettings Settings);

public static class InspectorConfiguration
{
    public static LoadedInspectorConfiguration Load(string path)
    {
        var loaded = ConfigurationFile.Load<InspectorFile, InspectorSettings>(path, "inspector", d =>
        {
            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
            return new InspectorSettings(System.IO.Path.GetFullPath(d.FontPath, directory),
                System.IO.Path.GetFullPath(d.ImagePath, directory), d.FontSize, d.Visible, d.Left, d.Top,
                d.Width, d.Height, d.ImageSize, d.MaxTextureDimension, d.MaxEncodedImageBytes);
        });
        return new(loaded.Path, loaded.Settings);
    }

    public static LoadedInspectorConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "inspector", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }

    private sealed record InspectorFile(string FontPath, string ImagePath, float FontSize, bool Visible,
        float Left, float Top, float Width, float Height, float ImageSize, int MaxTextureDimension, int MaxEncodedImageBytes);
}
