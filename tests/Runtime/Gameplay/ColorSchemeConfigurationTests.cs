using System.Globalization;
using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using BrutalBreakout.Presentation;
using CrushKit.Brutal.Draw2D;
using Xunit;

public class ColorSchemeConfigurationTests
{
    private const string Valid = """
        {
          "name": "Test scheme",
          "palette": { "blue": "#2176CC" },
          "roles": {
            "backgroundBottom": "blue", "heldRing": "blue", "background": "blue", "walls": "blue", "ball": "blue", "catchReady": "blue",
            "catchLine": "blue", "aim": "blue", "brickPrimary": "blue", "brickSecondary": "blue",
            "brickAccent": "blue", "hud": "blue", "win": "blue", "loss": "blue"
          }
        }
        """;

    public static IEnumerable<object[]> InvalidDocuments()
    {
        foreach (string json in new[] { "null", "[]", "false", "17", "{}", "{broken" }) yield return new object[] { json };
        foreach (string property in new[] { "name", "palette", "roles" })
        {
            var missing = JsonNode.Parse(Valid)!.AsObject(); missing.Remove(property);
            yield return new object[] { missing.ToJsonString() };
            var nulled = JsonNode.Parse(Valid)!.AsObject(); nulled[property] = null;
            yield return new object[] { nulled.ToJsonString() };
        }
        foreach (string name in new[] { "", "  ", "bad\nname" })
        {
            var node = JsonNode.Parse(Valid)!.AsObject(); node["name"] = name;
            yield return new object[] { node.ToJsonString() };
        }
        foreach (string palette in new[] { "{}", "[]", "17", "\"blue\"", "{\"\":\"#123456\"}",
            "{\"  \":\"#123456\"}", "{\"blue\":null}", "{\"blue\":17}" })
        {
            var node = JsonNode.Parse(Valid)!.AsObject(); node["palette"] = JsonNode.Parse(palette);
            yield return new object[] { node.ToJsonString() };
        }
        foreach (string hex in new[] { "2176CC", "#123", "#12345678", "#GGGGGG", "#12 456", "#12345 ", "#１２３４５６" })
        {
            var node = JsonNode.Parse(Valid)!.AsObject(); node["palette"]!["blue"] = hex;
            yield return new object[] { node.ToJsonString() };
        }
        foreach (string property in JsonNode.Parse(Valid)!["roles"]!.AsObject().Select(p => p.Key))
        {
            var missing = JsonNode.Parse(Valid)!.AsObject(); missing["roles"]!.AsObject().Remove(property);
            yield return new object[] { missing.ToJsonString() };
            var nulled = JsonNode.Parse(Valid)!.AsObject(); nulled["roles"]![property] = null;
            yield return new object[] { nulled.ToJsonString() };
        }
        foreach (string role in new[] { "\"absent\"", "\"Blue\"", "\"\"", "17", "{}", "[]" })
        {
            var node = JsonNode.Parse(Valid)!.AsObject(); node["roles"]!["aim"] = JsonNode.Parse(role);
            yield return new object[] { node.ToJsonString() };
        }
        foreach (string key in new[] { "extra", "Name" })
        {
            var node = JsonNode.Parse(Valid)!.AsObject(); node[key] = 1;
            yield return new object[] { node.ToJsonString() };
        }
        var unknownRole = JsonNode.Parse(Valid)!.AsObject(); unknownRole["roles"]!["extra"] = "blue";
        yield return new object[] { unknownRole.ToJsonString() };
        foreach (string json in new[]
        {
            Valid.Replace("\"name\":", "\"name\":\"duplicate\",\"name\":"),
            Valid.Replace("\"name\":", "\"na\\u006de\":\"duplicate\",\"name\":"),
            Valid.Replace("\"blue\": \"#2176CC\"", "\"blue\":\"#000000\",\"blue\":\"#2176CC\""),
            Valid.Replace("\"blue\": \"#2176CC\"", "\"bl\\u0075e\":\"#000000\",\"blue\":\"#2176CC\""),
            Valid.Replace("\"aim\":", "\"aim\":\"blue\",\"aim\":"),
            Valid.Replace("\"aim\":", "\"a\\u0069m\":\"blue\",\"aim\":")
        }) yield return new object[] { json };
    }

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public void StrictSchemaRejectsInvalidOrIncompleteSchemes(string json)
    {
        using var files = new Files();
        string path = files.Put("scheme.json", json);
        var error = Assert.Throws<InvalidDataException>(() => ColorSchemeConfiguration.Load(path));
        Assert.Contains(path, error.Message);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void RolesMayAllShareOneColorAndConvertFromSrgbExactlyOnce()
    {
        using var files = new Files();
        var loaded = ColorSchemeConfiguration.Load(files.Put("scheme.json", Valid));
        var expected = LinearColor.FromSrgb(33 / 255f, 118 / 255f, 204 / 255f);
        foreach (var property in typeof(ColorScheme).GetProperties().Where(p => p.PropertyType == typeof(LinearColor)))
            Assert.Equal(expected, (LinearColor)property.GetValue(loaded.Settings)!);
        Assert.Equal(.0152085f, loaded.Settings.Background.R, 6);
        Assert.Equal(.1811642f, loaded.Settings.Background.G, 6);
        Assert.Equal(.6038274f, loaded.Settings.Background.B, 6);
        Assert.Equal(1, loaded.Settings.Background.A);
    }

    [Fact]
    public void ArbitraryCaseSensitivePaletteNamesAndLowercaseHexAreSupported()
    {
        using var files = new Files();
        var node = JsonNode.Parse(Valid)!.AsObject();
        node["palette"]!["Blue"] = "#aabbcc";
        node["palette"]!["an unused color"] = "#123456";
        node["roles"]!["walls"] = "Blue";
        var loaded = ColorSchemeConfiguration.Load(files.Put("scheme.json", node.ToJsonString()));
        Assert.Equal(LinearColor.FromSrgb(170 / 255f, 187 / 255f, 204 / 255f), loaded.Settings.Walls);
        Assert.NotEqual(loaded.Settings.Walls, loaded.Settings.Ball);
    }

    [Fact]
    public void LoadedSchemeIsAnImmutableSnapshotIndependentOfLaterFileChanges()
    {
        using var files = new Files();
        string path = files.Put("scheme.json", Valid);
        var first = ColorSchemeConfiguration.Load(path);
        files.Put("scheme.json", Valid.Replace("#2176CC", "#FFFFFF"));
        var second = ColorSchemeConfiguration.Load(path);
        Assert.NotEqual(first.Settings.Background, second.Settings.Background);
        Assert.All(typeof(ColorScheme).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.Empty(typeof(ColorScheme).GetFields());
    }

    [Fact]
    public void DefaultAndRelativeOverridePathsUseIndependentBaseAndCallerDirectories()
    {
        using var files = new Files();
        string defaultPath = files.Put("base/Config/colors.json", Valid);
        string overridePath = files.Put("caller/override.json", Valid.Replace("Test scheme", "Override"));
        string baseDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(defaultPath)!)!;
        string currentDirectory = System.IO.Path.GetDirectoryName(overridePath)!;
        Assert.Equal(defaultPath, ColorSchemeConfiguration.LoadForMode([], true, baseDirectory, currentDirectory)!.Path);
        var loaded = ColorSchemeConfiguration.LoadForMode(["--physics-config", "irrelevant.json", "--colors-config", "override.json"],
            true, baseDirectory, currentDirectory)!;
        Assert.Equal(overridePath, loaded.Path);
        Assert.Equal("Override", loaded.Settings.Name);
    }

    [Fact]
    public void GalleryNeedsNoFileUnlessColorsOverrideIsExplicit()
    {
        using var files = new Files();
        Assert.Null(ColorSchemeConfiguration.LoadForMode([], false, files.Directory, files.Directory));
        string path = files.Put("override.json", Valid);
        Assert.NotNull(ColorSchemeConfiguration.LoadForMode(["--colors-config", path], false, files.Directory, files.Directory));
        Assert.Throws<InvalidDataException>(() => ColorSchemeConfiguration.LoadForMode(["--colors-config", "missing.json"],
            false, files.Directory, files.Directory));
        Assert.Throws<InvalidDataException>(() => ColorSchemeConfiguration.LoadForMode([], true, files.Directory, files.Directory));
    }

    [Theory]
    [InlineData("--colors-config")]
    [InlineData("--colors-config", "")]
    [InlineData("--colors-config", "--showcase")]
    [InlineData("--colors-config=scheme.json")]
    [InlineData("--colors-config", "one.json", "--colors-config", "two.json")]
    public void InvalidOverrideSyntaxFailsBeforeLoading(params string[] args)
    {
        using var files = new Files();
        Assert.Throws<ArgumentException>(() => ColorSchemeConfiguration.LoadForMode(args, false, files.Directory, files.Directory));
    }

    [Fact]
    public void DescriptionIsInvariantAndContainsEffectiveRolesAndSourcePath()
    {
        using var files = new Files();
        var loaded = ColorSchemeConfiguration.Load(files.Put("scheme.json", Valid));
        string expected = loaded.Describe();
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(expected, loaded.Describe());
        }
        finally { CultureInfo.CurrentCulture = original; }
        Assert.Contains("name=Test scheme;", expected);
        Assert.Contains("background=0.015", expected);
        Assert.Contains("brickAccent=", expected);
        Assert.Contains($"path={loaded.Path}", expected);
    }

