using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using CrushKit.Brutal.ImGui;
using Ui = Brutal.ImGuiApi.ImGui;

namespace BrutalBreakout.Editor;

/// <summary>Native scalar widgets over authoritative JSON. No I/O, validation or resource changes occur in Draw.</summary>
public sealed class ConfigurationControls
{
    private static readonly string[] RenderingPaths = ["sharedMeshes", "cpuBuiltTriangles"];
    private static readonly string[] CircleModes = ["automatic", "fixedSideCount"];
    private static readonly string[] ShmupGroups = ["ball", "avatar", "brick", "difficulty", "noise", "arena", "capacity"];
    private static readonly (ToolCategory Category, string[] Sections)[] CategoryDefinitions =
    [
        (new("shmup", "Shmup"), ["shmup"]),
        (new("breakout", "Breakout"), ["gameplay"]),
        (new("rendering", "Rendering"), ["rendering"]),
        (new("appearance", "Appearance"), ["colors", "style", "trails"]),
        (new("audio", "Audio"), ["audio"]),
        (new("input", "Input"), ["input"]),
        (new("physics", "Physics & workers"), ["physics", "execution"]),
        (new("window", "Window"), ["window"]),
        (new("tools", "HUD & tools"), ["overlay", "inspector"])
    ];
    private readonly ToolCategory[] categories;
    private readonly ConfigurationWorkspace.Document[][] categoryDocuments;
    private int selectedCategory;
    public string? SelectedCategory => categories.Length == 0 ? null : categories[selectedCategory].Id;
    internal bool CategorySidebarVisible { get; private set; }
    internal float BodyScrollMax { get; private set; }
    internal float BodyScrollY { get; private set; }
    internal float2 BodyMinimum { get; private set; }
    internal float2 BodyMaximum { get; private set; }
    // Native item evidence for short-viewport reachability regressions, never retained as ImGui objects.
    internal (float2 Minimum, float2 Maximum, bool Visible) ResetActionBounds { get; private set; }
    internal (float2 Minimum, float2 Maximum, bool Visible) StatusBounds { get; private set; }
    internal (float2 Minimum, float2 Maximum, bool Visible) ResetNoticeBounds { get; private set; }
    internal (float2 Minimum, float2 Maximum, bool Visible) RelaunchNoticeBounds { get; private set; }
    internal (float2 Minimum, float2 Maximum, bool Visible) RuntimeDiagnosticBounds { get; private set; }
    internal float2 NavigationOrigin { get; private set; }
    public void SelectCategory(string id)
    {
        int index = Array.FindIndex(categories, category => category.Id == id);
        if (index < 0) throw new ArgumentException("Unknown or unavailable configuration category.", nameof(id));
        selectedCategory = index;
    }
    public ConfigurationWorkspace Workspace { get; }
    public bool Visible { get; set; }
    public int DrawCount { get; private set; }
    public int ScalarDrawCount { get; private set; }
    public (string Path, float2 Minimum, float2 Maximum)? FirstVisibleField { get; private set; }
    private float2 position, size, lastScale;
    // Synchronous widgets consume this owner-held literal-key stack before traversal pops it.
    private readonly List<string> editKeys = [];
    private readonly Dictionary<string, bool> groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> widgetErrors = new(StringComparer.Ordinal);
    public ConfigurationControls(ConfigurationWorkspace workspace, GameplayWindowSettings settings)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        position = new(settings.Left, settings.Top); size = new(settings.Width, settings.Height);
        var available = CategoryDefinitions.Select(definition => (definition.Category,
            Documents: definition.Sections.SelectMany(section => workspace.Documents.Where(d => d.Section == section)).ToArray()))
            .Where(definition => definition.Documents.Length > 0).ToArray();
        categories = available.Select(definition => definition.Category).ToArray();
        categoryDocuments = available.Select(definition => definition.Documents).ToArray();
        // The primary Shmup tuning surface starts at its ball controls; other groups remain optional.
        groups["shmup.ball"] = groups["gameplay.ball"] = true;
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
        // The body normally owns scrolling, but a wrapped header/navigation can exceed a short display.
        // Keep native shell overflow scrolling available instead of stranding its actions or critical notices.
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
            DrawActionsAndStatus();
            if (categories.Length == 0) { Ui.TextWrapped("No configuration sources selected."u8); return; }
            NavigationOrigin = Ui.GetCursorScreenPos();
            using var body = ToolLayout.Categories("configuration.categories"u8, categories, ref selectedCategory, Ui.GetFontSize() * 18);
            CategorySidebarVisible = body.IsWide;
            if (!body.ContentVisible) return;
            Ui.PushID(Bytes(categories[selectedCategory].Id));
            try
            {
                // The selected settings body scrolls independently; the shell scrolls only on height overflow.
                bool childVisible = Ui.BeginChild("##settings.body"u8, new float2(0), ImGuiChildFlags.None, ImGuiWindowFlags.None);
                try
                {
                    BodyMinimum = Ui.GetWindowPos(); BodyMaximum = BodyMinimum + Ui.GetWindowSize();
                    if (childVisible)
                    {
                        if (groups.GetValueOrDefault("$help")) DrawPolicyHelp();
                        foreach (var document in categoryDocuments[selectedCategory])
                        {
                            if (categoryDocuments[selectedCategory].Length > 1) ToolLayout.Heading(Bytes(DocumentTitle(document.Section)));
                            Ui.TextWrapped(Bytes((document.Unsaved ? "Unsaved changes" : "Source saved") + " | " +
                                (document.Pending ? "Pending fields" : "Applied")));
                            if (Group(document.Section + ".$source", "Source path")) Ui.TextWrapped(Bytes(document.Path));
                            editKeys.Clear();
                            DrawDocument(document);
                        }
                    }
                    BodyScrollMax = Ui.GetScrollMaxY(); BodyScrollY = Ui.GetScrollY();
                }
                finally { Ui.EndChild(); }
            }
            finally { Ui.PopID(); }
        }
        finally { Ui.End(); }
    }
    private static (float2 Minimum, float2 Maximum, bool Visible) CurrentItemBounds() =>
        (Ui.GetItemRectMin(), Ui.GetItemRectMax(), Ui.IsItemVisible());
    private void DrawActionsAndStatus()
    {
        ResetActionBounds = ResetNoticeBounds = RelaunchNoticeBounds = RuntimeDiagnosticBounds = default;
        if (Ui.Button("Save changed"u8)) Workspace.Request(ConfigurationAction.Save);
        Ui.SetItemTooltip("Save validates all selected sources. It never resets or applies structural fields."u8);
        ToolLayout.SameLineIfFits("Discard"u8);
        if (Ui.Button("Discard"u8)) Workspace.Request(ConfigurationAction.Discard);
        ToolLayout.SameLineIfFits("Reload"u8);
        if (Ui.Button("Reload"u8)) Workspace.Request(ConfigurationAction.Reload);
        Ui.SetItemTooltip("Reload discards drafts and resolves external source conflicts."u8);
        if (Workspace.PendingResetFields.Count > 0)
        {
            ToolLayout.SameLineIfFits("Reset & apply"u8);
            Ui.BeginDisabled(!Workspace.RuntimeAvailable);
            try
            {
                if (Ui.Button("Reset & apply"u8)) Workspace.Request(ConfigurationAction.Apply);
                ResetActionBounds = CurrentItemBounds();
            }
            finally { Ui.EndDisabled(); }
            Ui.SetItemTooltip("Explicit structural reset; keeps mode/pause. Does not save or apply relaunch fields."u8);
        }
        ToolLayout.SameLineIfFits("Help"u8);
        if (Ui.Button("Help"u8)) groups["$help"] = !groups.GetValueOrDefault("$help");
        bool rejected = Workspace.Status.StartsWith("Rejected:", StringComparison.Ordinal);
        Ui.TextWrapped(rejected ? "Rejected operation. Open Help for details."u8
            : Workspace.Status.Contains("warning:", StringComparison.OrdinalIgnoreCase)
                ? "Source cleanup warning. Open Help for details."u8 : "Live edits; Save is separate."u8);
        StatusBounds = CurrentItemBounds();
        if (!Workspace.RuntimeAvailable) Ui.TextWrapped("Live tuning unavailable in this mode."u8);
        if (Workspace.PendingResetFields.Count > 0)
        {
            Ui.TextWrapped(Bytes($"Reset required: {Workspace.PendingResetFields.Count} field(s)."));
            ResetNoticeBounds = CurrentItemBounds();
        }
        if (Workspace.PendingRelaunchFields.Count > 0)
        {
            Ui.TextWrapped(Bytes($"Relaunch required: {Workspace.PendingRelaunchFields.Count} field(s)."));
            RelaunchNoticeBounds = CurrentItemBounds();
        }
        if (Workspace.RuntimeDiagnostic is { } diagnostic)
        {
            Ui.TextWrapped(Bytes(diagnostic));
            RuntimeDiagnosticBounds = CurrentItemBounds();
        }
        Ui.Separator();
    }
    private void DrawPolicyHelp()
    {
        ToolLayout.Heading("Help / pending reasons"u8);
        Ui.TextWrapped(Bytes(Workspace.Status));
        Ui.TextWrapped("LIVE: next owner update, even paused. FUTURE: new spawns/reset defaults. RESET: explicit structural rebuild. RELAUNCH: save then restart the app. Save never resets. Reload/Discard restore source drafts. Generic arrays keep their length."u8);
        foreach (var field in Workspace.PendingResetFields) Ui.TextWrapped(Bytes(field));
        foreach (var field in Workspace.PendingRelaunchFields) Ui.TextWrapped(Bytes(field));
    }
    private static string DocumentTitle(string section) => section switch
    {
        "colors" => "Palette & roles", "style" => "Shape styling", "trails" => "Ball trails",
        "physics" => "Physics clock & solver", "execution" => "Worker budgets",
        "overlay" => "Game HUD", "inspector" => "Inspector tools", _ => Label(section)
    };
    private void DrawDocument(ConfigurationWorkspace.Document document)
    {
        var obj = document.Draft;
        if (document.Section == "shmup")
        {
            // Prioritize live tuning over construction-only geometry; no extra document disclosure layer.
            foreach (string key in ShmupGroups)
                if (obj.TryGetProperty(key, out var value)) DrawProperty(document, key, value, document.Section);
            foreach (var property in obj.EnumerateObject())
                if (!ShmupGroups.Contains(property.Name)) DrawProperty(document, property.Name, property.Value, document.Section);
        }
        else DrawObject(document, obj, document.Section);
    }
    internal static bool EditValue(ConfigurationWorkspace.Document document, IReadOnlyList<string> keys, JsonNode? value) =>
        document.Set(keys, value);
    private void DrawObject(ConfigurationWorkspace.Document document, JsonElement obj, string path)
    {
        foreach (var property in obj.EnumerateObject()) DrawProperty(document, property.Name, property.Value, path);
    }
    private void DrawProperty(ConfigurationWorkspace.Document document, string key, JsonElement node, string path)
    {
        string id = path + "." + key;
        if (id == "rendering.circleDetail.fixedSides" &&
            document.Draft.GetProperty("circleDetail"u8).GetProperty("mode"u8).GetString() != "fixedSideCount") return;
        editKeys.Add(key);
        try
        {
            if (id == "rendering.circleDetail.mode")
            {
                string selected = node.GetString()!;
                using var row = ToolLayout.PropertyRow(Bytes(id), "Circle detail [LIVE]"u8, Ui.GetFontSize() * 7,
                    Bytes(ConfigurationImpact.For(id).Reason));
                if (!row.ContentVisible) return;
                if (Ui.BeginCombo(Bytes("##" + id), Bytes(selected == "automatic" ? "Automatic" : "Fixed side count"), ImGuiComboFlags.None))
                {
                    try
                    {
                        foreach (string value in CircleModes)
                            if (Ui.Selectable(Bytes(value == "automatic" ? "Automatic" : "Fixed side count"), value == selected, ImGuiSelectableFlags.None, default))
                                EditValue(document, editKeys, JsonValue.Create(value));
                    }
                    finally { Ui.EndCombo(); }
                }
                ScalarDrawCount++; RecordField(id);
            }
            else if (node.ValueKind == JsonValueKind.Object)
            {
                if (Group(id, Label(key))) { Ui.Indent(); try { DrawObject(document, node, id); } finally { Ui.Unindent(); } }
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                if (Group(id, Label(key))) DrawArray(document, node, id, key);
            }
            else if (id == "rendering.path" && node.ValueKind == JsonValueKind.String)
            {
                string selected = node.GetString()!;
                using var row = ToolLayout.PropertyRow(Bytes(id), "Rendering path [LIVE]"u8, Ui.GetFontSize() * 7,
                    Bytes(ConfigurationImpact.For(id).Reason));
                if (!row.ContentVisible) return;
                static string Name(string value) => value == "sharedMeshes" ? "Shared meshes" : value == "cpuBuiltTriangles" ? "CPU-built triangles" : value;
                if (Ui.BeginCombo(Bytes("##" + id), Bytes(Name(selected)), ImGuiComboFlags.None))
                {
                    try
                    {
                        foreach (string value in RenderingPaths)
                            if (Ui.Selectable(Bytes(Name(value)), value == selected, ImGuiSelectableFlags.None, default))
                                EditValue(document, editKeys, JsonValue.Create(value));
                    }
                    finally { Ui.EndCombo(); }
                }
                ScalarDrawCount++; RecordField(id);
            }
            else if (path == "colors.roles" && node.ValueKind == JsonValueKind.String)
            {
                string selected = node.GetString()!;
                using var row = ToolLayout.PropertyRow(Bytes(id), Bytes(Label(key) + " [LIVE]"), Ui.GetFontSize() * 7,
                    Bytes(ConfigurationImpact.For(id).Reason));
                if (!row.ContentVisible) return;
                if (Ui.BeginCombo(Bytes("##" + id), Bytes(selected), ImGuiComboFlags.None))
                {
                    try
                    {
                        var palette = document.Draft.GetProperty("palette"u8);
                        foreach (var entry in palette.EnumerateObject())
                            if (Ui.Selectable(Bytes(entry.Name), entry.Name == selected, ImGuiSelectableFlags.None, default))
                                EditValue(document, editKeys, JsonValue.Create(entry.Name));
                    }
                    finally { Ui.EndCombo(); }
                }
                ScalarDrawCount++; RecordField(id);
            }
            else DrawValue(node, id, Label(key), value => EditValue(document, editKeys, value));
        }
        finally { editKeys.RemoveAt(editKeys.Count - 1); }
    }
    private void DrawArray(ConfigurationWorkspace.Document document, JsonElement array, string path, string key)
    {
        var impact = ConfigurationImpact.For(path);
        Ui.TextWrapped(Bytes(impact.Effect.ToString().ToUpperInvariant()));
        Ui.SetItemTooltip(Bytes(impact.Reason));
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
                    EditValue(document, editKeys, replacement);
                }
            }
            return;
        }
        for (int i = 0; i < array.GetArrayLength(); i++)
        {
            string index = i.ToString(CultureInfo.InvariantCulture), id = path + "." + index;
            editKeys.Add(index);
            try
            {
                if (array[i].ValueKind == JsonValueKind.Object)
                {
                    if (Group(id, $"Item {i + 1}")) DrawObject(document, array[i], id);
                }
                else DrawValue(array[i], id, $"Item {i + 1}", value => EditValue(document, editKeys, value));
            }
            finally { editKeys.RemoveAt(editKeys.Count - 1); }
        }
        Ui.TextWrapped("Array entries are edited in place; list length is preserved."u8);
    }
    private void DrawValue(JsonElement value, string id, string label, Action<JsonNode> set)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return;
        ScalarDrawCount++;
        var impact = ConfigurationImpact.For(id);
        using var row = ToolLayout.PropertyRow(Bytes(id), Bytes(label + Unit(id) + " [" + impact.Effect.ToString().ToUpperInvariant() + "]"),
            Ui.GetFontSize() * 7, Bytes(impact.Reason));
        if (!row.ContentVisible) return;
        var widget = Bytes("##" + id);
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
                    id is "window.width" or "window.height" or "window.frameRateLimit" ? ImGuiInputTextFlags.EnterReturnsTrue : ImGuiInputTextFlags.None))
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
    private static string Unit(string path) => path.AsSpan(path.LastIndexOf('.') + 1) switch
    {
        "speed" or "moveSpeed" or "descentSpeed" or "maximumDescentSpeed" => " (units/s)",
        "radius" or "size" or "columnPitch" or "width" or "height" when !path.StartsWith("window.", StringComparison.Ordinal) && !path.StartsWith("inspector.", StringComparison.Ordinal) => " (units)",
        "invulnerabilitySeconds" or "rampSeconds" or "rowInterval" or "minimumRowInterval" => " (s)",
        "fireRate" => " (shots/s)", "updateHz" => " (Hz)", _ => ""
    };
    private static string Label(string key)
    {
        if (key == "vsync") return "VSync";
        if (key == "frameRateLimit") return "Frame rate cap (0 unlimited)";
        var result = new StringBuilder();
        foreach (char c in key) { if (char.IsUpper(c)) result.Append(' '); result.Append(c); }
        if (result.Length > 0) result[0] = char.ToUpperInvariant(result[0]);
        return result.ToString();
    }
}
