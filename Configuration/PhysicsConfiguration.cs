using System.Text.Json;
using System.Text.Json.Serialization;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Configuration;

public sealed record LoadedPhysicsConfiguration(string Path, PhysicsSettings Settings)
{
    public string Describe() => FormattableString.Invariant(
        $"Physics configuration: updateHz={Settings.UpdateHz:R}; stepSeconds={Settings.StepSeconds:R}; substeps={Settings.Substeps}; maxCatchUpSteps={Settings.MaxCatchUpSteps}; contactHertz={Settings.ContactHertz:R}; contactDampingRatio={Settings.ContactDampingRatio:R}; path={Path}");
}

/// <summary>Strict startup-only file loading. Production defaults exist only in physics.json.</summary>
public static class PhysicsConfiguration
{
    private sealed record PhysicsFile(double UpdateHz, int Substeps, int MaxCatchUpSteps,
        double ContactHertz, double ContactDampingRatio);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    public static LoadedPhysicsConfiguration Load(string path)
    {
        string displayPath = path ?? "<null>";
        try
        {
            displayPath = System.IO.Path.GetFullPath(path!);
            using var file = File.OpenRead(displayPath);
            using var document = JsonDocument.Parse(file);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Expected an object containing all physics settings.");
            // JsonDocument retains duplicate properties; reject them rather than accepting last-wins.
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) throw new JsonException($"Duplicate property '{property.Name}'.");
            var data = document.RootElement.Deserialize<PhysicsFile>(Options)
                ?? throw new JsonException("Physics settings cannot be null.");
            return new(displayPath, new PhysicsSettings(data.UpdateHz, data.Substeps,
                data.MaxCatchUpSteps, data.ContactHertz, data.ContactDampingRatio));
        }
        catch (Exception error) when (error is JsonException or ArgumentException or IOException
            or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidDataException($"Cannot load physics configuration '{displayPath}': {error.Message}", error);
        }
    }

    /// <summary>Explicit relative paths use caller cwd; the implicit asset uses the executable directory.</summary>
    public static LoadedPhysicsConfiguration? LoadForMode(IReadOnlyList<string> args, bool needsPhysics,
        string baseDirectory, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? selected = null;
        bool specified = false;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith("--physics-config=", StringComparison.Ordinal))
                throw new ArgumentException("Use --physics-config <path>.");
            if (args[i] != "--physics-config") continue;
            if (specified) throw new ArgumentException("Specify --physics-config only once.");
            specified = true;
            if (++i == args.Count || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("--physics-config requires a file path.");
            selected = args[i];
        }
        if (!specified && !needsPhysics) return null;
        if (!specified) return Load(System.IO.Path.Combine(baseDirectory, "physics.json"));
        try
        {
            return Load(System.IO.Path.GetFullPath(selected!, currentDirectory));
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException($"Invalid --physics-config path '{selected}': {error.Message}", error);
        }
    }
}
