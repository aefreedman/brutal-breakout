using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using BrutalBreakout.Presentation;
using Xunit;

public class PresentationStyleConfigurationTests
{
    internal const string Valid = """
        {"heldRingRadiusScreenFraction":0.2,"heldRingWidthScreenFraction":0.02,"heldRingDashCount":16,
        "heldRingGapFraction":0.5,"heldRingCyclesPerSecond":2,"aimWidthRadiusRatio":0.25,
        "impactRingWidthRadiusRatio":0.1,"reflectedLengthRadiusRatio":2,"fallbackAimLengthRadiusRatio":4,
        "brickCornerFraction":0.1,"catchLineWidthScreenFraction":0.01,
        "catchLineDashLengthMultiples":3,"catchLineGapMultiples":3}
        """;

    public static IEnumerable<object[]> Invalid()
    {
        foreach (string json in new[] { "null", "[]", "{}", "{broken", Valid.Replace("{", "{\"extra\":1,"),
            Valid.Replace("{", "{\"heldRingDashCount\":16,"), Valid.Replace("{", "{\"heldRingDashCo\\u0075nt\":16,") })
            yield return [json];
        foreach (var (key, _) in JsonNode.Parse(Valid)!.AsObject())
        {
            var missing = JsonNode.Parse(Valid)!.AsObject(); missing.Remove(key); yield return [missing.ToJsonString()];
            foreach (string value in new[] { "null", "0", "-1", "1e300", "1e-300" })
            {
                var invalid = JsonNode.Parse(Valid)!; invalid[key] = JsonNode.Parse(value); yield return [invalid.ToJsonString()];
            }
        }
        foreach (var (key, value) in new[] { ("heldRingGapFraction", "1"), ("heldRingDashCount", "4097"),
            ("heldRingDashCount", "1.5"), ("heldRingGapFraction", "0.01"), ("brickCornerFraction", "0.6"), ("catchLineGapMultiples", "0.5") })
        {
            var invalid = JsonNode.Parse(Valid)!; invalid[key] = JsonNode.Parse(value); yield return [invalid.ToJsonString()];
        }
    }

    [Theory] [MemberData(nameof(Invalid))]
    public void StrictRequiredSchemaRejectsInvalidAndUnsupportedSpacing(string json)
    {
        using var files = new GameplayConfigurationTests.Files();
        string path = files.Put(json);
        Assert.Contains(path, Assert.Throws<InvalidDataException>(() => PresentationStyleConfiguration.Load(path)).Message);
    }

    [Fact]
    public void LoadedSnapshotIsImmutableAndDefaultsAreOnlyInFile()
    {
        using var files = new GameplayConfigurationTests.Files();
        var loaded = PresentationStyleConfiguration.Load(files.Put(Valid));
        loaded.Settings.Validate(TestSettings.Gameplay);
        Assert.Equal(.2, loaded.Settings.HeldRingRadiusScreenFraction);
        Assert.Equal(16, loaded.Settings.HeldRingDashCount);
        Assert.All(typeof(PresentationStyle).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.Contains("HeldRingCyclesPerSecond=2", loaded.Describe());
    }

    [Fact]
    public void DefaultExecutablePathAndCallerRelativeOverrideAndGalleryRules()
    {
        using var files = new GameplayConfigurationTests.Files();
        string root = files.Put(Valid, "style.json");
        string custom = files.Put(Valid.Replace("\"heldRingCyclesPerSecond\":2", "\"heldRingCyclesPerSecond\":3"), "other.json");
        string dir = Path.GetDirectoryName(root)!;
        Assert.Equal(root, PresentationStyleConfiguration.LoadForMode([], true, dir, Path.GetTempPath())!.Path);
        Assert.Equal(3, PresentationStyleConfiguration.LoadForMode(["--style-config", "other.json"], false, "not-used", dir)!.Settings.HeldRingCyclesPerSecond);
        Assert.Null(PresentationStyleConfiguration.LoadForMode([], false, "missing", "missing"));
        Assert.Throws<InvalidDataException>(() => PresentationStyleConfiguration.LoadForMode(["--style-config", "missing"], false, dir, dir));
        Assert.Equal(custom, PresentationStyleConfiguration.LoadForMode(["--style-config", custom], false, dir, dir)!.Path);
    }

    [Theory]
    [InlineData("--style-config")]
    [InlineData("--style-config", "")]
    [InlineData("--style-config", "--showcase")]
    [InlineData("--style-config=x.json")]
    [InlineData("--style-config", "one", "--style-config", "two")]
    public void InvalidCliSyntaxFailsBeforeLoading(params string[] args) =>
        Assert.Throws<ArgumentException>(() => PresentationStyleConfiguration.LoadForMode(args, false, "unused", "unused"));

    [Fact]
    public void CrossGeometryIsValidatedWithoutNativeWorld()
    {
        using var files = new GameplayConfigurationTests.Files();
        var style = PresentationStyleConfiguration.Load(files.Put(Valid.Replace("\"catchLineWidthScreenFraction\":0.01", "\"catchLineWidthScreenFraction\":0.000001"))).Settings;
        Assert.Throws<ArgumentException>(() => style.Validate(TestSettings.Gameplay));
    }
}
