using System.Buffers.Binary;
using Brutal.StbApi;

namespace BrutalBreakout.Presentation.Overlay;

/// <summary>Owned straight-linear RGBA8. Sample with UNORM, never SRGB; backend uses SRC_ALPHA.</summary>
public sealed class OverlayPixels
{
    private readonly byte[] _rgba;
    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Rgba => _rgba;
    public bool HasCoverage { get; }
    internal OverlayPixels(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4)) throw new ArgumentException("Invalid RGBA dimensions.");
        Width = width; Height = height; _rgba = rgba;
        for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] != 0) { HasCoverage = true; break; }
    }

    public static byte SrgbToLinear(byte value)
    {
        var s = value / 255d;
        return (byte)Math.Round((s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4)) * 255);
    }

    public static OverlayPixels LoadPng(string path, int maxDimension, int maxEncodedBytes)
    {
        using var file = File.OpenRead(path);
        if (file.Length > maxEncodedBytes) throw new InvalidDataException("PNG exceeds encoded byte limit.");
        var bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes);
        return DecodePng(bytes, maxDimension, maxEncodedBytes);
    }

    public static OverlayPixels DecodePng(ReadOnlySpan<byte> encoded, int maxDimension, int maxEncodedBytes)
    {
        // Validate dimensions before invoking a native decoder (PNG IHDR uses big endian).
        if (encoded.Length > maxEncodedBytes || encoded.Length < 33 ||
            !encoded[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !encoded.Slice(12, 4).SequenceEqual("IHDR"u8)) throw new InvalidDataException("Expected a bounded PNG.");
        int width = BinaryPrimitives.ReadInt32BigEndian(encoded.Slice(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(encoded.Slice(20, 4));
        ValidateDimensions(width, height, maxDimension);
        using var image = Stb.LoadFromMemory(encoded, 4);
        if (image.IsNull() || image.Width != width || image.Height != height) throw new InvalidDataException("Stb could not decode PNG dimensions.");
        var rgba = image.AsSpan().ToArray(); // Copy before native free; caller never borrows Stb memory.
        if (rgba.Length != checked(width * height * 4)) throw new InvalidDataException("Unexpected Stb RGBA byte count.");
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = SrgbToLinear(rgba[i]); rgba[i + 1] = SrgbToLinear(rgba[i + 1]); rgba[i + 2] = SrgbToLinear(rgba[i + 2]);
            // Preserve straight alpha, including color at transparent edges. Do not premultiply twice.
        }
        return new(width, height, rgba);
    }

    internal static void ValidateDimensions(int width, int height, int maxDimension)
    {
        if (maxDimension <= 0 || width <= 0 || height <= 0 || width > maxDimension || height > maxDimension)
            throw new InvalidDataException("Overlay texture exceeds configured dimensions.");
        _ = checked(width * height * 4);
    }
}
