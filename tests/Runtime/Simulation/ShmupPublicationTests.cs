using BrutalBreakout.Simulation;
using Xunit;

namespace Simulation.Tests;

public sealed class ShmupPublicationTests
{
    [Fact]
    public void BoundedStagingNeverAliasesNonemptyPublicationsAcrossTicksTuningResetReuseAndDisposal()
    {
        var settings = ShmupSettingsTests.Settings(bounce: false, maxBalls: 3, maxBricks: 4, threshold: 1, speed: 1, descent: 1);
        var sim = new ShmupSimulation(TestSettings.Baseline, settings, new(1, 64, 1));
        var retained = new List<(ShmupSnapshot Snapshot, ShmupBallState[] Balls, ShmupBrickState[] Bricks)>();
        void Retain()
        {
            var snapshot = sim.Snapshot;
            Assert.NotEmpty(snapshot.Balls.ToArray()); Assert.NotEmpty(snapshot.Bricks.ToArray());
            Assert.Equal(snapshot.Balls.ToArray().OrderBy(b => b.Id).ToArray(), snapshot.Balls.ToArray());
            Assert.Equal(snapshot.Bricks.ToArray().OrderBy(b => b.Id).ToArray(), snapshot.Bricks.ToArray());
            retained.Add((snapshot, snapshot.Balls.ToArray(), snapshot.Bricks.ToArray()));
        }
        try
        {
            for (int life = 1; life <= 8; life++)
            {
                for (int i = 0; i < 3; i++) Assert.Equal(i, sim.SpawnBall(new(500 + i * 30, 500), new(0, -1)));
                for (int i = 0; i < 4; i++) Assert.Equal(i, sim.SpawnBrick(new(500 + i * 40, 100)));
                Assert.Equal(-1, sim.SpawnBall(new(640, 500), new(0, -1))); Assert.Equal(-1, sim.SpawnBrick(new(640, 100)));
                sim.Step(); Retain();
                Assert.All(sim.Snapshot.Balls.ToArray(), b => Assert.Equal(life, b.Generation));
                Assert.All(sim.Snapshot.Bricks.ToArray(), b => Assert.Equal(life, b.Generation));
                var before = sim.Snapshot;
                for (int tick = 0; tick < 3; tick++) { sim.Step(); Assert.NotSame(before, sim.Snapshot); before = sim.Snapshot; Retain(); }
                var tuned = new ShmupSettings(settings.Arena, settings.Avatar,
                    new(4, 2, settings.Ball.FireRate, settings.Ball.Damage, false, settings.Ball.MaxWallHits),
                    settings.Brick, settings.Difficulty, settings.Noise, settings.Capacity);
                sim.ApplyTuning(TestSettings.Baseline, tuned); Assert.NotSame(before, sim.Snapshot); Retain();
                Assert.All(sim.Snapshot.Balls.ToArray(), b => Assert.Equal(4, b.Radius));
                sim.Widen(700); Retain();
                Assert.Equal((3, 4), sim.PublicationStagingCapacity);
                sim.Restart(); Assert.Empty(sim.Snapshot.Balls.ToArray()); Assert.Empty(sim.Snapshot.Bricks.ToArray());
                Assert.Equal((3, 4), sim.PublicationStagingCapacity);
                sim.ApplyTuning(TestSettings.Baseline, settings);
            }
        }
        finally { sim.Dispose(); }
        foreach (var publication in retained)
        {
            Assert.Equal(publication.Balls, publication.Snapshot.Balls.ToArray());
            Assert.Equal(publication.Bricks, publication.Snapshot.Bricks.ToArray());
            Assert.InRange(publication.Snapshot.Tick, 1, 4);
            Assert.Equal(100, publication.Snapshot.PlayerHealth);
            Assert.InRange(publication.Snapshot.Width, 600, 700);
        }
        Assert.Equal(1, retained[0].Snapshot.Tick); Assert.Equal(600, retained[0].Snapshot.Width);
        Assert.All(retained[0].Snapshot.Balls.ToArray(), b => Assert.Equal(3, b.Radius));
    }
}
