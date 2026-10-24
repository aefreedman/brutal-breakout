using BrutalBreakout.Configuration;
using Xunit;

public class InputConfigurationTests
{
    [Theory]
    [InlineData("{}")] [InlineData("null")] [InlineData("[]")] [InlineData("false")]
    [InlineData("{broken")]
    [InlineData("{\"stickDeadzone\":null}")] [InlineData("{\"stickDeadzone\":\"0.2\"}")]
    [InlineData("{\"stickDeadzone\":true}")] [InlineData("{\"stickDeadzone\":-0.1}")]
    [InlineData("{\"stickDeadzone\":1}")] [InlineData("{\"stickDeadzone\":0.9999999999}")]
    [InlineData("{\"stickDeadzone\":1e999}")] [InlineData("{\"stickDeadzone\":1e-100}")]
    [InlineData("{\"stickDeadzone\":NaN}")]
    [InlineData("{\"stickDeadzone\":0.2,\"extra\":0}")]
    [InlineData("{\"stickDeadzone\":0.2,\"stickDeadzone\":0.4}")]
    [InlineData("{\"stickDeadzone\":0.2,\"stickDead\\u007aone\":0.4}")]
    public void StrictRequiredInputSchemaRejectsWithoutFallback(string json)
    {
        using var files = new GameplayConfigurationTests.Files();
        string path = files.Put(json);
        var error = Assert.Throws<InvalidDataException>(() => InputConfiguration.Load(path));
        Assert.Contains(path, error.Message); Assert.NotNull(error.InnerException);
    }

    [Theory]
    [InlineData(0)] [InlineData(.55)] [InlineData(.9)]
    public void ValidCustomDeadzoneLoads(double deadzone)
    {
        using var files = new GameplayConfigurationTests.Files();
        string path = files.Put(FormattableString.Invariant($"{{\"stickDeadzone\":{deadzone:R}}}"));
        var loaded = InputConfiguration.Load(path);
        Assert.Equal((float)deadzone, loaded.Settings.StickDeadzone);
    }
}
