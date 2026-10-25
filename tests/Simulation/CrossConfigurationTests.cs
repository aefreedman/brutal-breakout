using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class CrossConfigurationTests
{
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)] [InlineData(240)]
    public void ValidCombinedSettingsValidateWithoutAllocatingWorld(double rate)
        => TestSettings.Gameplay.ValidatePhysics(new(rate, 4, 8, 120, 1));

    [Fact]
    public void DisplacementOverflowRejectsPurelyAndForDirectNativeConsumer()
    {
        var physics = new PhysicsSettings(1e-20, 4, 8, 120, 1);
        Assert.Throws<ArgumentException>(() => TestSettings.Gameplay.ValidatePhysics(physics));
        Assert.Throws<ArgumentException>(() => new BallSimulation(physics, TestSettings.Gameplay, new(2, 64, 2)));
    }

    [Fact]
    public void NullPhysicsRejectsWithoutWorld() =>
        Assert.Throws<ArgumentNullException>(() => TestSettings.Gameplay.ValidatePhysics(null!));
}
