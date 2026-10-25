using Brutal.Numerics;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public class ShmupAimPreviewTests
{
    private static NativeShmupArena Native(ShmupSettings? settings = null) =>
        new(TestSettings.Baseline, settings ?? ShmupSettingsTests.Settings(threshold: 1), new(1, 64, 1));
    private static ShmupSimulation Simulation(ShmupSettings? settings = null) =>
        new(TestSettings.Baseline, settings ?? ShmupSettingsTests.Settings(threshold: 1), new(1, 64, 1));
    private static void Near(double expected, double actual) => Assert.InRange(actual, expected - .002, expected + .002);
    // Native circlecast contact slop is approximately 0.5 logical unit, not analytic inset geometry.
    private static void ContactNear(double expected, double actual) => Assert.InRange(actual, expected - .6, expected + .6);

    [Fact]
    public void SideWallUsesProjectileRadiusAndReflectsToOtherWallAfterOneUnitSkin()
    {
        using var native = Native();
        var preview = native.Preview(new(640, 300), new(1, 0));
        Assert.True(preview.Hit.HasHit); ContactNear(297, preview.Hit.Distance);
        ContactNear(937, preview.Hit.Position.X); Near(300, preview.Hit.Position.Y);
        Assert.Equal(new double2(-1, 0), preview.Hit.Normal);
        Assert.Equal(new double2(-1, 0), preview.Hit.ReflectedDirection);
        Assert.True(preview.NextHit.HasHit);
        Near(preview.Hit.Position.X - 1 - preview.NextHit.Position.X, preview.NextHit.Distance);
        ContactNear(343, preview.NextHit.Position.X);
    }

    [Theory]
    [InlineData(0, -1, 300)] [InlineData(0, 1, 420)]
    [InlineData(1e-300, -1, 300)]
    public void OpenEdgesPublishLongClearPathWithoutInventingBounce(double x, double y, double distance)
    {
        using var native = Native();
        var preview = native.Preview(new(640, 300), new(x, y));
        Assert.False(preview.Hit.HasHit); Near(distance, preview.Hit.Distance);
        Near(y < 0 ? 0 : 720, preview.Hit.Position.Y);
        Assert.Equal(default, preview.Hit.Normal); Assert.Equal(default, preview.Hit.ReflectedDirection);
        Assert.Equal(default, preview.NextHit);
    }

    [Fact]
    public void SecondCastMissEndsAtOpenEdgeForIndicatorClamping()
    {
        using var native = Native();
        var preview = native.Preview(new(900, 20), new(1, -.5));
        Assert.True(preview.Hit.HasHit);
        Assert.False(preview.NextHit.HasHit);
        Assert.InRange(preview.NextHit.Distance, 0.1, 10);
        Near(0, preview.NextHit.Position.Y);
    }

    [Fact]
    public void CurrentMovingBrickPoseIsUsedAndDisabledBricksAndBallsAreExcluded()
    {
        using var native = Native();
        native.ActivateBall(0, new(640, 400), new(1, 0));
        Assert.False(native.Query(new(640, 600), new(0, -1)).HasHit);
        native.ActivateBrick(0, new(640, 300));
        var hit = native.Query(new(640, 600), new(0, -1));
        Assert.True(hit.HasHit); ContactNear(281, hit.Distance); ContactNear(319, hit.Position.Y);
        native.Step(30, new(100, 600), new(100, 600));
        hit = native.Query(new(640, 600), new(0, -1));
        ContactNear(native.BrickPosition(0).Y + 19, hit.Position.Y);
        Assert.True(hit.Position.Y > 318.5);
        native.ClearBrick(0);
        hit = native.Query(new(640, 600), new(0, -1));
        Assert.False(hit.HasHit); Near(600, hit.Distance);
    }

    [Fact]
    public void PiercingIgnoresBricksIncludingInitialOverlapButRetainsWallBounce()
    {
        using var native = Native(ShmupSettingsTests.Settings(bounce: false, threshold: 1));
        native.ActivateBrick(0, new(640, 300));
        var hit = native.Query(new(640, 600), new(0, -1));
        Assert.False(hit.HasHit); Near(600, hit.Distance);
        hit = native.Query(new(640, 300), new(1, 0));
        Assert.True(hit.HasHit); ContactNear(297, hit.Distance);
    }

    [Fact]
    public void OverlapAndAmbiguousCornerAreZeroDistanceFallbackNotClearPath()
    {
        using var native = Native();
        native.ActivateBrick(0, new(640, 300));
        Assert.Equal(default, native.Query(new(640, 300), new(0, -1)));
        Assert.Equal(default, native.Query(new(341, 300), new(1, 0)));
        native.ClearBrick(0);
        native.ActivateBrick(0, new(930, 184));
        Assert.Equal(default, native.Query(new(640, 500), new(1, -1)));
        Assert.Equal(default, native.Query(new(double.NaN, 300), new(1, 0)));
        Assert.Equal(default, native.Query(new(640, 300), default));
    }

    [Fact]
    public void QueryDoesNotChangeNativeVelocitiesContactsOrPose()
    {
        using var native = Native();
        native.ActivateBrick(0, new(640, 300));
        native.ActivateBall(0, new(640, 400), new(1, 0));
        native.Step(30, new(640, 696), new(640, 696));
        var position = native.BallPosition(0); var velocity = native.BallVelocity(0);
        var brick = native.BrickPosition(0); int intervals = native.LastIntervalCount;
        for (int i = 0; i < 100; i++) native.Preview(new(640, 696), new(0, -1));
        Assert.Equal(position, native.BallPosition(0)); Assert.Equal(velocity, native.BallVelocity(0));
        Assert.Equal(brick, native.BrickPosition(0)); Assert.Equal(intervals, native.LastIntervalCount);
        Assert.Equal(0, native.BrickHits[0, 0]); Assert.Equal(0, native.WallHits[0]);
        Assert.False(native.AvatarContact);
    }

    [Fact]
    public void PublicationRecomputesOnTickWidenRestartAndRetainedPreviewIsImmutable()
    {
        using var sim = Simulation();
        var reset = sim.Snapshot;
        Assert.False(reset.AimPreview.Hit.HasHit); Near(696, reset.AimPreview.Hit.Distance);
        sim.Step(new(AimDirection: new(1, 0)));
        var before = sim.Snapshot; var preview = before.AimPreview;
        Assert.True(preview.Hit.HasHit); ContactNear(297, preview.Hit.Distance);
        sim.Widen(1000);
        Assert.Equal(before.Tick, sim.Snapshot.Tick); Assert.Equal(before.TimeSeconds, sim.Snapshot.TimeSeconds);
        Assert.Equal(before.PlayerHealth, sim.Snapshot.PlayerHealth); Assert.Equal(ShmupEvents.None, sim.Events);
        ContactNear(497, sim.Snapshot.AimPreview.Hit.Distance);
        Assert.Equal(preview, before.AimPreview); Assert.Equal(600, before.Width);
        sim.Restart();
        Assert.Equal(reset.AimPreview, sim.Snapshot.AimPreview);
        Assert.Equal(preview, before.AimPreview);
    }

    [Fact]
    public void PublicationUsesLiveBrickAndExcludesDestroyedBrickWithoutPreviewDamageOrEvents()
    {
        using var sim = Simulation(ShmupSettingsTests.Settings(threshold: 1, brickHealth: 10));
        sim.SpawnBrick(new(640, 600)); sim.Step();
        var live = sim.Snapshot;
        Assert.True(live.AimPreview.Hit.HasHit);
        ContactNear(live.Bricks[0].Position.Y + 19, live.AimPreview.Hit.Position.Y);
        Assert.Equal(10, live.Bricks[0].Health); Assert.Equal(100, live.PlayerHealth);
        Assert.Equal(ShmupEvents.None, sim.Events); Assert.Equal(1, live.Tick);
        sim.SpawnBall(new(640, 640), new(0, -1));
        for (int i = 0; i < 10 && sim.Snapshot.Bricks.Length != 0; i++) sim.Step();
        Assert.Empty(sim.Snapshot.Bricks.ToArray());
        Assert.False(sim.Snapshot.AimPreview.Hit.HasHit); Near(696, sim.Snapshot.AimPreview.Hit.Distance);
        Assert.True(live.AimPreview.Hit.HasHit); Assert.Single(live.Bricks.ToArray());
    }
}
