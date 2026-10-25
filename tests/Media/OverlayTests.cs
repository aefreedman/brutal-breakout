using System.Text.Json.Nodes;
using Brutal.TextureApi.Abstractions;
using BrutalBreakout.Rendering;
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
        foreach (var package in new[] { "ImGui", "ImGui.Backends", "ImGui.Native", "Localization", "OpenGl", "OpenGl.Native", "Sdl", "Sdl.Native", "Sdl.Ttf", "Sdl.Ttf.Native", "Stb", "Stb.Native", "Texture", "Texture.Abstractions", "Gli", "Gli.Native", "Ktx", "Ktx.Native" })
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Licenses", "Media", package);
            Assert.True(File.Exists(Path.Combine(directory, "LICENSE.txt")), package);
            Assert.True(File.Exists(Path.Combine(directory, "NOTICE.txt")), package);
            // Texture/Abstractions have no bundled third-party LICENSES tree; GLI/KTX and natives do.
            if (!package.StartsWith("Texture", StringComparison.Ordinal))
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
    public void TextureLoaderCopiesOwnedRgbaAndPremultipliesBeforeFiltering()
    {
        var s = Settings();
        var pixels = OverlayPixels.LoadPng(s.ImagePath, s.MaxTextureDimension, s.MaxEncodedImageBytes);
        try
        {
            Assert.Equal(16, pixels.Width); Assert.Equal(16, pixels.Height);
            Assert.Equal(16 * 16 * 4, pixels.Data.Length); Assert.True(OverlayPixels.HasCoverage(pixels));
            var data = pixels.Data.ToArray();
            Assert.Equal(new byte[] { 4, 55, 255, 255 }, data[..4]);
            // Blue is premultiplied to 80 BEFORE linear filtering, not kept straight at 255.
            Assert.Equal(80, data[15 * 4 + 2]); Assert.Equal(80, data[15 * 4 + 3]);
            for (int i = 0; i < data.Length; i += 4)
                for (int c = 0; c < 3; c++) Assert.InRange(data[i + c], 0, data[i + 3]);
            GC.Collect(); Assert.Equal(data, pixels.Data.ToArray()); // TextureLoader/Stb lifetime already ended.
            var preview = OverlayPixels.CreateInspectorPreview(pixels);
            try
            {
                Assert.Equal(255, preview.Data[15 * 4 + 2]); Assert.Equal(80, preview.Data[15 * 4 + 3]);
                Assert.Equal(data, pixels.Data.ToArray()); // Derived preview never changes game PM pixels.
            }
            finally { preview.Destroy(); }
        }
        finally { pixels.Destroy(); } // Not Using/Unload: GenericTexture.Destroy is NOT idempotent.
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
    public void TtfMeasuresRasterizesCachesAndOwnsPremultipliedCoverageTextures()
    {
        var s = Settings(); byte[] ownedCopy;
        using (var text = new OverlayText(s))
        {
            var first = text.Get("BRUTAL score 100");
            Assert.True(first.Width > 0 && first.Width <= s.MaxTextureDimension);
            Assert.True(first.Height > 0 && first.Height <= s.MaxTextureDimension);
            Assert.True(OverlayPixels.HasCoverage(first)); Assert.Same(first, text.Get("BRUTAL score 100"));
            Assert.Equal(1, text.Rasterizations); Assert.Equal(1, text.CacheHits);
            var plan = UploadRegions.Build(first);
            ownedCopy = new byte[(int)plan.RequiredBufferSizeBytes];
            OverlayTextureUpload.CopyStaging(first, plan, ownedCopy); // Borrow consumed before eviction/disposal.
            Assert.Equal(first.Data.ToArray(), ownedCopy);
            for (int i = 0; i < s.CacheEntries + 1; i++) text.Get($"Score {i}");
            Assert.InRange(text.Count, 1, s.CacheEntries); Assert.True(text.Generation > 0);
            Assert.NotSame(first, text.Get("BRUTAL score 100"));
            Assert.Throws<ArgumentException>(() => text.Get("line\nbreak"));
            Assert.Throws<ArgumentException>(() => text.Get(new string('x', s.MaxLabelCharacters + 1)));
        }
        bool partial = false;
        for (int i = 0; i < ownedCopy.Length; i += 4)
        {
            Assert.Equal(ownedCopy[i + 3], ownedCopy[i]); Assert.Equal(ownedCopy[i], ownedCopy[i + 1]); Assert.Equal(ownedCopy[i], ownedCopy[i + 2]);
            partial |= ownedCopy[i + 3] is > 0 and < 255;
        }
        Assert.True(partial); // RGB=A at all texels, including zero/partial alpha; no transparent-edge color leak.
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
