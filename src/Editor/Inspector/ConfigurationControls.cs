using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Ui = Brutal.ImGuiApi.ImGui;

namespace BrutalBreakout.Editor;

/// <summary>Native scalar widgets over authoritative JSON. No I/O, validation or resource changes occur in Draw.</summary>
public sealed class ConfigurationControls
{
    public ConfigurationWorkspace Workspace { get; }
    public bool Visible { get; set; }
    public int DrawCount { get; private set; }
    public int ScalarDrawCount { get; private set; }
    public (string Path, float2 Minimum, float2 Maximum)? FirstVisibleField { get; private set; }
    private float2 position, size, lastScale;
    private readonly Dictionary<string, bool> groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> widgetErrors = new(StringComparer.Ordinal);
    public ConfigurationControls(ConfigurationWorkspace workspace, GameplayWindowSettings settings)
    {
        Workspace = workspace;
        position = new(settings.Left, settings.Top); size = new(settings.Width, settings.Height);
    }
    public void SetGroupOpen(string path, bool open) => groups[path] = open;
    private bool Group(string key, string label)
    {
        Ui.SetNextItemOpen(groups.GetValueOrDefault(key), ImGuiCond.Always);
        bool open = Ui.CollapsingHeader(Bytes(label + "###" + key));
        groups[key] = open;
        return open;
    }
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    public void Draw()
    {
        if (!Visible) return;
        DrawCount++;
        FirstVisibleField = null;
        var scale = Ui.GetIO().DisplayFramebufferScale;
        var viewport = Ui.GetMainViewport();
        var condition = lastScale != default && scale != lastScale ? ImGuiCond.Always : ImGuiCond.Once;
        lastScale = scale;
        Ui.SetNextWindowSizeConstraints(new float2(0), viewport.Size);
        Ui.SetNextWindowPos(viewport.Pos + position / scale, condition);
        Ui.SetNextWindowSize(size / scale, condition);
        bool visible = Visible;
        bool expanded = Ui.Begin("Configuration"u8, ref visible, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse);
        Visible = visible;
        try
        {
            var nativeSize = Ui.GetWindowSize(); var nativePosition = Ui.GetWindowPos();
            var clampedSize = new float2(Math.Min(nativeSize.X, viewport.Size.X), Math.Min(nativeSize.Y, viewport.Size.Y));
            var clampedPosition = new float2(Math.Clamp(nativePosition.X, viewport.Pos.X, viewport.Pos.X + Math.Max(0, viewport.Size.X - clampedSize.X)),
                Math.Clamp(nativePosition.Y, viewport.Pos.Y, viewport.Pos.Y + Math.Max(0, viewport.Size.Y - clampedSize.Y)));
            if (nativeSize != clampedSize) Ui.SetWindowSize(clampedSize);
            if (nativePosition != clampedPosition) Ui.SetWindowPos(clampedPosition);
            position = (clampedPosition - viewport.Pos) * scale; size = clampedSize * scale;
            if (!expanded) return;
            if (Workspace.PendingResetFields.Count > 0)
            {
                Ui.BeginDisabled(!Workspace.RuntimeAvailable);
                try { if (Ui.Button("Reset & apply pending"u8)) Workspace.Request(ConfigurationAction.Apply); }
                finally { Ui.EndDisabled(); }
                Ui.SetItemTooltip("Only structural changes need reset. Mode/pause retained; relaunch fields excluded. Does not save."u8);
                if (Group("$reset", "Pending reset fields / reasons"))
                    foreach (var field in Workspace.PendingResetFields) Ui.TextWrapped(Bytes(field));
            }
            if (!Workspace.RuntimeAvailable) Ui.TextWrapped("Live tuning unavailable in showcase/sandbox modes."u8);
            if (Workspace.PendingRelaunchFields.Count > 0 && Group("$relaunch", "Pending relaunch fields / reasons"))
                foreach (var field in Workspace.PendingRelaunchFields) Ui.TextWrapped(Bytes(field));
            GameplayControls.SameLineIfFits("Save changed"u8);
            if (Ui.Button("Save changed"u8)) Workspace.Request(ConfigurationAction.Save);
            Ui.SetItemTooltip("Explicitly save changed selected JSON sources. Never resets or applies structural fields. All files are validated together."u8);
            GameplayControls.SameLineIfFits("Discard"u8);
            if (Ui.Button("Discard"u8)) Workspace.Request(ConfigurationAction.Discard);
            GameplayControls.SameLineIfFits("Reload"u8);
            if (Ui.Button("Reload"u8)) Workspace.Request(ConfigurationAction.Reload);
            Ui.SetItemTooltip("Reload from disk discards drafts and resolves external file conflicts."u8);
            bool rejected = Workspace.Status.StartsWith("Rejected:", StringComparison.Ordinal);
            Ui.TextWrapped(Bytes(rejected ? Workspace.Status : Workspace.Status.StartsWith("Runtime applied", StringComparison.Ordinal)
                ? "Runtime applied; source saves are separate." : Workspace.Status.StartsWith("Saved", StringComparison.Ordinal)
                ? Workspace.Status.Contains("Temporary cleanup warning:", StringComparison.Ordinal)
                    ? "Sources saved with cleanup warning; open status details."
                    : "Sources saved; pending reset / relaunch is separate."
                : "Valid live edits apply automatically; Save is explicit."));
            if (Group("$help", "Help / status details"))
            {
                Ui.TextWrapped(Bytes(Workspace.Status));
                Ui.TextWrapped("Geometry: game units; speed: units/sec; duration: seconds; angles: quarter turns. LIVE applies at the next owner update, even paused. FUTURE changes new-spawn/next-reset defaults only. RESET rebuilds structural worlds only on explicit request. RELAUNCH rebuilds font/render resources after saving. Invalid live edits keep last valid tuning. Reload/Discard stage source and valid live values take effect automatically. Save never resets. Generic arrays edit existing entries only."u8);
            }
            if (Workspace.RuntimeDiagnostic is { } diagnostic) Ui.TextWrapped(Bytes(diagnostic));
            string[] order = ["shmup", "gameplay", "colors", "trails", "style", "audio", "physics", "execution", "input", "window", "overlay", "inspector"];
            foreach (var document in Workspace.Documents.OrderBy(d => Array.IndexOf(order, d.Section)))
            {
                string state = (document.Unsaved ? "unsaved" : "saved") + " | " +
                    (document.Pending ? "pending fields" : "applied");
                if (!Group(document.Section, document.Section.ToUpperInvariant() + " [" + state + "]")) continue;
                if (Group(document.Section + ".$source", "Selected source path")) Ui.TextWrapped(Bytes(document.Path));
                Ui.TextWrapped("Per-field effects below; saved/source state is independent of runtime."u8);
                DrawObject(document, document.Draft, document.Section);
            }
        }
        finally { Ui.End(); }
    }
    private void Set(ConfigurationWorkspace.Document document, string path, JsonNode? value) =>
        document.Set(path[(document.Section.Length + 1)..], value);
    private void DrawObject(ConfigurationWorkspace.Document document, JsonElement obj, string path)
    {
        foreach (var property in obj.EnumerateObject())
        {
            string key = property.Name, id = path + "." + key;
            var node = property.Value;
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (Group(id, Label(key))) { Ui.Indent(); try { DrawObject(document, node, id); } finally { Ui.Unindent(); } }
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                if (Group(id, Label(key))) DrawArray(document, node, id, key);
            }
            else if (path == "colors.roles" && node.ValueKind == JsonValueKind.String)
            {
                string selected = node.GetString()!;
                Ui.TextWrapped(Bytes(Label(key) + " [LIVE]"));
                Ui.SetItemTooltip(Bytes(ConfigurationImpact.For(id).Reason));
                Ui.SetNextItemWidth(-1);
                if (Ui.BeginCombo(Bytes("##" + id), Bytes(selected), ImGuiComboFlags.None))
                {
                    try
                    {
                        var palette = document.Draft.GetProperty("palette"u8);
                        foreach (var entry in palette.EnumerateObject())
                            if (Ui.Selectable(Bytes(entry.Name), entry.Name == selected, ImGuiSelectableFlags.None, default))
                                Set(document, id, JsonValue.Create(entry.Name));
                    }
                    finally { Ui.EndCombo(); }
                }
            }
            else DrawValue(node, id, Label(key), value => Set(document, id, value));
        }
    }
    private void DrawArray(ConfigurationWorkspace.Document document, JsonElement array, string path, string key)
    {
        var impact = ConfigurationImpact.For(path);
        Ui.TextWrapped(Bytes(impact.Effect.ToString().ToUpperInvariant() + ": " + impact.Reason));
        if (key is "shapes" or "quarterTurns")
        {
            string[] options = key == "shapes" ? ["square", "2x1", "2x2", "l", "t"] : ["0", "1", "2", "3"];
            foreach (var option in options)
            {
                bool selected = array.EnumerateArray().Any(n => n.ToString() == option);
                if (Ui.Checkbox(Bytes(option + "##" + path + "." + option), ref selected))
                {
                    // Re-read after an earlier checkbox edit in this draw; the traversal view is immutable.
                    var current = document.Draft.GetProperty("brick"u8).GetProperty(key);
                    var values = current.EnumerateArray().Select(n => n.ToString()).ToHashSet(StringComparer.Ordinal);
                    if (selected) values.Add(option); else values.Remove(option);
                    var replacement = new JsonArray();
                    foreach (var value in options.Where(values.Contains))
                        replacement.Add(key == "shapes" ? JsonValue.Create(value) : JsonValue.Create(int.Parse(value, CultureInfo.InvariantCulture)));
                    Set(document, path, replacement);
                }
            }
            return;
        }
        for (int i = 0; i < array.GetArrayLength(); i++)
        {
            string id = path + "." + i;
            if (array[i].ValueKind == JsonValueKind.Object)
            {
                if (Group(id, $"Item {i + 1}")) DrawObject(document, array[i], id);
            }
            else DrawValue(array[i], id, $"Item {i + 1}", value => Set(document, id, value));
        }
        Ui.TextWrapped("Array entries are edited in place; list length is preserved."u8);
    }
    private void DrawValue(JsonElement value, string id, string label, Action<JsonNode> set)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return;
        ScalarDrawCount++;
        var impact = ConfigurationImpact.For(id);
        Ui.TextWrapped(Bytes(label + " [" + impact.Effect.ToString().ToUpperInvariant() + "]"));
        Ui.SetItemTooltip(Bytes(impact.Reason));
        var widget = Bytes("##" + id);
        Ui.SetNextItemWidth(-1);
        switch (value.ValueKind)
        {
            case System.Text.Json.JsonValueKind.True:
            case System.Text.Json.JsonValueKind.False:
                bool boolean = value.GetBoolean();
                if (Ui.Checkbox(widget, ref boolean)) set(JsonValue.Create(boolean)!);
                break;
            case System.Text.Json.JsonValueKind.Number:
                // Never infer integral schema types from lexical JSON values (e.g. speed: 576).
                double number = value.GetDouble();
                if (Ui.InputDouble(widget, ref number, 0, 0, "%.8g"u8,
                    id is "window.width" or "window.height" ? ImGuiInputTextFlags.EnterReturnsTrue : ImGuiInputTextFlags.None))
                {
                    if (double.IsFinite(number)) { set(JsonValue.Create(number)!); widgetErrors.Remove(id); }
                    else widgetErrors[id] = "Enter a finite number; NaN/Infinity was not staged.";
                }
                RecordField(id);
                if (widgetErrors.TryGetValue(id, out var error)) Ui.TextWrapped(Bytes(error));
                return;
            case System.Text.Json.JsonValueKind.String:
                string text = value.GetString()!;
                if (id.StartsWith("colors.palette.", StringComparison.Ordinal) && text.Length == 7 && text[0] == '#' &&
                    int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                {
                    var color = new float3(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
                    if (Ui.ColorEdit3(widget, ref color, ImGuiColorEditFlags.None))
                    {
                        static int Channel(float c) => (int)MathF.Round(Math.Clamp(c, 0, 1) * 255);
                        set(JsonValue.Create($"#{Channel(color.X):X2}{Channel(color.Y):X2}{Channel(color.Z):X2}")!);
                    }
                }
                else
                {
                    var buffer = new byte[Math.Max(4096, Encoding.UTF8.GetByteCount(text) + 1024)];
                    Encoding.UTF8.GetBytes(text, buffer);
                    if (Ui.InputText(widget, buffer, ImGuiInputTextFlags.None, (ImGuiInputTextCallback?)null, default))
                    {
                        int length = Array.IndexOf(buffer, (byte)0);
                        set(JsonValue.Create(Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length))!);
                    }
                }
                break;
        }
        RecordField(id);
    }
    private void RecordField(string id)
    {
        if (FirstVisibleField is null && Ui.IsItemVisible())
            FirstVisibleField = (id, Ui.GetItemRectMin(), Ui.GetItemRectMax());
    }
    private static string Label(string key)
    {
        var result = new StringBuilder();
        foreach (char c in key) { if (char.IsUpper(c)) result.Append(' '); result.Append(c); }
        if (result.Length > 0) result[0] = char.ToUpperInvariant(result[0]);
        return result.ToString();
    }
}
