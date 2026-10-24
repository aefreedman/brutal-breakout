using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class PhysicsSettingsTests
{
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    [InlineData(double.Epsilon)] [InlineData(double.MaxValue)]
    [InlineData(1e-39)] [InlineData(1e39)]
    public void InvalidNativeFrequencyRejected(double hz) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsSettings(hz, 4, 8, 120, 1));

    [Fact]
    public void SubstepInverseOverflowRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsSettings(1e38, 4, 8, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsSettings(1e30, int.MaxValue, 8, 0, 0));
    }

    [Theory]
    [InlineData(0, 8)] [InlineData(-1, 8)] [InlineData(4, 0)] [InlineData(4, -1)]
    public void CountsMustBePositive(int substeps, int cap) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsSettings(120, substeps, cap, 120, 1));

    [Theory]
    [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)] [InlineData(double.MaxValue)] [InlineData(double.Epsilon)]
    public void InvalidContactFieldsRejected(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsSettings(120, 4, 8, value, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsSettings(120, 4, 8, 120, value));
    }

    [Theory]
    [InlineData(1e-30)] [InlineData(1e30)]
    public void WideRepresentableRatesAcceptedWithoutArbitraryCaps(double hz)
    {
        var settings = new PhysicsSettings(hz, 1, int.MaxValue, 0, float.MaxValue);
        Assert.Equal(1 / hz, settings.StepSeconds);
        Assert.Equal(0, settings.ContactHertz);
        Assert.Equal(float.MaxValue, settings.ContactDampingRatio);
    }

    [Fact]
    public void RequiredSettingsRejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => new FixedStepClock(null!));
        Assert.Throws<ArgumentNullException>(() => new BallSimulation(null!));
        Assert.Throws<ArgumentNullException>(() => new NativeArena(null!));
        Assert.Throws<ArgumentNullException>(() => new BallSandboxSession(null!));
    }

    [Fact]
    public void SettingsHaveNoMutationOrDefaultConstructionEscape()
    {
        Assert.True(typeof(PhysicsSettings).IsSealed);
        Assert.Single(typeof(PhysicsSettings).GetConstructors());
        Assert.All(typeof(PhysicsSettings).GetConstructors()[0].GetParameters(), p => Assert.False(p.IsOptional));
        Assert.All(typeof(PhysicsSettings).GetProperties(), p => Assert.Null(p.SetMethod));
    }
}
