using Brutal.GlfwApi;
using Brutal.Numerics;
using BrutalBreakout.Configuration;
using Xunit;

public class WindowConfigurationTests
{
    [Theory]
    [InlineData("null")] [InlineData("[]")] [InlineData("{}")]
    [InlineData("{\"w\u0069dth\":1,\"vsync\":true,\"frameRateLimit\":0}")] [InlineData("{\"height\":1,\"vsync\":true,\"frameRateLimit\":0}")]
    [InlineData("{\"w\u0069dth\":0,\"height\":720,\"vsync\":true,\"frameRateLimit\":0}")]
    [InlineData("{\"w\u0069dth\":1280,\"height\":-1,\"vsync\":true,\"frameRateLimit\":0}")]
    [InlineData("{\"w\u0069dth\":null,\"height\":720,\"vsync\":true,\"frameRateLimit\":0}")]
    [InlineData("{\"w\u0069dth\":1.5,\"height\":720,\"vsync\":true,\"frameRateLimit\":0}")]
    [InlineData("{\"w\u0069dth\":1e300,\"height\":720,\"vsync\":true,\"frameRateLimit\":0}")]
    [InlineData("{\"w\u0069dth\":\"1280\",\"height\":720,\"vsync\":true,\"frameRateLimit\":0}")]
    [InlineData("{\"w\u0069dth\":1280,\"height\":720,\"future\":{},\"vsync\":true,\"frameRateLimit\":0}")]
    [InlineData("""{"width":1280,"width":1280,"height":720,"vsync":true,"frameRateLimit":0}""")]
    [InlineData("""{"width":1280,"w\u0069dth":1280,"height":720,"vsync":true,"frameRateLimit":0}""")]
    public void InvalidHasNoFallback(string json)
    {
        using var files = new GameplayConfigurationTests.Files();
        var path = files.Put(json);
        Assert.Contains(path, Assert.Throws<InvalidDataException>(() => WindowConfiguration.Load(path)).Message);
    }

    [Theory]
    [InlineData("""{"width":1280,"height":720}""")]
    [InlineData("""{"width":1280,"height":720,"vsync":true}""")]
    [InlineData("""{"width":1280,"height":720,"frameRateLimit":0}""")]
    [InlineData("""{"width":1280,"height":720,"vsync":1,"frameRateLimit":0}""")]
    [InlineData("""{"width":1280,"height":720,"vsync":"true","frameRateLimit":0}""")]
    [InlineData("""{"width":1280,"height":720,"vsync":null,"frameRateLimit":0}""")]
    [InlineData("""{"width":1280,"height":720,"vsync":true,"frameRateLimit":-1}""")]
    [InlineData("""{"width":1280,"height":720,"vsync":true,"frameRateLimit":1.5}""")]
    [InlineData("""{"width":1280,"height":720,"vsync":true,"frameRateLimit":1001}""")]
    [InlineData("""{"width":1280,"height":720,"vsync":true,"frameRateLimit":null}""")]
    [InlineData("""{"width":1280,"height":720,"vsync":true,"frameRateLimit":"60"}""")]
    [InlineData("""{"width":1280,"height":720,"vsync":true,"frameRateLimit":0,"extra":1}""")]
    [InlineData("""{"width":1280,"height":720,"vsync":true,"vsync":false,"frameRateLimit":0}""")]
    [InlineData("""{"width":1280,"height":720,"vsync":true,"frameRateLimit":0,"frameRateLimit":60}""")]
    public void PresentationPolicyIsStrictAndRequired(string json) => InvalidHasNoFallback(json);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(1000)]
    public void LimitsIncludeEndpoints(int limit)
    {
        var loaded = WindowConfiguration.Load("unused.json", $$"""{"width":1,"height":1,"vsync":true,"frameRateLimit":{{limit}}}""");
        Assert.Equal(limit, loaded.Settings.FrameRateLimit);
    }

    [Fact]
    public void PathsIndependentOverrideAndPublishedCreateInfoConsumeProvidedSettings()
    {
        using var files = new GameplayConfigurationTests.Files();
        string path = files.Put("{\"w\u0069dth\":1280,\"height\":720,\"vsync\":true,\"frameRateLimit\":0}", "Config/window.json");
        string custom = files.Put("{\"w\u0069dth\":800,\"height\":600,\"vsync\":false,\"frameRateLimit\":144}", "custom.json");
        string dir = files.Directory;
        var defaults = WindowConfiguration.LoadForMode([], dir, "unused");
        Assert.Equal(path, defaults.Path);
        Assert.Equal(1280, defaults.Settings.Width); Assert.Equal(720, defaults.Settings.Height);
        var loaded = WindowConfiguration.LoadForMode(["--window-config", "custom.json"], "unused", dir);
        Assert.Equal(custom, loaded.Path);
        Assert.True(defaults.Settings.VSync); Assert.Equal(0, defaults.Settings.FrameRateLimit);
        Assert.False(loaded.Settings.VSync); Assert.Equal(144, loaded.Settings.FrameRateLimit);
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
