import { spawn } from "node:child_process";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { Type } from "typebox";
import { defineTool, type ExtensionAPI } from "@earendil-works/pi-coding-agent";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const script = resolve(repositoryRoot, ".pi/scripts/support.ps1");
const operations = ["doctor", "build", "cleanup", "smoke", "package-versions", "package-assets"] as const;

export default function (pi: ExtensionAPI) {
  pi.registerTool(defineTool({
    name: "brutal_support",
    label: "Brutal project support",
    description: "Only on user request: run a bounded project support operation, not arbitrary shell. doctor checks prerequisites; build performs locked restore/build; cleanup adds headless cleanup self-test; smoke additionally runs hidden GPU shape/resize validation, not minimize/restore coverage. No visible lifecycle/stress operations are exposed; those require separate explicit user permission. Package operations query published NuGet metadata/assets, NOT API verification. No reference edits. Windows/PowerShell 7 required. Output includes script-root repository and caller cwd; capped at 32K characters.",
    parameters: Type.Object({
      operation: Type.Union(operations.map((operation) => Type.Literal(operation))),
      packageId: Type.Optional(Type.String({ pattern: "^[Aa][Hh][Ww][Oo][Oo]\\.[Bb][Rr][Uu][Tt][Aa][Ll]\\.[A-Za-z0-9_.-]+$" })),
      version: Type.Optional(Type.String({ pattern: "^[0-9]+\\.[0-9]+\\.[0-9]+(?:[.-][A-Za-z0-9.-]+)?$" })),
    }),
    outputSchema: Type.Object({
      operation: Type.String(), repositoryRoot: Type.String(), callerCwd: Type.String(),
      exitCode: Type.Number(), cancelled: Type.Boolean(), outputTruncated: Type.Boolean(), output: Type.String(),
    }),
    async execute(_id, params, signal, _onUpdate, ctx) {
      if (process.platform !== "win32") throw new Error("This adapter requires Windows and PowerShell 7; use the scripts directly on supported hosts.");
      // Keep a runtime allowlist even when called outside Pi's schema validator.
      if (!operations.includes(params.operation)) throw new Error("Unknown support operation.");
      if (params.operation.startsWith("package-") && !params.packageId) throw new Error("packageId required.");
      if (params.operation === "package-assets" && !params.version) throw new Error("version required.");
      if (signal?.aborted) throw new Error("Cancelled before launch.");
      const args = ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, "-Operation", params.operation];
      if (params.packageId) args.push("-PackageId", params.packageId);
      if (params.version) args.push("-Version", params.version);
      const result = await new Promise<{ code: number; cancelled: boolean; truncated: boolean; output: string }>((complete) => {
        const child = spawn("pwsh", args, { cwd: repositoryRoot, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
        let output = "";
        let truncated = false;
        let cancelled = false;
        let settled = false;
        let killDeadline: ReturnType<typeof setTimeout> | undefined;
        let drainDeadline: ReturnType<typeof setTimeout> | undefined;
        const append = (chunk: Buffer | string) => {
          output += chunk.toString();
          if (output.length > 32000) { output = output.slice(-32000); truncated = true; }
        };
        const finish = (code: number) => {
          if (settled) return;
          settled = true;
          clearTimeout(deadline);
          if (killDeadline) clearTimeout(killDeadline);
          if (drainDeadline) clearTimeout(drainDeadline);
          signal?.removeEventListener("abort", abort);
          child.stdout?.destroy();
          child.stderr?.destroy();
          complete({ code, cancelled, truncated, output });
        };
        const stopTree = () => {
          if (settled || killDeadline) return;
          // Pi's generic exec cancellation only kills its immediate child. On Windows
          // use taskkill /T so dotnet and the hidden smoke process cannot be orphaned.
          if (child.pid) {
            const killer = spawn("taskkill.exe", ["/PID", String(child.pid), "/T", "/F"], { windowsHide: true, stdio: "ignore" });
            killer.on("error", () => { child.kill(); });
            killer.on("exit", (code) => { if (code !== 0) child.kill(); });
            killDeadline = setTimeout(() => { killer.kill(); child.kill(); finish(cancelled ? 130 : 124); }, 5000);
          } else finish(cancelled ? 130 : 124);
        };
        const abort = () => { cancelled = true; stopTree(); };
        const deadline = setTimeout(() => { append("\nAdapter deadline exceeded.\n"); stopTree(); }, 360000);
        child.stdout?.on("data", append);
        child.stderr?.on("data", append);
        child.on("error", (error) => { append(error.message); finish(1); });
        const exitCode = (code: number | null) => cancelled ? 130 : killDeadline ? 124 : code ?? 1;
        child.on("close", (code) => finish(exitCode(code)));
        child.on("exit", (code) => {
          drainDeadline = setTimeout(() => { truncated = true; finish(exitCode(code)); }, 1000);
        });
        signal?.addEventListener("abort", abort, { once: true });
        if (signal?.aborted) abort();
      });
      const summary = {
        operation: params.operation, repositoryRoot, callerCwd: ctx.cwd,
        exitCode: result.code, cancelled: result.cancelled, outputTruncated: result.truncated, output: result.output,
      };
      return {
        content: [{ type: "text", text: JSON.stringify(summary) }],
        details: summary, structuredContent: summary, isError: result.code !== 0,
      };
    },
  }));
}
