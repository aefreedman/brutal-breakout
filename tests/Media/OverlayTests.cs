using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using BrutalBreakout.Presentation.Overlay;
using Xunit;

namespace BrutalBreakout.Media.Tests;

public sealed class OverlayTests
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "overlay.json");
    private static OverlaySettings Settings() => OverlayConfiguration.Load(ConfigPath).Settings;

    [Fact]
    public void ShippedConfigurationResolvesAssetsRelativeToConfig()
    {
        var s = Settings();
        Assert.True(Path.IsPathFullyQualified(s.FontPath));
        Assert.True(Path.IsPathFullyQualified(s.ImagePath));
        Assert.True(File.Exists(s.FontPath));
        Assert.True(File.Exists(s.ImagePath));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(s.FontPath)!, "OFL.txt")));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(s.FontPath)!, "PROVENANCE.txt")));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(s.ImagePath)!, "LICENSE.txt")));
    }

    [Fact]
    public void IntroducedPackageNoticesAreDeployed()
    {
        foreach (var package in new[] { "ImGui", "ImGui.Backends", "ImGui.Native", "Localization", "OpenGl", "OpenGl.Native", "Sdl", "Sdl.Native", "Sdl.Ttf", "Sdl.Ttf.Native", "Stb", "Stb.Native" })
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Licenses", "Media", package);
            Assert.True(File.Exists(Path.Combine(directory, "LICENSE.txt")), package);
            Assert.True(File.Exists(Path.Combine(directory, "NOTICE.txt")), package);
            Assert.NotEmpty(Directory.GetFiles(directory, "*THIRD-PARTY-NOTICES.txt", SearchOption.AllDirectories));
        }
    }

    [Theory]
    [InlineData("unknown", "1")]
    [InlineData("fontSize", "0")]
    [InlineData("fontSize", "null")]
    [InlineData("cacheEntries", "0")]
    [InlineData("maxTextureDimension", "8")]
    [InlineData("maxEncodedImageBytes", "1")]
    [InlineData("fontPath", "null")]
    [InlineData("fontPath", "\"missing.ttf\"")]
    [InlineData("imagePath", "\"missing.png\"")]
    [InlineData("left", "-1")]
    [InlineData("debugImageSize", "0")]
    public void RejectsInvalidConfiguration(string property, string value)
    {
        WithConfig(n => n[property] = JsonNode.Parse(value), path => Assert.Throws<InvalidDataException>(() => OverlayConfiguration.Load(path)));
    }

    [Fact]
    public void RejectsMissingRequiredAndDuplicateProperties()
    {
        WithConfig(n => n.Remove("enabled"), path => Assert.Throws<InvalidDataException>(() => OverlayConfiguration.Load(path)));
        WithConfig(_ => { }, path =>
        {
            File.WriteAllText(path, File.ReadAllText(path).TrimEnd().TrimEnd('}') + ",\"enabled\":true}");
            Assert.Throws<InvalidDataException>(() => OverlayConfiguration.Load(path));
        });
    }

    [Fact]
    public void SelectPathEnforcesExplicitArgumentContract()
    {
        Assert.Null(OverlayConfiguration.LoadForMode([], false, "invalid", "invalid"));
        foreach (string[] args in new[] { new[] { "--overlay-config" }, new[] { "--overlay-config=x" }, new[] { "--overlay-config", "x", "--overlay-config", "y" } })
            Assert.Throws<ArgumentException>(() => OverlayConfiguration.LoadForMode(args, false, AppContext.BaseDirectory, AppContext.BaseDirectory));
        Assert.NotNull(OverlayConfiguration.LoadForMode(["--overlay-config", "overlay.json"], false, "unused", AppContext.BaseDirectory));
    }

    [Fact]
    public void CaptureNeverLeaksWhenClosedAndSignalsBothTransitions()
    {
        var capture = new OverlayCaptureTracker();
        Assert.Equal(new OverlayCapture(false, false, false), capture.Update(false, true, true));
        Assert.Equal(new OverlayCapture(false, false, true), capture.Update(true, false, false));
        Assert.Equal(new OverlayCapture(true, true, true), capture.Update(true, true, true));
        Assert.Equal(new OverlayCapture(true, true, false), capture.Update(true, true, true));
        Assert.Equal(new OverlayCapture(false, false, true), capture.Update(false, true, true));
        Assert.Equal(new OverlayCapture(false, false, false), capture.Update(false, false, false));
    }

    [Fact]
    public void CapturedSourcesDoNotReplayHeldEdgesOrStealGamepad()
    {
        var tracker = new OverlayCaptureTracker();
        var router = new BrutalBreakout.Platform.Input.InputRouter(new(0.2)); // Explicit test fixture, not a runtime default.
        var held = new BrutalBreakout.Platform.Input.InputSnapshot
        {
            Focused = true, KeyboardPrimary = true, MousePrimary = true, KeyboardRestart = true,
            Cursor = new(10, 20), PadConnected = true, PadId = 0, PadPrimary = true, PadStick = new(1, 0)
        };
        var captured = tracker.Update(true, true, true);
        var filtered = OverlayInputRouting.Filter(held, captured);
        Assert.False(filtered.KeyboardPrimary); Assert.False(filtered.KeyboardRestart); Assert.False(filtered.MousePrimary);
        Assert.True(double.IsNaN(filtered.Cursor.X)); Assert.True(filtered.PadPrimary); Assert.Equal(held.PadStick, filtered.PadStick);
        Assert.False(router.Route(filtered).PrimaryPressed);
        var closed = tracker.Update(false, true, true);
        Assert.True(closed.ResetBaselines);
        router.Reset(); // Host additionally flushes the session's latched edges via one inactive Advance.
        Assert.False(router.Route(OverlayInputRouting.Filter(held, closed)).PrimaryPressed);
        Assert.False(router.Route(held).PrimaryPressed);
        router.Route(held with { KeyboardPrimary = false, MousePrimary = false, PadPrimary = false });
        Assert.True(router.Route(held).PrimaryPressed);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(128, 55)]
    [InlineData(255, 255)]
    public void StraightLinearConversionDoesNotDoubleDecode(byte srgb, byte linear) => Assert.Equal(linear, OverlayPixels.SrgbToLinear(srgb));

    [Fact]
    public void StbCopiesOwnedRgbaAndPreservesStraightAlpha()
    {
        var s = Settings();
        var pixels = OverlayPixels.LoadPng(s.ImagePath, s.MaxTextureDimension, s.MaxEncodedImageBytes);
        Assert.Equal(16, pixels.Width); Assert.Equal(16, pixels.Height);
        Assert.Equal(16 * 16 * 4, pixels.Rgba.Length); Assert.True(pixels.HasCoverage);
        var data = pixels.Rgba.ToArray();
        // First pixel sRGB (32,128,255,255); last-column alpha is 80, blue stays 255 (not premultiplied).
        Assert.Equal(new byte[] { 4, 55, 255, 255 }, data[..4]);
        Assert.Equal(255, data[15 * 4 + 2]); Assert.Equal(80, data[15 * 4 + 3]);
        GC.Collect();
        Assert.Equal(data, pixels.Rgba.ToArray()); // Stb's native buffer was already freed by LoadPng.
    }

    [Fact]
    public void RejectsImageBeforeNativeDecodeForDimensionsAndEncodingBounds()
    {
        var bytes = File.ReadAllBytes(Settings().ImagePath);
        Assert.Throws<InvalidDataException>(() => OverlayPixels.DecodePng(bytes, 8, bytes.Length));
        Assert.Throws<InvalidDataException>(() => OverlayPixels.DecodePng(bytes, 16, bytes.Length - 1));
        Assert.Throws<InvalidDataException>(() => OverlayPixels.DecodePng(new byte[33], 16, 33));
        Assert.Throws<InvalidDataException>(() => OverlayPixels.DecodePng(bytes[..32], 16, 33));
    }

    [Fact]
    public void TtfMeasuresRasterizesCachesAndOwnsWhiteCoveragePixels()
    {
        var s = Settings(); OverlayPixels first;
        using (var text = new OverlayText(s))
        {
            first = text.Get("BRUTAL score 100");
            Assert.True(first.Width > 0 && first.Width <= s.MaxTextureDimension);
            Assert.True(first.Height > 0 && first.Height <= s.MaxTextureDimension);
            Assert.True(first.HasCoverage); Assert.Same(first, text.Get("BRUTAL score 100"));
            Assert.Equal(1, text.Rasterizations); Assert.Equal(1, text.CacheHits);
            for (int i = 0; i < s.CacheEntries + 1; i++) text.Get($"Score {i}");
            Assert.InRange(text.Count, 1, s.CacheEntries); Assert.True(text.Generation > 0);
            Assert.Throws<ArgumentException>(() => text.Get("line\nbreak"));
            Assert.Throws<ArgumentException>(() => text.Get(new string('x', s.MaxLabelCharacters + 1)));
        }
        Assert.Equal(first.Width * first.Height * 4, first.Rgba.Length);
        var bytes = first.Rgba.ToArray(); bool partial = false;
        for (int i = 0; i < bytes.Length; i += 4)
        {
            Assert.Equal(255, bytes[i]); Assert.Equal(255, bytes[i + 1]); Assert.Equal(255, bytes[i + 2]);
            partial |= bytes[i + 3] is > 0 and < 255;
        }
        Assert.True(partial); // Antialiased transparent edges retain white RGB; no dark fringe/double premultiply.
    }

    [Fact]
    public void TextDisposalIsIdempotentAndThreadBound()
    {
        var text = new OverlayText(Settings());
        Exception? error = null;
        var thread = new Thread(() => { try { text.Get("wrong thread"); } catch (Exception e) { error = e; } });
        thread.Start(); thread.Join(); Assert.IsType<InvalidOperationException>(error);
        text.Dispose(); text.Dispose(); Assert.Throws<ObjectDisposedException>(() => text.Get("disposed"));
    }

    private static void WithConfig(Action<JsonObject> mutate, Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "overlay-config-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "overlay.json");
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(ConfigPath))!.AsObject();
            var s = Settings(); node["fontPath"] = s.FontPath; node["imagePath"] = s.ImagePath;
            mutate(node); File.WriteAllText(path, node.ToJsonString()); test(path);
        }
        finally { Directory.Delete(directory, true); }
    }
}
