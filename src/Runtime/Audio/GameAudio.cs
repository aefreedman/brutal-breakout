using BrutalBreakout.Simulation;
using CrushKit.Brutal.Audio;

namespace BrutalBreakout.Audio;

/// <summary>Mode-specific event-to-cue mapping and focus policy; playback/voice ownership belongs to the kit.</summary>
public sealed class GameAudio : IDisposable
{
    private VoicePlayer<AudioCue> voices;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private bool disposed;
    public AudioSettings Settings { get; private set; }
    public GameAudio(AudioSettings settings, IAudioOutput<AudioCue> output)
    {
        ArgumentNullException.ThrowIfNull(settings);
        voices = new(settings.Output, output);
        Settings = settings;
    }
    /// <summary>One main-thread SDL owner: retire before creating, never overlap global mixer lifetimes.
    /// A validated edit can apply without an available device; fallback is explicit and nonfatal.</summary>
    public string? Reconfigure(AudioSettings settings, bool mute)
    {
        Guard();
        ArgumentNullException.ThrowIfNull(settings);
        string? diagnostic = null;
        bool retirementFailed = false;
        try { voices.Dispose(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            retirementFailed = true;
            diagnostic = "Audio retirement failed; using silent output (relaunch to recover): " + error.Message;
        }
        IAudioOutput<AudioCue>? output = null;
        try
        {
            output = mute || retirementFailed ? new NullAudioOutput<AudioCue>() : SdlMixerOutput<AudioCue>.Create(settings.Output, out diagnostic);
            voices = new(settings.Output, output);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            try { output?.Dispose(); } catch (Exception cleanup) { diagnostic = "Audio cleanup: " + cleanup.Message; }
            voices = new(settings.Output, new NullAudioOutput<AudioCue>());
            diagnostic = "Audio unavailable after Apply; using silent output: " + error.Message;
        }
        Settings = settings;
        voices.SetMuted(true); // fresh owner does not replay events from the retired run
        return diagnostic;
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
