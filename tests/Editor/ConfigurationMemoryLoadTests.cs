using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using BrutalBreakout.Editor;
using Xunit;

public sealed class ConfigurationMemoryLoadTests
{
    [Theory]
    [InlineData("window", typeof(WindowConfiguration))]
    [InlineData("physics", typeof(PhysicsConfiguration))]
    [InlineData("execution", typeof(ExecutionConfiguration))]
    [InlineData("gameplay", typeof(GameplayConfiguration))]
    [InlineData("shmup", typeof(ShmupConfiguration))]
    [InlineData("input", typeof(InputConfiguration))]
    [InlineData("trails", typeof(BallTrailConfiguration))]
    [InlineData("colors", typeof(ColorSchemeConfiguration))]
    [InlineData("style", typeof(PresentationStyleConfiguration))]
    [InlineData("audio", typeof(AudioConfiguration))]
    [InlineData("overlay", typeof(OverlayConfiguration))]
    [InlineData("inspector", typeof(InspectorConfiguration))]
    public void InMemoryLoadsAreEquivalentAndRemainStrict(string section, Type loader)
    {
        string path = ConfigurationWorkspaceTests.Paths()[section]; string text = File.ReadAllText(path);
        var fromFile = loader.GetMethod("Load", [typeof(string)])!.Invoke(null, [path])!;
        var loadMemory = loader.GetMethod("Load", [typeof(string), typeof(string)])!;
        var fromMemory = loadMemory.Invoke(null, [path, text])!;
        Assert.Equal(path, fromMemory.GetType().GetProperty("Path")!.GetValue(fromMemory));
        var settings = fromMemory.GetType().GetProperty("Settings")!;
        if (fromFile is LoadedGameplayConfiguration gameFile && fromMemory is LoadedGameplayConfiguration gameMemory)
        {
            Assert.Equal(gameFile.Describe(), gameMemory.Describe());
            Assert.Equal(gameFile.Settings.Ball.Spawn, gameMemory.Settings.Ball.Spawn);
            Assert.Equal(JsonSerializer.Serialize(gameFile.Settings.Breakout!.Bricks), JsonSerializer.Serialize(gameMemory.Settings.Breakout!.Bricks));
        }
        else if (fromFile is LoadedShmupConfiguration shmupFile && fromMemory is LoadedShmupConfiguration shmupMemory)
        {
            Assert.Equal(shmupFile.Describe(), shmupMemory.Describe());
            Assert.Equal(shmupFile.Settings.Ball.Speed, shmupMemory.Settings.Ball.Speed);
            Assert.Equal(shmupFile.Settings.Brick.Shapes.ToArray(), shmupMemory.Settings.Brick.Shapes.ToArray());
            Assert.Equal(shmupFile.Settings.Brick.QuarterTurns.ToArray(), shmupMemory.Settings.Brick.QuarterTurns.ToArray());
        }
        else Assert.Equal(JsonSerializer.Serialize(settings.GetValue(fromFile)), JsonSerializer.Serialize(settings.GetValue(fromMemory)));
        var node = JsonNode.Parse(text)!.AsObject(); string first = node.First().Key;
        var duplicate = text.TrimEnd().TrimEnd('}') + ",\"" + first + "\":" + node[first]!.ToJsonString() + "}";
        void Reject(string json)
        {
            var error = Assert.Throws<TargetInvocationException>(() => loadMemory.Invoke(null, [path, json]));
            Assert.IsType<InvalidDataException>(error.InnerException); Assert.Contains(path, error.InnerException!.Message);
        }
        Reject(duplicate); node["unknownSetting"] = true; Reject(node.ToJsonString());
        node.Remove("unknownSetting"); node.Remove(first); Reject(node.ToJsonString());
    }

    [Fact]
    public void ConfigurationWindowBoundsAreRequiredAndValidatedByInspectorLoader()
    {
        string path = ConfigurationWorkspaceTests.Paths()["inspector"];
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json["configurationWindow"]!["width"] = 0;
        Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(path, json.ToJsonString()));
        json.Remove("configurationWindow");
        Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(path, json.ToJsonString()));
    }

    [Fact]
    public void AssetsResolveUsingActualSelectedSourcePathWithoutReadingItsJsonFile()
    {
        string audioPath = ConfigurationWorkspaceTests.Paths()["audio"];
        string nonexistentPath = Path.Combine(Path.GetDirectoryName(audioPath)!, "not-on-disk.json");
        var memory = AudioConfiguration.Load(nonexistentPath, File.ReadAllText(audioPath));
        Assert.Equal(AudioConfiguration.Load(audioPath).Settings.Assets, memory.Settings.Assets);
        var inspectorPath = ConfigurationWorkspaceTests.Paths()["inspector"];
        var inspector = InspectorConfiguration.Load(nonexistentPath, File.ReadAllText(inspectorPath));
        Assert.Equal(InspectorConfiguration.Load(inspectorPath).Settings.FontPath, inspector.Settings.FontPath);
        Assert.Null(inspector.Settings.Preview);
        Assert.Equal(InspectorConfiguration.Load(inspectorPath).HistoryCapacity, inspector.HistoryCapacity);
        Assert.False(File.Exists(nonexistentPath));
    }

    [Fact]
    public void ProjectConfigsOnlyChangesNeededImplicitPathsAndNeverFallsBack()
    {
        string directory = AppContext.BaseDirectory;
        Assert.Null(PhysicsConfiguration.LoadForMode(["--project-configs"], false, "unused", "unused"));
        var loaded = PhysicsConfiguration.LoadForMode(["--project-configs"], true, "unused", directory)!;
        Assert.Equal(ConfigurationWorkspaceTests.Paths()["physics"], loaded.Path);
        var explicitPath = PhysicsConfiguration.LoadForMode(["--project-configs", "--physics-config", loaded.Path], false, "unused", directory)!;
        Assert.Equal(loaded.Path, explicitPath.Path);
        Assert.Equal(loaded.Path, PhysicsConfiguration.LoadForMode([], true, directory, "unused")!.Path);
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var error = Assert.Throws<InvalidDataException>(() => WindowConfiguration.LoadForMode(["--project-configs"], directory, missing));
        Assert.Contains(Path.Combine(missing, "Config", "window.json"), error.Message);
    }
}
