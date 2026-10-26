using System.Diagnostics;
using BrutalBreakout.Configuration;
using Xunit;

public class ConfigurationStartupTests
{
    [Fact]
    public async Task ProjectConfigsMissingSourceFailsBeforePlatformAndDoesNotUseDeployedFallback()
    {
        string cwd = Path.Combine(Path.GetTempPath(), "brutal-project-configs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cwd);
        try
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "BrutalBreakout.App.dll"));
            start.ArgumentList.Add("--project-configs"); start.ArgumentList.Add("--smoke-test");
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000)) { process.Kill(true); throw new TimeoutException("Project source rejection timed out."); }
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains(Path.Combine(cwd, "Config", "window.json"), await stderr);
            Assert.DoesNotContain("Window created", await stdout);
        }
        finally { Directory.Delete(cwd, true); }
    }

    [Theory]
    [InlineData("--gameplay-smoke-test")]
    [InlineData("--smoke-test")]
    public async Task InvalidExplicitConfigurationExitsBeforePlatformStartup(string mode)
    {
        string path = Path.Combine(Path.GetTempPath(), "brutal-bad-physics-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{}");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "BrutalBreakout.App.dll"));
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add("--physics-config");
        start.ArgumentList.Add(path);
        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000))
            {
                process.Kill(true);
                throw new TimeoutException("Configuration rejection did not exit promptly.");
            }
            string output = await stdout;
            string errors = await stderr;
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains(path, errors);
            Assert.Contains("Cannot load physics configuration", errors);
            Assert.DoesNotContain("Vulkan", output);
            Assert.DoesNotContain("GPU:", output);
            Assert.DoesNotContain("Ball arena:", output);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); }
            File.Delete(path);
        }
    }
}
