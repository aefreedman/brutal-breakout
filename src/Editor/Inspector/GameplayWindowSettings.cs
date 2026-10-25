namespace BrutalBreakout.Editor;

/// <summary>Validated initial gameplay window bounds in physical framebuffer pixels.</summary>
public sealed class GameplayWindowSettings
{
    public float Left { get; }
    public float Top { get; }
    public float Width { get; }
    public float Height { get; }

    public GameplayWindowSettings(float left, float top, float width, float height)
    {
        static float Range(float n, float min, float max, string name) => float.IsFinite(n) && n >= min && n <= max
            ? n : throw new ArgumentOutOfRangeException(name);
        Left = Range(left, 0, 65536, nameof(left));
        Top = Range(top, 0, 65536, nameof(top));
        Width = Range(width, 1, 8192, nameof(width));
        Height = Range(height, 1, 8192, nameof(height));
    }
}
