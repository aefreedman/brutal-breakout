using CrushKit.Brutal.Vulkan;
using CrushKit.Brutal.ImGui;
namespace BrutalBreakout.Configuration;

public sealed record LoadedOverlayConfiguration(string Path, OverlaySettings Settings, InspectorSettings Inspector);

public static class OverlayConfiguration
{
    public static LoadedOverlayConfiguration Load(string path)
    {
        var loaded = ConfigurationFile.Load<OverlayFile, (OverlaySettings Text, InspectorSettings Inspector)>(path, "overlay", d =>
        {
            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
            var text = new OverlaySettings(d.Enabled, System.IO.Path.GetFullPath(d.FontPath, directory),
                d.FontSize, d.Left, d.Top, d.LineSpacing, d.CacheEntries, d.MaxLabelCharacters, d.MaxLabels, d.MaxTextureDimension);
            var inspector = new InspectorSettings(System.IO.Path.GetFullPath(d.DebugFontPath, directory),
                System.IO.Path.GetFullPath(d.ImagePath, directory), d.FontSize, d.DebugVisible, d.DebugLeft, d.DebugTop,
                d.DebugWidth, d.DebugHeight, d.DebugImageSize, d.MaxTextureDimension, d.MaxEncodedImageBytes);
            return (text, inspector);
        });
        return new(loaded.Path, loaded.Settings.Text, loaded.Settings.Inspector);
    }

    public static LoadedOverlayConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "overlay", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }

    private sealed record OverlayFile(bool Enabled, string FontPath, string DebugFontPath, string ImagePath, float FontSize,
        float Left, float Top, float LineSpacing, int CacheEntries, int MaxLabelCharacters, int MaxLabels,
        int MaxTextureDimension, int MaxEncodedImageBytes, bool DebugVisible, float DebugLeft, float DebugTop,
        float DebugWidth, float DebugHeight, float DebugImageSize);
}
