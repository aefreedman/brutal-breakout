using System.Collections.ObjectModel;
using System.Buffers.Binary;

namespace BrutalBreakout.Audio;

public enum AudioCue { Launch, Catch, Brick, Miss, Win, Loss }

/// <summary>Validated before native allocation. Paths are resolved relative to the configuration file.</summary>
public sealed class AudioSettings
{
    public bool Enabled { get; }
    public float MasterGain { get; }
    public int MaxVoices { get; }
    public IReadOnlyDictionary<AudioCue, string> Assets { get; }
    public AudioSettings(bool enabled, double masterGain, int maxVoices, IReadOnlyDictionary<AudioCue, string> assets)
    {
        if (!double.IsFinite(masterGain) || masterGain < 0 || masterGain > 1) throw new ArgumentOutOfRangeException(nameof(masterGain));
        if (maxVoices < 1 || maxVoices > 256) throw new ArgumentOutOfRangeException(nameof(maxVoices));
        ArgumentNullException.ThrowIfNull(assets);
        var copy = new Dictionary<AudioCue, string>();
        if (assets.Count != Enum.GetValues<AudioCue>().Length) throw new ArgumentException("Exactly one asset per cue is required.");
        foreach (var cue in Enum.GetValues<AudioCue>())
        {
            if (!assets.TryGetValue(cue, out var path) || string.IsNullOrWhiteSpace(path)) throw new ArgumentException($"Missing {cue} asset.");
            path = Path.GetFullPath(path);
            ValidateWave(path);
            copy.Add(cue, path);
        }
        (Enabled, MasterGain, MaxVoices, Assets) = (enabled, (float)masterGain, maxVoices, new ReadOnlyDictionary<AudioCue, string>(copy));
    }

    // Deliberately narrow project asset contract: canonical mono PCM16 WAV, no codec/runtime probe.
    private static void ValidateWave(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[44];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header.Slice(8, 8).SequenceEqual("WAVEfmt "u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(header[16..]) != 16 ||
            BinaryPrimitives.ReadUInt16LittleEndian(header[20..]) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(header[22..]) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(header[34..]) != 16 ||
            !header.Slice(36, 4).SequenceEqual("data"u8)) throw new InvalidDataException($"Unsupported cue WAV '{path}'.");
        uint rate = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
        uint bytes = BinaryPrimitives.ReadUInt32LittleEndian(header[40..]);
        if (rate == 0 || rate > uint.MaxValue / 2 || BinaryPrimitives.ReadUInt32LittleEndian(header[28..]) != rate * 2 ||
            BinaryPrimitives.ReadUInt16LittleEndian(header[32..]) != 2 || bytes == 0 || bytes % 2 != 0 ||
            stream.Length != 44L + bytes || BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != stream.Length - 8)
            throw new InvalidDataException($"Invalid cue WAV length/format '{path}'.");
    }
}
