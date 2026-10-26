using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Brutal.Numerics;
using BrutalBreakout.Performance;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;
using Xunit;

namespace BrutalBreakout.Performance.Tests;

public sealed class StudyContractTests
{
    private static string Config => Path.Combine(AppContext.BaseDirectory, "Config");
    private static Manifest Manifest => Manifest.Load(Path.Combine(AppContext.BaseDirectory, "study.json"));
    private static ResolvedCase Resolve(string id = "whole-2000", int smoke = 3)
    {
        var m = Manifest;
        return ResolvedCase.Load(m.Cases.Single(c => c.Id == id), m.Policy, Config, smoke);
    }
    private static ResolvedCase Small()
    {
        var c = Resolve(); var s = c.Settings;
        var settings = new ShmupSettings(s.Arena, s.Avatar, s.Ball, s.Brick, s.Difficulty, s.Noise, new(8, 8));
        return c with { Spec = c.Spec with { Target = 2 }, Settings = settings };
    }
    private static Layout SmallLayout(ResolvedCase c) => new(c.Settings, c.Policy.Width,
        new double2[] { new(500, 500), new(600, 500) }, new BrickSeed[] { new(new(600, 100), ShmupBrickShape.L, 1) });

    [Fact]
    public void FrozenManifestHas28OrderedFamiliesAndValidatedOwnedSettings()
    {
        var manifest = Manifest;
        Assert.Equal(28, manifest.Cases.Count);
        Assert.Equal("native-0500", manifest.Cases[0].Id);
        Assert.Equal("catchup-8", manifest.Cases[^1].Id);
        foreach (var spec in manifest.Cases)
        {
            var c = ResolvedCase.Load(spec, manifest.Policy, Config);
            Assert.Equal(4096, c.Settings.Capacity.MaxBalls); Assert.Equal(1024, c.Settings.Capacity.MaxBricks);
            Assert.Equal(1280, c.Settings.Arena.InitialPlayAreaWidth);
            Assert.Equal(2 * c.Physics.UpdateHz, c.WarmTicks); Assert.Equal(20 * c.Physics.UpdateHz, c.MeasureTicks);
            Assert.InRange(c.TotalTicks, 1, 5280); Assert.True(c.StorageEstimateBytes > 36L * 1024 * 1024);
            Assert.Equal(2, c.Execution.Jobs.WorkerCount); // Physics worker sweep is one-factor only.
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => ResolvedCase.Load(manifest.Cases[0], manifest.Policy, Config, 121));
        var authoritative = BrutalBreakout.Configuration.ShmupConfiguration.Load(Path.Combine(Config, "shmup.json")).Settings;
        Assert.Equal(authoritative.Ball.MaxWallHits, Resolve("spawn-reuse").Settings.Ball.MaxWallHits);
        Assert.Equal(manifest.Policy.WallHits, Resolve().Settings.Ball.MaxWallHits);
    }
    [Fact]
    public void InvalidManifestAndMemoryBudgetsFailBeforeFactoryOrNativeAllocation()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "study.json"));
        var node = JsonNode.Parse(json)!; node["policy"]!["ballSlots"] = 8192;
        Assert.Throws<InvalidDataException>(() => BrutalBreakout.Performance.Manifest.Parse(node.ToJsonString()));
        node = JsonNode.Parse(json)!; node["cases"]![1]!["id"] = "native-0500";
        Assert.Throws<InvalidDataException>(() => BrutalBreakout.Performance.Manifest.Parse(node.ToJsonString()));
        var m = Manifest;
        Assert.Throws<InvalidDataException>(() => ResolvedCase.Load(m.Cases[0], m.Policy with { PrivateBytes = 100 }, Config));
        Assert.False(MemoryGuard.Allows(new(100, 100, 90), m.Policy, 1000, true));
        Assert.Throws<ArgumentException>(() => new ShmupCapacitySettings(4096, 2048));
    }
    [Fact]
    public void FourThousandSeedCirclesFitEveryRotatedCompoundAndUniformPrefixes()
    {
        var c = Resolve("whole-4000"); var a = new Layout(c.Settings, c.Policy); var b = new Layout(c.Settings, c.Policy);
        Assert.Equal(4000, a.Balls.Count); Assert.Equal(40, a.Bricks.Count);
        Assert.Equal(a.Balls, b.Balls); Assert.Equal(a.Bricks, b.Bricks);
        Assert.Equal(5, a.Bricks.Select(brick => brick.Shape).Distinct().Count());
        Assert.Equal(4, a.Bricks.Select(brick => brick.Turns).Distinct().Count());
        foreach (int prefix in new[] { 500, 1000, 2000, 4000 })
        {
            var strata = a.Balls.Take(prefix).GroupBy(p => ((int)(p.X / 128), (int)((p.Y - 430) / 25))).ToArray();
            Assert.Equal(100, strata.Length); Assert.All(strata, group => Assert.Equal(prefix / 100, group.Count()));
        }
        Assert.Throws<InvalidDataException>(() => Layout.Validate(c.Settings, 432, a.Balls, a.Bricks));
        Assert.Throws<InvalidDataException>(() => Layout.Validate(c.Settings, 1280, new[] { new double2(1, 100) }, a.Bricks));
        var first = a.Bricks[0];
        Assert.Throws<InvalidDataException>(() => Layout.Validate(c.Settings, 1280, Array.Empty<double2>(), new[] { first, first }));
    }
    [Fact]
    public void DescriptiveQuantilesAndClockDropsDoNotHideUnequalWork()
    {
        var d = Distribution.Of(Enumerable.Range(1, 100).Select(x => (double)x));
        Assert.Equal(100, d.N); Assert.False(d.LowN); Assert.Equal(50, d.P50); Assert.Equal(95, d.P95); Assert.Equal(99, d.P99); Assert.Equal(100, d.Max);
        Assert.True(Distribution.Of(new[] { 7.0 }).LowN); Assert.Null(Distribution.Of(Array.Empty<double>()).Max);
        Assert.Throws<InvalidDataException>(() => Distribution.Of(new[] { double.NaN }));
        var c = Resolve();
        var tapes = new[] { 1, 4, 8 }.Select(cap => ClockTape.Build(new(120, 4, cap, 120, 1), 1200)).ToArray();
        Assert.True(tapes[0].InputFrames.Length < tapes[1].InputFrames.Length);
        Assert.True(tapes[1].InputFrames.Length <= tapes[2].InputFrames.Length);
        Assert.All(tapes, tape => Assert.Equal(tape.OfferedSeconds, tape.AdmittedSeconds + tape.DroppedSeconds + tape.RemainderSeconds, 8));
        Assert.True(tapes[0].DroppedSeconds > tapes[2].DroppedSeconds);
        var bounded = ClockTape.Build(c.Physics, 1200, 20);
        Assert.Equal(20, bounded.OfferedSeconds, 8); Assert.InRange(bounded.InputFrames.Length, 1, 2400);
        var work = new WorkAccounting(); work.Accept(); work.Complete(0); work.Capture();
        Assert.Throws<InvalidOperationException>(() => work.Validate(true)); work.Present(); work.Validate(true);
        Assert.Throws<InvalidOperationException>(() => work.Complete(0));
    }
    [Theory]
    [InlineData("direct")]
    [InlineData("immediate")]
    [InlineData("overlap")]
    public void FillDrainCapturesAndPresentsEveryCompletedCommandIncludingZeroAndFinal(string execution)
    {
        var c = Small(); c = c with { Spec = c.Spec with { Execution = execution } };
        var run = CaseRunner.Run(c, (settings, _) => new StudyController(settings, SmallLayout(settings)),
            () => new(0, 16ul * 1024 * 1024 * 1024, 10));
        Assert.Equal("completed", run.Result.Status);
        Assert.Equal(3, run.Result.Work.Accepted); Assert.Equal(3, run.Result.Work.Completed);
        Assert.Equal(3, run.Result.Work.Captured); Assert.Equal(3, run.Result.Work.Presented);
        Assert.Equal(new long[] { 0, 1, 2 }, run.Raw.Select(r => r.Sequence));
        Assert.All(run.Raw, r => { Assert.True(r.Displayed >= r.Published); Assert.True(r.Indices > 0); Assert.True(r.Facts.TickAdvanced); });
        Assert.Equal(0, run.Raw[^1].DisplayInputSequenceAge);
        Assert.Equal(execution == "overlap" ? 1 : 0, run.Raw[0].DisplayInputSequenceAge);
        Assert.Equal(3, run.Result.EndToEndCommand!.N);
        Assert.All(run.Raw, r => Assert.Equal(r.Facts.BallsBefore - r.Facts.RetiredBalls, r.Facts.BallsAfter));
    }
    [Fact]
    public void CooperativeTruncationAndMemoryPreflightNeverInventSteadySamples()
    {
        var c = Small() with { Smoke = false, WarmTicks = 2, MeasureTicks = 3 };
        int checks = 0;
        var run = CaseRunner.Run(c, (settings, _) => new StudyController(settings, SmallLayout(settings)),
            () => new(0, 16ul * 1024 * 1024 * 1024, 10), () => ++checks == 1 ? 0 : 31);
        Assert.Equal("warm-truncation-wall", run.Result.Status);
        Assert.Equal(1, run.Result.WarmCompleted); Assert.Equal(0, run.Result.MeasuredCompleted);
        Assert.False(run.Result.SteadyStatisticsAvailable); Assert.Null(run.Result.Step);
        Assert.Equal(1, run.Result.Work.Presented);
        var rejected = CaseRunner.Run(c, (_, _) => throw new Exception("Must not allocate native state"),
            () => new(0, 1, 100));
        Assert.Equal("memory-preflight-skip", rejected.Result.Status); Assert.Empty(rejected.Raw);
    }
    [Fact]
    public void AuthoritativeDuplicateKeysAreRejectedBeforeOverrideNormalization()
    {
        string directory = Path.Combine(Path.GetTempPath(), "breakout-study-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (string name in new[] { "physics", "shmup", "execution", "trails", "colors", "style" })
                File.Copy(Path.Combine(Config, name + ".json"), Path.Combine(directory, name + ".json"));
            string path = Path.Combine(directory, "physics.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("{", "{\"updateHz\":60,", StringComparison.Ordinal));
            var m = Manifest;
            Assert.Throws<InvalidDataException>(() => ResolvedCase.Load(m.Cases[0], m.Policy, directory));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData(60, 1, 60, .7)]
    [InlineData(120, 2, 120, 1)]
    [InlineData(240, 4, 120, 1)]
    public void SmallQualityPresetsSatisfySemanticBoundsWithoutClaimingTrajectoryEquality(int hz, int solver, int contact, double damping)
    {
        var c = Small() with { Physics = new(hz, solver, 8, contact, damping) };
        using var controller = new StudyController(c, SmallLayout(c));
        var publication = controller.Execute(new(0, default, 0));
        var snapshot = Assert.IsType<ShmupSnapshot>(publication.Snapshot);
        Assert.Equal(2, snapshot.Balls.Length); Assert.Equal(1, snapshot.Tick);
        Assert.Equal(1.0 / hz, snapshot.TimeSeconds, 12);
        Assert.Equal(8 + 8, snapshot.EntityCount);
        foreach (var ball in snapshot.Balls)
        {
            Assert.True(double.IsFinite(ball.Position.X) && double.IsFinite(ball.Position.Y));
            Assert.InRange(ball.Position.X, ball.Radius, 1280 - ball.Radius);
            Assert.InRange(ball.Position.Y, ball.Radius, 720 - ball.Radius);
            Assert.Equal(c.Settings.Ball.Speed, Math.Sqrt(ball.Velocity.X * ball.Velocity.X + ball.Velocity.Y * ball.Velocity.Y), 2);
            Assert.Equal(1, ball.Generation);
        }
        Assert.Null(publication.Facts.NativeExpiredExact); Assert.Null(publication.Facts.NativeWallHitsExact);
        Assert.True(publication.Facts.Intervals > 0);
    }
    [Fact]
    public void StepFailureIsNotRelabelledAsStickyOwnerTeardownFailure()
    {
        var c = Small(); c = c with { Spec = c.Spec with { Execution = "immediate" } };
        var run = CaseRunner.Run(c, (_, _) => new FakeController { Throw = true }, () => new(0, 16ul * 1024 * 1024 * 1024, 10));
        Assert.Equal("measure-failure", run.Result.Status); Assert.Contains("step-fault", run.Result.Error);
        Assert.Null(run.Result.TeardownError);
    }
    [Fact]
    public void WindowsMemoryContractHasCorrectSizeAndFailsClosed()
    {
        Assert.Equal(64, Marshal.SizeOf<MemoryGuard.MemoryStatus>());
        var result = MemoryGuard.Query((ref MemoryGuard.MemoryStatus status) =>
        {
            Assert.Equal(64u, status.Length); status.TotalPhysical = 1000; status.AvailablePhysical = 300; status.Load = 70; return true;
        });
        Assert.Equal(300ul, result.AvailablePhysical);
        Assert.Throws<System.ComponentModel.Win32Exception>(() => MemoryGuard.Query((ref MemoryGuard.MemoryStatus _) => false));
        Assert.Throws<InvalidDataException>(() => MemoryGuard.Query((ref MemoryGuard.MemoryStatus _) => true));
    }
    private sealed class FakeController : IStudyController
    {
        public readonly ManualResetEventSlim Started = new(), Release = new();
        public bool Block, Throw;
        public int ConstructThread, StepThread, DisposeThread, Executed;
        public FakeController() => ConstructThread = Environment.CurrentManagedThreadId;
        public Publication Execute(StudyCommand command)
        {
            StepThread = Environment.CurrentManagedThreadId; Started.Set();
            if (Block) Release.Wait();
            if (Throw) throw new InvalidOperationException("step-fault");
            Executed++; return new(null, ShmupEvents.Fire, default);
        }
        public void Dispose() => DisposeThread = Environment.CurrentManagedThreadId;
    }
    [Fact]
    public async Task DedicatedOwnerRejectsActiveAndUnobservedCompletionAndDrainsAcceptedWork()
    {
        FakeController? fake = null;
        var owner = new StudyOwner(() => fake = new() { Block = true });
        Assert.True(owner.TrySubmit(0, default)); fake!.Started.Wait();
        Assert.False(owner.TrySubmit(1, default));
        var disposal = Task.Run(owner.Dispose);
        fake.Release.Set(); await disposal;
        var completed = owner.Observe();
        Assert.Equal(0, completed.Sequence); Assert.Equal(ShmupEvents.Fire, completed.Publication.Events);
        Assert.Equal(1, fake.Executed); Assert.Equal(owner.OwnerThreadId, fake.ConstructThread);
        Assert.Equal(fake.ConstructThread, fake.StepThread); Assert.Equal(fake.StepThread, fake.DisposeThread);
        Assert.False(owner.TrySubmit(1, default));
    }
    [Fact]
    public async Task OwnerInitAndStepFaultReachWaitingConsumerWithoutHanging()
    {
        Assert.Throws<InvalidOperationException>(() => new StudyOwner(() => throw new InvalidOperationException("init-fault")));
        FakeController? fake = null;
        var owner = new StudyOwner(() => fake = new() { Block = true, Throw = true });
        Assert.True(owner.TrySubmit(0, default)); fake!.Started.Wait();
        var waiting = Task.Run(() => Assert.Throws<InvalidOperationException>(() => owner.Observe()));
        fake.Release.Set(); await waiting;
        Assert.Throws<InvalidOperationException>(() => owner.Dispose());
        Assert.Equal(fake.StepThread, fake.DisposeThread);
    }
    [Fact]
    public void OwnerCompletionOrderAndCapacityIncludeCompletedUnobservedSlot()
    {
        using var owner = new StudyOwner(() => new FakeController());
        for (int sequence = 0; sequence < 4; sequence++)
        {
            Assert.True(owner.TrySubmit(sequence, default));
            Assert.False(owner.TrySubmit(sequence + 1, default));
            var done = owner.Observe(); Assert.Equal(sequence, done.Sequence);
            Assert.True(done.Enqueued <= done.Started && done.Started <= done.Published);
        }
    }
    [Fact]
    public void SmallNativeOwnerPreservesEverySnapshotEventsRestartGenerationAndBorrowedFence()
    {
        var c = Small(); var layout = SmallLayout(c);
        using var owner = new StudyOwner(() => new StudyController(c, layout));
        var retained = new List<(ShmupSnapshot Snapshot, ShmupBallState[] Balls)>();
        var history = new BallTrailHistory(c.Trails!, c.Settings.Capacity.MaxBalls);
        using var draw = new ShapeDrawList();
        long generation = 0;
        for (int sequence = 0; sequence < 5; sequence++)
        {
            Assert.True(owner.TrySubmit(sequence, new(FireHeld: sequence == 1, RestartPressed: sequence == 2)));
            var done = owner.Observe(); var snapshot = Assert.IsType<ShmupSnapshot>(done.Publication.Snapshot);
            history.Capture(snapshot);
            if (sequence == 2)
            {
                Assert.Equal(ShmupEvents.Restart, done.Publication.Events); Assert.Empty(snapshot.Balls.ToArray());
                Assert.False(done.Publication.Facts.TickAdvanced); Assert.Equal(0, done.Publication.Facts.Intervals);
                Assert.Equal(0, done.Publication.Facts.RetiredBalls);
            }
            else
            {
                Assert.Equal(sequence == 1 ? 3 : 2, snapshot.Balls.Length);
                if (sequence == 1) Assert.True(done.Publication.Events.HasFlag(ShmupEvents.Fire));
                if (sequence == 0) generation = snapshot.Balls[0].Generation;
                if (sequence > 2) Assert.True(snapshot.Balls[0].Generation > generation);
            }
            retained.Add((snapshot, snapshot.Balls.ToArray()));
            var frame = ShmupPresentation.Build(draw, snapshot, c.Settings, c.Colors, c.Style, 1280, 720, history);
            // Own only copied evidence before draw-list reuse; no borrowed frame escapes this scope.
            var indices = frame.Indices.ToArray(); Assert.NotEmpty(indices);
        }
        Assert.All(retained, p => Assert.Equal(p.Balls, p.Snapshot.Balls.ToArray()));
        Assert.True(history.PointCount > 0);
    }
}
