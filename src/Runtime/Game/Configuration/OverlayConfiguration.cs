using CrushKit.Brutal.Vulkan;
namespace BrutalBreakout.Configuration;

public sealed record LoadedOverlayConfiguration(string Path, OverlaySettings Settings);

public static class OverlayConfiguration
{
    public static LoadedOverlayConfiguration Load(string path)
    {
        var loaded = ConfigurationFile.Load<OverlayFile, OverlaySettings>(path, "overlay", d =>
        {
            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
            return new OverlaySettings(d.Enabled, System.IO.Path.GetFullPath(d.FontPath, directory),
                d.FontSize, d.Left, d.Top, d.LineSpacing, d.CacheEntries, d.MaxLabelCharacters, d.MaxLabels, d.MaxTextureDimension);
        });
        return new(loaded.Path, loaded.Settings);
    }

    public static LoadedOverlayConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "overlay", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }

    private sealed record OverlayFile(bool Enabled, string FontPath, float FontSize,
        float Left, float Top, float LineSpacing, int CacheEntries, int MaxLabelCharacters, int MaxLabels,
        int MaxTextureDimension);
}
