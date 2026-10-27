using System.Diagnostics;
using BrutalBreakout.Configuration;
using Xunit;

public sealed class RenderingConfigurationStartupTests
{
    [Theory]
    [InlineData("--smoke-test", false)]
    [InlineData("--shmup-smoke-test", false)]
    [InlineData("--shmup-smoke-test", true)]
    public async Task InvalidRenderingSchemaOrCapacityExitsBeforeGlfwAndNativeWorld(string mode, bool capacity)
    {
        using var files = new GameplayConfigurationTests.Files();
        string json = capacity ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "rendering.json")).Replace("8192", "1") : "{}";
        string path = files.Put(json);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { Path.Combine(AppContext.BaseDirectory, "BrutalBreakout.App.dll"), mode, "--rendering-config", path }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000)) { process.Kill(true); throw new TimeoutException("Rendering rejection timed out."); }
            string output = await stdout, errors = await stderr;
            Assert.NotEqual(0, process.ExitCode); Assert.Contains("Rendering", errors, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Window created", output); Assert.DoesNotContain("Vulkan", output); Assert.DoesNotContain("GPU:", output);
        }
        finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } }
    }
}
