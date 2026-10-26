using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Numerics;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;

namespace BrutalBreakout.Performance;

/// <summary>Native-world-free CPU geometry study. Synthetic fixed publications are not physics replay.
/// Frozen owned frames allow a separate renderer/GPU experiment without simulation or tessellation.</summary>
internal static class RenderCpuStudy
{
    internal const uint FrameMagic = 0x31524642;
    internal sealed record Variant(string Id, int Balls, int Width, int Height, bool Trails);
    internal static Variant[] Variants() =>
    [
        ..new[] { 500, 1000, 2000, 4000 }.SelectMany(n => new[] { true, false }
            .Select(t => new Variant($"balls-{n}-{(t ? "trails" : "no-trails")}-720", n, 1280, 720, t))),
        ..new[] { (1920, 1080), (3840, 2160) }.SelectMany(size => new[] { true, false }
            .Select(t => new Variant($"balls-2000-{(t ? "trails" : "no-trails")}-{size.Item2}", 2000, size.Item1, size.Item2, t))),
        new("balls-2000-trails-720-repeat", 2000, 1280, 720, true)
    ];

    // Allocations and fixed-tick trail sampling occur only during setup, never geometry measurement.
    internal static (ShmupSnapshot Snapshot, BallTrailHistory? History) Fixture(ResolvedCase c, Layout layout, int count, bool trails)
    {
        if (count < 1 || count > layout.Balls.Count) throw new ArgumentOutOfRangeException(nameof(count));
        var settings = c.Settings;
        var history = trails ? new BallTrailHistory(c.Trails ?? throw new InvalidDataException("Missing trail settings."), settings.Capacity.MaxBalls) : null;
        int samples = c.Trails?.MaxPointsPerBall ?? 1;
        var controller = new ShmupAvatarController(settings); controller.Reset();
        ShmupSnapshot? snapshot = null;
        for (int tick = 0; tick < samples; tick++)
        {
            var balls = new ShmupBallState[count];
            for (int i = 0; i < count; i++)
            {
                var p = layout.Balls[i];
                double x = Reflect(p.X - (samples - 1 - tick) * settings.Ball.Speed * c.Physics.StepSeconds,
                    settings.Ball.Radius, settings.Arena.Width - settings.Ball.Radius);
                balls[i] = new(i, 1, new(x, p.Y), new(settings.Ball.Speed, 0), settings.Ball.Radius, 0);
            }
            // Each publication transfers fresh arrays. No snapshot or borrowed frame storage is recycled.
            var bricks = layout.Bricks.Select((b, i) => new ShmupBrickState(i, 1, b.Position,
                settings.Brick.Size, settings.Brick.Health, b.Shape, b.Turns, settings.Brick.Health)).ToArray();
            snapshot = new(settings.Arena.InitialPlayAreaWidth, settings.Arena.Height, controller.Position,
                new(0, -1), settings.Avatar.Health, (tick + 1) * c.Physics.StepSeconds, settings.Ball.Speed,
                settings.Difficulty.DescentSpeed, settings.Difficulty.RowInterval, 0, tick + 1, 0, 0, 0,
                count + bricks.Length + 1, count + bricks.Length + 3, balls, bricks,
                playerMaximumHealth: settings.Avatar.Health);
            history?.Capture(snapshot);
        }
        return (snapshot!, history);
    }
    private static double Reflect(double x, double minimum, double maximum)
    {
        double length = maximum - minimum;
        double phase = (x - minimum) % (2 * length);
        if (phase < 0) phase += 2 * length;
        return minimum + (phase <= length ? phase : 2 * length - phase);
    }

