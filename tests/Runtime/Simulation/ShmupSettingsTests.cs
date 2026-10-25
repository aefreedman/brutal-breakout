using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class ShmupSettingsTests
{
    internal static ShmupSettings Settings(bool bounce = true, int maxBalls = 32, int maxBricks = 128,
        int health = 100, double threshold = .35, double speed = 600, double descent = 30,
        double rowInterval = 1.5, double width = 600, int wallHits = 20, int brickHealth = 100,
        double invulnerability = .75) => new(new(1280, 720, width), new(12, 250, health, 10, invulnerability),
        new(3, speed, 12, 10, bounce, wallHits), new(32, 40, brickHealth, 10),
        new(rowInterval, Math.Min(rowInterval, .35), descent, Math.Max(descent, 120), 60, .9, 1.1),
        new(1, .25, .2, threshold), new(maxBalls, maxBricks));

    [Fact]
    public void SettingsAreExplicitImmutableAndShared()
    {
        var settings = Settings();
        settings.ValidatePhysics(TestSettings.Baseline);
        Assert.Equal(1280, settings.Arena.Width);
        Assert.Equal(20, settings.Ball.MaxWallHits);
        Assert.All(typeof(ShmupSettings).GetProperties(), p => Assert.False(p.CanWrite));
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidDimensionsRatesAndNoise(double value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ShmupArenaSettings(value, 720, 600));
        Assert.ThrowsAny<ArgumentException>(() => new ShmupAvatarSettings(12, value, 100, 10, .75));
        Assert.ThrowsAny<ArgumentException>(() => new ShmupBallSettings(3, 600, value, 10, true, 20));
        Assert.ThrowsAny<ArgumentException>(() => new ShmupNoiseSettings(1, value, .2, .35));
    }
    [Fact]
    public void RejectsInvalidHpLimitsGeometryAndCrossPhysics()
    {
        Assert.ThrowsAny<ArgumentException>(() => Settings(health: 0));
        Assert.ThrowsAny<ArgumentException>(() => Settings(wallHits: 0));
        Assert.ThrowsAny<ArgumentException>(() => Settings(brickHealth: 0));
        Assert.ThrowsAny<ArgumentException>(() => Settings(width: 20));
        Assert.ThrowsAny<ArgumentException>(() => Settings(threshold: 1.1));
        Assert.ThrowsAny<ArgumentException>(() => new ShmupCapacitySettings(4096, 4096));
        Assert.ThrowsAny<ArgumentException>(() => new ShmupDifficultySettings(1, 2, 3, 12, 60, .9, 1.1));
        Assert.ThrowsAny<ArgumentException>(() => Settings(speed: double.MaxValue).ValidatePhysics(TestSettings.Baseline));
        Assert.ThrowsAny<ArgumentException>(() => Settings().ValidatePhysics(new(10, 1, 1, 120, 1)));
        Assert.ThrowsAny<ArgumentException>(() => Settings(rowInterval: .001).ValidatePhysics(TestSettings.Baseline));
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void BothModesRejectExcessiveNativeIntervalBudgetBeforeAllocation(bool bounce)
    {
        var settings = Settings(bounce: bounce, speed: 2_000_000);
        Assert.Throws<ArgumentException>(() => settings.ValidatePhysics(TestSettings.Baseline));
        Assert.Throws<ArgumentException>(() => new ShmupSimulation(TestSettings.Baseline, settings, new(1, 64, 1)));
    }
    [Fact]
    public void PerlinNoiseIsSeededContinuousBoundedAndReproducible()
    {
        var samples = Enumerable.Range(0, 200).Select(i => ShmupNoise.Sample(1, i * .25, i * .2)).ToArray();
        Assert.Equal(samples, Enumerable.Range(0, 200).Select(i => ShmupNoise.Sample(1, i * .25, i * .2)));
        Assert.NotEqual(samples, Enumerable.Range(0, 200).Select(i => ShmupNoise.Sample(2, i * .25, i * .2)).ToArray());
        Assert.All(samples, x => Assert.InRange(x, 0, 1));
        Assert.Contains(samples, x => x < .35);
        Assert.Contains(samples, x => x > .6);
        Assert.InRange(Math.Abs(ShmupNoise.Sample(1, 1 - 1e-6, .3) - ShmupNoise.Sample(1, 1 + 1e-6, .3)), 0, 1e-5);
    }
}
