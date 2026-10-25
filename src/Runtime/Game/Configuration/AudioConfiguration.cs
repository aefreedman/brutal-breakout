using BrutalBreakout.Audio;

namespace BrutalBreakout.Configuration;

public sealed record LoadedAudioConfiguration(string Path, AudioSettings Settings);

/// <summary>Strict audio tuning. Defaults exist only in audio.json.</summary>
public static class AudioConfiguration
{
    private sealed record CueFiles(string Launch, string Catch, string Brick, string Miss, string Win, string Loss,
        string ShmupFire, string ShmupHit, string ShmupDestroy, string ShmupDamage);
    private sealed record AudioFile(bool Enabled, double MasterGain, int MaxVoices, CueFiles Cues);

    public static LoadedAudioConfiguration Load(string path)
    {
        var loaded = ConfigurationFile.Load<AudioFile, AudioSettings>(path, "audio", data =>
        {
            ArgumentNullException.ThrowIfNull(data.Cues);
            string root = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
            string Resolve(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Empty cue path.") : System.IO.Path.GetFullPath(value, root);
            return new(data.Enabled, data.MasterGain, data.MaxVoices, new Dictionary<AudioCue, string>
            {
                [AudioCue.Launch] = Resolve(data.Cues.Launch), [AudioCue.Catch] = Resolve(data.Cues.Catch),
                [AudioCue.Brick] = Resolve(data.Cues.Brick), [AudioCue.Miss] = Resolve(data.Cues.Miss),
                [AudioCue.Win] = Resolve(data.Cues.Win), [AudioCue.Loss] = Resolve(data.Cues.Loss),
                [AudioCue.ShmupFire] = Resolve(data.Cues.ShmupFire), [AudioCue.ShmupHit] = Resolve(data.Cues.ShmupHit),
                [AudioCue.ShmupDestroy] = Resolve(data.Cues.ShmupDestroy), [AudioCue.ShmupDamage] = Resolve(data.Cues.ShmupDamage)
            });
        });
        return new(loaded.Path, loaded.Settings);
    }

    public static LoadedAudioConfiguration? LoadForMode(IReadOnlyList<string> args, bool needed, string baseDirectory, string currentDirectory)
    {
        var path = ConfigurationFile.SelectPath(args, needed, "audio", baseDirectory, currentDirectory);
        return path is null ? null : Load(path);
    }
}
