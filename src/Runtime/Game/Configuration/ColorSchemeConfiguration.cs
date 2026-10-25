using BrutalBreakout.Presentation;
using CrushKit.Brutal.Draw2D;

namespace BrutalBreakout.Configuration;

public sealed record LoadedColorSchemeConfiguration(string Path, ColorScheme Settings)
{
    public string Describe()
    {
        static string Rgb(LinearColor color) => FormattableString.Invariant($"{color.R:R},{color.G:R},{color.B:R}");
        var s = Settings;
        return $"Colors configuration: name={s.Name}; background={Rgb(s.Background)}; backgroundBottom={Rgb(s.BackgroundBottom)}; heldRing={Rgb(s.HeldRing)}; walls={Rgb(s.Walls)}; " +
            $"ball={Rgb(s.Ball)}; trail={Rgb(s.Trail)}; catchReady={Rgb(s.CatchReady)}; catchLine={Rgb(s.CatchLine)}; aim={Rgb(s.Aim)}; " +
            $"brickPrimary={Rgb(s.BrickPrimary)}; brickSecondary={Rgb(s.BrickSecondary)}; brickAccent={Rgb(s.BrickAccent)}; " +
            $"hud={Rgb(s.Hud)}; win={Rgb(s.Win)}; loss={Rgb(s.Loss)}; path={Path}";
    }
}

/// <summary>Strict startup-only palette and semantic roles. Defaults exist only in colors.json.</summary>
public static class ColorSchemeConfiguration
{
    private sealed record RolesFile(string Background, string Walls, string Ball, string CatchReady,
        string CatchLine, string Aim, string BrickPrimary, string BrickSecondary, string BrickAccent,
        string Hud, string Win, string Loss, string BackgroundBottom, string HeldRing, string Trail);
    private sealed record SchemeFile(string Name, Dictionary<string, string> Palette, RolesFile Roles);

    public static LoadedColorSchemeConfiguration Load(string path)
    {
        var loaded = ConfigurationFile.Load<SchemeFile, ColorScheme>(path, "colors", data =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(data.Name);
            ArgumentNullException.ThrowIfNull(data.Palette);
            ArgumentNullException.ThrowIfNull(data.Roles);
            if (data.Palette.Count == 0) throw new ArgumentException("Palette must contain at least one named color.");
            var palette = new Dictionary<string, LinearColor>(StringComparer.Ordinal);
            foreach (var (name, hex) in data.Palette)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                if (hex is null || hex.Length != 7 || hex[0] != '#' || hex.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF"))
                    throw new ArgumentException($"Palette color '{name}' must be #RRGGBB.");
                palette.Add(name, LinearColor.FromSrgb(Convert.ToByte(hex[1..3], 16) / 255f,
                    Convert.ToByte(hex[3..5], 16) / 255f, Convert.ToByte(hex[5..7], 16) / 255f));
            }
            LinearColor Resolve(string? name)
            {
                if (name is null || !palette.TryGetValue(name, out var color))
                    throw new ArgumentException($"Role references an undefined palette color '{name ?? "<null>"}'.");
                return color;
            }
            var r = data.Roles;
            return new(data.Name, Resolve(r.Background), Resolve(r.Walls), Resolve(r.Ball), Resolve(r.CatchReady),
                Resolve(r.CatchLine), Resolve(r.Aim), Resolve(r.BrickPrimary), Resolve(r.BrickSecondary),
                Resolve(r.BrickAccent), Resolve(r.Hud), Resolve(r.Win), Resolve(r.Loss), Resolve(r.BackgroundBottom), Resolve(r.HeldRing), Resolve(r.Trail));
        });
        return new(loaded.Path, loaded.Settings);
    }

    public static LoadedColorSchemeConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "colors", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }
}
