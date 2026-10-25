using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using BrutalBreakout.Platform.Input;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using Xunit;

public class GameplayConfigurationTests
{
    internal const string Valid = """
        {"arena":{"width":1800,"height":1000,"left":80,"top":120,"right":1600,"bottom":850,"wallThickness":24},
         "ball":{"radius":23,"speed":950,"spawnX":420,"spawnY":750,"initialAimX":3,"initialAimY":-4},
         "breakout":{"startingBalls":3,"catchZoneHeightFraction":0.3333333333333333,"maxBouncesWithoutBrick":12,"bricks":[{"x":400,"y":200,"width":100,"height":30},{"x":800,"y":200,"width":100,"height":30}]}}
        """;
    internal const string Physics = """
        {"updateHz":120,"substeps":4,"maxCatchUpSteps":8,"contactHertz":120,"contactDampingRatio":1}
        """;
    internal sealed class Files : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "brutal-game-config-" + Guid.NewGuid().ToString("N"));
        public Files() => System.IO.Directory.CreateDirectory(Directory);
        public string Put(string json, string name = "custom.json")
        {
            string path = Path.GetFullPath(Path.Combine(Directory, name));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json); return path;
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }

    [Fact]
    public void AllCustomFieldsLoadAndNormalizeWithoutFallback()
    {
        using var files = new Files();
        var loaded = GameplayConfiguration.Load(files.Put(Valid));
        var a = loaded.Settings.Arena; var b = loaded.Settings.Ball;
        Assert.Equal((1800d, 1000d, 80d, 120d, 1600d, 850d, 24d),
            (a.Width, a.Height, a.Left, a.Top, a.Right, a.Bottom, a.WallThickness));
        Assert.Equal((23d, 950d, 420d, 750d), (b.Radius, b.Speed, b.Spawn.X, b.Spawn.Y));
        Assert.Equal(.6, b.InitialAim.X, 10); Assert.Equal(-.8, b.InitialAim.Y, 10);
    }

    [Theory]
    [InlineData("arena", "width")] [InlineData("arena", "height")] [InlineData("arena", "left")]
    [InlineData("arena", "top")] [InlineData("arena", "right")] [InlineData("arena", "bottom")]
    [InlineData("arena", "wallThickness")]
    [InlineData("ball", "radius")] [InlineData("ball", "speed")] [InlineData("ball", "spawnX")]
    [InlineData("ball", "spawnY")] [InlineData("ball", "initialAimX")] [InlineData("ball", "initialAimY")]
    public void EveryNestedFieldIsRequired(string section, string field)
    {
        var node = JsonNode.Parse(Valid)!;
        node[section]!.AsObject().Remove(field);
        Reject(node.ToJsonString());
    }

    [Theory]
    [InlineData("{}")] [InlineData("null")] [InlineData("[]")] [InlineData("false")]
    [InlineData("{broken")]
    [InlineData("{\"arena\":null,\"ball\":null}")]
    [InlineData("{\"arena\":{},\"ball\":{}}")]
    public void InvalidRootsAndSectionsReject(string json) => Reject(json);

    [Theory]
    [InlineData("arena")] [InlineData("ball")]
    public void MissingNullAndWrongTypeSectionsReject(string section)
    {
        foreach (string? replacement in new string?[] { null, "null", "[]", "true", "1" })
        {
            var node = JsonNode.Parse(Valid)!.AsObject();
            if (replacement is null) node.Remove(section);
            else node[section] = JsonNode.Parse(replacement);
            Reject(node.ToJsonString());
        }
    }

    [Theory]
    [InlineData("root", "extra", "1")] [InlineData("arena", "extra", "1")]
    [InlineData("ball", "extra", "1")]
    [InlineData("root", "arena", "{}")] [InlineData("arena", "width", "1800")]
    [InlineData("ball", "speed", "950")]
    [InlineData("arena", "wi\\u0064th", "1800")]
    [InlineData("ball", "spe\\u0065d", "950")]
    public void UnknownAndDuplicateKeysRejectAtEveryLevelIncludingEscapes(string section, string name, string value)
    {
        if (section == "root") Reject(Valid.TrimEnd()[..^1] + ",\"" + name + "\":" + value + "}");
        else
        {
            string start = "\"" + section + "\":{";
            Reject(Valid.Replace(start, start + "\"" + name + "\":" + value + ","));
        }
    }

    [Theory]
    [InlineData("arena", "width", "0")] [InlineData("arena", "height", "-1")]
    [InlineData("arena", "width", "2000000")] [InlineData("arena", "width", "\"1800\"")]
    [InlineData("arena", "left", "1700")] [InlineData("arena", "top", "900")]
    [InlineData("arena", "wallThickness", "100")]
    [InlineData("arena", "right", "1790")]
    [InlineData("arena", "bottom", "990")]
    [InlineData("ball", "radius", "1000")] [InlineData("ball", "radius", "1e-100")]
    [InlineData("ball", "speed", "0")] [InlineData("ball", "speed", "1e100")]
    [InlineData("ball", "speed", "null")] [InlineData("ball", "speed", "true")]
    [InlineData("ball", "spawnX", "0")] [InlineData("ball", "spawnY", "1000")]
    [InlineData("ball", "initialAimY", "1e999")]
    public void InvalidValuesAndCrossFieldRelationshipsReject(string section, string key, string value)
    {
        var node = JsonNode.Parse(Valid)!;
        node[section]![key] = JsonNode.Parse(value);
        Reject(node.ToJsonString());
    }

    [Fact]
    public void ZeroAimRejects()
    {
        var node = JsonNode.Parse(Valid)!;
        node["ball"]!["initialAimX"] = 0;
        node["ball"]!["initialAimY"] = 0;
        Reject(node.ToJsonString());
    }

    [Fact]
    public void ShippedAssetsAreCopiedAndLoadedFromExecutableDirectory()
    {
        var game = GameplayConfiguration.LoadForMode([], true, AppContext.BaseDirectory, Path.GetTempPath())!;
        var input = InputConfiguration.LoadForMode([], true, AppContext.BaseDirectory, Path.GetTempPath())!;
        using var gameJson = JsonDocument.Parse(File.ReadAllText(game.Path));
        using var inputJson = JsonDocument.Parse(File.ReadAllText(input.Path));
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Config", "gameplay.json"), game.Path);
        Assert.Equal(gameJson.RootElement.GetProperty("arena"u8).GetProperty("width"u8).GetDouble(), game.Settings.Arena.Width);
        Assert.Equal(gameJson.RootElement.GetProperty("ball"u8).GetProperty("radius"u8).GetDouble(), game.Settings.Ball.Radius);
        Assert.Equal(gameJson.RootElement.GetProperty("ball"u8).GetProperty("speed"u8).GetDouble(), game.Settings.Ball.Speed);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Config", "input.json"), input.Path);
        Assert.Equal(inputJson.RootElement.GetProperty("stickDeadzone"u8).GetSingle(), input.Settings.StickDeadzone);
    }

    [Fact]
    public void OverridesAreIndependentAndAllThreeMayBeCombined()
    {
        using var files = new Files(); using var executable = new Files();
        string g = files.Put(Valid, "g.json"), i = files.Put("{\"stickDeadzone\":0.55}", "i.json"), p = files.Put(Physics, "p.json");
        string[] args = ["--input-config", "i.json", "--physics-config", "p.json", "--gameplay-config", "g.json"];
        var game = GameplayConfiguration.LoadForMode(args, true, executable.Directory, files.Directory)!;
        var input = InputConfiguration.LoadForMode(args, true, executable.Directory, files.Directory)!;
        var physics = PhysicsConfiguration.LoadForMode(args, true, executable.Directory, files.Directory)!;
        Assert.Equal(g, game.Path); Assert.Equal(i, input.Path); Assert.Equal(p, physics.Path);
        GameplayConfiguration.ValidatePhysics(game, physics);
        Assert.Null(GameplayConfiguration.LoadForMode([], false, executable.Directory, files.Directory));
        Assert.Null(InputConfiguration.LoadForMode([], false, executable.Directory, files.Directory));
        Assert.Equal(g, GameplayConfiguration.LoadForMode(["--gameplay-config", g], false, "unused", files.Directory)!.Path);
        Assert.Equal(i, InputConfiguration.LoadForMode(["--input-config", i], false, "unused", files.Directory)!.Path);
    }

    [Theory]
    [InlineData("gameplay")] [InlineData("input")]
    public void MissingFilesAndMalformedArgumentsFailWithoutFallback(string section)
    {
        using var files = new Files();
        foreach (string[] args in new string[][]
        {
            ["--" + section + "-config"], ["--" + section + "-config", " "],
            ["--" + section + "-config", "--showcase"], ["--" + section + "-config=x"],
            ["--" + section + "-config", "a", "--" + section + "-config", "b"]
        })
        {
            if (section == "gameplay") Assert.Throws<ArgumentException>(() => GameplayConfiguration.LoadForMode(args, false, files.Directory, files.Directory));
            else Assert.Throws<ArgumentException>(() => InputConfiguration.LoadForMode(args, false, files.Directory, files.Directory));
        }
        var error = section == "gameplay"
            ? Assert.Throws<InvalidDataException>(() => GameplayConfiguration.LoadForMode([], true, files.Directory, files.Directory))
            : Assert.Throws<InvalidDataException>(() => InputConfiguration.LoadForMode([], true, files.Directory, files.Directory));
        Assert.Contains(Path.Combine(files.Directory, "Config", section + ".json"), error.Message);
    }

    [Fact]
    public void CombinedDisplacementOverflowHasBothPathsAndNoWorldIsNeeded()
    {
        using var files = new Files();
        var game = GameplayConfiguration.Load(files.Put(Valid));
        var physics = PhysicsConfiguration.Load(files.Put(Physics.Replace("120,\"substeps", "1e-20,\"substeps"), "p.json"));
        var error = Assert.Throws<InvalidDataException>(() => GameplayConfiguration.ValidatePhysics(game, physics));
        Assert.Contains(game.Path, error.Message); Assert.Contains(physics.Path, error.Message);
    }

    [Fact]
    public void RealSessionUsesLoadedGeometryInputAndNativeSpeedThenResetsConfiguredPose()
    {
        using var files = new Files();
        var game = GameplayConfiguration.Load(files.Put(Valid));
        var input = InputConfiguration.Load(files.Put("{\"stickDeadzone\":0.55}", "i.json"));
        using var session = new BallSandboxSession(TestSettings.Baseline, game.Settings, input.Settings, new(2, 64, 2));
        Assert.Same(game.Settings, session.Simulation.Gameplay);
        Assert.Same(input.Settings, session.Router.Settings);
        var viewport = new ArenaViewport(1000, 1000, 2000, 2000, game.Settings.Arena);
        var idle = new InputSnapshot { Focused = true, PadConnected = true, PadId = 0 };
        session.Advance(idle, 0, viewport);
        session.Advance(idle with { PadStick = new(.3f, 0) }, session.Settings.StepSeconds, viewport);
        Assert.Equal(game.Settings.Ball.InitialAim, session.Simulation.State.AimDirection);
        session.Advance(idle with { PadStick = new(.8f, 0) }, session.Settings.StepSeconds, viewport);
        Assert.Equal(1, session.Simulation.State.AimDirection.X);
        session.Advance(idle with { KeyboardPrimary = true }, session.Settings.StepSeconds, viewport);
        Assert.InRange(Math.Abs(session.Simulation.State.Velocity.X - game.Settings.Ball.Speed), 0, .01);
        session.Advance(idle with { KeyboardRestart = true }, session.Settings.StepSeconds, viewport);
        Assert.Equal(game.Settings.Ball.Spawn, session.Simulation.State.Position);
        Assert.Equal(game.Settings.Ball.InitialAim, session.Simulation.State.AimDirection);
        // Logical cursor is generated from the configured world through the 2x-DPI, letterboxed viewport.
        var target = game.Settings.Ball.Spawn + new Brutal.Numerics.double2(100, 0);
        var cursor = new Brutal.Numerics.double2((target.X * viewport.Scale + viewport.X) / 2,
            (target.Y * viewport.Scale + viewport.Y) / 2);
        session.Advance(idle with { PadConnected = false, Cursor = cursor }, session.Settings.StepSeconds, viewport);
        Assert.InRange(session.Simulation.State.AimDirection.X, .99999, 1);
        Assert.InRange(Math.Abs(session.Simulation.State.AimDirection.Y), 0, .00001);
    }

    [Fact]
    public void DescriptionsUseActualNormalizedValuesAndInvariantCulture()
    {
        using var files = new Files();
        var game = GameplayConfiguration.Load(files.Put(Valid));
        var input = InputConfiguration.Load(files.Put("{\"stickDeadzone\":0.55}", "i.json"));
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Contains("initialAimX=0.6; initialAimY=-0.8;", game.Describe());
            Assert.Contains("radius=23; speed=950; spawnX=420; spawnY=750;", game.Describe());
            Assert.Contains("stickDeadzone=0.55;", input.Describe());
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    private static void Reject(string json)
    {
        using var files = new Files();
        string path = files.Put(json);
        var error = Assert.Throws<InvalidDataException>(() => GameplayConfiguration.Load(path));
        Assert.Contains(path, error.Message); Assert.NotNull(error.InnerException);
    }
}
