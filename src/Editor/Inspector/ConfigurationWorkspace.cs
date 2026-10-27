using System.Text.Json;
using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using CrushKit.Brutal.ImGui;

namespace BrutalBreakout.Editor;

public enum ConfigurationAction { None, Apply, Save, Discard, Reload }

/// <summary>App-specific registry. Draft JSON retains asset spellings; production loaders own every schema and rule.</summary>
public sealed class ConfigurationWorkspace
{
    public sealed class Document
    {
        public string Section { get; }
        public string Path { get; }
        public bool Runtime { get; }
        private JsonObject draft;
        private JsonObject sourceNode, appliedNode;
        private string source, applied;
        private string? text;
        private JsonElement? view;
        private bool? unsaved, pending;
        private readonly Action changed;
        private static readonly JsonSerializerOptions TextOptions = new() { WriteIndented = true };
        public long Revision { get; private set; }
        internal int SerializationCount { get; private set; }
        internal int ParseCount { get; private set; }
        public JsonElement Draft
        {
            get
            {
                if (view is null)
                {
                    using var document = JsonDocument.Parse(Text); ParseCount++;
                    view = document.RootElement.Clone();
                }
                return view.Value;
            }
        }
        internal string Source
        {
            get => source;
            set { if (source == value) return; source = value; sourceNode = Parse(value); unsaved = null; changed(); }
        }
        internal string Applied
        {
            get => applied;
            set { if (applied == value) return; applied = value; appliedNode = Parse(value); pending = null; changed(); }
        }
        internal Func<string, object> Load;
        internal Document(string section, string path, bool runtime, Func<string, object> load, Action changed)
        {
            Section = section; Path = System.IO.Path.GetFullPath(path); Runtime = runtime; Load = load; this.changed = changed;
            source = File.ReadAllText(Path); load(source); applied = source;
            draft = Parse(source); sourceNode = Parse(source); appliedNode = Parse(source);
        }
        public string Text
        {
            get { if (text is null) { text = draft.ToJsonString(TextOptions); SerializationCount++; } return text; }
        }
        public bool Unsaved => unsaved ??= !JsonNode.DeepEquals(draft, sourceNode);
        public bool Pending => pending ??= !JsonNode.DeepEquals(draft, appliedNode);
        /// <summary>Edits a section-relative dot path (including array indices). Inputs are detached before commitment.</summary>
        public bool Set(string path, JsonNode? value) => Set(path.Split('.'), value);
        /// <summary>Edits literal section-relative keys; numeric segments are indices only when their parent is an array.</summary>
        public bool Set(IReadOnlyList<string> keys, JsonNode? value)
        {
            if (keys.Count == 0) throw new ArgumentException("An edit needs at least one key.", nameof(keys));
            JsonNode parent = draft;
            for (int i = 0; i < keys.Count - 1; i++)
                parent = Child(parent, keys[i]) ?? throw new ArgumentException("Unknown edit key: " + keys[i], nameof(keys));
            string last = keys[^1];
            if (JsonNode.DeepEquals(Child(parent, last), value)) return false;
            var owned = value?.DeepClone();
            if (parent is JsonArray array) array[int.Parse(last, System.Globalization.CultureInfo.InvariantCulture)] = owned;
            else parent[last] = owned;
            Invalidate(); return true;
        }
        public bool Set(string path, JsonElement value) => Set(path, JsonNode.Parse(value.GetRawText()));
        public bool Set(IReadOnlyList<string> keys, JsonElement value) => Set(keys, JsonNode.Parse(value.GetRawText()));
        private static JsonNode? Child(JsonNode node, string key) => node is JsonArray array
            ? array[int.Parse(key, System.Globalization.CultureInfo.InvariantCulture)] : node[key];
        private void Invalidate() { Revision++; text = null; view = null; unsaved = pending = null; changed(); }
        internal void Discard()
        {
            if (JsonNode.DeepEquals(draft, sourceNode)) return;
            draft = (JsonObject)sourceNode.DeepClone(); Invalidate();
        }
        internal void Reload(string text) { Source = text; Discard(); }
        internal string Merged(bool reset)
        {
            if (!Pending) return Applied;
            var target = (JsonObject)appliedNode.DeepClone();
            ConfigurationImpact.Merge(target, draft, Section, reset);
            return JsonNode.DeepEquals(target, appliedNode) ? Applied : target.ToJsonString();
        }
        internal IEnumerable<string> PendingFields(ConfigurationEffect effect) => ConfigurationImpact.Fields(draft, Section)
            .Where(f => ConfigurationImpact.For(f.Path).Effect == effect &&
                !JsonNode.DeepEquals(f.Value, Find(appliedNode, f.Keys)))
            .Select(f => f.Path + ": " + ConfigurationImpact.For(f.Path).Reason);
        private JsonObject Parse(string text) { ParseCount++; return JsonNode.Parse(text)!.AsObject(); }
    }
    public IReadOnlyList<Document> Documents => documentView;
    private readonly List<Document> documents = [];
    private readonly IReadOnlyList<Document> documentView;
    private ConfigurationAction request;
    private Candidate? appliedCandidate;
    public string Status { get; private set; } = "Valid safe edits apply automatically. Save and structural reset are separate.";
    private long revision, lastAttempt = -1;
    private string? inactiveModeSection, liveContextRevision;
    private Action<Candidate>? preflightLive;
    private object? contextOwner;
    private double contextWidth;
    public void SetLiveContext(object? owner, double width)
    {
        if (ReferenceEquals(contextOwner, owner) && contextWidth == width) return;
        contextOwner = owner; contextWidth = width; Changed();
    }
    private IReadOnlyList<string>? pendingResetFields, pendingRelaunchFields;
    internal int ValidationCount { get; private set; }
    internal int MergeCount { get; private set; }
    internal int PendingBuildCount { get; private set; }
    internal long ChangeRevision => revision;
    private void Changed() { revision++; pendingResetFields = pendingRelaunchFields = null; }
    public string? InactiveModeSection { get => inactiveModeSection; set { if (inactiveModeSection == value) return; inactiveModeSection = value; Changed(); } }
    public string? LiveContextRevision { get => liveContextRevision; set { if (liveContextRevision == value) return; liveContextRevision = value; Changed(); } }
    public Action<Candidate>? PreflightLive { get => preflightLive; set { if (preflightLive == value) return; preflightLive = value; Changed(); } }
    private Candidate ValidateLive(Dictionary<string, string> texts)
    {
        var candidate = Validate(texts);
        PreflightLive?.Invoke(candidate);
        return candidate;
    }
    public IReadOnlyList<string> PendingResetFields => pendingResetFields ??= PendingFields(ConfigurationEffect.Reset);
    public IReadOnlyList<string> PendingRelaunchFields => pendingRelaunchFields ??= PendingFields(ConfigurationEffect.Relaunch);
    private IReadOnlyList<string> PendingFields(ConfigurationEffect effect)
    {
        PendingBuildCount++;
        return Array.AsReadOnly(documents.Where(d => !(effect == ConfigurationEffect.Reset && d.Section == InactiveModeSection))
            .SelectMany(d => d.PendingFields(effect)).ToArray());
    }
    private static JsonNode? Find(JsonNode? node, IEnumerable<string> keys)
    { foreach (var key in keys) node = node?[key]; return node; }
    public bool RuntimeAvailable { get; }
    public string? RuntimeDiagnostic { get; set; }
    internal Action<string, string> ReplaceFile { get; set; } = (temporary, target) => File.Replace(temporary, target, null);
    internal Action<string> DeleteTemporaryFile { get; set; } = File.Delete;
    public ConfigurationWorkspace(IReadOnlyDictionary<string, string> paths, bool runtimeAvailable)
    {
        RuntimeAvailable = runtimeAvailable;
        documentView = documents.AsReadOnly();
        foreach (var (section, path) in paths)
        {
            Func<string, object> load = section switch
            {
                "physics" => text => PhysicsConfiguration.Load(path, text),
                "gameplay" => text => GameplayConfiguration.Load(path, text),
                "shmup" => text => ShmupConfiguration.Load(path, text),
                "execution" => text => ExecutionConfiguration.Load(path, text),
                "input" => text => InputConfiguration.Load(path, text),
                "trails" => text => BallTrailConfiguration.Load(path, text),
                "colors" => text => ColorSchemeConfiguration.Load(path, text),
                "style" => text => PresentationStyleConfiguration.Load(path, text),
                "audio" => text => AudioConfiguration.Load(path, text),
                "rendering" => text => RenderingConfiguration.Load(path, text),
                "window" => text => WindowConfiguration.Load(path, text),
                "overlay" => text => OverlayConfiguration.Load(path, text),
                "inspector" => text => InspectorConfiguration.Load(path, text),
                _ => throw new ArgumentException($"Unknown configuration section {section}.")
            };
            documents.Add(new(section, path, section is not ("window" or "overlay" or "inspector"), load, Changed));
        }
        appliedCandidate = Validate(Capture(false));
    }
    internal static bool Equivalent(string a, string b) => a == b || JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b));
    public void Request(ConfigurationAction action) => request = action;
    public bool IsChanged(Candidate candidate, string section) =>
        documents.Any(d => d.Section == section && !Equivalent(d.Applied, candidate.Texts[section]));
    public void ReportFailure(Exception error) => Status = "Rejected: " + error.Message;
    private Dictionary<string, string> Capture(bool runtimeOnly) => documents.ToDictionary(d => d.Section,
        d => runtimeOnly && !d.Runtime ? d.Applied : d.Text, StringComparer.Ordinal);

    public sealed class Candidate
    {
        internal readonly Dictionary<string, object> loaded;
        internal readonly Dictionary<string, string> Texts;
        public bool ResetRequired { get; internal set; }
        internal Candidate(Dictionary<string, object> loaded, Dictionary<string, string> texts) => (this.loaded, Texts) = (loaded, texts);
        public T? Get<T>(string section) where T : class => loaded.GetValueOrDefault(section) as T;
    }
    private Candidate Validate(Dictionary<string, string> texts, bool reuseApplied = true)
    {
        ValidationCount++;
        var candidate = new Candidate(documents.ToDictionary(d => d.Section, d =>
            reuseApplied && appliedCandidate is not null && Equivalent(appliedCandidate.Texts[d.Section], texts[d.Section])
                ? appliedCandidate.loaded[d.Section] : d.Load(texts[d.Section])), texts);
        ConfigurationValidation.Validate(candidate.Get<LoadedPhysicsConfiguration>("physics"), candidate.Get<LoadedGameplayConfiguration>("gameplay"),
            candidate.Get<LoadedShmupConfiguration>("shmup"), candidate.Get<LoadedBallTrailConfiguration>("trails"),
            candidate.Get<LoadedPresentationStyleConfiguration>("style"), candidate.Get<LoadedOverlayConfiguration>("overlay"),
            candidate.Get<LoadedRenderingConfiguration>("rendering"));
        if (candidate.Get<LoadedOverlayConfiguration>("overlay") is { } overlay) InspectorHost.Validate(overlay.Settings);
        return candidate;
    }
    /// <summary>Owner update only. Captures independent strings before validating/allocating; no native work in Draw.</summary>
    public void Process(Action<Candidate> apply)
    {
        var action = request; request = ConfigurationAction.None;
        if (action == ConfigurationAction.None) { AutoApply(apply); return; }
        try
        {
            switch (action)
            {
                case ConfigurationAction.Discard:
                    foreach (var d in documents) d.Discard();
                    Status = "Discarded drafts to source; valid live values apply automatically.";
                    break;
                case ConfigurationAction.Reload:
                    var disk = documents.ToDictionary(d => d.Section, d => File.ReadAllText(d.Path));
                    Validate(disk, reuseApplied: false);
                    foreach (var d in documents) d.Reload(disk[d.Section]);
                    Status = "Reloaded sources; valid live values apply automatically. Structural/relaunch values remain pending.";
                    break;
                case ConfigurationAction.Apply:
                    if (!RuntimeAvailable) throw new InvalidOperationException("Runtime Apply is unavailable in showcase/sandbox modes.");
                    var candidate = Validate(Merged(true));
                    candidate.ResetRequired = PendingResetFields.Count > 0;
                    apply(candidate);
                    Commit(candidate);
                    Status = candidate.ResetRequired ? "Runtime applied; structural reset completed (mode/pause kept). Not saved. Relaunch drafts NOT applied."
                        : "Runtime applied live; run retained. Not saved. Relaunch drafts NOT applied.";
                    break;
                case ConfigurationAction.Save:
                    var texts = Capture(false);
                    Validate(texts, reuseApplied: false);
                    var cleanupWarning = Save(texts);
                    Status = "Saved changed sources. Save does NOT reset or apply structural/relaunch fields.";
                    if (cleanupWarning is not null) Status += " " + cleanupWarning;
                    break;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { ReportFailure(error); return; }
        if (action is ConfigurationAction.Discard or ConfigurationAction.Reload) AutoApply(apply);
    }
    private Dictionary<string, string> Merged(bool reset)
    {
        MergeCount++;
        return documents.ToDictionary(d => d.Section, d => d.Merged(reset || d.Section == InactiveModeSection));
    }
    private void Commit(Candidate candidate)
    {
        foreach (var d in documents) d.Applied = candidate.Texts[d.Section];
        appliedCandidate = candidate;
    }
    private void AutoApply(Action<Candidate> apply)
    {
        if (!RuntimeAvailable) return;
        if (revision == lastAttempt) return;
        lastAttempt = revision; // Includes invalid attempts: unchanged invalid frames do not parse/load/allocate again.
        var texts = Merged(false);
        if (documents.All(d => Equivalent(d.Applied, texts[d.Section]))) return;
        try
        {
            Candidate candidate;
            string? pending = null;
            try { candidate = ValidateLive(texts); }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
            {
                // Invalid intermediate edits in one document must not block unrelated valid live controls.
                pending = error.Message;
                var accepted = documents.ToDictionary(d => d.Section, d => d.Applied);
                foreach (var d in documents.Where(d => !Equivalent(d.Applied, texts[d.Section])))
                {
                    var attempt = new Dictionary<string, string>(accepted) { [d.Section] = texts[d.Section] };
                    try { ValidateLive(attempt); accepted = attempt; }
                    catch (Exception invalid) when (invalid is not OutOfMemoryException and not StackOverflowException)
                    {
                        pending = d.Section + ": " + invalid.Message;
                        // Keep independent valid fields in the same JSON responsive too. Atomic arrays stay intact.
                        // Two bounded passes allow ordinary coupled limits to settle without per-frame retry work.
                        var fields = ConfigurationImpact.Fields(JsonNode.Parse(texts[d.Section])!.AsObject(), d.Section).ToArray();
                        for (int pass = 0; pass < 2; pass++) foreach (var field in fields)
                        {
                            var target = JsonNode.Parse(accepted[d.Section])!.AsObject();
                            var keys = field.Keys;
                            if (JsonNode.DeepEquals(Find(target, keys), field.Value)) continue;
                            JsonNode parent = target;
                            foreach (var key in keys.SkipLast(1)) parent = parent[key]!;
                            parent[keys[^1]] = field.Value?.DeepClone();
                            var single = new Dictionary<string, string>(accepted) { [d.Section] = target.ToJsonString() };
                            try { ValidateLive(single); accepted = single; }
                            catch (Exception rejected) when (rejected is not OutOfMemoryException and not StackOverflowException)
                            { pending = field.Path + ": " + rejected.Message; }
                        }
                    }
                }
                if (documents.All(d => Equivalent(d.Applied, accepted[d.Section])))
                { Status = "Rejected: live values pending against current structural settings: " + pending; return; }
                candidate = ValidateLive(accepted);
                if (documents.All(d => Equivalent(texts[d.Section], accepted[d.Section]))) pending = null;
            }
            apply(candidate);
            Commit(candidate);
            Status = pending is null ? "Runtime applied live; run retained. Save/reset/relaunch are separate."
                : "Rejected: some live values remain pending against current structural settings: " + pending + ". Other valid live edits applied.";
            lastAttempt = revision;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { ReportFailure(error); }
    }
    private string? Save(Dictionary<string, string> texts)
    {
        // Validate against the entire observed source bundle; an unchanged but externally edited file is a conflict too.
        foreach (var d in documents)
            if (File.ReadAllText(d.Path) != d.Source)
                throw new IOException($"Source changed externally: {d.Path}. Reload from disk (discards drafts) before saving.");
        var changed = documents.Where(d => d.Unsaved).ToArray();
        var prepared = new Dictionary<Document, string>();
        var temporaryPaths = new List<string>();
        var committed = new List<Document>();
        var cleanupFailures = new List<string>();
        string? primaryFailure = null;
        Exception? primaryError = null;
        string Prepare(Document d, string text)
        {
            string temp = d.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            temporaryPaths.Add(temp); // retain every path, including failed writes and rollback preparations
            File.WriteAllText(temp, text);
            return temp;
        }
        try
        {
            foreach (var d in changed) prepared[d] = Prepare(d, texts[d.Section]);
            foreach (var d in changed)
            {
                if (File.ReadAllText(d.Path) != d.Source) throw new IOException($"Source changed externally: {d.Path}. Reload before saving.");
                ReplaceFile(prepared[d], d.Path);
                committed.Add(d);
            }
            foreach (var d in changed) d.Source = texts[d.Section];
        }
        catch (Exception saveError)
        {
            primaryError = saveError;
            var failures = new List<string>();
            foreach (var d in committed.AsEnumerable().Reverse())
            {
                try { ReplaceFile(Prepare(d, d.Source), d.Path); }
                catch (Exception rollbackError) { failures.Add(d.Path + ": " + rollbackError.Message); }
            }
            primaryFailure = failures.Count == 0 ? "Save failed; originals retained/restored. " + saveError.Message
                : "PARTIAL SAVE; rollback failed: " + string.Join("; ", failures) + ". Reload to inspect disk. " + saveError.Message;
        }
        finally
        {
            // Cleanup is secondary: attempt every path, never obscure the committed/rollback outcome.
            // File.Delete is intentionally safe for paths already consumed by a successful replacement.
            foreach (var temp in temporaryPaths)
            {
                try { DeleteTemporaryFile(temp); }
                catch (Exception cleanupError) { cleanupFailures.Add(temp + ": " + cleanupError.Message); }
            }
        }
        string? warning = cleanupFailures.Count == 0 ? null : "Temporary cleanup warning: " + string.Join("; ", cleanupFailures);
        if (primaryFailure is not null)
            throw new IOException(primaryFailure + (warning is null ? "" : " " + warning), primaryError);
        return warning;
    }
}
