using System.Diagnostics;
using BrutalBreakout.Configuration;
using Xunit;

public class GameplayConfigurationStartupTests
{
    [Theory]
    [InlineData("--gameplay-smoke-test", "gameplay")]
    [InlineData("--smoke-test", "gameplay")]
    [InlineData("--gameplay-smoke-test", "input")]
    [InlineData("--smoke-test", "input")]
    [InlineData("--gameplay-smoke-test", "combined")]
    [InlineData("--gameplay-smoke-test", "style")]
    [InlineData("--smoke-test", "style")]
    public async Task InvalidNewConfigurationOrCombinedSettingsExitsBeforePlatform(string mode, string invalid)
    {
        using var files = new GameplayConfigurationTests.Files();
        string physics = files.Put(invalid == "combined"
            ? GameplayConfigurationTests.Physics.Replace("120,\"substeps", "1e-20,\"substeps")
            : GameplayConfigurationTests.Physics, "p.json");
        string game = files.Put(invalid == "gameplay" ? "{}" : GameplayConfigurationTests.Valid, "g.json");
        string input = files.Put(invalid == "input" ? "{}" : "{\"stickDeadzone\":0.55}", "i.json");
        string style = files.Put(invalid == "style" ? "{}" : PresentationStyleConfigurationTests.Valid, "s.json");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(GameplayConfiguration).Assembly.Location);
        foreach (string arg in new[] { mode, "--physics-config", physics, "--gameplay-config", game, "--input-config", input, "--style-config", style })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000)) { process.Kill(true); throw new TimeoutException("Configuration rejection timed out."); }
            string output = await stdout, errors = await stderr;
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains(invalid == "style" ? style : invalid == "input" ? input : game, errors);
            if (invalid == "combined") Assert.Contains(physics, errors);
            Assert.DoesNotContain("GPU:", output); Assert.DoesNotContain("Vulkan", output);
            Assert.DoesNotContain("Ball arena:", output); Assert.DoesNotContain("Gameplay stage:", output);
        }
        finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } }
    }
}
