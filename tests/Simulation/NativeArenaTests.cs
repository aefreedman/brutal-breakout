using BrutalBreakout.Simulation;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Simulation.Tests;

public class NativeArenaTests
{
    [Fact]
    public void PublishedNativeWorldCreateStepCastAndDispose()
    {
        using var world = new NativeArena(TestSettings.Baseline);
        var hit = world.Query(new(640, 360), new(1, 0));
        Assert.True(hit.HasHit);
        ArenaTests.Near(new(1220, 360), hit.Position, 1);
        world.Launch(new(0, -1));
        Assert.False(world.Step());
        ArenaTests.Near(new(640, 607), world.Position, .01);
        world.Dispose();
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(() => world.Step());
    }
}
