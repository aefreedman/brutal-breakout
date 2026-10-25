namespace BrutalBreakout.Configuration;

public sealed record LoadedOverlayConfiguration(string Path, OverlaySettings Settings);

public static class OverlayConfiguration
{
    public static LoadedOverlayConfiguration Load(string path)
    {
        var loaded = ConfigurationFile.Load<OverlayFile, OverlaySettings>(path, "overlay", d => new(
            d.Enabled, System.IO.Path.GetFullPath(d.FontPath, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!),
            System.IO.Path.GetFullPath(d.DebugFontPath, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!),
            System.IO.Path.GetFullPath(d.ImagePath, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!),
            d.FontSize, d.Left, d.Top, d.LineSpacing, d.CacheEntries, d.MaxLabelCharacters, d.MaxLabels,
            d.MaxTextureDimension, d.MaxEncodedImageBytes, d.DebugVisible, d.DebugLeft, d.DebugTop,
            d.DebugWidth, d.DebugHeight, d.DebugImageSize));
        return new(loaded.Path, loaded.Settings);
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
