namespace BrutalBreakout.Editor;

/// <summary>Explicit application notification preferences, validated before native startup.</summary>
public sealed class InspectorFeedbackSettings
{
    public int Capacity { get; }
    public int MaxTextCharacters { get; }
    public double SuccessSeconds { get; }
    public double InformationSeconds { get; }
    public InspectorFeedbackSettings(int capacity, int maxTextCharacters, double successSeconds, double informationSeconds)
    {
        if (capacity is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(capacity));
        // Application captions/IDs need room; this is an admission bound, not an implicit default.
        if (maxTextCharacters is < 128 or > 65536) throw new ArgumentOutOfRangeException(nameof(maxTextCharacters));
        if (!double.IsFinite(successSeconds) || successSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(successSeconds));
        if (!double.IsFinite(informationSeconds) || informationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(informationSeconds));
        Capacity = capacity; MaxTextCharacters = maxTextCharacters;
        SuccessSeconds = successSeconds; InformationSeconds = informationSeconds;
    }
}
