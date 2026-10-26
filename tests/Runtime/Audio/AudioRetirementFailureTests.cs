using BrutalBreakout.Audio;
using BrutalBreakout.Configuration;
using CrushKit.Brutal.Audio;
using Xunit;

public sealed class AudioRetirementFailureTests
{
    private sealed class Output(bool failDispose = false) : IAudioOutput<AudioCue>
    {
        public int Disposals;
        public bool Playing(int voice) => false;
        public void Play(int voice, AudioCue cue, float gain) { }
        public void StopAll() { }
        public void Dispose()
        {
            Disposals++;
            if (failDispose) throw new IOException("injected uncertain native retirement");
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FailedRetirementQuarantinesEveryLaterApplyUntilANewOwnerIsCreated(bool initiallyMuted)
    {
        var original = AudioConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "Config", "audio.json")).Settings;
        var failing = new Output(failDispose: true);
        int factoryCalls = 0;
        (IAudioOutput<AudioCue>, string?) FakeFactory(AudioSettings settings)
        {
            factoryCalls++;
            return (new Output(), "fake output created"); // no execution path in this test can open SDL
        }
        using (var audio = new GameAudio(original, failing, FakeFactory))
        {
            for (int i = 0; i < 4; i++)
            {
                var next = new AudioSettings(true, .2 + i * .1, 4, original.Assets);
                string? diagnostic = audio.Reconfigure(next, i == 0 && initiallyMuted);
                Assert.Same(next, audio.Settings);
                Assert.Contains("relaunch to recover", diagnostic);
                Assert.Contains("injected uncertain native retirement", diagnostic);
                Assert.Equal(0, factoryCalls);
                audio.SetSuppressed(false, true);
                audio.ConsumeShmup([BrutalBreakout.Simulation.ShmupEvents.Fire]);
            }
            Assert.Equal(1, failing.Disposals); // successful disposal of later Null outputs cannot clear quarantine
        }
        using var newOwner = new GameAudio(original, new Output(), FakeFactory);
        Assert.Equal("fake output created", newOwner.Reconfigure(original, mute: false));
        Assert.Equal(1, factoryCalls); // lifetime-local, never a process-global override
    }
}
