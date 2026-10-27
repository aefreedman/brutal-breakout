using Brutal.Numerics;
using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public sealed class ShmupCallbackTests
{
    private static readonly double2 Away = new(400, 650);
    private static void Step(NativeShmupArena arena) => arena.Step(0, Away, Away);

    [Fact]
    public void InstanceCallbacksKeepValueContextAcrossBallsQueriesArenasTuningAndResetReuse()
    {
        var settings = ShmupSettingsTests.Settings(bounce: false, maxBalls: 2, maxBricks: 2,
            speed: 6000, descent: 1, threshold: 1);
        var slow = ShmupSettingsTests.Settings(bounce: false, maxBalls: 2, maxBricks: 2,
            speed: 1, descent: 1, threshold: 1);
        using var a = new NativeShmupArena(TestSettings.Baseline, settings, new(1, 64, 1));
        using var b = new NativeShmupArena(TestSettings.Baseline, slow, new(1, 64, 1));
        // Readonly instance fields can only be assigned at construction, not in the query hot methods.
        foreach (string field in new[] { "ballOverlap", "ballSweep" })
            Assert.True(typeof(NativeShmupArena).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.IsInitOnly);
        var callbacks = a.BallQueryCallbacks; var otherCallbacks = b.BallQueryCallbacks;
        Assert.Same(a, callbacks.Overlap.Target); Assert.Same(a, callbacks.Sweep.Target);
        Assert.Equal("RecordBallOverlap", callbacks.Overlap.Method.Name); Assert.Equal("RecordBallSweep", callbacks.Sweep.Method.Name);
        Assert.NotSame(callbacks.Overlap, otherCallbacks.Overlap); Assert.NotSame(callbacks.Sweep, otherCallbacks.Sweep);
        a.ActivateBrick(0, new(500, 300)); a.ActivateBrick(1, new(700, 300));
        a.ActivateBall(0, new(500, 300), new(0, -1)); // initial overlap
        a.ActivateBall(1, new(700, 350), new(0, -1)); // relative sweep followed by overlap
        b.ActivateBrick(0, new(700, 300)); b.ActivateBrick(1, new(500, 300));
        b.ActivateBall(0, new(500, 300), new(1, 0)); b.ActivateBall(1, new(700, 300), new(1, 0));
        _ = a.Preview(new(640, 650), new(0, -1)); Step(b); Step(a);
        Assert.Equal(1, a.BrickHits[0, 0]); Assert.Equal(1, a.BrickHits[1, 1]);
        Assert.Equal(0, a.BrickHits[0, 1]); Assert.Equal(0, a.BrickHits[1, 0]);
        Assert.Equal(1, b.BrickHits[0, 1]); Assert.Equal(1, b.BrickHits[1, 0]);
        Assert.Equal(0, b.BrickHits[0, 0]); Assert.Equal(0, b.BrickHits[1, 1]);
        _ = b.Preview(new(640, 650), new(-1, -1)); Step(a); Step(b);
        Assert.Empty(b.HitPairs.ToArray()); // retained overlaps are not new entries
        var tuned = new ShmupSettings(settings.Arena, settings.Avatar,
            new(10, 1, settings.Ball.FireRate, settings.Ball.Damage, false, settings.Ball.MaxWallHits, settings.Ball.MaxShotsPerTick),
            settings.Brick, settings.Difficulty, settings.Noise, settings.Capacity);
        a.ApplyTuning(TestSettings.Baseline, tuned, 600);
        Assert.InRange(double2.Distance(default, a.BallVelocity(1)), .9999, 1.0001);
        for (int life = 0; life < 6; life++)
        {
            a.Reset(); a.ActivateBrick(0, new(500, 300)); a.ActivateBrick(1, new(700, 300));
            // Outside the old radius but inside the current radius; no constructor-captured settings.
            a.ActivateBall(1, new(523, 300), new(1, 0)); a.ActivateBall(0, new(723, 300), new(1, 0));
            _ = a.Preview(new(640, 650), new(1, -1)); Step(a); Step(b);
            Assert.Equal(1, a.BrickHits[1, 0]); Assert.Equal(1, a.BrickHits[0, 1]);
            Assert.Equal(0, a.BrickHits[0, 0]); Assert.Equal(0, a.BrickHits[1, 1]);
            Assert.Same(callbacks.Overlap, a.BallQueryCallbacks.Overlap); Assert.Same(callbacks.Sweep, a.BallQueryCallbacks.Sweep);
            Assert.Same(otherCallbacks.Overlap, b.BallQueryCallbacks.Overlap); Assert.Same(otherCallbacks.Sweep, b.BallQueryCallbacks.Sweep);
        }
    }
}
