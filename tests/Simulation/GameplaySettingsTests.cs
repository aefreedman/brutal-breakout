using Brutal.Numerics;
using Brutal.Box2DApi;
using BrutalBreakout.Simulation;
using BrutalBreakout.Presentation;
using BrutalBreakout.Rendering.Shapes;
using Xunit;

namespace Simulation.Tests;

public class GameplaySettingsTests(Xunit.Abstractions.ITestOutputHelper output)
{
    internal static GameplaySettings Custom(double radius = 7, double speed = 350) =>
        new(new(1800, 1000, 80, 120, 1600, 850, 24), new(radius, speed, 420, 600, 3, -4), null);

    [Theory]
    [InlineData(7, 350)] [InlineData(23, 950)]
    public void CustomNativeMotionCastContactCatchAndExactReset(double radius, double speed)
    {
        var g = Custom(radius, speed);
        using var sim = new BallSimulation(TestSettings.Baseline, g);
        Assert.Same(g, sim.Gameplay);
        Assert.Same(TestSettings.Baseline, sim.Settings);
        var initial = sim.State;
        ArenaTests.Near(new(.6, -.8), initial.AimDirection);
        // Full circle, not a ray; own ball excluded despite originating at its center.
        var hit = sim.Query(g.Ball.Spawn, new(1, 0));
        Assert.True(hit.HasHit);
        Assert.InRange(hit.Position.X, g.MaxX, g.MaxX + .51);
        Assert.Equal(g.Ball.Spawn.Y, hit.Position.Y);
        Assert.False(sim.Query(new(1, 1), new(-1, 0)).HasHit);
        sim.Step(new(PrimaryPressed: true));
        ArenaTests.Near(initial.Position + initial.AimDirection * (speed * sim.StepSeconds), sim.State.Position, .01);
        ArenaTests.Near(initial.AimDirection * speed, sim.State.Velocity, .01);
        for (int i = 0; i < 1200 && !sim.State.CatchEligible; i++) sim.Step();
        Assert.True(sim.State.CatchEligible);
        Assert.InRange(double2.Distance(default, sim.State.Velocity), speed - .1, speed + .1);
        var contact = sim.State.Position;
        sim.Step(new(PrimaryPressed: true));
        Assert.Equal(contact, sim.State.Position);
        for (int i = 0; i < 10; i++) sim.Step();
        Assert.Equal(contact, sim.State.Position);
        sim.Step(new(RestartPressed: true));
        Assert.Equal(initial, sim.State);
        sim.Step(new(PrimaryPressed: true));
        ArenaTests.Near(initial.Position + initial.AimDirection * (speed * sim.StepSeconds), sim.State.Position, .01);
        sim.Dispose(); sim.Dispose();
    }

    [Fact]
    public void AbovePublishedDefaultSpeedIsNotSilentlyCappedAndCastReachesLargeCanvas()
    {
        double speed = B2WorldDef.Default.MaximumLinearSpeed * 200;
        output.WriteLine($"Published default maximum = {B2WorldDef.Default.MaximumLinearSpeed} m/s; configured = {speed} logical units/s.");
        var g = new GameplaySettings(new(100000, 80000, 1000, 1000, 99000, 79000, 100), new(40, speed, 50000, 40000, 1, 0), null);
        using var sim = new BallSimulation(TestSettings.Baseline, g);
        Assert.True(speed > B2WorldDef.Default.MaximumLinearSpeed * 100);
        var hit = sim.Preview();
        Assert.True(hit.HasHit);
        Assert.InRange(hit.Position.X, g.MaxX - .1, g.MaxX + .6);
        sim.Step(new(PrimaryPressed: true));
        Assert.InRange(sim.State.Velocity.X, speed - .1, speed + .1);
        Assert.InRange(sim.State.Position.X, 50000 + speed * sim.StepSeconds - .1, 50000 + speed * sim.StepSeconds + .1);
    }

