using System.Globalization;
using System.Text.Json;
using BrutalBreakout.Configuration;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using Xunit;

public class PhysicsConfigurationTests
{
    private const string Valid = """
        {"updateHz":144.5,"substeps":3,"maxCatchUpSteps":7,"contactHertz":90,"contactDampingRatio":0.7}
        """;

    private sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "brutal-physics-config-" + Guid.NewGuid().ToString("N"));
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public string Put(string text, string name = "custom.json")
        {
            var path = Path.Combine(Directory, name);
            File.WriteAllText(path, text);
            return path;
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }

    [Fact]
    public void CustomRequiredFieldsLoadWithoutProductionDefaultsOrFloatConstructorBinding()
    {
        using var files = new Files();
        var path = files.Put(Valid);
        var loaded = PhysicsConfiguration.Load(path);
        Assert.Equal(Path.GetFullPath(path), loaded.Path);
        Assert.Equal(144.5, loaded.Settings.UpdateHz);
        Assert.Equal(1 / 144.5, loaded.Settings.StepSeconds);
        Assert.Equal(3, loaded.Settings.Substeps);
        Assert.Equal(7, loaded.Settings.MaxCatchUpSteps);
        Assert.Equal(90, loaded.Settings.ContactHertz);
        Assert.Equal(.7f, loaded.Settings.ContactDampingRatio);
    }

    [Fact]
    public void ShippedAssetIsCopiedAndTheActualFileValuesAreLoaded()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "physics.json");
        Assert.True(File.Exists(path));
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var loaded = PhysicsConfiguration.LoadForMode([], true, AppContext.BaseDirectory, Path.GetTempPath());
        Assert.NotNull(loaded);
        Assert.Equal(path, loaded.Path);
        Assert.Equal(doc.RootElement.GetProperty("updateHz"u8).GetDouble(), loaded.Settings.UpdateHz);
        Assert.Equal(doc.RootElement.GetProperty("substeps"u8).GetInt32(), loaded.Settings.Substeps);
        Assert.Equal(doc.RootElement.GetProperty("maxCatchUpSteps"u8).GetInt32(), loaded.Settings.MaxCatchUpSteps);
        Assert.Equal(doc.RootElement.GetProperty("contactHertz"u8).GetSingle(), loaded.Settings.ContactHertz);
        Assert.Equal(doc.RootElement.GetProperty("contactDampingRatio"u8).GetSingle(), loaded.Settings.ContactDampingRatio);
    }

    [Theory]
    [InlineData("updateHz")] [InlineData("substeps")] [InlineData("maxCatchUpSteps")]
    [InlineData("contactHertz")] [InlineData("contactDampingRatio")]
    public void EveryFieldIsRequired(string omitted)
    {
        using var doc = JsonDocument.Parse(Valid);
        string json = JsonSerializer.Serialize(doc.RootElement.EnumerateObject()
            .Where(p => p.Name != omitted).ToDictionary(p => p.Name, p => p.Value));
        Reject(json);
    }

    [Theory]
    [InlineData("null")] [InlineData("[]")] [InlineData("true")] [InlineData("{}")]
    [InlineData("{broken")] [InlineData("{\"updateHz\":NaN}")]
    [InlineData("{\"updateHz\":Infinity}")]
    public void BadRootAndMalformedNumbersReject(string json) => Reject(json);

    [Theory]
    [InlineData("\"extra\":1")]
    [InlineData("\"updateHZ\":60")]
    [InlineData("\"updateHz\":60")]
    [InlineData("\"update\\u0048z\":60")]
    public void UnknownMisspelledAndDuplicateIncludingEscapedPropertiesReject(string extra)
        => Reject(Valid.TrimEnd()[..^1] + "," + extra + "}");

    [Theory]
    [InlineData("updateHz", "0")] [InlineData("updateHz", "-1")]
    [InlineData("updateHz", "1e999")] [InlineData("updateHz", "1e300")]
    [InlineData("updateHz", "1e-300")] [InlineData("updateHz", "\"60\"")]
    [InlineData("updateHz", "null")] [InlineData("updateHz", "true")]
    [InlineData("substeps", "0")] [InlineData("substeps", "-1")]
    [InlineData("substeps", "1.5")] [InlineData("substeps", "2147483648")]
    [InlineData("maxCatchUpSteps", "0")] [InlineData("maxCatchUpSteps", "-1")]
    [InlineData("maxCatchUpSteps", "null")] [InlineData("maxCatchUpSteps", "[]")]
    [InlineData("contactHertz", "-1")] [InlineData("contactHertz", "1e100")]
    [InlineData("contactHertz", "1e-100")]
    [InlineData("contactDampingRatio", "-1")] [InlineData("contactDampingRatio", "1e999")]
    [InlineData("contactDampingRatio", "\"1\"")]
    public void InvalidTypesAndNativeNumericRangesReject(string name, string replacement)
    {
        using var doc = JsonDocument.Parse(Valid);
        string json = "{" + string.Join(",", doc.RootElement.EnumerateObject()
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + (p.Name == name ? replacement : p.Value.GetRawText()))) + "}";
        Reject(json);
    }

    [Theory]
    [InlineData("")] [InlineData("\0bad.json")]
    public void InvalidPathHasContext(string path)
    {
        var error = Assert.Throws<InvalidDataException>(() => PhysicsConfiguration.Load(path));
        Assert.Contains("physics configuration", error.Message);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void MissingFileFailsWithAbsolutePathRatherThanFallback()
    {
        using var files = new Files();
        var missing = Path.Combine(files.Directory, "absent.json");
        var error = Assert.Throws<InvalidDataException>(() => PhysicsConfiguration.Load(missing));
        Assert.Contains(missing, error.Message);
        Assert.IsType<FileNotFoundException>(error.InnerException);
    }

    [Fact]
    public void GalleryDoesNotLoadImplicitFileButValidatesExplicitOverride()
    {
        using var files = new Files();
        Assert.Null(PhysicsConfiguration.LoadForMode(["--showcase"], false, files.Directory, files.Directory));
        files.Put(Valid);
        var loaded = PhysicsConfiguration.LoadForMode(["--showcase", "--physics-config", "custom.json"],
            false, Path.Combine(files.Directory, "not-used"), files.Directory);
        Assert.Equal(144.5, loaded!.Settings.UpdateHz);
        Assert.Throws<InvalidDataException>(() => PhysicsConfiguration.LoadForMode(
            ["--showcase", "--physics-config", "missing.json"], false, files.Directory, files.Directory));
    }

    [Theory]
    [InlineData("missing")] [InlineData("empty")] [InlineData("next-option")]
    [InlineData("duplicate")] [InlineData("equals")]
    public void InvalidCommandLineFailsWithoutUsingDefault(string kind)
    {
        string[] args = kind switch
        {
            "missing" => ["--physics-config"],
            "empty" => ["--physics-config", " "],
            "next-option" => ["--physics-config", "--showcase"],
            "duplicate" => ["--physics-config", "a.json", "--physics-config", "b.json"],
            _ => ["--physics-config=a.json"]
        };
        Assert.Throws<ArgumentException>(() => PhysicsConfiguration.LoadForMode(args, true, "unused", "unused"));
    }

    [Fact]
    public void ExplicitAbsoluteAndRelativePathsUseCallerDirectoryNotExecutableDirectory()
    {
        using var caller = new Files();
        using var executable = new Files();
        var path = caller.Put(Valid);
        executable.Put("invalid", "physics.json");
        foreach (string selected in new[] { path, "custom.json" })
        {
            var loaded = PhysicsConfiguration.LoadForMode(["--physics-config", selected], true,
                executable.Directory, caller.Directory);
            Assert.Equal(path, loaded!.Path);
            Assert.Equal(144.5, loaded.Settings.UpdateHz);
        }
    }

    [Fact]
    public void StartupDescriptionUsesInvariantActualValues()
    {
        using var files = new Files();
        var loaded = PhysicsConfiguration.Load(files.Put(Valid));
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string text = loaded.Describe();
            Assert.Contains("updateHz=144.5;", text);
            Assert.Contains("substeps=3; maxCatchUpSteps=7; contactHertz=90; contactDampingRatio=0.7;", text);
            Assert.Contains(loaded.Path, text);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(144)] [InlineData(240)]
    public void CustomRateAndSingleCatchUpStepPreservePendingAndFocusRules(double rate)
    {
        var settings = new PhysicsSettings(rate, 4, 1, 90, 1);
        using var session = new BallSandboxSession(settings);
        Assert.Same(settings, session.Settings);
        Assert.Same(settings, session.Simulation.Settings);
        var view = new ArenaViewport(1280, 720, 1280, 720);
        var idle = new InputSnapshot { Focused = true };
        session.Advance(idle, 0, view);
        Assert.Equal(0, session.Advance(idle with { KeyboardPrimary = true }, settings.StepSeconds / 2, view));
        Assert.Equal(1, session.Advance(idle, settings.StepSeconds * 100, view));
        Assert.Equal(BallPhase.Flying, session.Simulation.State.Phase);
        Assert.Equal(1, session.TotalTicks);
        session.Advance(default, 100, view);
        var held = idle with { KeyboardPrimary = true };
        Assert.Equal(0, session.Advance(held, 100, view));
        var position = session.Simulation.State.Position;
        Assert.Equal(1, session.Advance(held, settings.StepSeconds, view));
        Assert.Equal(BallPhase.Flying, session.Simulation.State.Phase);
        Assert.NotEqual(position, session.Simulation.State.Position);
    }

    [Fact]
    public void ConfiguredTouchingPoseRelaunchMovesOnSeparateEdgeAndMayRegainEligibility()
    {
        // This explicit fixture reaches a native touching pose; a new begin event on relaunch is valid.
        var settings = new PhysicsSettings(60, 2, 1, 60, 1);
        using var session = new BallSandboxSession(settings);
        var view = new ArenaViewport(1280, 720, 1280, 720);
        var idle = new InputSnapshot { Focused = true };
        var press = idle with { KeyboardPrimary = true };
        session.Advance(idle, 0, view);
        session.Advance(press, settings.StepSeconds, view);
        for (int i = 0; i < 120 && !session.Simulation.State.CatchEligible; i++)
            session.Advance(idle, settings.StepSeconds, view);
        Assert.True(session.Simulation.State.CatchEligible);
        var caught = session.Simulation.State.Position;
        session.Advance(press, settings.StepSeconds, view);
        Assert.Equal(BallPhase.Aiming, session.Simulation.State.Phase);
        for (int i = 0; i < 6; i++) session.Advance(press, settings.StepSeconds, view);
        Assert.Equal(caught, session.Simulation.State.Position);
        var away = idle with { KeyboardDirection = new(0, -1) };
        session.Advance(away, settings.StepSeconds, view);
        session.Advance(away with { KeyboardPrimary = true }, settings.StepSeconds, view);
        Assert.Equal(BallPhase.Flying, session.Simulation.State.Phase);
        Assert.True(session.Simulation.State.Position.Y > caught.Y);
        Assert.True(session.Simulation.State.CatchEligible);
    }

    private static void Reject(string json)
    {
        using var files = new Files();
        string path = files.Put(json);
        var error = Assert.Throws<InvalidDataException>(() => PhysicsConfiguration.Load(path));
        Assert.Contains(path, error.Message);
        Assert.NotNull(error.InnerException);
    }
}
