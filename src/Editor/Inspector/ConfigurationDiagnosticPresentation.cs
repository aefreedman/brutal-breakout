using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CrushKit.Brutal.ImGui;
using Ui = Brutal.ImGuiApi.ImGui;

namespace BrutalBreakout.Editor;

/// <summary>One change-driven presentation of a persistent diagnostic. The header is fixed-size metadata;
/// all sanitized text remains selectable in a bounded-height, natively scrolling details widget.</summary>
internal sealed class ConfigurationDiagnosticPresentation
{
    public string Summary { get; }
    public string Details { get; }
    public InspectorTone Tone { get; }
    public ReadOnlySpan<byte> SummaryUtf8 => summary;
    private readonly byte[] summary, details;

    public ConfigurationDiagnosticPresentation(string text, bool runtime = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        bool rejected = text.StartsWith("Rejected:", StringComparison.Ordinal);
        bool warning = text.Contains("warning:", StringComparison.OrdinalIgnoreCase);
        Tone = rejected ? InspectorTone.Error : runtime || warning ? InspectorTone.Warning : InspectorTone.Secondary;
        Summary = runtime ? "Runtime warning — open Help."
            : rejected
                ? text.Contains("Source changed externally", StringComparison.OrdinalIgnoreCase)
                    ? "Source conflict — open Help."
                    : text.Contains("PARTIAL SAVE", StringComparison.OrdinalIgnoreCase) ? "Partial save — open Help." : "Rejected — open Help."
                : text.Contains("warning:", StringComparison.OrdinalIgnoreCase) ? "Warning — open Help." : "Live edits; Save is separate.";
        summary = Encoding.UTF8.GetBytes(Summary);
        var safe = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                safe.Append(c).Append(text[++i]);
            else safe.Append(c == '\0' ? '\u2400' : char.IsSurrogate(c) || char.IsControl(c) && c is not ('\n' or '\r' or '\t') ? '\uFFFD' : c);
        }
        Details = safe.ToString();
        // Native InputText expects its bounded buffer to include a trailing NUL; content itself is NUL-free.
        details = new byte[checked(Encoding.UTF8.GetByteCount(Details) + 1)];
        Encoding.UTF8.GetBytes(Details, details);
    }

    public void DrawDetails(ReadOnlySpan<byte> id)
    {
        float height = Math.Max(Ui.GetFrameHeight() * 2,
            Math.Min(Ui.GetFrameHeight() * 6, Ui.GetContentRegionAvail().Y / 2));
        Ui.InputTextMultiline(id, details, new float2(-1, height), ImGuiInputTextFlags.ReadOnly,
            (ImGuiInputTextCallback?)null, default);
    }
}
