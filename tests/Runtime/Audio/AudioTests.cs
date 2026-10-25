using BrutalBreakout.Audio;
using CrushKit.Brutal.Audio;
using BrutalBreakout.Configuration;
using BrutalBreakout.Simulation;
using BrutalBreakout.Presentation;
using BrutalBreakout.Platform.Input;
using Xunit;

namespace Audio.Tests;

public class AudioTests
{
    private static string Config => Path.Combine(AppContext.BaseDirectory, "Config", "audio.json");
    private static AudioSettings Settings(int voices = 8, double gain = .5, bool enabled = true) =>
        new(enabled, gain, voices, AudioConfiguration.Load(Config).Settings.Assets);
    private sealed class Fake : IAudioOutput<AudioCue>
    {
        public readonly HashSet<int> Busy = [];
        public readonly List<(int Voice, AudioCue Cue, float Gain)> Played = [];
        public int Stops;
        public bool Playing(int voice) => Busy.Contains(voice);
        public void Play(int voice, AudioCue cue, float gain) { Busy.Add(voice); Played.Add((voice, cue, gain)); }
        public void StopAll() { Busy.Clear(); Stops++; }
        public void Dispose() => StopAll();
    }

    [Theory]
    [InlineData(SimulationEvents.Launch, AudioCue.Launch)]
    [InlineData(SimulationEvents.Catch, AudioCue.Catch)]
    [InlineData(SimulationEvents.Brick, AudioCue.Brick)]
    [InlineData(SimulationEvents.Miss, AudioCue.Miss)]
    [InlineData(SimulationEvents.Brick | SimulationEvents.Win, AudioCue.Win)]
    [InlineData(SimulationEvents.Miss | SimulationEvents.Loss, AudioCue.Loss)]
    public void CueMapping(SimulationEvents fact, AudioCue cue)
    {
        var fake = new Fake(); using var audio = new GameAudio(Settings(), fake);
        audio.Consume([fact]); Assert.Equal(cue, Assert.Single(fake.Played).Cue);
    }
    [Theory]
    [InlineData(ShmupEvents.Fire, AudioCue.ShmupFire)]
    [InlineData(ShmupEvents.BrickHit, AudioCue.ShmupHit)]
    [InlineData(ShmupEvents.BrickHit | ShmupEvents.BrickDestroyed, AudioCue.ShmupDestroy)]
    [InlineData(ShmupEvents.PlayerDamaged, AudioCue.ShmupDamage)]
    [InlineData(ShmupEvents.Defeat | ShmupEvents.PlayerDamaged | ShmupEvents.Fire, AudioCue.Loss)]
    public void ShmupCueMapping(ShmupEvents facts, AudioCue cue)
    {
        var fake = new Fake(); using var audio = new GameAudio(Settings(), fake);
        audio.ConsumeShmup([facts]); Assert.Equal(cue, Assert.Single(fake.Played).Cue);
    }
    [Fact]
    public void ShmupCoalescesHitDestructionAndPrioritizesDamageBeforeStream()
    {
        var fake = new Fake(); using var audio = new GameAudio(Settings(), fake);
        audio.ConsumeShmup([ShmupEvents.Fire | ShmupEvents.BrickHit | ShmupEvents.BrickDestroyed | ShmupEvents.PlayerDamaged]);
        Assert.Equal(new[] { AudioCue.ShmupDamage, AudioCue.ShmupDestroy, AudioCue.ShmupFire }, fake.Played.Select(p => p.Cue));
    }
    [Fact]
    public void ShmupDefeatPreemptsFullVoicesAndRestartStopsTails()
    {
        var fake = new Fake(); using var audio = new GameAudio(Settings(1), fake);
        audio.ConsumeShmup([ShmupEvents.Fire, ShmupEvents.BrickHit, ShmupEvents.Defeat | ShmupEvents.Fire]);
        Assert.Equal(new[] { AudioCue.ShmupFire, AudioCue.Loss }, fake.Played.Select(p => p.Cue));
        audio.ConsumeShmup([ShmupEvents.Restart | ShmupEvents.Fire]); Assert.Empty(fake.Busy);
        Assert.Equal(2, fake.Played.Count);
    }
    [Fact]
    public void ShmupSuppressionDropsFactsAndModeChangeStopsOldTails()
    {
        var fake = new Fake(); using var audio = new GameAudio(Settings(), fake);
        audio.Consume([SimulationEvents.Launch]);
        audio.SetSuppressed(false, false); Assert.Empty(fake.Busy);
        audio.ConsumeShmup([ShmupEvents.Fire]);
        audio.SetSuppressed(true, true); audio.ConsumeShmup([ShmupEvents.PlayerDamaged]);
        audio.SetSuppressed(false, true); audio.ConsumeShmup([]);
        Assert.Single(fake.Played);
        audio.ConsumeShmup([ShmupEvents.Fire]); Assert.Equal(AudioCue.ShmupFire, fake.Played[^1].Cue);
    }
    [Fact]
    public void ShmupConsumeHonorsLifetimeAndThreadEvenWhenEmpty()
    {
        var audio = new GameAudio(Settings(), new Fake());
        Exception? error = null;
        var thread = new Thread(() => error = Record.Exception(() => audio.ConsumeShmup([])));
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(error);
        audio.Dispose(); Assert.Throws<ObjectDisposedException>(() => audio.ConsumeShmup([]));
    }
    [Fact]
    public void ShmupCatchupFactsDrainOnceAndCannotSurviveFocusOrRenderOnlyFrames()
    {
        var settings = ShmupConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "shmup.json")).Settings;
        using var session = new ShmupSession(Physics, settings, new(.2), new(1, 64, 1));
        var viewport = new ShmupViewport(1280, 720, 1280, 720, settings.Arena);
        var idle = new InputSnapshot { Focused = true, Cursor = new(double.NaN) };
        session.Advance(idle, 0, viewport);
        Assert.Equal(8, session.Advance(idle with { KeyboardPrimary = true }, 8 * Physics.StepSeconds, viewport));
        var ticks = session.DrainEvents().ToArray();
        Assert.Equal(8, ticks.Length); Assert.Equal(ShmupEvents.Fire, ticks[0]);
        Assert.All(ticks.Skip(1), f => Assert.Equal(ShmupEvents.None, f));
        Assert.True(session.DrainEvents().IsEmpty);
        session.Advance(idle, 0, viewport); Assert.True(session.DrainEvents().IsEmpty);
        session.Advance(default, 1, viewport); Assert.True(session.DrainEvents().IsEmpty);
        session.Advance(idle, 1, viewport); Assert.True(session.DrainEvents().IsEmpty);
        session.Advance(idle with { KeyboardRestart = true }, 8 * Physics.StepSeconds, viewport);
        ticks = session.DrainEvents().ToArray();
        Assert.Equal(ShmupEvents.Restart, ticks[0]);
        Assert.All(ticks.Skip(1), f => Assert.Equal(ShmupEvents.None, f));
        session.Dispose(); Assert.Throws<ObjectDisposedException>(() => session.DrainEvents().ToArray());
    }
    [Fact]
    public void AllShmupAssetsAreValidatedAndRequiredByStrictConfiguration()
    {
        var settings = AudioConfiguration.Load(Config).Settings;
        Assert.Equal(10, settings.Assets.Count);
        foreach (var cue in new[] { AudioCue.ShmupFire, AudioCue.ShmupHit, AudioCue.ShmupDestroy, AudioCue.ShmupDamage })
            Assert.True(File.Exists(settings.Assets[cue]));
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Config))!;
        var cues = node["cues"]!.AsObject();
        foreach (var entry in cues.ToArray())
            cues[entry.Key] = Path.GetFullPath(entry.Value!.GetValue<string>(), Path.GetDirectoryName(Config)!);
        cues.Remove("shmupFire");
        string path = Path.GetTempFileName();
        try { File.WriteAllText(path, node.ToJsonString()); Assert.Throws<InvalidDataException>(() => AudioConfiguration.Load(path)); }
        finally { File.Delete(path); }
    }
    [Fact]
    public void EmptyConsumeStillHonorsLifetimeAndThread()
    {
        var audio = new GameAudio(Settings(), new Fake());
        Exception? error = null;
        var thread = new Thread(() => error = Record.Exception(() => audio.Consume([])));
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(error);
        audio.Dispose(); audio.Dispose();
        Assert.Throws<ObjectDisposedException>(() => audio.Consume([]));
    }
    [Fact]
    public void FirstIdleThenDropNewAndReuseCompletedVoice()
    {
        var fake = new Fake(); using var audio = new GameAudio(Settings(2), fake);
        audio.Consume([SimulationEvents.Launch, SimulationEvents.Brick, SimulationEvents.Catch]);
        Assert.Equal(new[] { 0, 1 }, fake.Played.Select(x => x.Voice));
        fake.Busy.Remove(0); audio.Consume([SimulationEvents.Miss]);
        Assert.Equal((0, AudioCue.Miss, .5f), fake.Played.Last());
    }
    [Fact]
    public void SuppressionAndRestartDiscardRatherThanQueue()
    {
        var fake = new Fake(); using var audio = new GameAudio(Settings(), fake);
        audio.SetSuppressed(false, false); audio.Consume([SimulationEvents.Launch]);
        audio.SetSuppressed(true, true); audio.Consume([SimulationEvents.Brick]);
        audio.SetSuppressed(false, true); audio.Consume([]);
        Assert.Empty(fake.Played);
        audio.Consume([SimulationEvents.Launch, SimulationEvents.Restart, SimulationEvents.None]);
        Assert.Single(fake.Played); Assert.Empty(fake.Busy);
    }
    [Theory]
    [InlineData(false, .5)] [InlineData(true, 0)]
    public void DisabledAndZeroGainAreSilent(bool enabled, double gain)
    {
        var fake = new Fake(); using var audio = new GameAudio(Settings(gain: gain, enabled: enabled), fake);
        audio.Consume([SimulationEvents.Launch]);
        audio.ConsumeShmup([ShmupEvents.Fire | ShmupEvents.PlayerDamaged | ShmupEvents.Defeat]);
        Assert.Empty(fake.Played);
    }
    [Fact]
    public void DisabledNativeFactoryDoesNotLoadHardware()
    {
        using var output = SdlMixerOutput<AudioCue>.Create(Settings(enabled: false).Output, out var diagnostic);
        Assert.IsType<NullAudioOutput<AudioCue>>(output); Assert.Null(diagnostic);
    }
    [Theory]
    [InlineData(-1, 8)] [InlineData(2, 8)] [InlineData(double.NaN, 8)] [InlineData(.5, 0)] [InlineData(.5, 257)]
    public void InvalidTuningRejected(double gain, int voices) => Assert.Throws<ArgumentOutOfRangeException>(() => Settings(voices, gain));

    [Theory]
    [InlineData("\"unexpected\": 1,")]
    [InlineData("\"enabled\": true,")]
    public void StrictLoaderRejectsUnknownAndDuplicate(string addition)
    {
        string path = Path.GetTempFileName();
        try { File.WriteAllText(path, File.ReadAllText(Config).Insert(1, addition)); Assert.Throws<InvalidDataException>(() => AudioConfiguration.Load(path)); }
        finally { File.Delete(path); }
    }
    [Fact]
    public void MissingFieldsNullAndMissingPathsRejected()
    {
        string path = Path.GetTempFileName();
        try
        {
            foreach (string data in new[] { "{}", "{\"enabled\":true,\"masterGain\":0.5,\"maxVoices\":2,\"cues\":null}", File.ReadAllText(Config) })
            { File.WriteAllText(path, data); Assert.Throws<InvalidDataException>(() => AudioConfiguration.Load(path)); }
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void CorruptWaveAndMissingCueRejectedBeforeNative()
    {
        var map = Settings().Assets.ToDictionary(); string path = Path.GetTempFileName();
        try
        {
            map[AudioCue.Brick] = path;
            Assert.Throws<EndOfStreamException>(() => new AudioSettings(true, .5, 2, map));
            File.WriteAllBytes(path, new byte[44]);
            Assert.Throws<InvalidDataException>(() => new AudioSettings(true, .5, 2, map));
            map.Remove(AudioCue.Brick); Assert.Throws<ArgumentException>(() => new AudioSettings(true, .5, 2, map));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void CommandPathSelectionIsStrictAndModeAware()
    {
        Assert.Null(AudioConfiguration.LoadForMode([], false, AppContext.BaseDirectory, Environment.CurrentDirectory));
        Assert.NotNull(AudioConfiguration.LoadForMode([], true, AppContext.BaseDirectory, Environment.CurrentDirectory));
        Assert.Throws<ArgumentException>(() => AudioConfiguration.LoadForMode(["--audio-config"], false, "", ""));
        Assert.Throws<ArgumentException>(() => AudioConfiguration.LoadForMode(["--audio-config=x"], false, "", ""));
        Assert.Throws<ArgumentException>(() => AudioConfiguration.LoadForMode(["--audio-config", Config, "--audio-config", Config], true, "", ""));
        Assert.NotNull(AudioConfiguration.LoadForMode(["--audio-config", Config], false, "", Environment.CurrentDirectory));
    }
    private static PhysicsSettings Physics => new(120, 4, 8, 120, 1);
    private static GameplaySettings Game => new(new(1000, 900, 50, 50, 950, 850, 20), new(10, 600, 500, 750, 0, -1),
        new(3, 1.0 / 3, 12, [new(450, 200, 100, 30), new(100, 120, 80, 25)]));
    [Fact]
    public void CatchupCapturesFirstTickLaunchOnceAndNeverHeldReturnLaunch()
    {
        using var session = new BallSandboxSession(Physics, Game, new InputSettings(.2), new(2, 64, 2));
        var viewport = new ArenaViewport(1000, 900, 1000, 900, session.Simulation.Gameplay.Arena);
        var idle = new InputSnapshot { Focused = true };
        var held = idle with { KeyboardPrimary = true };
        session.Advance(idle, 0, viewport);
        Assert.Equal(8, session.Advance(held, 8 * Physics.StepSeconds, viewport));
        var ticks = session.DrainEvents().ToArray();
        Assert.Equal(8, ticks.Length); Assert.Equal(SimulationEvents.Launch, ticks[0]);
        Assert.All(ticks.Skip(1), x => Assert.Equal(SimulationEvents.None, x)); Assert.True(session.DrainEvents().IsEmpty);
        for (int i = 0; i < 100 && !session.Simulation.State.CatchEligible; i++) { session.Advance(held, 8 * Physics.StepSeconds, viewport); session.DrainEvents(); }
        session.Advance(idle, Physics.StepSeconds, viewport); session.DrainEvents();
        session.Advance(held, 8 * Physics.StepSeconds, viewport);
        Assert.Equal(SimulationEvents.Catch, session.DrainEvents()[0]);
        session.Advance(held, 8 * Physics.StepSeconds, viewport);
        Assert.All(session.DrainEvents().ToArray(), x => Assert.Equal(SimulationEvents.None, x));
        Assert.Equal(BallPhase.Aiming, session.Simulation.State.Phase);
    }
    [Fact]
    public void MissInsideCatchupBatchSurvivesLaterIdleTicks()
    {
        using var session = new BallSandboxSession(Physics, Game, new InputSettings(.2), new(2, 64, 2));
        var viewport = new ArenaViewport(1000, 900, 1000, 900, session.Simulation.Gameplay.Arena);
        var idle = new InputSnapshot { Focused = true, KeyboardDirection = new(0, -1) };
        session.Advance(idle, 0, viewport);
        session.Advance(idle with { KeyboardPrimary = true }, 8 * Physics.StepSeconds, viewport);
        session.DrainEvents();
        int misses = 0;
        bool followedByIdle = false;
        for (int i = 0; i < 10; i++)
        {
            session.Advance(idle, 8 * Physics.StepSeconds, viewport);
            var ticks = session.DrainEvents().ToArray();
            int index = Array.FindIndex(ticks, x => (x & SimulationEvents.Miss) != 0);
            if (index >= 0) { misses++; followedByIdle = index < ticks.Length - 1 && ticks[^1] == SimulationEvents.None; }
        }
        Assert.Equal(1, misses); Assert.True(followedByIdle);
    }
    [Fact]
    public void FocusAndRestartCannotReplayUndrainedFacts()
    {
        using var session = new BallSandboxSession(Physics, Game, new InputSettings(.2), new(2, 64, 2));
        var viewport = new ArenaViewport(1000, 900, 1000, 900, session.Simulation.Gameplay.Arena);
        var idle = new InputSnapshot { Focused = true };
        session.Advance(idle, 0, viewport);
        session.Advance(idle with { KeyboardPrimary = true }, Physics.StepSeconds, viewport);
        session.Advance(default, 0, viewport); Assert.True(session.DrainEvents().IsEmpty);
        session.Advance(idle, 1, viewport); Assert.True(session.DrainEvents().IsEmpty);
        session.Advance(idle with { KeyboardRestart = true }, 8 * Physics.StepSeconds, viewport);
        Assert.Equal(SimulationEvents.Restart, session.DrainEvents()[0]);
    }
    [Fact]
    public void SimulationFactsDistinguishMissWinLossAndClearOnIdleRestart()
    {
        using var miss = new BallSimulation(Physics, Game, new(2, 64, 2));
        miss.Step(new(new(0, 1), PrimaryPressed: true));
        for (int i = 0; i < 300 && (miss.Events & SimulationEvents.Miss) == 0; i++) miss.Step();
        Assert.Equal(SimulationEvents.Miss, miss.Events); Assert.Equal(BallPhase.Aiming, miss.State.Phase);
        miss.Step(); Assert.Equal(SimulationEvents.None, miss.Events);
        miss.Step(new(RestartPressed: true)); Assert.Equal(SimulationEvents.Restart, miss.Events);
        var game = Game;
        using var win = new BallSimulation(Physics, new(game.Arena, game.Ball, new(1, 1.0 / 3, 12, [new(450, 200, 100, 30)])), new(2, 64, 2));
        win.Step(new(PrimaryPressed: true));
        for (int i = 0; i < 300 && win.RoundPhase == RoundPhase.Playing; i++) win.Step();
        Assert.Equal(SimulationEvents.Brick | SimulationEvents.Win, win.Events);
        win.Step(); Assert.Equal(SimulationEvents.None, win.Events);
        using var loss = new BallSimulation(Physics, new(game.Arena, game.Ball, new(1, 1.0 / 3, 12, [new(450, 200, 100, 30)])), new(2, 64, 2));
        loss.Step(new(new(0, 1), PrimaryPressed: true));
        for (int i = 0; i < 300 && loss.RoundPhase == RoundPhase.Playing; i++) loss.Step();
        Assert.Equal(SimulationEvents.Miss | SimulationEvents.Loss, loss.Events);
    }
}
