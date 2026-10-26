using System.Globalization;
using System.Text;
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
            Ui.BeginDisabled(!Workspace.RuntimeAvailable);
            try { if (Ui.Button("Apply & restart"u8)) Workspace.Request(ConfigurationAction.Apply); }
            finally { Ui.EndDisabled(); }
            if (!Workspace.RuntimeAvailable) Ui.TextWrapped("Apply unavailable in showcase/sandbox modes."u8);
            Ui.SetItemTooltip("Apply runtime drafts and restart, keeping mode/pause. Does not save or apply relaunch-only settings."u8);
            GameplayControls.SameLineIfFits("Save changed"u8);
            if (Ui.Button("Save changed"u8)) Workspace.Request(ConfigurationAction.Save);
            Ui.SetItemTooltip("Explicitly save changed selected JSON sources. Does not apply. All files are validated together."u8);
            GameplayControls.SameLineIfFits("Discard"u8);
            if (Ui.Button("Discard"u8)) Workspace.Request(ConfigurationAction.Discard);
            GameplayControls.SameLineIfFits("Reload"u8);
            if (Ui.Button("Reload"u8)) Workspace.Request(ConfigurationAction.Reload);
            Ui.SetItemTooltip("Reload from disk discards drafts and resolves external file conflicts."u8);
            bool rejected = Workspace.Status.StartsWith("Rejected:", StringComparison.Ordinal);
            Ui.TextWrapped(Bytes(rejected ? Workspace.Status : Workspace.Status.StartsWith("Runtime applied", StringComparison.Ordinal)
                ? "Runtime applied; source saves are separate." : Workspace.Status.StartsWith("Saved", StringComparison.Ordinal)
                ? "Sources saved; Apply / relaunch is separate." : "Drafts staged; Apply and Save are separate."));
            if (Group("$help", "Help / status details"))
            {
                Ui.TextWrapped(Bytes(Workspace.Status));
                Ui.TextWrapped("Geometry uses game units, speed units/sec, durations seconds, angles quarter turns. Integer fields reject fractions. Apply restarts the run without saving. Save writes all changed selected sources after cross-validation. Reload discards drafts and resolves external changes. Renderer/platform settings require Save and app relaunch. Generic arrays edit existing entries only."u8);
            }
            if (Workspace.RuntimeDiagnostic is { } diagnostic) Ui.TextWrapped(Bytes(diagnostic));
            string[] order = ["shmup", "gameplay", "colors", "trails", "style", "audio", "physics", "execution", "input", "window", "overlay", "inspector"];
            foreach (var document in Workspace.Documents.OrderBy(d => Array.IndexOf(order, d.Section)))
            {
                string state = (document.Unsaved ? "unsaved" : "saved") + " | " +
                    (document.Pending ? document.Runtime ? "pending Apply" : "pending relaunch" : "applied");
                if (!Group(document.Section, document.Section.ToUpperInvariant() + " [" + state + "]")) continue;
                if (Group(document.Section + ".$source", "Selected source path")) Ui.TextWrapped(Bytes(document.Path));
                Ui.TextWrapped(document.Runtime ? "Runtime — Apply & restart"u8 : "Save & app relaunch only (not applied here)"u8);
                DrawObject(document.Draft, document.Section);
            }
        }
        finally { Ui.End(); }
    }
    private void DrawObject(JsonObject obj, string path)
    {
        foreach (var key in obj.Select(p => p.Key).ToArray())
        {
            string id = path + "." + key;
            var node = obj[key];
            if (node is JsonObject child)
            {
                if (Group(id, Label(key))) { Ui.Indent(); try { DrawObject(child, id); } finally { Ui.Unindent(); } }
            }
            else if (node is JsonArray array)
            {
                if (Group(id, Label(key))) DrawArray(array, id, key);
            }
            else if (path == "colors.roles" && node is JsonValue role && role.TryGetValue<string>(out var selected))
            {
                Ui.TextWrapped(Bytes(Label(key)));
                Ui.SetNextItemWidth(-1);
                if (Ui.BeginCombo(Bytes("##" + id), Bytes(selected), ImGuiComboFlags.None))
                {
                    try
                    {
                        var palette = Workspace.Documents.First(d => d.Section == "colors").Draft["palette"]!.AsObject();
                        foreach (var name in palette.Select(p => p.Key))
                            if (Ui.Selectable(Bytes(name), name == selected, ImGuiSelectableFlags.None, default)) obj[key] = name;
                    }
                    finally { Ui.EndCombo(); }
                }
            }
            else DrawValue(node, id, Label(key), value => obj[key] = value);
        }
    }
    private void DrawArray(JsonArray array, string path, string key)
    {
        if (key is "shapes" or "quarterTurns")
        {
            string[] options = key == "shapes" ? ["square", "2x1", "2x2", "l", "t"] : ["0", "1", "2", "3"];
            foreach (var option in options)
            {
                bool selected = array.Any(n => n!.ToString() == option);
                if (Ui.Checkbox(Bytes(option + "##" + path + "." + option), ref selected))
                {
                    var values = array.Select(n => n!.ToString()).ToHashSet(StringComparer.Ordinal);
                    if (selected) values.Add(option); else values.Remove(option);
                    array.Clear();
                    foreach (var value in options.Where(values.Contains))
                        array.Add(key == "shapes" ? JsonValue.Create(value) : JsonValue.Create(int.Parse(value, CultureInfo.InvariantCulture)));
                }
            }
            return;
        }
        for (int i = 0; i < array.Count; i++)
        {
            int index = i; string id = path + "." + index;
            if (array[i] is JsonObject child)
            {
                if (Group(id, $"Item {i + 1}")) DrawObject(child, id);
            }
            else DrawValue(array[i], id, $"Item {i + 1}", value => array[index] = value);
        }
        Ui.TextWrapped("Array entries are edited in place; list length is preserved."u8);
    }
    private void DrawValue(JsonNode? node, string id, string label, Action<JsonNode> set)
    {
        if (node is not JsonValue value) return;
        ScalarDrawCount++;
        Ui.TextWrapped(Bytes(label));
        var widget = Bytes("##" + id);
        Ui.SetNextItemWidth(-1);
        switch (value.GetValueKind())
        {
            case System.Text.Json.JsonValueKind.True:
            case System.Text.Json.JsonValueKind.False:
                bool boolean = value.GetValue<bool>();
                if (Ui.Checkbox(widget, ref boolean)) set(JsonValue.Create(boolean)!);
                break;
            case System.Text.Json.JsonValueKind.Number:
                // Never infer integral schema types from lexical JSON values (e.g. speed: 576).
                double number = value.GetValue<double>();
                if (Ui.InputDouble(widget, ref number, 0, 0, "%.8g"u8, ImGuiInputTextFlags.None))
                {
                    if (double.IsFinite(number)) { set(JsonValue.Create(number)!); widgetErrors.Remove(id); }
                    else widgetErrors[id] = "Enter a finite number; NaN/Infinity was not staged.";
                }
                RecordField(id);
                if (widgetErrors.TryGetValue(id, out var error)) Ui.TextWrapped(Bytes(error));
                return;
            case System.Text.Json.JsonValueKind.String:
                string text = value.GetValue<string>();
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
