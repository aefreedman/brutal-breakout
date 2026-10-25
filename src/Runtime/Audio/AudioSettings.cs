using CrushKit.Brutal.Audio;

namespace BrutalBreakout.Audio;

public enum AudioCue { Launch, Catch, Brick, Miss, Win, Loss, ShmupFire, ShmupHit, ShmupDestroy, ShmupDamage }

/// <summary>Game schema requires every cue. The same immutable output settings reach the kit backend/player.</summary>
public sealed class AudioSettings
{
    public AudioSettings<AudioCue> Output { get; }
    public bool Enabled => Output.Enabled;
    public float MasterGain => Output.MasterGain;
    public int MaxVoices => Output.MaxVoices;
    public IReadOnlyDictionary<AudioCue, string> Assets => Output.Assets;
    public AudioSettings(bool enabled, double masterGain, int maxVoices, IReadOnlyDictionary<AudioCue, string> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (assets.Count != Enum.GetValues<AudioCue>().Length || Enum.GetValues<AudioCue>().Any(c => !assets.ContainsKey(c)))
            throw new ArgumentException("Exactly one asset per cue is required.", nameof(assets));
        Output = new(enabled, masterGain, maxVoices, assets);
    }
}
