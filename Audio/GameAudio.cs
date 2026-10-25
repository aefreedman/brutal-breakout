using BrutalBreakout.Simulation;

namespace BrutalBreakout.Audio;

/// <summary>Game-owned fixed voice boundary. No callbacks or backend handles cross it.</summary>
public interface IAudioOutput : IDisposable
{
    bool Playing(int voice);
    void Play(int voice, AudioCue cue, float gain);
    void StopAll();
}

public sealed class NullAudioOutput : IAudioOutput
{
    public bool Playing(int voice) => false;
    public void Play(int voice, AudioCue cue, float gain) { }
    public void StopAll() { }
    public void Dispose() { }
}

/// <summary>First idle voice, otherwise drop the new cue. No queue, stealing, or deferred replay.</summary>
public sealed class GameAudio : IDisposable
{
    private readonly AudioSettings settings;
    private readonly IAudioOutput output;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private bool muted, focused = true, disposed;
    public GameAudio(AudioSettings settings, IAudioOutput output)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.output = output ?? throw new ArgumentNullException(nameof(output));
    }
    private void Guard()
    {
        if (owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Audio belongs to its creating main thread.");
        ObjectDisposedException.ThrowIf(disposed, this);
    }
    public void SetSuppressed(bool mute, bool hasFocus)
    {
        Guard();
        muted = mute; focused = hasFocus;
        if (muted || !focused) output.StopAll();
    }
    public void Consume(ReadOnlySpan<SimulationEvents> ticks)
    {
        Guard();
        foreach (var facts in ticks)
        {
            if ((facts & SimulationEvents.Restart) != 0) { output.StopAll(); continue; }
            if (!settings.Enabled || muted || !focused || settings.MasterGain == 0) continue;
            // Stable ordering; terminal cues replace miss/brick feedback on that tick.
            if ((facts & SimulationEvents.Win) != 0) Emit(AudioCue.Win);
            else if ((facts & SimulationEvents.Loss) != 0) Emit(AudioCue.Loss);
            else
            {
                if ((facts & SimulationEvents.Brick) != 0) Emit(AudioCue.Brick);
                if ((facts & SimulationEvents.Miss) != 0) Emit(AudioCue.Miss);
            }
            if ((facts & SimulationEvents.Launch) != 0) Emit(AudioCue.Launch);
            if ((facts & SimulationEvents.Catch) != 0) Emit(AudioCue.Catch);
        }
    }
    private void Emit(AudioCue cue)
    {
        for (int i = 0; i < settings.MaxVoices; i++)
            if (!output.Playing(i)) { output.Play(i, cue, settings.MasterGain); return; }
    }
    public void Dispose()
    {
        if (owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Audio belongs to its creating main thread.");
        if (disposed) return;
        output.Dispose(); disposed = true;
    }
}