    public static IEnumerable<object[]> InvalidRelationships()
    {
        yield return new object[] { new ArenaSettings(1800, 1000, 20, 120, 1600, 850, 24), new BallSettings(7, 350, 420, 600, 1, 0) };
        yield return new object[] { new ArenaSettings(1800, 1000, 80, 20, 1600, 850, 24), new BallSettings(7, 350, 420, 600, 1, 0) };
        yield return new object[] { new ArenaSettings(1800, 1000, 80, 120, 1790, 850, 24), new BallSettings(7, 350, 420, 600, 1, 0) };
        yield return new object[] { new ArenaSettings(1800, 1000, 80, 120, 1600, 990, 24), new BallSettings(7, 350, 420, 600, 1, 0) };
        foreach (var b in new[] { new BallSettings(400, 350, 420, 600, 1, 0), new BallSettings(7, 350, 80, 600, 1, 0), new BallSettings(7, 350, 420, 120, 1, 0), new BallSettings(7, 350, 1600, 600, 1, 0), new BallSettings(7, 350, 420, 850, 1, 0), new BallSettings(1e-50, 350, 420, 600, 1, 0), new BallSettings(1e-8, 350, 420, 600, 1, 0), new BallSettings(7, 1e40, 420, 600, 1, 0), new BallSettings(7, 1e-50, 420, 600, 1, 0), new BallSettings(1e-20, 350, 420, 600, 1, 0) })
            yield return new object[] { Custom().Arena, b };
        yield return new object[] { new ArenaSettings(1800, 1000, 80, 120, 1600, 850, 1e-8), Custom().Ball };
    }

