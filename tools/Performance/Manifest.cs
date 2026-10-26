using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BrutalBreakout.Configuration;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;

namespace BrutalBreakout.Performance;

internal sealed record Policy
{
    public required double WarmSeconds { get; init; }
    public required double MeasureSeconds { get; init; }
    public required double CaseWallSeconds { get; init; }
    public required double CampaignWallSeconds { get; init; }
    public required long PrivateBytes { get; init; }
    public required long HeadroomBytes { get; init; }
    public required int MaxTicks { get; init; }
    public required int Seed { get; init; }
    public required int BallSlots { get; init; }
    public required int BrickSlots { get; init; }
    public required int Bricks { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required int Health { get; init; }
    public required int WallHits { get; init; }
    public required double NaturalFireHz { get; init; }
    public void Validate()
    {
        if (!double.IsFinite(WarmSeconds) || WarmSeconds < 0 || WarmSeconds > 2 ||
            !double.IsFinite(MeasureSeconds) || MeasureSeconds <= 0 || MeasureSeconds > 20 ||
            !double.IsFinite(CaseWallSeconds) || CaseWallSeconds <= 0 || CaseWallSeconds > 30 ||
            !double.IsFinite(CampaignWallSeconds) || CampaignWallSeconds <= 0 || CampaignWallSeconds > 1200 ||
            PrivateBytes <= 0 || PrivateBytes > 1L << 30 || HeadroomBytes < 0 || MaxTicks is < 1 or > 4800 ||
            BallSlots != 4096 || BrickSlots != 1024 || Bricks != 40 || Width != 1280 || Height != 720 ||
            Health <= 0 || WallHits <= 0 || !double.IsFinite(NaturalFireHz) || NaturalFireHz <= 0 || NaturalFireHz > 60)
            throw new InvalidDataException("Study policy exceeds the approved bounds.");
    }
}
internal sealed record CaseSpec
{
    public required string Id { get; init; }
    public string Scope { get; init; } = "whole";
    public int Target { get; init; } = 2000;
    public int? Workers { get; init; }
    public double? Hz { get; init; }
    public int? Substeps { get; init; }
    public double? ContactHertz { get; init; }
    public double? ContactDamping { get; init; }
    public double SpeedMultiplier { get; init; } = 1;
    public bool? Bounce { get; init; }
    public bool Natural { get; init; }
    public bool Recreate { get; init; }
    public string Trails { get; init; } = "enabled";
    public string Execution { get; init; } = "direct";
    public int? Catchup { get; init; }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
            Scope is not ("whole" or "native") || Target is < 1 or > 4000 ||
            Workers is not (null or 1 or 2 or 4 or 8) || Hz is not (null or 60 or 120 or 240) ||
            Substeps is not (null or 1 or 2 or 4) || Catchup is not (null or 1 or 4 or 8) ||
            Trails is not ("enabled" or "absent" or "draw-disabled") || Execution is not ("direct" or "immediate" or "overlap") ||
            !double.IsFinite(SpeedMultiplier) || SpeedMultiplier is < 1 or > 4 ||
            (ContactHertz.HasValue && (!double.IsFinite(ContactHertz.Value) || ContactHertz < 0)) ||
            (ContactDamping.HasValue && (!double.IsFinite(ContactDamping.Value) || ContactDamping < 0)))
            throw new InvalidDataException("Invalid study case.");
        if (Scope == "native" && (Natural || Catchup.HasValue || Execution != "direct" || Recreate))
            throw new InvalidDataException("Native scope cannot claim whole-world execution/spawn/clock controls.");
    }
}
internal sealed record ManifestFile(int Version, Policy Policy, CaseSpec[] Cases);
internal sealed class Manifest
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false
    };
    public Policy Policy { get; }
    public ReadOnlyCollection<CaseSpec> Cases { get; }
    private Manifest(ManifestFile file)
    {
        if (file.Version != 1 || file.Policy is null || file.Cases is null || file.Cases.Length is < 1 or > 28)
            throw new InvalidDataException("Invalid manifest version/case count.");
        file.Policy.Validate();
        foreach (var c in file.Cases) c.Validate();
        if (file.Cases.Select(c => c.Id).Distinct().Count() != file.Cases.Length)
            throw new InvalidDataException("Duplicate case ID.");
        Policy = file.Policy; Cases = Array.AsReadOnly(file.Cases.ToArray());
    }
    public static Manifest Load(string path) => Parse(File.ReadAllText(path));
    public static Manifest Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        CheckKeys(document.RootElement);
        return new(JsonSerializer.Deserialize<ManifestFile>(json, Json) ?? throw new InvalidDataException("Empty manifest."));
    }
    private static void CheckKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidDataException("Duplicate manifest key: " + property.Name);
                CheckKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) CheckKeys(item);
    }
}

