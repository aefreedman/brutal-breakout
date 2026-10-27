using BrutalBreakout.Configuration;
using Brutal.Numerics;
using CrushKit.Brutal.ImGui;

namespace BrutalBreakout.Editor;

public sealed record LoadedInspectorConfiguration(string Path, InspectorSettings Settings, GameplayWindowSettings GameplayWindow,
    GameplayWindowSettings ConfigurationWindow, int HistoryCapacity, InspectorTheme Theme, InspectorFeedbackSettings Feedback);

public static class InspectorConfiguration
{
    public static LoadedInspectorConfiguration Load(string path) => Load(path, null);

    public static LoadedInspectorConfiguration Load(string path, string? jsonText)
    {
        var loaded = ConfigurationFile.Load<InspectorFile, LoadedInspectorConfiguration>(path, "inspector", d =>
        {
            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
            ArgumentNullException.ThrowIfNull(d.GameplayWindow); ArgumentNullException.ThrowIfNull(d.ConfigurationWindow);
            ArgumentNullException.ThrowIfNull(d.Theme); ArgumentNullException.ThrowIfNull(d.Theme.Colors);
            ArgumentNullException.ThrowIfNull(d.Theme.Metrics); ArgumentNullException.ThrowIfNull(d.Feedback);
            var window = new GameplayWindowSettings(d.GameplayWindow.Left, d.GameplayWindow.Top, d.GameplayWindow.Width, d.GameplayWindow.Height);
            var configurationWindow = new GameplayWindowSettings(d.ConfigurationWindow.Left, d.ConfigurationWindow.Top,
                d.ConfigurationWindow.Width, d.ConfigurationWindow.Height);
            if (d.HistoryCapacity is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(d.HistoryCapacity));
            var inspector = new InspectorSettings(System.IO.Path.GetFullPath(d.FontPath, directory),
                d.FontSize, d.Visible, d.Left, d.Top, d.Width, d.Height, d.DiagnosticsVisible);
            var c = d.Theme.Colors; var m = d.Theme.Metrics;
            var theme = new InspectorTheme(new(Color(c.Text), Color(c.MutedText), Color(c.Window), Color(c.Surface),
                Color(c.Popup), Color(c.Border), Color(c.Accent), Color(c.AccentHovered), Color(c.AccentActive),
                Color(c.Success), Color(c.Warning), Color(c.Error)),
                new(Pair(m.WindowPaddingEm), Pair(m.FramePaddingEm), Pair(m.ItemSpacingEm), Pair(m.InnerSpacingEm),
                    Pair(m.CellPaddingEm), m.RoundingEm, m.BorderEm, m.ScrollbarEm, m.GrabMinEm, m.IndentEm, m.DisabledAlpha));
            var feedback = new InspectorFeedbackSettings(d.Feedback.Capacity, d.Feedback.MaxTextCharacters,
                d.Feedback.SuccessSeconds, d.Feedback.InformationSeconds);
            return new(path, inspector, window, configurationWindow, d.HistoryCapacity, theme, feedback);
        }, jsonText);
        return loaded.Settings with { Path = loaded.Path };
    }

    private static float4 Color(float[] values) => values is { Length: 4 }
        ? new(values[0], values[1], values[2], values[3]) : throw new ArgumentException("Theme colors require four native straight-RGBA components.");
    private static float2 Pair(float[] values) => values is { Length: 2 }
        ? new(values[0], values[1]) : throw new ArgumentException("Theme metric pairs require two components.");

    public static LoadedInspectorConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed,
        string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "inspector", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }

    private sealed record InspectorFile(string FontPath, float FontSize, bool Visible, bool DiagnosticsVisible, int HistoryCapacity,
        float Left, float Top, float Width, float Height, GameplayWindowFile GameplayWindow, GameplayWindowFile ConfigurationWindow,
        ThemeFile Theme, FeedbackFile Feedback);
    private sealed record GameplayWindowFile(float Left, float Top, float Width, float Height);
    private sealed record ThemeFile(ColorsFile Colors, MetricsFile Metrics);
    private sealed record ColorsFile(float[] Text, float[] MutedText, float[] Window, float[] Surface, float[] Popup, float[] Border,
        float[] Accent, float[] AccentHovered, float[] AccentActive, float[] Success, float[] Warning, float[] Error);
    private sealed record MetricsFile(float[] WindowPaddingEm, float[] FramePaddingEm, float[] ItemSpacingEm, float[] InnerSpacingEm,
        float[] CellPaddingEm, float RoundingEm, float BorderEm, float ScrollbarEm, float GrabMinEm, float IndentEm, float DisabledAlpha);
    private sealed record FeedbackFile(int Capacity, int MaxTextCharacters, double SuccessSeconds, double InformationSeconds);
}
