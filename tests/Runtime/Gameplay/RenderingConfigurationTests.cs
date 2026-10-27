using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using Xunit;

public sealed class RenderingConfigurationTests
{
    private static string Json => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "rendering.json"));
    private static void Reject(string json) => Assert.Contains("rendering", Assert.Throws<InvalidDataException>(() => RenderingConfiguration.Load("rendering.json", json)).Message);
    [Fact]
    public void ShippedDefaultIsSharedAndAllFieldsAreRequiredTypedAndImmutable()
    {
        var loaded = RenderingConfiguration.Load("unused.json", Json);
        Assert.Equal(ShmupRenderingPath.SharedMeshes, loaded.Settings.Path);
        loaded.Settings.Validate(ShmupConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "shmup.json")).Settings);
        Assert.All(typeof(RenderingSettings).GetProperties(), p => Assert.Null(p.SetMethod));
        var original = JsonNode.Parse(Json)!.AsObject();
        foreach (string key in new[] { "path", "limits", "circleDetail" })
        {
            var node = original.DeepClone().AsObject(); node.Remove(key); Reject(node.ToJsonString());
            node = original.DeepClone().AsObject(); node[key] = null; Reject(node.ToJsonString());
        }
        foreach (string key in original["limits"]!.AsObject().Select(p => p.Key))
        {
            var node = original.DeepClone(); node["limits"]!.AsObject().Remove(key); Reject(node.ToJsonString());
            foreach (JsonNode? invalid in new JsonNode?[] { null, JsonValue.Create("1"), JsonValue.Create(true), JsonValue.Create(1.5), JsonValue.Create(-1) })
            { node = original.DeepClone(); node["limits"]![key] = invalid?.DeepClone(); Reject(node.ToJsonString()); }
        }
        foreach (string path in new[] { "shared", "SharedMeshes", "cpu", "", "CPU-built triangles" })
        { var node = original.DeepClone(); node["path"] = path; Reject(node.ToJsonString()); }
        foreach (string key in new[] { "meshCount", "cpuBytes", "gpuBytes", "instances", "operations" })
        { var node = original.DeepClone(); node["limits"]![key] = 1_000_000_000; Reject(node.ToJsonString()); }
        var low = original.DeepClone(); low["limits"]!["cpuBytes"] = 8191; Reject(low.ToJsonString());
        var gpu = original.DeepClone(); gpu["limits"]!["gpuBytes"] = 8192; Reject(gpu.ToJsonString());
    }
    [Fact]
    public void CircleDetailIsStrictNestedRequiredAndNotNormalized()
    {
        var settings = RenderingConfiguration.Load("unused.json", Json).Settings;
        Assert.Equal(CrushKit.Brutal.Draw2D.CircleDetailMode.Automatic, settings.CircleDetail.Mode);
        Assert.Equal(12, settings.CircleDetail.FixedSides);
        foreach (string key in new[] { "mode", "fixedSides" })
        {
            var node = JsonNode.Parse(Json)!; node["circleDetail"]!.AsObject().Remove(key); Reject(node.ToJsonString());
            node = JsonNode.Parse(Json)!; node["circleDetail"]![key] = null; Reject(node.ToJsonString());
        }
        foreach (long n in new long[] { 3,9,10,-4,0,4100,int.MaxValue,2147483648 })
        {
            var node=JsonNode.Parse(Json)!;node["circleDetail"]!["fixedSides"]=n;Reject(node.ToJsonString());
        }
        foreach (string mode in new[] { "", "Automatic", "fixed", "12" })
        { var node=JsonNode.Parse(Json)!;node["circleDetail"]!["mode"]=mode;Reject(node.ToJsonString()); }
        Reject(Json.Replace("\"fixedSides\":", "\"fixedSides\":12,\"fixedSides\":"));
        Reject(Json.Replace("\"fixedSides\":", "\"unknown\":0,\"fixedSides\":"));
    }
    [Theory]
    [InlineData("null")] [InlineData("[]")] [InlineData("{}")] [InlineData("{broken")]
    [InlineData("{\"path\":true,\"limits\":{}}")] [InlineData("{\"path\":1,\"limits\":{}}")] [InlineData("{\"limits\":[]}")]
    public void MalformedRootAndTypesReject(string json) => Reject(json);
    [Fact]
    public void UnknownDuplicateAndEscapedDuplicateFieldsRejectAtEveryLevel()
    {
        Reject(Json.Replace("\"path\":", "\"unknown\":0,\"path\":"));
        Reject(Json.Replace("\"meshCount\":", "\"unknown\":0,\"meshCount\":"));
        Reject(Json.Replace("\"path\":", "\"path\":\"sharedMeshes\",\"path\":"));
        Reject(Json.Replace("\"meshCount\":", "\"meshCount\":128,\"meshCount\":"));
        Reject(Json.Replace("\"meshCount\":", "\"mesh\\u0043ount\":128,\"meshCount\":"));
    }
    [Fact]
    public void PathsUseExistingSelectionRulesAndCrossFileAdmissionPrecedesNativeAllocation()
    {
        using var files = new GameplayConfigurationTests.Files();
        string path = files.Put(Json, "Config/rendering.json");
        string custom = files.Put(Json.Replace("sharedMeshes", "cpuBuiltTriangles"), "custom.json");
        Assert.Equal(path, RenderingConfiguration.LoadForMode([], files.Directory, "unused").Path);
        var loaded = RenderingConfiguration.LoadForMode(["--rendering-config", "custom.json"], "unused", files.Directory);
        Assert.Equal(custom, loaded.Path); Assert.Equal(ShmupRenderingPath.CpuBuiltTriangles, loaded.Settings.Path);
        Assert.Equal(path, RenderingConfiguration.LoadForMode(["--project-configs"], "unused", files.Directory).Path);
        var node = JsonNode.Parse(Json)!; node["limits"]!["instances"] = 1;
        var tooSmall = RenderingConfiguration.Load("unused.json", node.ToJsonString());
        var shmup = ShmupConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "shmup.json"));
        Assert.Throws<ArgumentException>(() => ConfigurationValidation.Validate(null, null, shmup, null, null, null, tooSmall));
        node = JsonNode.Parse(Json)!; node["limits"]!["operations"] = 1;
        Assert.Throws<ArgumentException>(() => RenderingConfiguration.Load("unused.json", node.ToJsonString()).Settings.Validate(shmup.Settings));
    }
    [Theory]
    [InlineData("--rendering-config")] [InlineData("--rendering-config", "")]
    [InlineData("--rendering-config", "--showcase")] [InlineData("--rendering-config=x")]
    [InlineData("--rendering-config", "one", "--rendering-config", "two")]
    public void InvalidArguments(params string[] args) => Assert.Throws<ArgumentException>(() => RenderingConfiguration.LoadForMode(args, "unused", "unused"));
}