    internal static void WriteFrame(Stream stream, ShapeFrame frame)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        writer.Write(FrameMagic); writer.Write(frame.Width); writer.Write(frame.Height);
        writer.Write(frame.Vertices.Length); writer.Write(frame.Indices.Length); writer.Write(frame.Commands.Length);
        foreach (var v in frame.Vertices.Span)
        {
            writer.Write(v.Position.X); writer.Write(v.Position.Y);
            writer.Write(v.Color.X); writer.Write(v.Color.Y); writer.Write(v.Color.Z); writer.Write(v.Color.W);
        }
        foreach (uint i in frame.Indices.Span) writer.Write(i);
        foreach (var command in frame.Commands.Span)
        {
            writer.Write(command.FirstIndex); writer.Write(command.IndexCount);
            writer.Write(command.Clip.Left); writer.Write(command.Clip.Top); writer.Write(command.Clip.Right); writer.Write(command.Clip.Bottom);
        }
    }

    internal static int Run(Manifest manifest, string config, string output, int? smoke)
    {
        Directory.CreateDirectory(output);
        string frozen = Path.Combine(output, "input-config"); Directory.CreateDirectory(frozen);
        foreach (string name in new[] { "physics", "shmup", "execution", "trails", "colors", "style" })
            File.WriteAllText(Path.Combine(frozen, name + ".json"), File.ReadAllText(Path.Combine(config, name + ".json")));
        // Reuse only the approved layout/settings policy, not the physics campaign case selection.
        var resolved = ResolvedCase.Load(new CaseSpec { Id = "render-fixture", Target = 4000 }, manifest.Policy, frozen, smoke);
        var layout = new Layout(resolved.Settings, manifest.Policy);
        File.WriteAllText(Path.Combine(output, "metadata.json"), JsonSerializer.Serialize(new
        {
            scope = "CPU geometry only; synthetic reflected-horizontal fixed-tick trails; no native physics world, GPU or frame pacing",
            schema = 1, startedUtc = DateTimeOffset.UtcNow,
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            build = typeof(RenderCpuStudy).Assembly.ManifestModule.ModuleVersionId,
            source = typeof(RenderCpuStudy).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            resolved, variants = Variants(), warmBuilds = smoke.HasValue ? 0 : 200, measuredBuilds = smoke ?? 400,
            maxCaseSeconds = manifest.Policy.CaseWallSeconds, maxCampaignSeconds = 120,
            normalJit = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), smoke
        }, Manifest.Json));
        string frames = Path.Combine(output, "frames"); Directory.CreateDirectory(frames);
        var campaign = Stopwatch.StartNew();
        using var results = new StreamWriter(Path.Combine(output, "cpu.jsonl"), false) { AutoFlush = true };
        foreach (var variant in Variants())
        {
            var clock = Stopwatch.StartNew();
            if (campaign.Elapsed.TotalSeconds >= 118)
            {
                results.WriteLine(JsonSerializer.Serialize(new { variant, status = "campaign-budget-skip" }, Manifest.Json)); continue;
            }
            var memory = MemoryGuard.Sample();
            if (!MemoryGuard.Allows(memory, manifest.Policy, resolved.StorageEstimateBytes, true))
            {
                results.WriteLine(JsonSerializer.Serialize(new { variant, status = "memory-skip", memory }, Manifest.Json)); continue;
            }
            var fixture = Fixture(resolved, layout, variant.Balls, variant.Trails);
            using var draw = new ShapeDrawList();
            ShapeFrame Build() => ShmupPresentation.Build(draw, fixture.Snapshot, resolved.Settings,
                resolved.Colors, resolved.Style, variant.Width, variant.Height, fixture.History);
            int warm = smoke.HasValue ? 0 : 200;
            int warmDone = 0;
            bool Fits() => clock.Elapsed.TotalSeconds < manifest.Policy.CaseWallSeconds - manifest.Policy.CleanupReserveSeconds && campaign.Elapsed.TotalSeconds < 118;
            for (; warmDone < warm && Fits(); warmDone++) Build();
            var samples = new double[smoke ?? 400];
            long allocation = GC.GetAllocatedBytesForCurrentThread();
            var pause = GC.GetTotalPauseDuration();
            int completed = 0;
            for (; warmDone == warm && completed < samples.Length && Fits(); completed++)
            {
                long before = Stopwatch.GetTimestamp();
                Build();
                samples[completed] = Stopwatch.GetElapsedTime(before).TotalMilliseconds;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocation;
            double pauseMs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds;
            // Export separately and synchronously before any producer reuse. Formatting/I/O are not geometry samples.
            var frame = Build();
            string file = Path.Combine(frames, variant.Id + ".frame");
            using (var stream = File.Create(file)) WriteFrame(stream, frame);
            string hash;
            using (var stream = File.OpenRead(file)) hash = Convert.ToHexString(SHA256.HashData(stream));
            string status = warmDone != warm ? "warm-truncated" : completed != samples.Length ? "measure-truncated" : "completed";
            results.WriteLine(JsonSerializer.Serialize(new
            {
                variant, status, warmDone, measured = Distribution.Of(samples.Take(completed)),
                rawGeometryMs = samples.Take(completed).ToArray(), allocatedBytes = allocated, globalGcPauseMs = pauseMs, historyPoints = fixture.History?.PointCount ?? 0,
                vertices = frame.Vertices.Length, indices = frame.Indices.Length, commands = frame.Commands.ToArray(),
                geometryUploadBytes = (long)frame.Vertices.Length * 24 + (long)frame.Indices.Length * 4,
                setupAndStudyWallMs = clock.Elapsed.TotalMilliseconds, frameSha256 = hash, memory
            }, Manifest.Json));
            Console.WriteLine(variant.Id + ": " + status);
        }
        return 0;
    }
}
