using BrutalBreakout.Simulation;
using CrushKit.Brutal.Audio;

namespace BrutalBreakout.Audio;

/// <summary>Mode-specific event-to-cue mapping and focus policy; playback/voice ownership belongs to the kit.</summary>
public sealed class GameAudio : IDisposable
{
    private readonly VoicePlayer<AudioCue> voices;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private bool disposed;
    public GameAudio(AudioSettings settings, IAudioOutput<AudioCue> output)
    {
        ArgumentNullException.ThrowIfNull(settings);
        voices = new(settings.Output, output);
    }
    public void SetSuppressed(bool mute, bool hasFocus) => voices.SetMuted(mute || !hasFocus);
    private void Guard()
    {
        if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("Audio belongs to its creating thread.");
        ObjectDisposedException.ThrowIf(disposed, this);
    }
    public void Consume(ReadOnlySpan<SimulationEvents> ticks)
    {
        Guard();
        foreach (var facts in ticks)
        {
            if ((facts & SimulationEvents.Restart) != 0) { voices.StopAll(); continue; }
            // Terminal cues replace same-tick brick/miss feedback. Other cue order is game policy.
            if ((facts & SimulationEvents.Win) != 0) voices.TryPlay(AudioCue.Win);
            else if ((facts & SimulationEvents.Loss) != 0) voices.TryPlay(AudioCue.Loss);
            else
            {
                if ((facts & SimulationEvents.Brick) != 0) voices.TryPlay(AudioCue.Brick);
                if ((facts & SimulationEvents.Miss) != 0) voices.TryPlay(AudioCue.Miss);
            }
            if ((facts & SimulationEvents.Launch) != 0) voices.TryPlay(AudioCue.Launch);
            if ((facts & SimulationEvents.Catch) != 0) voices.TryPlay(AudioCue.Catch);
        }
    }
    public void ConsumeShmup(ReadOnlySpan<ShmupEvents> ticks)
    {
        Guard();
        foreach (var facts in ticks)
        {
            if ((facts & ShmupEvents.Restart) != 0) { voices.StopAll(); continue; }
            if ((facts & ShmupEvents.Defeat) != 0)
            {
                // Cut short any stream/hit tails so game over is audible even at capacity.
                voices.StopAll(); voices.TryPlay(AudioCue.Loss); continue;
            }
            if ((facts & ShmupEvents.PlayerDamaged) != 0) voices.TryPlay(AudioCue.ShmupDamage);
            if ((facts & ShmupEvents.BrickDestroyed) != 0) voices.TryPlay(AudioCue.ShmupDestroy);
            else if ((facts & ShmupEvents.BrickHit) != 0) voices.TryPlay(AudioCue.ShmupHit);
            if ((facts & ShmupEvents.Fire) != 0) voices.TryPlay(AudioCue.ShmupFire);
        }
    }
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("Audio belongs to its creating thread.");
        try { voices.Dispose(); } finally { disposed = true; }
    }
}
