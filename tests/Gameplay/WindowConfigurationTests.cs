using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalBreakout.Configuration;
using Xunit;

public class WindowConfigurationTests
{
    [Theory]
    [InlineData("null")] [InlineData("[]")] [InlineData("{}")]
    [InlineData("{\"w\u0069dth\":1}")] [InlineData("{\"height\":1}")]
    [InlineData("{\"w\u0069dth\":0,\"height\":720}")]
    [InlineData("{\"w\u0069dth\":1280,\"height\":-1}")]
    [InlineData("{\"w\u0069dth\":null,\"height\":720}")]
    [InlineData("{\"w\u0069dth\":1.5,\"height\":720}")]
    [InlineData("{\"w\u0069dth\":1e300,\"height\":720}")]
    [InlineData("{\"w\u0069dth\":\"1280\",\"height\":720}")]
    [InlineData("{\"w\u0069dth\":1280,\"height\":720,\"future\":{}}")]
    [InlineData("""{"width":1280,"width":1280,"height":720}""")]
    [InlineData("""{"width":1280,"w\u0069dth":1280,"height":720}""")]
    public void InvalidHasNoFallback(string json)
    {
        using var files = new GameplayConfigurationTests.Files();
        var path = files.Put(json);
        Assert.Contains(path, Assert.Throws<InvalidDataException>(() => WindowConfiguration.Load(path)).Message);
    }

    [Fact]
    public void PathsIndependentOverrideAndPublishedCreateInfoConsumeProvidedSettings()
    {
        using var files = new GameplayConfigurationTests.Files();
        string path = files.Put("{\"w\u0069dth\":1280,\"height\":720}", "window.json");
        string custom = files.Put("{\"w\u0069dth\":800,\"height\":600}", "custom.json");
        string dir = Path.GetDirectoryName(path)!;
        var defaults = WindowConfiguration.LoadForMode([], dir, "unused");
        Assert.Equal(path, defaults.Path);
        Assert.Equal(1280, defaults.Settings.Width); Assert.Equal(720, defaults.Settings.Height);
        var loaded = WindowConfiguration.LoadForMode(["--window-config", "custom.json"], "unused", dir);
        Assert.Equal(custom, loaded.Path);
        var info = new GlfwWindow.CreateInfo { Title = "CPU compile probe", Size = new int2(loaded.Settings.Width, loaded.Settings.Height) };
        Assert.Equal(800, info.Size.X); Assert.Equal(600, info.Size.Y);
        Assert.Equal(1280, TestSettings.Gameplay.Arena.Width);
        Assert.Contains("w\u0069dth=800; height=600", loaded.Describe());
        Assert.All(typeof(WindowSettings).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.Throws<InvalidDataException>(() => WindowConfiguration.LoadForMode([], "missing", dir));
    }

    [Theory]
    [InlineData("--window-config")] [InlineData("--window-config", "")]
    [InlineData("--window-config", "--showcase")] [InlineData("--window-config=x")]
    [InlineData("--window-config", "one", "--window-config", "two")]
    public void InvalidArguments(params string[] args) =>
        Assert.Throws<ArgumentException>(() => WindowConfiguration.LoadForMode(args, "unused", "unused"));
}
