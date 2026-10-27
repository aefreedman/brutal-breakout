using System.Text;
using System.Text.Json;
using CrushKit.Brutal.ImGui;

namespace BrutalBreakout.Editor;

/// <summary>Game-authored presentation only. Production loaders remain the sole schema/validation authority.</summary>
internal sealed class ConfigurationFieldPresentation
{
    public ToolProperty Property { get; }
    public ToolDoubleEditor Real { get; }
    public ToolInt64Editor? Integer { get; }
    public byte[] WidgetId { get; }
    public ToolFieldPreset[] Presets { get; }
    private readonly JsonElement[] presetValues;
    public bool CommitOnEnter { get; }
    public JsonElement PresetValue(int index) => presetValues[index];

    public ConfigurationFieldPresentation(string id, string label)
    {
        CommitOnEnter = id is "window.width" or "window.height" or "window.frameRateLimit";
        Real = ToolDoubleEditor.Input("%.17g");
        if (IntegerFields.Contains(id)) Integer = ToolInt64Editor.Input();
        if (id == "shmup.ball.maxShotsPerTick") Integer = ToolInt64Editor.Drag(1, 64, 1);
        if (id == "rendering.circleDetail.fixedSides") Integer = ToolInt64Editor.Drag(8, 4096, 4);
        if (id is "shmup.ball.speed" or "shmup.avatar.moveSpeed" or "gameplay.ball.speed" or
            "shmup.difficulty.descentSpeed" or "shmup.difficulty.maximumDescentSpeed")
            Real = ToolDoubleEditor.Drag(1, 2000, 1, "%.3f");
        if (id is "shmup.ball.radius" or "shmup.avatar.radius" or "gameplay.ball.radius")
            Real = ToolDoubleEditor.Drag(.1, 128, .1f, "%.3f");
        if (id == "shmup.ball.fireRate") Real = ToolDoubleEditor.Drag(.1, 1000, .1f, "%.3f");
        if (id == "shmup.avatar.invulnerabilitySeconds") Real = ToolDoubleEditor.Drag(.01, 10, .01f, "%.3f");
        if (id == "input.stickDeadzone") Real = ToolDoubleEditor.Slider(0, .95, "%.3f");
        if (id is "audio.masterGain" or "trails.opacity") Real = ToolDoubleEditor.Slider(0, 1, "%.3f");
        var impact = ConfigurationImpact.For(id);
        string interaction = CommitOnEnter ? "Press Enter to stage. Partial digits do not resize or change the cap."
            : Real.Mode != ToolNumberMode.Input || Integer?.Mode is ToolNumberMode.Drag or ToolNumberMode.Slider
                ? "Drag/slide for quick adjustment; Ctrl-click for exact entry. The drag range is not a validation limit."
                : "Enter an exact value. Production validation runs outside drawing.";
        Property = new(id, label, Units(id), impact.Effect.ToString(), new ToolHelp(label, impact.Reason, interaction));
        WidgetId = Encoding.UTF8.GetBytes("##" + id);
        (string Label, JsonElement Value)[] presets = id switch
        {
            "window.frameRateLimit" => [("Unlimited", JsonSerializer.SerializeToElement(0L)), ("60 fps", JsonSerializer.SerializeToElement(60L)),
                ("120 fps", JsonSerializer.SerializeToElement(120L)), ("144 fps", JsonSerializer.SerializeToElement(144L))],
            "shmup.ball.fireRate" => [("12.5 shots/s", JsonSerializer.SerializeToElement(12.5)), ("60 shots/s", JsonSerializer.SerializeToElement(60d)),
                ("300 shots/s", JsonSerializer.SerializeToElement(300d))],
            "rendering.circleDetail.fixedSides" => [("12 sides", JsonSerializer.SerializeToElement(12L)), ("16 sides", JsonSerializer.SerializeToElement(16L)),
                ("32 sides", JsonSerializer.SerializeToElement(32L))],
            _ => []
        };
        Presets = presets.Select(p => new ToolFieldPreset(p.Label)).ToArray();
        presetValues = presets.Select(p => p.Value).ToArray();
    }

    private static readonly HashSet<string> IntegerFields = new(StringComparer.Ordinal)
    {
        "window.width", "window.height", "window.frameRateLimit", "physics.substeps", "physics.maxCatchUpSteps",
        "execution.workerCount", "execution.physicsWorkerCount", "execution.maxExpectedConcurrentJobs",
        "shmup.avatar.health", "shmup.avatar.contactDamage", "shmup.ball.damage", "shmup.ball.maxWallHits", "shmup.ball.maxShotsPerTick",
        "shmup.brick.health", "shmup.brick.escapeDamage", "shmup.noise.seed", "shmup.capacity.maxBalls", "shmup.capacity.maxBricks",
        "gameplay.breakout.startingBalls", "gameplay.breakout.maxBouncesWithoutBrick", "audio.maxVoices",
        "rendering.circleDetail.fixedSides", "rendering.limits.meshCount", "rendering.limits.cpuBytes", "rendering.limits.gpuBytes",
        "rendering.limits.instances", "rendering.limits.operations", "trails.maxPointsPerBall", "trails.maxRenderedSegments",
        "overlay.cacheEntries", "overlay.maxLabelCharacters", "overlay.maxLabels", "overlay.maxTextureDimension",
        "inspector.historyCapacity", "inspector.feedback.capacity", "inspector.feedback.maxTextCharacters"
    };
    private static string Units(string path) => path.AsSpan(path.LastIndexOf('.') + 1) switch
    {
        "speed" or "moveSpeed" or "descentSpeed" or "maximumDescentSpeed" => "units/s",
        "radius" or "size" or "columnPitch" => "game units",
        "width" or "height" when path.StartsWith("window.", StringComparison.Ordinal) || path.StartsWith("inspector.", StringComparison.Ordinal) => "physical px",
        "width" or "height" => "game units",
        "invulnerabilitySeconds" or "rampSeconds" or "rowInterval" or "minimumRowInterval" or "durationSeconds" => "seconds",
        "fireRate" => "shots/s", "updateHz" or "contactHertz" => "Hz", "frameRateLimit" => "fps · 0 = Unlimited", _ => ""
    };
}
