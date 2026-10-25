using System.Diagnostics;
using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using Xunit;

public sealed class BallTrailConfigurationStartupTests
{
    private static string Json => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "trails.json"));
    [Fact]
    public void EachFieldIsRequiredAndRejectsNullAndUnexpectedMembers()
    {
        var original = JsonNode.Parse(Json)!.AsObject();
        foreach (var (field, _) in original)
        {
            var missing = original.DeepClone().AsObject(); missing.Remove(field); Reject(missing.ToJsonString());
            var nullValue = original.DeepClone(); nullValue[field] = null; Reject(nullValue.ToJsonString());
        }
        var extra = original.DeepClone(); extra["extra"] = 1; Reject(extra.ToJsonString());
        Reject("[]"); Reject("null"); Reject("{broken");
    }
    [Theory]
    [InlineData("--trails-config")]
    [InlineData("--trails-config=x")]
    [InlineData("--trails-config", "a", "--trails-config", "b")]
    public void InvalidOptionFormsRejected(params string[] args) =>
        Assert.Throws<ArgumentException>(() => BallTrailConfiguration.LoadForMode(args, false, "unused", Environment.CurrentDirectory));
    [Fact]
    public void CrossCapacityFailsBeforeNativeEvenWhenTrailsDisabled()
    {
        using var files = new GameplayConfigurationTests.Files();
        var node = JsonNode.Parse(Json)!; node["enabled"] = false; node["maxPointsPerBall"] = 1048576;
        var trails = BallTrailConfiguration.Load(files.Put(node.ToJsonString()));
        var shmup = ShmupConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "shmup.json"));
        var error = Assert.Throws<InvalidDataException>(() => BallTrailConfiguration.ValidateCapacity(trails, shmup));
        Assert.Contains(trails.Path, error.Message); Assert.Contains(shmup.Path, error.Message);
    }
    [Theory]
    [InlineData("--smoke-test", false)]
    [InlineData("--shmup-smoke-test", false)]
    [InlineData("--shmup-smoke-test", true)]
    public async Task InvalidExplicitTrailSettingsRejectBeforeGlfwOrNative(string mode, bool crossCapacity)
    {
        using var files = new GameplayConfigurationTests.Files();
        var node = JsonNode.Parse(Json)!; node["maxPointsPerBall"] = 1048576;
        string path = files.Put(crossCapacity ? node.ToJsonString() : "{}");
        var start = new ProcessStartInfo("dotnet")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "BrutalBreakout.App.dll"));
        start.ArgumentList.Add(mode); start.ArgumentList.Add("--trails-config"); start.ArgumentList.Add(path);
        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000)) { process.Kill(true); throw new TimeoutException("Trail config rejection timed out."); }
            string output = await stdout, error = await stderr;
            Assert.NotEqual(0, process.ExitCode); Assert.Contains(path, error); Assert.Contains("trails configuration", error);
            Assert.DoesNotContain("Window created", output); Assert.DoesNotContain("Vulkan", output);
            Assert.DoesNotContain("Ball arena:", output); Assert.DoesNotContain("GLFW", error);
        }
        finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } }
    }
    private static void Reject(string json)
    {
        using var files = new GameplayConfigurationTests.Files();
        Assert.Throws<InvalidDataException>(() => BallTrailConfiguration.Load(files.Put(json)));
    }
}
