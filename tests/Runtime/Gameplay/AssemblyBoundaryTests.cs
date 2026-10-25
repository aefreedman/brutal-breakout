using System.Diagnostics;
using BrutalBreakout.Configuration;
using BrutalBreakout.Simulation;
using Xunit;

public sealed class AssemblyBoundaryTests
{
    [Fact]
    public void RuntimeAssembliesDoNotReferenceEditorOrImGui()
    {
        foreach (var assembly in new[] { typeof(OverlayConfiguration).Assembly, typeof(BallSimulation).Assembly,
            typeof(BrutalBreakout.Platform.Input.InputRouter).Assembly, typeof(BrutalBreakout.Audio.GameAudio).Assembly })
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
                reference.Name!.Contains("ImGui", StringComparison.OrdinalIgnoreCase) ||
                reference.Name.Contains("Editor", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData("overlay")]
    [InlineData("inspector")]
    [InlineData("shmup")]
    public async Task InvalidHudOrInspectorConfigurationExitsBeforeNativeStartup(string section)
    {
        var path = Path.Combine(Path.GetTempPath(), "breakout-invalid-" + Guid.NewGuid() + ".json");
        await File.WriteAllTextAsync(path, "{}");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in new[] { Path.Combine(AppContext.BaseDirectory, "BrutalBreakout.App.dll"),
            "--smoke-test", "--" + section + "-config", path }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000))
            {
                process.Kill(true);
                throw new TimeoutException("Configuration rejection timed out.");
            }
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("Cannot load " + section + " configuration", await stderr);
            Assert.DoesNotContain("Window created:", await stdout);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); }
            File.Delete(path);
        }
    }
}
