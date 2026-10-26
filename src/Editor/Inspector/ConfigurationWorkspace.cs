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
        public JsonObject Draft { get; private set; }
        internal string Source, Applied;
        internal Func<string, object> Load;
        internal Document(string section, string path, bool runtime, Func<string, object> load)
        {
            Section = section; Path = System.IO.Path.GetFullPath(path); Runtime = runtime; Load = load;
            Source = File.ReadAllText(Path); load(Source); Applied = Source; Draft = Parse(Source);
        }
        public string Text => Draft.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        public bool Unsaved => !Equivalent(Text, Source);
        public bool Pending => !Equivalent(Text, Applied);
        internal void Discard() => Draft = Parse(Source);
        internal void Reload(string text) { Source = text; Draft = Parse(text); }
        private static JsonObject Parse(string text) => JsonNode.Parse(text)!.AsObject();
    }
    public IReadOnlyList<Document> Documents => documents;
    private readonly List<Document> documents = [];
    private ConfigurationAction request;
    public string Status { get; private set; } = "Drafts are staged. Apply does not save; Save does not apply.";
    public bool RuntimeAvailable { get; }
    public string? RuntimeDiagnostic { get; set; }
    internal Action<string, string> ReplaceFile { get; set; } = (temporary, target) => File.Replace(temporary, target, null);
    internal Action<string> DeleteTemporaryFile { get; set; } = File.Delete;
    public ConfigurationWorkspace(IReadOnlyDictionary<string, string> paths, bool runtimeAvailable)
    {
        RuntimeAvailable = runtimeAvailable;
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
                "window" => text => WindowConfiguration.Load(path, text),
                "overlay" => text => OverlayConfiguration.Load(path, text),
                "inspector" => text => InspectorConfiguration.Load(path, text),
                _ => throw new ArgumentException($"Unknown configuration section {section}.")
            };
            documents.Add(new(section, path, section is not ("window" or "overlay" or "inspector"), load));
        }
        Validate(Capture(false));
    }
    internal static bool Equivalent(string a, string b) => JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b));
    public void Request(ConfigurationAction action) => request = action;
    public bool IsChanged(Candidate candidate, string section) =>
        documents.Any(d => d.Section == section && !Equivalent(d.Applied, candidate.Texts[section]));
    public void ReportFailure(Exception error) => Status = "Rejected: " + error.Message;
    private Dictionary<string, string> Capture(bool runtimeOnly) => documents.ToDictionary(d => d.Section,
        d => runtimeOnly && !d.Runtime ? d.Applied : d.Text, StringComparer.Ordinal);

    public sealed class Candidate
    {
        private readonly Dictionary<string, object> loaded;
        internal readonly Dictionary<string, string> Texts;
        internal Candidate(Dictionary<string, object> loaded, Dictionary<string, string> texts) => (this.loaded, Texts) = (loaded, texts);
        public T? Get<T>(string section) where T : class => loaded.GetValueOrDefault(section) as T;
    }
    private Candidate Validate(Dictionary<string, string> texts)
    {
        var candidate = new Candidate(documents.ToDictionary(d => d.Section, d => d.Load(texts[d.Section])), texts);
        ConfigurationValidation.Validate(candidate.Get<LoadedPhysicsConfiguration>("physics"), candidate.Get<LoadedGameplayConfiguration>("gameplay"),
            candidate.Get<LoadedShmupConfiguration>("shmup"), candidate.Get<LoadedBallTrailConfiguration>("trails"),
            candidate.Get<LoadedPresentationStyleConfiguration>("style"), candidate.Get<LoadedOverlayConfiguration>("overlay"));
        if (candidate.Get<LoadedOverlayConfiguration>("overlay") is { } overlay) InspectorHost.Validate(overlay.Settings);
        return candidate;
    }
    /// <summary>Owner update only. Captures independent strings before validating/allocating; no native work in Draw.</summary>
    public void Process(Action<Candidate> apply)
    {
        var action = request; request = ConfigurationAction.None;
        if (action == ConfigurationAction.None) return;
        try
        {
            switch (action)
            {
                case ConfigurationAction.Discard:
                    foreach (var d in documents) d.Discard();
                    Status = "Discarded drafts to last loaded/saved source. Applied run is unchanged.";
                    break;
                case ConfigurationAction.Reload:
                    var disk = documents.ToDictionary(d => d.Section, d => File.ReadAllText(d.Path));
                    Validate(disk);
                    foreach (var d in documents) d.Reload(disk[d.Section]);
                    Status = "Reloaded source files. Apply to restart runtime; relaunch-only settings are not applied.";
                    break;
                case ConfigurationAction.Apply:
                    if (!RuntimeAvailable) throw new InvalidOperationException("Runtime Apply is unavailable in showcase/sandbox modes.");
                    var candidate = Validate(Capture(true));
                    apply(candidate);
                    foreach (var d in documents.Where(d => d.Runtime)) d.Applied = candidate.Texts[d.Section];
                    Status = "Runtime applied and restarted (mode/pause kept). Not saved. Relaunch-only drafts were NOT applied.";
                    break;
                case ConfigurationAction.Save:
                    var texts = Capture(false);
                    Validate(texts);
                    var cleanupWarning = Save(texts);
                    Status = "Saved changed sources. Save does NOT apply. Window/overlay/inspector require app relaunch.";
                    if (cleanupWarning is not null) Status += " " + cleanupWarning;
                    break;
            }
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