// Resolved settings are production-loader objects. JSON strings are owned provenance, not mutable nodes.
internal sealed record ResolvedCase(CaseSpec Spec, Policy Policy,
    [property: JsonIgnore] PhysicsSettings Physics, [property: JsonIgnore] ShmupSettings Settings,
    [property: JsonIgnore] ExecutionSettings Execution, [property: JsonIgnore] BallTrailSettings? Trails,
    [property: JsonIgnore] ColorScheme Colors, [property: JsonIgnore] PresentationStyle Style,
    string PhysicsJson, string ShmupJson, string ExecutionJson, string TrailsJson, string ColorsJson, string StyleJson,
    bool Smoke, int WarmTicks, int MeasureTicks, long StorageEstimateBytes)
{
    public int TotalTicks => WarmTicks + MeasureTicks;
    public static ResolvedCase Load(CaseSpec spec, Policy policy, string config, int? smokeTicks = null)
    {
        spec.Validate(); policy.Validate();
        // Own each source text once. Validate BEFORE JsonNode normalization (duplicate keys included),
        // and parse the exact validated bytes rather than re-reading a mutable file.
        var source = new[] { "physics", "shmup", "execution", "trails", "colors", "style" }
            .ToDictionary(name => name, name => File.ReadAllText(Path.Combine(config, name + ".json")));
        var originalPhysics = PhysicsConfiguration.Load(Path.Combine(config, "physics.json"), source["physics"]);
        var originalShmup = ShmupConfiguration.Load(Path.Combine(config, "shmup.json"), source["shmup"]);
        ShmupConfiguration.ValidatePhysics(originalShmup, originalPhysics);
        _ = ExecutionConfiguration.Load(Path.Combine(config, "execution.json"), source["execution"]);
        _ = BallTrailConfiguration.Load(Path.Combine(config, "trails.json"), source["trails"]);
        JsonObject Read(string name) => JsonNode.Parse(source[name])!.AsObject();
        var p = Read("physics"); var s = Read("shmup"); var e = Read("execution"); var t = Read("trails");
        if (spec.Hz is { } hz) p["updateHz"] = hz;
        if (spec.Substeps is { } sub) p["substeps"] = sub;
        if (spec.Catchup is { } catchup) p["maxCatchUpSteps"] = catchup;
        if (spec.ContactHertz is { } contact) p["contactHertz"] = contact;
        if (spec.ContactDamping is { } damping) p["contactDampingRatio"] = damping;
        if (spec.Workers is { } workers) e["physicsWorkerCount"] = workers;
        s["arena"]!["initialPlayAreaWidth"] = policy.Width;
        if (s["arena"]!["width"]!.GetValue<double>() != policy.Width || s["arena"]!["height"]!.GetValue<double>() != policy.Height)
            throw new InvalidDataException("Authoritative canvas no longer matches study geometry.");
        s["capacity"]!["maxBalls"] = policy.BallSlots; s["capacity"]!["maxBricks"] = policy.BrickSlots;
        s["brick"]!["health"] = policy.Health;
        s["avatar"]!["health"] = policy.Health;
        if (!spec.Natural) s["ball"]!["maxWallHits"] = policy.WallHits;
        s["ball"]!["speed"] = s["ball"]!["speed"]!.GetValue<double>() * spec.SpeedMultiplier;
        if (spec.Bounce is { } bounce) s["ball"]!["bounceOnBricks"] = bounce;
        if (spec.Natural) s["ball"]!["fireRate"] = policy.NaturalFireHz;
        s["noise"]!["gapThreshold"] = 1; // Synthetic row suppression; manual refill is timed.
        if (spec.Trails == "draw-disabled") t["enabled"] = false;
        string pj = p.ToJsonString(), sj = s.ToJsonString(), ej = e.ToJsonString(), tj = t.ToJsonString();
        var physics = PhysicsConfiguration.Load(Path.Combine(config, "physics.json"), pj).Settings;
        var settings = ShmupConfiguration.Load(Path.Combine(config, "shmup.json"), sj).Settings;
        var execution = ExecutionConfiguration.Load(Path.Combine(config, "execution.json"), ej).Settings;
        var trails = spec.Trails == "absent" || spec.Scope == "native" ? null : BallTrailConfiguration.Load(Path.Combine(config, "trails.json"), tj).Settings;
        settings.ValidatePhysics(physics); trails?.ValidateCapacity(settings.Capacity.MaxBalls);
        int warm = checked((int)Math.Ceiling(policy.WarmSeconds * physics.UpdateHz));
        int measured = checked((int)Math.Ceiling(policy.MeasureSeconds * physics.UpdateHz));
        // 240Hz is 480 warm + 4800 measured: maxTicks bounds MEASURED ticks, not their sum.
        if (measured > policy.MaxTicks && smokeTicks is null) throw new InvalidDataException("Measured tick budget exceeded.");
        if (smokeTicks is { } smoke)
        {
            if (smoke is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(smokeTicks));
            warm = 0; measured = smoke;
        }
        long estimate = EstimateStorage(policy.BallSlots, policy.BrickSlots, trails?.MaxPointsPerBall ?? 0, warm + measured);
        if (estimate + policy.HeadroomBytes >= policy.PrivateBytes) throw new InvalidDataException("Storage estimate exceeds private-memory budget.");
        return new(spec, policy, physics, settings, execution, trails,
            ColorSchemeConfiguration.Load(Path.Combine(config, "colors.json"), source["colors"]).Settings,
            PresentationStyleConfiguration.Load(Path.Combine(config, "style.json"), source["style"]).Settings,
            pj, sj, ej, tj, source["colors"], source["style"], smokeTicks.HasValue, warm, measured, estimate);
    }
    public static long EstimateStorage(int balls, int bricks, int trailPoints, int ticks)
    {
        if (balls <= 0 || bricks <= 0 || trailPoints < 0 || ticks <= 0) throw new ArgumentOutOfRangeException();
        // Conservative planning estimate, NOT measured native heap bytes. Full pair list peaks, all matrices,
        // body/shape/dictionaries, ECS, draw buffers, two retained publications, history and raw value arrays.
        return checked(64L * 1024 * 1024 + (long)balls * bricks * (9 + 4 * 8 * 2) +
            (balls + bricks * 4L) * 4096 + balls * (trailPoints * 24L + 64) +
            2L * (balls * 80L + bricks * 80L) + 32L * 1024 * 1024 + ticks * 1024L);
    }
}
