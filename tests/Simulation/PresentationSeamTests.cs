using Brutal.Numerics;
using BrutalBreakout.Presentation;
using CrushKit.Brutal.Draw2D;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class PresentationSeamTests
{
    [Theory]
    [InlineData(1280, 720)] [InlineData(960, 540)]
    public void ActualCpuConsumerHandlesNativePreviewFlightAndCatch(int width, int height)
    {
        using var sim = new BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, new(2, 64, 2));
        using var draw = new ShapeDrawList();
        BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, 0, width, height, out var aiming);
        Assert.True(aiming.ArenaIndices > 0 && aiming.BallIndices > 0 && aiming.AimIndices > 0);
        sim.Step(new(new double2(580, -552))); // ordinary native corner normal, never a synthetic one
        Assert.True(BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, 0, width, height, out _).Indices.Length > 0);
        sim.Step(new(PrimaryPressed: true));
        BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, 0, width, height, out var flying);
        Assert.Equal(0, flying.AimIndices);
        for (int tick = 0; tick < 400 && !sim.State.CatchEligible; tick++) sim.Step();
        Assert.True(sim.State.CatchEligible);
        sim.Step(new(PrimaryPressed: true));
        Assert.True(BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, 0, width, height, out var caught).Indices.Length > 0);
        Assert.True(caught.AimIndices > 0); // no-hit pose still has a short direction indicator
        sim.Step(new(RestartPressed: true));
        BallSandbox.Build(draw, sim, TestSettings.Colors, TestSettings.Style, 0, width, height, out var reset);
        Assert.Equal(aiming, reset);
    }
}
