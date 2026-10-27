using System.Text;
using CrushKit.Brutal.ImGui;

namespace BrutalBreakout.Editor;

/// <summary>Application outcome policy over the bounded kit model. Collection/expiry occur on owner updates, never Draw.</summary>
public sealed class ConfigurationFeedback
{
    public InspectorFeedbackSettings Settings { get; }
    public InspectorTheme? Theme { get; }
    public ToolFeedbackBuffer Buffer { get; }
    public int Count => Buffer.Count;
    private long observedOutcome;
    private string? pendingDismissal;
    private ToolFeedbackNotice? summaryNotice;
    private byte[] summaryText = [];
    public ConfigurationFeedback(InspectorFeedbackSettings settings, InspectorTheme? theme = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings)); Theme = theme;
        Buffer = new(settings.Capacity, settings.MaxTextCharacters);
    }
    public void Update(double nowSeconds, ConfigurationOutcome? outcome)
    {
        if (pendingDismissal is { } id) { Buffer.Dismiss(id); pendingDismissal = null; }
        Buffer.Advance(nowSeconds); RefreshSummary();
        if (outcome is null || outcome.Sequence <= observedOutcome) return;
        observedOutcome = outcome.Sequence;
        if (outcome.Succeeded && outcome.Action == ConfigurationAction.None) return;
        var severity = !outcome.Succeeded ? ToolFeedbackSeverity.Error : outcome.Warning ? ToolFeedbackSeverity.Warning : ToolFeedbackSeverity.Success;
        string eventId = outcome.Succeeded ? "configuration." + outcome.Action.ToString().ToLowerInvariant() : "configuration.rejected";
        string title = outcome.Succeeded ? outcome.Action + " completed" : "Operation rejected";
        Post(eventId, severity, title, outcome.Message, nowSeconds);
    }
    public void Copied(double nowSeconds) => Post("configuration.copy", ToolFeedbackSeverity.Success, "Value copied", "JSON value copied to the clipboard.", nowSeconds);
    private void Post(string id, ToolFeedbackSeverity severity, string title, string detail, double nowSeconds)
    {
        int remaining = Settings.MaxTextCharacters - id.Length - title.Length;
        // Exception messages may be long or contain native-string delimiters. Bound/sanitize before the kit's strict validation.
        var safe = new StringBuilder(Math.Min(remaining, detail.Length));
        for (int i = 0; i < detail.Length && safe.Length < remaining; i++)
        {
            char c = detail[i];
            if (char.IsHighSurrogate(c) && i + 1 < detail.Length && char.IsLowSurrogate(detail[i + 1]))
            { if (safe.Length + 2 > remaining) break; safe.Append(c).Append(detail[++i]); }
            else safe.Append(c == '\0' || char.IsSurrogate(c) ? '\uFFFD' : c);
        }
        double duration = severity == ToolFeedbackSeverity.Success ? Settings.SuccessSeconds : Settings.InformationSeconds;
        // The host clock can eventually outgrow a tiny configured duration's precision. Never let optional reporting fail the operation.
        duration = Math.Max(duration, Math.BitIncrement(nowSeconds) - nowSeconds);
        if (!double.IsFinite(duration) || !double.IsFinite(nowSeconds + duration)) return;
        Buffer.Post(id, severity, title, safe.ToString(), nowSeconds, duration); RefreshSummary();
    }
    private void RefreshSummary()
    {
        var latest = Buffer.Count == 0 ? null : Buffer[Buffer.Count - 1];
        if (ReferenceEquals(latest, summaryNotice)) return;
        summaryNotice = latest;
        summaryText = latest is null ? [] : Encoding.UTF8.GetBytes(latest.Severity + ": " + latest.Title);
    }
    /// <summary>One short event-cached title, not a growing stack of detailed messages in the fixed header.</summary>
    public void DrawSummary()
    {
        if (summaryNotice is null) return;
        var tone = Tone(summaryNotice.Severity);
        if (Theme is null) global::Brutal.ImGuiApi.ImGui.TextWrapped(summaryText);
        else Theme.TextWrapped(summaryText, tone);
    }
    private static InspectorTone Tone(ToolFeedbackSeverity severity) => severity switch
    { ToolFeedbackSeverity.Success => InspectorTone.Success, ToolFeedbackSeverity.Warning => InspectorTone.Warning,
        ToolFeedbackSeverity.Error => InspectorTone.Error, _ => InspectorTone.Secondary };
    public void Draw()
    {
        for (int i = 0; i < Buffer.Count; i++)
        {
            var notice = Buffer[i];
            var tone = Tone(notice.Severity);
            if (Theme is null) { if (ToolFeedbackView.DrawNotice(notice, true)) pendingDismissal = notice.Id; }
            else
            {
                using var color = Theme.PushTone(tone);
                if (ToolFeedbackView.DrawNotice(notice, true)) pendingDismissal = notice.Id;
            }
        }
    }
}
