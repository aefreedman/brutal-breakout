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
    internal (float2 Minimum, float2 Maximum, bool Visible) StatusDetailsBounds { get; private set; }
    internal (float2 Minimum, float2 Maximum, bool Visible) RuntimeDetailsBounds { get; private set; }
    internal ConfigurationDiagnosticPresentation? StatusPresentation { get; private set; }
    internal ConfigurationDiagnosticPresentation? RuntimePresentation { get; private set; }
    private string? presentedStatus, presentedRuntime;
    private void RefreshDiagnostics()
    {
        if (!string.Equals(presentedStatus, Workspace.Status, StringComparison.Ordinal))
        { presentedStatus = Workspace.Status; StatusPresentation = new(presentedStatus); }
        if (!string.Equals(presentedRuntime, Workspace.RuntimeDiagnostic, StringComparison.Ordinal))
        {
            presentedRuntime = Workspace.RuntimeDiagnostic;
            RuntimePresentation = presentedRuntime is null ? null : new(presentedRuntime, runtime: true);
        }
    }
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
    private readonly Dictionary<string, ConfigurationFieldPresentation> presentation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> groupLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<ConfigurationWorkspace.Document, long> sourceRevisions = [];
    private string? pendingClipboard;
    public InspectorTheme? Theme { get; }
    public ConfigurationFeedback? Feedback { get; set; }
    public string? PresentationSummary { get; private set; }
    public void SetPresentationFacts(int frameCap, bool requestedVSync, string? effectiveMode)
    {
        string cap = frameCap == 0 ? "Unlimited (no application cap)" : $"{frameCap} fps";
        string requested = requestedVSync ? "On (FIFO)" : "Off (Immediate; tearing possible)";
        string effective = effectiveMode switch { "FifoKHR" => "FIFO", "ImmediateKHR" => "Immediate", null => "not created yet", _ => effectiveMode };
        bool pending = effectiveMode is not null && effectiveMode != (requestedVSync ? "FifoKHR" : "ImmediateKHR");
        PresentationSummary = $"Frame cap: {cap}. Requested VSync: {requested}. Effective present mode: {effective}{(pending ? " (requested change pending)" : "")}. Unlimited does not disable VSync.";
    }
    internal (string Path, float2 Minimum, float2 Maximum)? FirstVisibleAction { get; private set; }
    public string? DrainClipboardText() { var text = pendingClipboard; pendingClipboard = null; return text; }
    public ConfigurationControls(ConfigurationWorkspace workspace, GameplayWindowSettings settings) : this(workspace, settings, null) { }
    public ConfigurationControls(ConfigurationWorkspace workspace, GameplayWindowSettings settings, InspectorTheme? theme)
    {
        Theme = theme;
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
        if (!groupLabels.TryGetValue(key, out var encoded)) groupLabels.Add(key, encoded = Bytes(label + "###" + key));
        bool open = Ui.CollapsingHeader(encoded);
        groups[key] = open;
        return open;
    }
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    public void Draw()
    {
        if (!Visible) return;
        DrawCount++; RefreshDiagnostics();
        FirstVisibleField = null; FirstVisibleAction = null;
        foreach (var document in Workspace.Documents)
        {
            if (!sourceRevisions.TryGetValue(document, out long revision) || revision != document.SourceRevision)
            { presentation.Clear(); groupLabels.Clear(); sourceRevisions[document] = document.SourceRevision; }
        }
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
                        if (categories[selectedCategory].Id == "window" && PresentationSummary is { } summary) Text(summary, InspectorTone.Secondary);
                        if (Feedback is { Count: > 0 } && Group("$feedback", "Recent actions")) Feedback.Draw();
                        if (groups.GetValueOrDefault("$help")) DrawPolicyHelp();
                        foreach (var document in categoryDocuments[selectedCategory])
                        {
                            if (categoryDocuments[selectedCategory].Length > 1) ToolLayout.Heading(Bytes(DocumentTitle(document.Section)));
                            Text((document.Unsaved ? "Unsaved changes" : "Source saved") + " | " +
                                (document.Pending ? "Pending fields" : "Applied"), document.Pending ? InspectorTone.Warning : InspectorTone.Secondary);
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
        Feedback?.DrawSummary();
        Text(StatusPresentation!.SummaryUtf8, StatusPresentation.Tone);
        StatusBounds = CurrentItemBounds();
        if (!Workspace.RuntimeAvailable) Ui.TextWrapped("Live tuning unavailable in this mode."u8);
        if (Workspace.PendingResetFields.Count > 0)
        {
            Text($"Reset required: {Workspace.PendingResetFields.Count} field(s).", InspectorTone.Warning);
            ResetNoticeBounds = CurrentItemBounds();
        }
        if (Workspace.PendingRelaunchFields.Count > 0)
        {
            Text($"Relaunch required: {Workspace.PendingRelaunchFields.Count} field(s).", InspectorTone.Warning);
            RelaunchNoticeBounds = CurrentItemBounds();
        }
        if (RuntimePresentation is { } runtime)
        {
            Text(runtime.SummaryUtf8, InspectorTone.Warning);
            RuntimeDiagnosticBounds = CurrentItemBounds();
        }
        Ui.Separator();
    }
    private void DrawPolicyHelp()
    {
        ToolLayout.Heading("Help / pending reasons"u8);
        StatusPresentation!.DrawDetails("##persistent.status"u8);
        StatusDetailsBounds = CurrentItemBounds();
        RuntimeDetailsBounds = default;
        if (RuntimePresentation is { } runtime)
        {
            Ui.TextWrapped("Runtime diagnostic"u8);
            runtime.DrawDetails("##persistent.runtime"u8);
            RuntimeDetailsBounds = CurrentItemBounds();
        }
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
                var description = Presentation(id, "Circle detail", humanLabel: true);
                using var row = ToolLayout.PropertyRow(description.Property, Ui.GetFontSize() * 7);
                if (!row.ContentVisible) return;
                ToolFieldActions.ReserveControlWidth();
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
                ScalarDrawCount++; RecordField(id); DrawFieldActions(document, node, description);
            }
            else if (node.ValueKind == JsonValueKind.Object)
            {
                if (Group(id, Presentation(id, key).Property.Label)) { Ui.Indent(); try { DrawObject(document, node, id); } finally { Ui.Unindent(); } }
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                if (Group(id, Presentation(id, key).Property.Label)) DrawArray(document, node, id, key);
            }
            else if (id == "rendering.path" && node.ValueKind == JsonValueKind.String)
            {
                string selected = node.GetString()!;
                var description = Presentation(id, "Rendering path", humanLabel: true);
                using var row = ToolLayout.PropertyRow(description.Property, Ui.GetFontSize() * 7);
                if (!row.ContentVisible) return;
                ToolFieldActions.ReserveControlWidth();
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
                ScalarDrawCount++; RecordField(id); DrawFieldActions(document, node, description);
            }
            else if (path == "colors.roles" && node.ValueKind == JsonValueKind.String)
            {
                string selected = node.GetString()!;
                var description = Presentation(id, key);
                using var row = ToolLayout.PropertyRow(description.Property, Ui.GetFontSize() * 7);
                if (!row.ContentVisible) return;
                ToolFieldActions.ReserveControlWidth();
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
                ScalarDrawCount++; RecordField(id); DrawFieldActions(document, node, description);
            }
            else DrawValue(document, node, id, key);
        }
        finally { editKeys.RemoveAt(editKeys.Count - 1); }
    }
    private void DrawArray(ConfigurationWorkspace.Document document, JsonElement array, string path, string key)
    {
        var impact = ConfigurationImpact.For(path);
        Text(impact.Effect.ToString(), InspectorTone.Secondary);
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
            DrawFieldActions(document, array, Presentation(path, key));
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
                    if (Group(id, Presentation(id, key, arrayIndex: i).Property.Label)) DrawObject(document, array[i], id);
                }
                else DrawValue(document, array[i], id, key, i);
            }
            finally { editKeys.RemoveAt(editKeys.Count - 1); }
        }
        Ui.TextWrapped("Array entries are edited in place; list length is preserved."u8);
        DrawFieldActions(document, array, Presentation(path, key));
    }
    private void DrawValue(ConfigurationWorkspace.Document document, JsonElement value, string id, string key, int arrayIndex = -1)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return;
        ScalarDrawCount++;
        var description = Presentation(id, key, arrayIndex: arrayIndex);
        using var row = ToolLayout.PropertyRow(description.Property, Ui.GetFontSize() * 7);
        if (!row.ContentVisible) return;
        ToolFieldActions.ReserveControlWidth();
        var widget = description.WidgetId;
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
            case JsonValueKind.False:
                bool boolean = value.GetBoolean();
                if (Ui.Checkbox(widget, ref boolean)) EditValue(document, editKeys, JsonValue.Create(boolean));
                break;
            case JsonValueKind.Number:
                var flags = description.CommitOnEnter ? ImGuiInputTextFlags.EnterReturnsTrue : ImGuiInputTextFlags.None;
                if (description.Integer is { } integer && value.TryGetInt64(out long whole))
                {
                    if (ToolControls.EditInt64(widget, ref whole, integer, help: description.Property.Help, inputFlags: flags))
                    { EditValue(document, editKeys, JsonValue.Create(whole)); widgetErrors.Remove(id); }
                }
                else
                {
                    // Nonintegral drafts for integer schema fields stay exact and repairable, never silently cast.
                    double number = value.GetDouble();
                    if (ToolControls.EditDouble(widget, ref number, description.Real, help: description.Property.Help, inputFlags: flags))
                    {
                        if (double.IsFinite(number)) { EditValue(document, editKeys, JsonValue.Create(number)); widgetErrors.Remove(id); }
                        else widgetErrors[id] = "Enter a finite number; NaN/Infinity was not staged.";
                    }
                }
                break;
            case JsonValueKind.String:
                string text = value.GetString()!;
                if (id.StartsWith("colors.palette.", StringComparison.Ordinal) && text.Length == 7 && text[0] == '#' &&
                    int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                {
                    var color = new float3(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
                    if (Ui.ColorEdit3(widget, ref color, ImGuiColorEditFlags.None))
                    {
                        static int Channel(float c) => (int)MathF.Round(Math.Clamp(c, 0, 1) * 255);
                        EditValue(document, editKeys, JsonValue.Create($"#{Channel(color.X):X2}{Channel(color.Y):X2}{Channel(color.Z):X2}"));
                    }
                }
                else
                {
                    var buffer = new byte[Math.Max(4096, Encoding.UTF8.GetByteCount(text) + 1024)];
                    Encoding.UTF8.GetBytes(text, buffer);
                    if (Ui.InputText(widget, buffer, ImGuiInputTextFlags.None, (ImGuiInputTextCallback?)null, default))
                    {
                        int length = Array.IndexOf(buffer, (byte)0);
                        EditValue(document, editKeys, JsonValue.Create(Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length)));
                    }
                }
                break;
        }
        RecordField(id);
        DrawFieldActions(document, value, description);
        if (widgetErrors.TryGetValue(id, out var error)) Text(error, InspectorTone.Error);
    }
    private ConfigurationFieldPresentation Presentation(string id, string key, bool humanLabel = false, int arrayIndex = -1)
    {
        if (!presentation.TryGetValue(id, out var value)) presentation.Add(id,
            value = new(id, humanLabel ? key : arrayIndex >= 0 ? $"Item {arrayIndex + 1}" : Label(key)));
        return value;
    }
    private void DrawFieldActions(ConfigurationWorkspace.Document document, JsonElement value, ConfigurationFieldPresentation description)
    {
        var action = ToolFieldActions.Draw(description.Property.IdUtf8, document.CanRestoreSource(editKeys), true,
            description.Presets, restoreLabel: "Restore from source"u8);
        if (FirstVisibleAction is null && Ui.IsItemVisible())
            FirstVisibleAction = (description.Property.Id, Ui.GetItemRectMin(), Ui.GetItemRectMax());
        ExecuteFieldAction(document, editKeys, value, description, action);
    }
    internal void ExecuteFieldAction(ConfigurationWorkspace.Document document, IReadOnlyList<string> keys, JsonElement value,
        ConfigurationFieldPresentation description, ToolFieldAction action)
    {
        switch (action.Kind)
        {
            case ToolFieldActionKind.Restore: document.RestoreSource(keys); widgetErrors.Remove(description.Property.Id); break;
            case ToolFieldActionKind.Copy: pendingClipboard = value.GetRawText(); break;
            case ToolFieldActionKind.Preset: document.Set(keys, description.PresetValue(action.PresetIndex)); widgetErrors.Remove(description.Property.Id); break;
        }
    }
    private void Text(string message, InspectorTone tone) => Text(Bytes(message), tone);
    private void Text(ReadOnlySpan<byte> message, InspectorTone tone)
    {
        if (Theme is null) Ui.TextWrapped(message); else Theme.TextWrapped(message, tone);
    }
    private void RecordField(string id)
    {
        if (FirstVisibleField is null && Ui.IsItemVisible())
            FirstVisibleField = (id, Ui.GetItemRectMin(), Ui.GetItemRectMax());
    }
    private static string Label(string key)
    {
        if (key == "vsync") return "VSync";
        if (key == "frameRateLimit") return "Frame rate cap";
        var result = new StringBuilder();
        foreach (char c in key) { if (char.IsUpper(c)) result.Append(' '); result.Append(c); }
        if (result.Length > 0) result[0] = char.ToUpperInvariant(result[0]);
        return result.ToString();
    }
}
