# brutal-breakout

A Windows-first 2D game built with C# and [BRUTAL](https://github.com/ahwootech/BRUTAL).

## Requirements

- .NET SDK 10.0.201 (or a newer patch in the same feature band; see `global.json`).
- Windows for the currently tested setup.
- A Vulkan 1.3-capable GPU and driver, with a combined graphics/present queue and swapchain transfer-destination support (required by this first clear-only implementation).
- Vulkan SDK validation layers for Debug development and smoke tests. The SDK is not required for players; the GPU driver supplies the Vulkan runtime.

BRUTAL packages are restored from NuGet; cloning or building the framework source is not required.

## Build and run

From this directory:

```powershell
dotnet restore --locked-mode
dotnet build --no-restore
dotnet run --no-build
```

Expected result: a 1280 x 720 window titled **Brutal Breakout**, cleared to dark blue through Vulkan. Resize/minimize/restore it, then close it using the window close button. No shapes or gameplay exist yet.

Debug builds enable `VK_LAYER_KHRONOS_validation` by default and fail startup if it is unavailable. Use `dotnet run -- --no-validation` to opt out. Release builds enable validation only with `--validation` (or `--smoke-test`).

### Validation

```powershell
dotnet run -- --smoke-test
dotnet run -c Release -- --smoke-test
dotnet run -- --cleanup-self-test
```

The smoke test enables validation, requires successful presentations after resize and restore, and verifies that rendering stays idle while minimized. It exits automatically, with a 15-second lifecycle deadline. Validation errors produce a nonzero exit code; warnings are logged and counted.

The cleanup self-test injects failures into the resource-lifetime helper. It does not induce real GPU device loss.

## Project-local Pi support

Review `.pi/` before granting project trust. Start Pi **in this repository/worktree** and approve trusted project resources; use `/reload` after edits. A session started in `C:\Users\aaron\dev` does not automatically discover this child repo's resources: restart here, or explicitly load `.pi/extensions/brutal-support.ts` with `--extension` and each skill directory with `--skill` (absolute paths from another cwd). No settings, global packages, or auto-run hooks are installed.

- `/skill:brutal-package-audit`: published NuGet first, with required symbol/compile-probe evidence and a reuse/gap table. Metadata/assets alone are **not** API verification; no automatic reference edits.
- `/skill:validate-renderer`: doctor, locked restore/build, headless cleanup test, and explicit opt-in GPU smoke. Always report the 3 known GOG naming warnings separately from validation errors; observed counts vary. Doctor may also show unrelated loader warnings.
- Optional `brutal_support` tool: the same six bounded operations below, only on user request; `smoke` requires explicit permission for a visible GPU window and SDK validation.

Scripts also work without Pi, from any cwd (Windows, PowerShell 7):

```powershell
pwsh -NoProfile -File <repo>/.pi/scripts/support.ps1 -Operation doctor
pwsh -NoProfile -File <repo>/.pi/scripts/support.ps1 -Operation build
pwsh -NoProfile -File <repo>/.pi/scripts/support.ps1 -Operation cleanup
pwsh -NoProfile -File <repo>/.pi/scripts/support.ps1 -Operation smoke # visible window; coordinate GPU use
pwsh -NoProfile -File <repo>/.pi/scripts/support.ps1 -Operation package-versions -PackageId Ahwoo.BRUTAL.Core
pwsh -NoProfile -File <repo>/.pi/scripts/support.ps1 -Operation package-assets -PackageId Ahwoo.BRUTAL.Core -Version 2026.10.4
```

`cleanup` includes build; `smoke` includes cleanup. JSON reports root/caller cwd, bounded output tails, timeouts, and nonzero failures. Processes are bounded per stage; the tool adds a six-minute deadline and cancellation of the Windows process tree. Package downloads use public NuGet, are disposable, and require network access. Doctor checks PATH before existing SDK executable paths; it changes no machine configuration. Enumeration does not replace a GPU smoke test. Abrupt process-tree cancellation may leave a temporary download directory. Output truncation means incomplete evidence; there is no full-log artifact or automatic API probe execution.

Support regression checks (no interactive Pi, GPU, or new dependencies):

```powershell
pwsh -NoProfile -File <repo>/.pi/tests/process.tests.ps1
node <repo>/.pi/tests/extension.test.mjs <installed-pi-package-root>
```

The second check uses the installed Pi loader/API and runs doctor plus cancellation checks; it requires a configured local Vulkan toolchain. Validated with Pi 1.4.0; it is not a standalone TypeScript compiler check.

## How this differs from Unity

- `Program.cs` owns initialization, event processing, and shutdown. There is no Unity player managing the lifecycle.
- GLFW creates the native window and handles OS events. It does not draw the game.
- The main thread polls OS events and renders. While minimized it waits briefly for events to avoid busy-spinning. Gameplay will need a fixed-step simulation with explicit time handling.
- Vulkan explicitly acquires a swapchain image, records clear/layout-transition commands, submits them, and presents. A fence protects command-buffer reuse; semaphores coordinate acquisition/presentation.
- BRUTAL is a framework: scenes, entities, asset loading, and rendering are our responsibility. We will add only what this game needs.

## Project files

- `Program.cs`: main-thread application lifecycle and bounded smoke/self-tests.
- `Rendering/VulkanRenderer.cs`: instance/device selection, surface, swapchain, clear commands, synchronization, and resource disposal.
- `Rendering/ResourceLifetime.cs`: best-effort cleanup that preserves original failures alongside cleanup errors.
- `brutal-breakout.csproj`: target framework, C# settings, and direct package references.
- `global.json`: SDK selection.
- `packages.lock.json`: resolved direct and transitive dependency versions; commit changes when intentionally updating packages.
- `.gitignore`: excludes generated build output and local IDE files.
- `.gitattributes`: source text normalization.

## Dependencies

Direct references:

- `Ahwoo.BRUTAL.Core` 2026.10.4
- `Ahwoo.BRUTAL.Glfw` 2026.10.5
- `Ahwoo.BRUTAL.Vulkan` 2026.10.3
- `Ahwoo.BRUTAL.Vulkan.Abstractions` 2026.10.3

GLFW's native library and other BRUTAL dependencies arrive transitively. The current dependency graph also includes a preview version of `Microsoft.Extensions.ObjectPool`; see the lock file. We have not added that preview package directly.

Vulkan is the selected graphics API. This milestone needs no shader compiler because it clears images using transfer commands, rather than drawing geometry. Shader compilation comes with the next graphics pipeline milestone.

## Git

The repository uses `main` and a private GitHub remote at `https://github.com/aefreedman/brutal-breakout.git` (HTTPS). Work on feature branches and use focused subagents for implementation/review; validate and review before merging into `main`.

Useful commands:

```powershell
git status
git log --oneline
git diff
```

## Release checklist

Before distributing a build, review the current [BRUTAL license](https://github.com/ahwootech/BRUTAL) and all shipped third-party notices. BRUTAL documents requirements for copyright attribution in credits, its logo before gameplay, and a shipped `NOTICE.txt`. Those release assets are not implemented in this rendering milestone.

## Next milestone

Add a small Vulkan graphics pipeline for abstract 2D shapes, then gamepad input and a timed simulation loop. Gameplay direction is direct ball catch/aim/relaunch, informed by PrototypeStudio, not an assumed paddle game. Final interaction rules remain to be agreed.

Planning files live outside this repository at `C:\Users\aaron\obs-mono\vaults\personal\brutal-breakout`, named `YY-MM-DD-kebab-case.md`. Avoid building a general-purpose engine ahead of the game.
