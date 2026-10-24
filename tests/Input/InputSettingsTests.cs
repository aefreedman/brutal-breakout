using BrutalBreakout.Platform.Input;
using Xunit;

namespace BrutalBreakout.Input.Tests;

public sealed class InputSettingsTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.1)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(0.9999999999999999)] // Valid double range, but rounds to float 1.
    [InlineData(double.Epsilon)] // Positive double must not silently become zero.
    public void InvalidDeadzoneRejectedWithoutNativeInitialization(double deadzone)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new InputSettings(deadzone));
        Assert.Equal("stickDeadzone", error.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.2)]
    [InlineData(0.75)]
    [InlineData(0.9999999403953552)] // Largest float below 1.
    [InlineData(1.401298464324817E-45)] // Smallest positive float.
    public void ValidDeadzoneStoredAsFloat(double deadzone)
        => Assert.Equal((float)deadzone, new InputSettings(deadzone).StickDeadzone);

    [Fact]
    public void SettingsAreSealedReadOnlyAndRequired()
    {
        Assert.True(typeof(InputSettings).IsSealed);
        var property = Assert.Single(typeof(InputSettings).GetProperties());
        Assert.Equal(nameof(InputSettings.StickDeadzone), property.Name);
        Assert.Null(property.SetMethod);
        var parameter = Assert.Single(Assert.Single(typeof(InputSettings).GetConstructors()).GetParameters());
        Assert.Equal(typeof(double), parameter.ParameterType);
        Assert.False(parameter.IsOptional);

        var routerProperty = Assert.Single(typeof(InputRouter).GetProperties());
        Assert.Equal(nameof(InputRouter.Settings), routerProperty.Name);
        Assert.Null(routerProperty.SetMethod);
        var routerParameter = Assert.Single(Assert.Single(typeof(InputRouter).GetConstructors()).GetParameters());
        Assert.Equal(typeof(InputSettings), routerParameter.ParameterType);
        Assert.False(routerParameter.IsOptional);
    }

    [Fact]
    public void RouterRetainsSameSettingsAcrossRoutingAndReset()
    {
        var settings = new InputSettings(0.37);
        var router = new InputRouter(settings);
        Assert.Same(settings, router.Settings);
        router.Route(new InputSnapshot { Focused = true });
        router.Reset();
        Assert.Same(settings, router.Settings);
        Assert.Equal(0.37f, settings.StickDeadzone);
    }

    [Fact]
    public void NullSettingsRejectedWithoutNativeInitialization()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new InputRouter(null!));
        Assert.Equal("settings", error.ParamName);
    }
}
