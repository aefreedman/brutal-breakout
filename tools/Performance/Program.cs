using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace BrutalBreakout.Performance;

internal static class Program
{
#if DEBUG
    private const bool ReleaseBuild = false;
#else
    private const bool ReleaseBuild = true;
#endif
    // All heavy execution is opt-in. Parent launches only the case process; never kills host trees/apps.
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1) throw new ArgumentException("Usage: list|campaign --config DIR --manifest FILE --output DIR [--smoke-ticks 1..120] (case is an internal isolated-child entry)");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal) || !options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Invalid/duplicate option.");
            }
            if (options.Keys.Any(k => k is not ("--config" or "--manifest" or "--output" or "--id" or "--smoke-ticks" or "--child-token"))) throw new ArgumentException("Unknown option.");
            string Required(string key) => Path.GetFullPath(options.TryGetValue(key, out var value) ? value : throw new ArgumentException("Missing " + key));
            string config = Required("--config"), manifestPath = Required("--manifest");
            var manifest = Manifest.Load(manifestPath);
            int? smoke = options.TryGetValue("--smoke-ticks", out var count) ? int.Parse(count, System.Globalization.CultureInfo.InvariantCulture) : null;
            if (smoke is < 1 or > 120) throw new ArgumentException("Smoke must be 1..120 ticks.");
            if (args[0] == "list")
            {
                foreach (var spec in manifest.Cases) Console.WriteLine(JsonSerializer.Serialize(ResolvedCase.Load(spec, manifest.Policy, config, smoke), Manifest.Json));
                return 0;
            }
            if (smoke is null && (!ReleaseBuild || Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0" ||
                Environment.GetEnvironmentVariable("COMPlus_TieredCompilation") == "0"))
                throw new InvalidOperationException("Measured runs require normal Release JIT, not Debug or globally disabled tiering.");
            string output = Required("--output"); Directory.CreateDirectory(output);
            if (args[0] == "campaign")
            {
                if (options.ContainsKey("--id") || options.ContainsKey("--child-token")) throw new ArgumentException("Campaign preserves the complete declared order.");
                return Campaign(manifest, config, output, smoke);
            }
            if (args[0] != "case" || !options.TryGetValue("--id", out var id)) throw new ArgumentException("case requires --id.");
            if (!options.TryGetValue("--child-token", out var childToken) || childToken != Environment.GetEnvironmentVariable("BRUTAL_STUDY_CHILD_TOKEN"))
                throw new InvalidOperationException("case must be launched by the deadline-owning campaign process.");
            var selected = manifest.Cases.Single(c => c.Id == id);
            var resolved = ResolvedCase.Load(selected, manifest.Policy, config, smoke);
            var run = CaseRunner.Run(resolved);
            // No formatting, file IO, logging or percentile sorting inside measured ticks.
            string temporary = Path.Combine(output, id + ".jsonl.partial");
            using (var writer = new StreamWriter(temporary, false))
            {
                writer.WriteLine(JsonSerializer.Serialize(new { kind = "summary", result = run.Result }, Manifest.Json));
                foreach (var raw in run.Raw) writer.WriteLine(JsonSerializer.Serialize(new { kind = "tick", raw }, Manifest.Json));
            }
            File.Move(temporary, Path.Combine(output, id + ".jsonl"), true);
            Console.WriteLine(id + ": " + run.Result.Status);
            return run.Result.Status.EndsWith("failure", StringComparison.Ordinal) ? 2 : 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }
    private static int Campaign(Manifest manifest, string config, string output, int? smoke)
    {
        var campaign = Stopwatch.StartNew();
        // Freeze owned source bytes and input manifest. Child processes never re-read live Config or caller manifest.
        string frozenConfig = Path.Combine(output, "input-config"); Directory.CreateDirectory(frozenConfig);
        foreach (string name in new[] { "physics", "shmup", "execution", "trails", "colors", "style" })
            File.WriteAllText(Path.Combine(frozenConfig, name + ".json"), File.ReadAllText(Path.Combine(config, name + ".json")));
        string frozenManifest = Path.Combine(output, "input-study.json");
        File.WriteAllText(frozenManifest, JsonSerializer.Serialize(new ManifestFile(1, manifest.Policy, manifest.Cases.ToArray()), Manifest.Json));
        // Any invalid resolved case fails before native allocation.
        var cases = manifest.Cases.Select(c => ResolvedCase.Load(c, manifest.Policy, frozenConfig, smoke)).ToArray();
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
        {
            schema = 1, startedUtc = DateTimeOffset.UtcNow, framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, processors = Environment.ProcessorCount,
            build = typeof(Program).Assembly.ManifestModule.ModuleVersionId, jitTieredEnvironment = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            normalReleaseBaseline = ReleaseBuild && !System.Diagnostics.Debugger.IsAttached,
            informationalVersion = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            smokeTicks = smoke, cases
        }, Manifest.Json));
        using var log = new StreamWriter(Path.Combine(output, "campaign.jsonl"), false) { AutoFlush = true };
        foreach (var c in cases)
        {
            double remaining = manifest.Policy.CampaignWallSeconds - campaign.Elapsed.TotalSeconds;
            if (remaining <= 0)
            {
                log.WriteLine(JsonSerializer.Serialize(new { id = c.Spec.Id, status = "campaign-budget-skip", elapsedSeconds = campaign.Elapsed.TotalSeconds }, Manifest.Json));
                continue;
            }
            // No stale result can masquerade as a final output from this child.
            string final = Path.Combine(output, c.Spec.Id + ".jsonl");
            File.Delete(final); File.Delete(final + ".partial");
            var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); psi.ArgumentList.Add("case");
            string childToken = Guid.NewGuid().ToString("N"); psi.Environment["BRUTAL_STUDY_CHILD_TOKEN"] = childToken;
            foreach (var pair in new[] { ("--config", frozenConfig), ("--manifest", frozenManifest), ("--output", output), ("--id", c.Spec.Id), ("--child-token", childToken) })
            { psi.ArgumentList.Add(pair.Item1); psi.ArgumentList.Add(pair.Item2); }
            if (smoke is { } ticks) { psi.ArgumentList.Add("--smoke-ticks"); psi.ArgumentList.Add(ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            long start = Stopwatch.GetTimestamp();
            using var child = Process.Start(psi) ?? throw new InvalidOperationException("Could not launch isolated case process.");
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            int milliseconds = Math.Max(1, (int)Math.Floor(Math.Min(c.Policy.CaseWallSeconds, remaining) * 1000));
            bool exited = child.WaitForExit(milliseconds);
            if (!exited)
            {
                try { child.Kill(entireProcessTree: false); }
                catch (InvalidOperationException) when (child.HasExited) { /* Deadline/exit race; still record timeout. */ }
                child.WaitForExit();
            }
            string standardOutput = stdout.GetAwaiter().GetResult(), standardError = stderr.GetAwaiter().GetResult();
            string status = !exited ? "timeout" : child.ExitCode != 0 ? "case-process-failure" : File.Exists(final) ? "final-output" : "missing-final-output";
            log.WriteLine(JsonSerializer.Serialize(new { id = c.Spec.Id, status, pid = child.Id, exitCode = child.ExitCode,
                wallMs = StudyController.Milliseconds(Stopwatch.GetTimestamp() - start), standardOutput, standardError,
                hasFinalOutput = File.Exists(final) }, Manifest.Json));
            // A launch/runtime failure is a safety stop, not an invitation to try alternate launchers/settings.
            if (status is "case-process-failure" or "missing-final-output") return 2;
        }
        return 0;
    }
}