    [Fact]
    public void ShippedUnityPaletteAndAlternativeAreCompleteAndDistinct()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "BrutalBreakout.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var unity = ColorSchemeConfiguration.Load(System.IO.Path.Combine(directory!.FullName, "Config", "colors.json")).Settings;
        var night = ColorSchemeConfiguration.Load(System.IO.Path.Combine(directory.FullName, "Config", "ColorSchemes", "night.json")).Settings;
        Assert.Equal("Prototype Studio", unity.Name);
        Assert.Equal(LinearColor.FromSrgb(51 / 255f, 51 / 255f, 45 / 255f), unity.Background);
        Assert.Equal(new LinearColor(1, 1, 1), unity.Walls);
        Assert.Equal(LinearColor.FromSrgb(255 / 255f, 125 / 255f, 110 / 255f), unity.Ball);
        Assert.Equal(unity.Ball, unity.BrickAccent);
        Assert.Equal(LinearColor.FromSrgb(56 / 255f, 27 / 255f, 24 / 255f), unity.BackgroundBottom);
        Assert.Equal(unity.Ball, unity.CatchReady);
        Assert.Equal(unity.Ball, unity.BrickPrimary);
        Assert.Equal(unity.Ball, unity.BrickSecondary);
        Assert.Equal(unity.Walls, unity.Aim);
        Assert.Equal(LinearColor.FromSrgb(232 / 255f, 231 / 255f, 203 / 255f), unity.HeldRing);
        Assert.Equal(unity.HeldRing, unity.CatchLine);
        Assert.NotEqual(unity.Background, night.Background);
        Assert.NotEqual(unity.Ball, night.Ball);
        Assert.NotEqual(unity.Name, night.Name);
    }

    [Theory]
    [InlineData(float.NaN, 0, 0, 1)]
    [InlineData(0, float.PositiveInfinity, 0, 1)]
    [InlineData(0, 0, -1, 1)]
    [InlineData(1.1f, 0, 0, 1)]
    [InlineData(0, 0, 0, 0)]
    public void DirectTypedConstructionRejectsInvalidLinearColors(float r, float g, float b, float a)
    {
        var color = new LinearColor(r, g, b, a);
        Assert.Throws<ArgumentException>(() => new ColorScheme("Test", color, color, color, color, color, color,
            color, color, color, color, color, color, color, color));
    }

    private sealed class Files : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "brutal-colors-" + Guid.NewGuid().ToString("N"));
        public string Put(string relativePath, string text)
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, relativePath));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
    }
}
