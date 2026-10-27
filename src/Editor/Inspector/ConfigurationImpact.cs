using System.Text.Json.Nodes;

namespace BrutalBreakout.Editor;

public enum ConfigurationEffect { Live, Future, Reset, Relaunch }
public readonly record struct ConfigurationFieldImpact(ConfigurationEffect Effect, string Reason);

/// <summary>Field policy, shared by candidate construction and disclosure. Arrays are atomic fields.</summary>
public static class ConfigurationImpact
{
    public static ConfigurationFieldImpact For(string path) => path switch
    {
        "rendering.path" => new(ConfigurationEffect.Live, "Shared meshes or CPU-built triangles on the next owner update, including while paused. Retain the run, time, entities and bounded geometry cache; Save is separate."),
        var p when p.StartsWith("rendering.limits.") => new(ConfigurationEffect.Relaunch, "Immutable CPU/GPU mesh and instance/operation allocation bounds. Save then relaunch; no live native allocation policy replacement."),
        "window.vsync" => new(ConfigurationEffect.Live, "Enable FIFO synchronization, or disable using supported Immediate (tearing possible). Applies next owner update without resetting gameplay. The cap and VSync combine at the lower throughput; 0 cap does not disable VSync."),
        "window.frameRateLimit" => new(ConfigurationEffect.Live, "Press Enter to apply the render frame cap (0 unlimited; 1–1000). Includes work and VSync waits, even paused. Very low caps retain the existing fixed-tick catch-up/drop policy; simulation Hz is unchanged."),
        "window.width" or "window.height" => new(ConfigurationEffect.Live, "Press Enter to resize the physical window on the owner update; partial digits do not resize. Logical game bounds stay unchanged."),
        "physics.updateHz" or "physics.maxCatchUpSteps" => new(ConfigurationEffect.Reset, "Fixed clock and bounded event buffers are constructed for this tick budget."),
        "physics.substeps" or "physics.contactHertz" or "physics.contactDampingRatio" => new(ConfigurationEffect.Live, "Update the existing native world's solver; retain bodies and contact metadata."),
        var p when p.StartsWith("execution.") => new(ConfigurationEffect.Reset, "Execution workers/scheduler are owned for the world's lifetime."),
        "shmup.arena.initialPlayAreaWidth" => new(ConfigurationEffect.Future, "Default width for the next restart; never shrink or widen the current run."),
        var p when p.StartsWith("shmup.arena.") || p.StartsWith("shmup.capacity.") => new(ConfigurationEffect.Reset, "Logical bounds or fixed native/ECS pool topology requires reconstruction."),
        "shmup.ball.speed" => new(ConfigurationEffect.Live, "Existing active native velocities change magnitude, preserve direction/pose/contact streak; world speed cap updates too."),
        "shmup.ball.radius" => new(ConfigurationEffect.Live, "Update pooled native circle and wall geometry without replacing bodies; must fit current live width (widen first)."),
        "shmup.ball.fireRate" => new(ConfigurationEffect.Live, "Preserve fractional firing deadline under the new rate; no immediate shot or held-input replay."),
        "shmup.ball.damage" => new(ConfigurationEffect.Live, "All existing and future balls use the new damage on subsequent hits; existing brick HP is not rescaled."),
        "shmup.ball.maxWallHits" => new(ConfigurationEffect.Live, "Existing streaks retained; the new limit is enforced on subsequent collision intervals."),
        "shmup.brick.escapeDamage" => new(ConfigurationEffect.Live, "Existing and future bricks use this damage when they escape; current HP is unchanged until an escape."),
        "shmup.avatar.radius" => new(ConfigurationEffect.Live, "Managed query circle, not a rigid body. Clamp pose to live walls only if needed; must fit current width (widen first)."),
        "shmup.avatar.invulnerabilitySeconds" => new(ConfigurationEffect.Live, "New contact duration; clamp remaining invulnerability when lowering, never extend an existing timer."),
        "shmup.avatar.moveSpeed" => new(ConfigurationEffect.Live, "Current avatar movement speed on the next tick; retain pose, aim and input latches."),
        "shmup.avatar.contactDamage" => new(ConfigurationEffect.Live, "New contact damage on subsequent contacts; no immediate damage or healing."),
        "shmup.ball.bounceOnBricks" => new(ConfigurationEffect.Reset, "Published Box2D has no SetSensor; fixture contact mode is construction-only."),
        "shmup.brick.size" or "shmup.brick.columnPitch" => new(ConfigurationEffect.Reset, "Existing compound fixtures and persistent spawn lattice use this geometry."),
        "shmup.brick.health" or "shmup.brick.shapes" or "shmup.brick.quarterTurns" => new(ConfigurationEffect.Future, "New brick lives only; existing HP, shape, rotation and generation are preserved."),
        "shmup.avatar.health" => new(ConfigurationEffect.Future, "Next restart health; lowering clamps current health, raising never heals or resurrects."),
        var p when p.StartsWith("shmup.noise.") => new(ConfigurationEffect.Live, "Future rows use new noise; existing bricks and lattice phase stay unchanged."),
        var p when p.StartsWith("shmup.difficulty.") => new(ConfigurationEffect.Live, "Current elapsed-time difficulty updates; pending row deadline is bounded, never rewound or burst-spawned."),
        var p when p.StartsWith("shmup.") => new(ConfigurationEffect.Live, "Existing run tuning; retain time, entities, contact streaks and progression. Radius clamps avatar pose only if needed."),
        var p when p.StartsWith("gameplay.arena.") || p.StartsWith("gameplay.breakout.bricks") => new(ConfigurationEffect.Reset, "Logical arena/brick fixtures are construction geometry."),
        "gameplay.ball.spawnX" or "gameplay.ball.spawnY" or "gameplay.ball.initialAimX" or "gameplay.ball.initialAimY" or "gameplay.breakout.startingBalls" => new(ConfigurationEffect.Future, "Next reset/new ball default; current pose, aim and remaining lives are unchanged."),
        var p when p.StartsWith("gameplay.") => new(ConfigurationEffect.Live, "Existing ball speed/radius or catch/bounce policy; preserve phase, pose, terminal state and separate relaunch."),
        var p when p.StartsWith("input.") => new(ConfigurationEffect.Live, "Deadzone updates without resetting armed/held input latches."),
        var p when p.StartsWith("trails.") => new(ConfigurationEffect.Live, "Retain live-life history; point budget migrates newest bounded samples. No simulation reset."),
        var p when p.StartsWith("audio.") => new(ConfigurationEffect.Live, "Reconfigure audio only when edited; no gameplay reset. Failed-device quarantine persists."),
        var p when p.StartsWith("colors.") || p.StartsWith("style.") => new(ConfigurationEffect.Live, "Presentation updates immediately, including while paused; no simulation tick."),
        _ => new(ConfigurationEffect.Relaunch, "Published renderer ConfigureOverlay is startup-only and exposes no immutable settings update (including HUD layout/enabled); save then relaunch. Colors/style/HUD tints remain live.")
    };

    internal static IEnumerable<(string Path, JsonNode? Value, string[] Keys)> Fields(JsonObject obj, string prefix) =>
        Fields(obj, prefix, []);
    private static IEnumerable<(string Path, JsonNode? Value, string[] Keys)> Fields(JsonObject obj, string prefix, string[] parentKeys)
    {
        // Only enumerated on change. Display/policy paths are not reversible addresses for arbitrary JSON keys.
        foreach (var pair in obj)
        {
            string[] keys = [.. parentKeys, pair.Key];
            if (pair.Value is JsonObject child)
                foreach (var field in Fields(child, prefix + "." + pair.Key, keys)) yield return field;
            else yield return (prefix + "." + pair.Key, pair.Value, keys);
        }
    }
    internal static void Merge(JsonObject target, JsonObject source, string prefix, bool reset)
    {
        foreach (var pair in source)
        {
            string path = prefix + "." + pair.Key;
            if (pair.Value is JsonObject child && target[pair.Key] is JsonObject destination) Merge(destination, child, path, reset);
            else if (For(path).Effect is ConfigurationEffect.Live or ConfigurationEffect.Future || reset && For(path).Effect == ConfigurationEffect.Reset)
                target[pair.Key] = pair.Value?.DeepClone();
        }
    }
}
