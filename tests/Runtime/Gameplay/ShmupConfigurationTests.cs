using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using Xunit;

public sealed class ShmupConfigurationTests
{
    private static string DefaultJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "shmup.json"));
    [Fact]
    public void ShippedSettingsLoadAndValidateBeforeNativeAllocation()
    {
        using var files = new GameplayConfigurationTests.Files();
        var settings = ShmupConfiguration.Load(files.Put(DefaultJson)).Settings;
        var physics = PhysicsConfiguration.Load(files.Put(GameplayConfigurationTests.Physics, "physics.json"));
        settings.ValidatePhysics(physics.Settings);
        Assert.Equal(1280, settings.Arena.Width);
        Assert.Equal(720, settings.Arena.Height);
        Assert.Equal(100, settings.Avatar.Health);
        Assert.Equal(20, settings.Ball.MaxWallHits);
        Assert.True(settings.Ball.BounceOnBricks);
    }
    [Fact]
    public void EveryFieldIsRequiredAndEverySectionRejectsUnknownsAndNull()
    {
        var original = JsonNode.Parse(DefaultJson)!.AsObject();
        foreach (var (section, value) in original)
        {
            var missingSection = original.DeepClone().AsObject();
            missingSection.Remove(section); Reject(missingSection.ToJsonString());
            var nullSection = original.DeepClone();
            nullSection[section] = null; Reject(nullSection.ToJsonString());
            var extra = original.DeepClone();
            extra[section]!["extra"] = 1; Reject(extra.ToJsonString());
            foreach (var (field, _) in value!.AsObject())
            {
                var node = original.DeepClone();
                node[section]!.AsObject().Remove(field);
                Reject(node.ToJsonString());
            }
        }
    }
    [Theory]
    [InlineData("{}")] [InlineData("null")] [InlineData("[]")]
    [InlineData("{broken")] [InlineData("{\"arena\":{},\"arena\":{}}")]
    public void MalformedAndDuplicateDataReject(string json) => Reject(json);
    [Theory]
    [InlineData("arena", "width", -1)]
    [InlineData("arena", "initialPlayAreaWidth", 5000)]
    [InlineData("avatar", "health", 0)]
    [InlineData("avatar", "invulnerabilitySeconds", -1)]
    [InlineData("ball", "fireRate", 0)]
    [InlineData("ball", "maxWallHits", 0)]
    [InlineData("brick", "health", -1)]
    [InlineData("noise", "gapThreshold", 2)]
    [InlineData("noise", "columnScale", 0)]
    [InlineData("capacity", "maxBalls", 999999)]
    public void InvalidTuningRejects(string section, string field, double value)
    {
        var node = JsonNode.Parse(DefaultJson)!;
        node[section]![field] = value;
        Reject(node.ToJsonString());
    }
    [Fact]
    public void InvalidPhysicsCombinationRejectsManagedOnly()
    {
        using var files = new GameplayConfigurationTests.Files();
        var node = JsonNode.Parse(DefaultJson)!;
        node["ball"]!["fireRate"] = 1000;
        var shmup = ShmupConfiguration.Load(files.Put(node.ToJsonString()));
        var physics = PhysicsConfiguration.Load(files.Put(GameplayConfigurationTests.Physics, "physics.json"));
        Assert.Throws<InvalidDataException>(() => ShmupConfiguration.ValidatePhysics(shmup, physics));
    }
    private static void Reject(string json)
    {
        using var files = new GameplayConfigurationTests.Files();
        var path = files.Put(json);
        Assert.Contains(path, Assert.Throws<InvalidDataException>(() => ShmupConfiguration.Load(path)).Message);
    }
}
