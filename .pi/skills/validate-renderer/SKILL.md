---
name: validate-renderer
description: Validate this Vulkan renderer with prerequisite diagnostics, locked restore/build, headless cleanup self-test, and hidden GPU smoke tests and separately permission-gated visible lifecycle checks. Use after renderer lifecycle changes or to diagnose the local Windows toolchain.
compatibility: Windows with PowerShell 7 and .NET SDK; smoke requires Vulkan GPU/driver and SDK validation layers.
---

# Renderer validation

Resolve `../../scripts/support.ps1` relative to this skill directory, regardless of caller cwd. The script resolves the worktree root from its own path; `.git` may be a worktree file. It emits bounded JSON with command/cwd provenance, exit codes, timeout flags, output tails, and separately collected GOG warning lines.

```powershell
pwsh -NoProfile -File <resolved-script> -Operation doctor
pwsh -NoProfile -File <resolved-script> -Operation cleanup
# Hidden GPU test; coordinate exclusive GPU use:
pwsh -NoProfile -File <resolved-script> -Operation smoke
```

- `doctor`: check selected .NET SDK, Git branch/status, glslc resolution/version, Vulkan loader via vulkaninfo, and enumerated VK_LAYER_KHRONOS_validation. PATH is checked before an existing VULKAN_SDK/Bin executable. It does not mutate machine configuration. Missing tooling means failed/unverified prerequisites, not a renderer error. glslc is checked for future shader work but is not used by today's shape renderer.
- `build`: locked restore followed by build --no-restore.
- `cleanup`: build sequence plus `dotnet run --no-build -- --cleanup-self-test`. Headless; no real device-loss injection.
- `smoke`: cleanup sequence plus `dotnet run --no-build -- --smoke-test`. Renders the actual shapes in a hidden GLFW window and exercises resize/resumed presentation, with SDK validation; never add --no-validation. This is GPU-backed, not headless CPU and not minimize/restore coverage. Coordinate with other GPU work so only one smoke run occurs. After lifecycle changes this check is required by AGENTS.md; if prerequisites are missing, report incomplete validation rather than claiming completion.

Visible checks are separate, **only with explicit user permission for desktop/focus disturbance**: `dotnet run --no-build -- --lifecycle-smoke-test` retains resize/minimize/idle/restore recovery (15-second app deadline); `pwsh -NoProfile -File tests/Rendering/resize-smoke.ps1 -Configuration Debug` runs bounded visible external resize stress. Both request nonfocused creation/show; native restore in lifecycle mode can still activate on Windows. Never promise portable zero focus change. The support tool deliberately exposes neither visible action. Do not run them automatically after hidden smoke.

For window metadata evidence, after building use `pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Debug` (and Release). It samples foreground handles and all process top-level windows every 10ms, asserts unchanged foreground/no visible test windows, and has a 25-second process deadline. It never moves user focus. External focus activity makes the observation inconclusive; sampling cannot prove absence of sub-interval transients.

Each child has a timeout (restore/build default 120 seconds each, cleanup 30, smoke 45; doctor checks 10–30). `-TimeoutSeconds` adjusts restore/build/network limits, from 1 to 600 seconds. A failed stage stops later validation stages and returns nonzero (124 for timeout). The support operations do not run CPU shape tests. Run the actual headless project separately in both configurations:

```powershell
dotnet restore Rendering/Shapes/BrutalBreakout.Shapes.csproj --locked-mode
dotnet restore tests/Shapes/Shapes.Tests.csproj --locked-mode
dotnet test tests/Shapes/Shapes.Tests.csproj -c Debug --no-restore
dotnet test tests/Shapes/Shapes.Tests.csproj -c Release --no-restore
```

Use the repository root as cwd; these headless commands do not require GPU opt-in.

If loaded, the optional `brutal_support` tool exposes the same known operation enum only on user request; never run it from hooks. It uses the extension's repository root even when explicitly loaded from another cwd; inspect provenance before use.

## Report

List each command, cwd, exit code, timeout/truncation, and evidence. **Always flag the 3 known existing upstream GOG naming warnings separately** from other build diagnostics and Vulkan validation errors. They may be absent in incremental builds; do not hardcode observed warning/error counts, suppress warnings, or count warnings as validation errors. Report actual new/other warnings and errors. Truncated logs are incomplete evidence; rerun a narrower check or inspect manually. Doctor enumeration is not GPU validation. Cleanup is not a smoke test.
