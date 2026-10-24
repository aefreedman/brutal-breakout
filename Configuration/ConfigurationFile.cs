using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrutalBreakout.Configuration;

/// <summary>Shared strict file/path mechanics for the three concrete startup configuration schemas.</summary>
internal static class ConfigurationFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    internal static (string Path, TSettings Settings) Load<TFile, TSettings>(string path, string section,
        Func<TFile, TSettings> validate) where TFile : class
    {
        string displayPath = path ?? "<null>";
        try
        {
            displayPath = System.IO.Path.GetFullPath(path!);
            using var file = File.OpenRead(displayPath);
            using var document = JsonDocument.Parse(file);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException($"Expected an object containing all {section} settings.");
            RejectDuplicates(document.RootElement);
            var data = document.RootElement.Deserialize<TFile>(Options)
                ?? throw new JsonException($"{section} settings cannot be null.");
            return (displayPath, validate(data));
        }
        catch (Exception error) when (error is JsonException or ArgumentException or IOException
            or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidDataException($"Cannot load {section} configuration '{displayPath}': {error.Message}", error);
        }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException($"Duplicate property '{property.Name}'.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
    }

    internal static string? SelectPath(IReadOnlyList<string> args, bool needed, string section,
        string baseDirectory, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        string option = $"--{section}-config";
        string? selected = null;
        bool specified = false;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith(option + "=", StringComparison.Ordinal))
                throw new ArgumentException($"Use {option} <path>.");
            if (args[i] != option) continue;
            if (specified) throw new ArgumentException($"Specify {option} only once.");
            specified = true;
            if (++i == args.Count || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"{option} requires a file path.");
            selected = args[i];
        }
        if (!specified && !needed) return null;
        if (!specified) return System.IO.Path.Combine(baseDirectory, section + ".json");
        try { return System.IO.Path.GetFullPath(selected!, currentDirectory); }
        catch (ArgumentException error)
        {
            throw new InvalidDataException($"Invalid {option} path '{selected}': {error.Message}", error);
        }
    }
}