    [Theory] [MemberData(nameof(InvalidRelationships))]
    public void InvalidCrossGeometryRejectedWithoutWorld(ArenaSettings a, BallSettings b) =>
        Assert.ThrowsAny<ArgumentException>(() => new GameplaySettings(a, b, null));

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidLocalValues(double x)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ArenaSettings(x, 1000, 80, 120, 1600, 850, 24));
        Assert.ThrowsAny<ArgumentException>(() => new ArenaSettings(1800, x, 80, 120, 1600, 850, 24));
        Assert.ThrowsAny<ArgumentException>(() => new ArenaSettings(1800, 1000, 80, 120, 1600, 850, x));
        Assert.ThrowsAny<ArgumentException>(() => new BallSettings(x, 350, 420, 600, 1, 0));
        Assert.ThrowsAny<ArgumentException>(() => new BallSettings(7, x, 420, 600, 1, 0));
    }

    [Fact]
    public void AimFacesEnvelopeAndDependenciesAreValidated()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ArenaSettings(1800, 1000, 1600, 120, 80, 850, 24));
        Assert.ThrowsAny<ArgumentException>(() => new ArenaSettings(1800, 1000, 80, 850, 1600, 120, 24));
        Assert.ThrowsAny<ArgumentException>(() => new ArenaSettings(1 << 21, 1000, 80, 120, 1600, 850, 24));
        foreach (double x in new[] { double.NaN, double.PositiveInfinity })
        {
            Assert.ThrowsAny<ArgumentException>(() => new BallSettings(7, 350, x, 600, 1, 0));
            Assert.ThrowsAny<ArgumentException>(() => new BallSettings(7, 350, 420, x, 1, 0));
            Assert.ThrowsAny<ArgumentException>(() => new BallSettings(7, 350, 420, 600, x, 0));
            Assert.ThrowsAny<ArgumentException>(() => new BallSettings(7, 350, 420, 600, 1, x));
        }
        Assert.ThrowsAny<ArgumentException>(() => new BallSettings(7, 350, 420, 600, 0, 0));
        Assert.Throws<ArgumentNullException>(() => new GameplaySettings(null!, Custom().Ball, null));
        Assert.Throws<ArgumentNullException>(() => new GameplaySettings(Custom().Arena, null!, null));
        Assert.Throws<ArgumentNullException>(() => new BallSimulation(TestSettings.Baseline, null!));
        Assert.Throws<ArgumentNullException>(() => new BallSandboxSession(TestSettings.Baseline, Custom(), null!));
        var extremeAim = new BallSettings(7, 350, 420, 600, double.MaxValue, double.MaxValue);
        ArenaTests.Near(new(Math.Sqrt(.5), Math.Sqrt(.5)), extremeAim.InitialAim);
    }

    [Fact]
    public void CustomWorldFiltersStayIndependentAcrossRepeatedOwnership()
    {
        using var held = new BallSimulation(TestSettings.Baseline, Custom());
        var preview = held.Preview();
        for (int i = 0; i < 20; i++)
        {
            using var other = new BallSimulation(TestSettings.Baseline, Custom(23, 950));
            other.Step(new(PrimaryPressed: true));
            Assert.Equal(preview, held.Preview());
            other.Dispose(); other.Dispose();
        }
    }

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void EveryWallFaceMustBeFinite(double x)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ArenaSettings(1800, 1000, x, 120, 1600, 850, 24));
        Assert.ThrowsAny<ArgumentException>(() => new ArenaSettings(1800, 1000, 80, x, 1600, 850, 24));
        Assert.ThrowsAny<ArgumentException>(() => new ArenaSettings(1800, 1000, 80, 120, x, 850, 24));
        Assert.ThrowsAny<ArgumentException>(() => new ArenaSettings(1800, 1000, 80, 120, 1600, x, 24));
    }

    [Fact]
    public void NativeIntermediateSafetyAndPublishedMaximumSpeedField()
    {
        Assert.Equal(typeof(float), typeof(B2WorldDef).GetField(nameof(B2WorldDef.MaximumLinearSpeed))!.FieldType);
        var physics = new PhysicsSettings(1e-15, 1, 1, 0, 0);
        Assert.Throws<ArgumentException>(() => new BallSimulation(physics, Custom(7, 1e10)));
        // All relationships are valid as doubles, but walls disappear at float coordinates.
        Assert.Throws<ArgumentException>(() => new GameplaySettings(
            new(100000, 80000, 10000, 10000, 90000, 70000, .000001),
            new(20, 600, 50000, 40000, 1, 0), null));
    }

    [Fact]
    public void SessionMapsDpiPointerViaItsOwnCustomArena()
    {
        var g = Custom();
        using var session = new BallSandboxSession(TestSettings.Baseline, g, TestSettings.Input);
        var v = new ArenaViewport(1000, 1000, 2000, 2000, g.Arena);
        var idle = new BrutalBreakout.Platform.Input.InputSnapshot { Focused = true, Cursor = new(500, 500) };
        session.Advance(idle, 0, v);
        // A changed pointer beats the focus baseline, maps to world (1080, 500).
        var moved = idle with { Cursor = new(600, 500) };
        session.Advance(moved, session.Settings.StepSeconds, v);
        var expected = double2.Normalize(new double2(1080, 500) - g.Ball.Spawn);
        ArenaTests.Near(expected, session.Simulation.State.AimDirection, .0001);
        // Letterbox input must preserve the last valid aim.
        session.Advance(moved with { Cursor = new(600, 0) }, session.Settings.StepSeconds, v);
        ArenaTests.Near(expected, session.Simulation.State.AimDirection, .0001);
    }

    [Fact]
    public void ActualViewportAndVerticesUseCustomGeometry()
    {
        var g = Custom(23, 950);
        var viewport = new ArenaViewport(1000, 1000, 2000, 2000, g.Arena);
        Assert.True(viewport.TryWorld(new(500, 500), out var center));
        ArenaTests.Near(new(900, 500), center, .001);
        Assert.False(viewport.TryWorld(new(500, 0), out _));
        Assert.False(viewport.TryWorld(new(float.NaN, 1), out _));
        Assert.False(new ArenaViewport(0, 0, 0, 0, g.Arena).TryWorld(default, out _));
        using var session = new BallSandboxSession(TestSettings.Baseline, g, TestSettings.Input);
        Assert.Same(TestSettings.Input, session.Router.Settings);
        Assert.Throws<ArgumentException>(() => session.Advance(default, 0, new(1000, 1000, 2000, 2000, Custom().Arena)));
        using var draw = new ShapeDrawList();
        var frame = BallSandbox.Build(draw, session.Simulation, 1800, 1000, out var evidence);
        Assert.Equal(24, evidence.ArenaIndices);
        Assert.True(evidence.BallIndices > 0 && evidence.AimIndices > 0);
        var wall = LinearColor.FromSrgb(.25f, .38f, .48f);
        var gold = LinearColor.FromSrgb(1, .75f, .25f);
        var walls = new List<float2>(); var balls = new List<float2>();
        foreach (var vertex in frame.Vertices.Span)
        {
            if (vertex.Color.X == wall.R && vertex.Color.Y == wall.G) walls.Add(vertex.Position);
            if (vertex.Color.X == gold.R && vertex.Color.Y == gold.G) balls.Add(vertex.Position);
        }
        Assert.Equal(56f, walls.Min(p => p.X)); Assert.Equal(1624f, walls.Max(p => p.X));
        Assert.Equal(96f, walls.Min(p => p.Y)); Assert.Equal(874f, walls.Max(p => p.Y));
        // Halo centerline radius = configured radius + 9, outer edge adds half the 3-unit stroke.
        Assert.InRange(balls.Max(p => p.X), 453.4f, 453.6f);
        Assert.InRange(balls.Min(p => p.X), 386.4f, 386.6f);
        Assert.Contains(balls, p => Math.Abs(p.X - (420 - 23)) < .01 && Math.Abs(p.Y - 600) < .01);
    }
}
