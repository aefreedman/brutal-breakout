using CrushKit.Brutal.Vulkan;
using CrushKit.Brutal.ImGui;
using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using BrutalBreakout.Configuration;
using BrutalBreakout.Presentation.Overlay;
using Xunit;

namespace BrutalBreakout.Media.Tests;

[CollectionDefinition("Native inspector font", DisableParallelization = true)]
public sealed class InspectorFontCollection { }

/// <summary>Offscreen font atlas only: no GLFW, Vulkan, windows, devices or OS DPI changes.</summary>
[Collection("Native inspector font")]
public sealed class InspectorFontTests
{
    [Fact]
    public void ConfiguredTtfReplacesImplicitPixelStyleFontAndBakesAtFramebufferDensity()
    {
        var settings = OverlayConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "overlay.json")).Settings;
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = default; io.LogFilename = default;
            var font = InspectorFont.Load(io, settings.DebugFontPath, settings.FontSize);
            Assert.Equal(font, io.FontDefault);
            Assert.True(io.Fonts.Fonts.Count == 1, "Native ImVector must contain only the explicitly configured font.");
            Assert.Equal(new FileInfo(settings.DebugFontPath).Length, font.Sources[0].FontDataSize);
            Assert.Contains("Iosevka-Regular", font.GetDebugName().ToString());
            Assert.DoesNotContain("Aile", font.GetDebugName().ToString());
            Assert.False(font.Sources[0].PixelSnapH); // Default ProggyClean explicitly enables pixel snapping.
            Assert.False(font.Sources[0].PixelSnapV);
            Assert.Equal(1, font.Sources[0].RasterizerDensity);
            Assert.False(io.ConfigDpiScaleFonts);
            Assert.Equal(1, ImGui.GetStyle().FontScaleMain);
            Assert.Equal(1, ImGui.GetStyle().FontScaleDpi);
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.DisplaySize = new float2(800, 600);
            io.DeltaTime = 1 / 60f;
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f, 1f })
            {
                io.DisplayFramebufferScale = new float2(scale);
                ImGui.GetStyle().FontSizeBase = InspectorFont.LogicalSize(settings.FontSize, io.DisplayFramebufferScale);
                ImGui.NewFrame();
                ImGui.SetNextWindowSize(new float2(400, 300));
                if (ImGui.Begin("Headless font metrics"u8)) ImGui.Text("Iosevka A0123456789i"u8);
                var baked = ImGui.GetFontBaked();
                Assert.Equal(scale, baked.RasterizerDensity);
                // Native ImGui rounds cached baked size to a logical pixel, then scales geometry to the exact requested size.
                Assert.InRange(baked.Size * baked.RasterizerDensity, settings.FontSize - scale * .5f, settings.FontSize + scale * .5f);
                Assert.InRange(ImGui.GetFontSize() * scale, settings.FontSize - scale * .5f, settings.FontSize + scale * .5f);
                Assert.False(baked.FindGlyph((uint)'A').IsNull());
                Assert.True(baked.Glyphs.Count > 0);
                Assert.Equal(baked.FindGlyph((uint)'A').AdvanceX, baked.FindGlyph((uint)'i').AdvanceX); // Standard monospaced inspector face.
                ImGui.End(); ImGui.EndFrame();
            }
        }
        finally { ImGui.DestroyContext(context); }
    }

    [Fact]
    public void OnlyTwoPinnedRegularFacesAreDeployedWithOneLicenseAndProvenance()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        var files = Directory.GetFiles(directory, "*.ttf").Order(StringComparer.Ordinal).Select(Path.GetFileName).ToArray();
        Assert.Equal(new[] { "Iosevka-Regular.ttf", "IosevkaAile-Regular.ttf" }, files);
        var expected = new[]
        {
            ("IosevkaAile-Regular.ttf", 11878496L, "be7e1730d23b0ec9d68966402222ec810c8d6d2a708c1ec3b10e5bab74c5186b"),
            ("Iosevka-Regular.ttf", 11613712L, "1ba24b81d82df65a68c9d0bb44bda306fdcdb5d7763afe89b0dea29ea827d1a2")
        };
        foreach (var (name, bytes, hash) in expected)
        {
            var path = Path.Combine(directory, name);
            Assert.Equal(bytes, new FileInfo(path).Length);
            Assert.Equal(hash, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        }
        Assert.Contains("Renzhi", File.ReadAllText(Path.Combine(directory, "OFL.txt")));
        Assert.Contains("23492208", File.ReadAllText(Path.Combine(directory, "PROVENANCE.txt")));
        Assert.Empty(Directory.GetFiles(directory, "*.zip"));
    }

    [Fact]
    public void PreviousDefaultFontSourceIsPixelSnappedNotConfiguredIosevka()
    {
        var context = ImGui.CreateContext();
        try
        {
            var font = ImGui.GetIO().Fonts.AddFontDefault();
            Assert.Contains("ProggyClean", font.GetDebugName().ToString());
            Assert.True(font.Sources[0].PixelSnapH);
            Assert.Equal(1, font.Sources[0].OversampleH);
            Assert.Equal(1, font.Sources[0].OversampleV);
            Assert.Equal(13, font.Sources[0].SizePixels);
            var io = ImGui.GetIO();
            io.IniFilename = default; io.LogFilename = default;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.DisplaySize = new float2(800, 600); io.DeltaTime = 1 / 60f;
            ImGui.GetStyle().FontSizeBase = 22;
            ImGui.NewFrame();
            if (ImGui.Begin("Previous default metrics"u8)) ImGui.Text("A0123"u8);
            Assert.Equal(22, ImGui.GetFontBaked().Size); // Dynamic TTF rasterization, not an old 13px bitmap upscaled.
            ImGui.End(); ImGui.EndFrame();
        }
        finally { ImGui.DestroyContext(context); }
    }
}
