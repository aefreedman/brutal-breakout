using System.Text;
using Brutal.SdlApi;
using Brutal.SdlApi.Ttf;
using BrutalBreakout.Configuration;

namespace BrutalBreakout.Presentation.Overlay;

/// <summary>Bounded whole-label SDL.Ttf cache. All access (including disposal) belongs to the creating thread.</summary>
public sealed unsafe class OverlayText : IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly OverlaySettings _settings;
    private readonly Dictionary<string, OverlayPixels> _cache = new(StringComparer.Ordinal);
    private TtfFont _font;
    private bool _initialized, _disposed;
    public int Rasterizations { get; private set; }
    public int CacheHits { get; private set; }
    public int Count => _cache.Count;
    public int Generation { get; private set; }

    public OverlayText(OverlaySettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        try { SdlTtf.Init(); _initialized = true; _font = SdlTtf.OpenFont(settings.FontPath, settings.FontSize);
            if (_font.IsNull()) throw new InvalidDataException("SDL.Ttf could not open the configured font."); }
        catch { Dispose(); throw; }
    }

    public OverlayPixels Get(string label)
    {
        CheckThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(label) || label.Length > _settings.MaxLabelCharacters || label.Any(char.IsControl))
            throw new ArgumentException("Overlay label is empty, too long, or contains control characters.", nameof(label));
        if (_cache.TryGetValue(label, out var hit)) { CacheHits++; return hit; }
        int width = 0, height = 0;
        var length = (nuint)Encoding.UTF8.GetByteCount(label);
        if (!SdlTtf.GetStringSize(_font, label, length, &width, &height)) throw new InvalidDataException("SDL.Ttf measurement failed.");
        OverlayPixels.ValidateDimensions(width, height, _settings.MaxTextureDimension);
        var surface = SdlTtf.RenderTextBlended(_font, label, length, new SdlColor { r = 255, g = 255, b = 255, a = 255 });
        if (surface == null) throw new InvalidDataException("SDL.Ttf rasterization failed.");
        try
        {
            var converted = Sdl.PInvoke.ConvertSurface(surface, SdlPixelFormat.Rgba32);
            if (converted == null) throw new InvalidDataException("SDL RGBA conversion failed.");
            try
            {
                OverlayPixels.ValidateDimensions(converted->w, converted->h, _settings.MaxTextureDimension);
                var rowBytes = checked(converted->w * 4);
                if (converted->pitch < rowBytes || converted->pixels == null) throw new InvalidDataException("Invalid SDL surface pitch/pixels.");
                var pixels = new byte[checked(rowBytes * converted->h)];
                for (int y = 0; y < converted->h; y++)
                    new ReadOnlySpan<byte>((byte*)converted->pixels + y * converted->pitch, rowBytes).CopyTo(pixels.AsSpan(y * rowBytes));
                for (int i = 0; i < pixels.Length; i += 4) pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
                // White coverage mask is already straight-linear (RGB=1). Alpha is coverage, not sRGB.
                var result = new OverlayPixels(converted->w, converted->h, pixels);
                if (_cache.Count == _settings.CacheEntries) { _cache.Clear(); Generation++; }
                _cache.Add(label, result); Rasterizations++;
                return result;
            }
            finally { Sdl.PInvoke.DestroySurface(converted); }
        }
        finally { Sdl.PInvoke.DestroySurface(surface); }
    }

    private void CheckThread()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Overlay text is main-thread owned.");
    }
    public void Dispose()
    {
        CheckThread(); if (_disposed) return; _disposed = true;
        _cache.Clear();
        if (_initialized)
        {
            try { if (!_font.IsNull()) SdlTtf.CloseFont(_font); }
            finally { SdlTtf.Quit(); }
        }
    }
}
