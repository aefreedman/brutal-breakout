# Development and repository guide

All commands in these guides run from the game repository root, not the document directory. See the [README](../../README.md) for requirements and the local-package quick start.

## Repository layout

- `Assets/`: source audio, images, fonts and their provenance/licenses; no application C#.
- `Config/`: designer-facing authoritative JSON; alternate palettes live in `Config/ColorSchemes/`.
- `docs/`: maintained [engineering, design and player guides](../README.md).
- `src/App/`: executable composition and platform/render loop.
- `src/Runtime/Game/`: `BrutalBreakout.Runtime` assembly, configuration loading, game presentation, HUD and sessions.
- `src/Runtime/{Simulation,Input,Audio}/`: existing independent runtime assemblies.
- `src/Editor/`: optional `BrutalBreakout.Editor` assembly, inspector configuration, toggle and capture routing. Runtime has no Editor or ImGui dependency.
- `tools/Showcase/`: primitive-gallery assembly used by development modes.
- `tests/Runtime/`, `tests/Editor/`: CPU/offscreen suites; `tests/Fixtures/`: test-only configuration; `tests/Rendering/`: GPU scripts and scripted gameplay scenario assembly.
- `BrutalBreakout.slnx`: all projects. Package locks live beside their owning projects.

Build/publish output preserves `Config/` and `Assets/`. Default configuration paths resolve beneath the executable's `Config/`, independent of working directory; explicit overrides remain relative to the caller. Asset paths resolve relative to their JSON file.

## Optional editor

Normal development builds include the F1 / gamepad Select/Share diagnostics inspector and game-mode controls. HUD text is configured independently in `Config/overlay.json`; editor font, layout, initial controls/diagnostics visibility and frame-history capacity are configured in `Config/inspector.json` (`--inspector-config <path>`). Settings are validated before native initialization and passed unchanged to their consumers. Normal controls require no PNG; the overlay smoke opts into its own explicit image fixture. Configuration uses the kit's opt-in category/property-row helpers with application-owned selection and group/window state; actions and navigation remain outside the selected scrolling body. A disposable net10.0 compile probe against exact published `Ahwoo.BRUTAL.ImGui [2026.10.3]` from nuget.org verified public `BeginChild(ImString, float2, ImGuiChildFlags, ImGuiWindowFlags)` / `EndChild()` with zero warnings/errors before adoption; this proves API shape, not visual quality.

Inspector appearance and feedback preferences are required authoritative JSON. The same immutable `InspectorTheme` is supplied to the kit's four-argument inspector constructor and application controls. Per-field cached `ToolProperty`, numeric/help/action descriptions are presentation metadata only; production loaders remain validation authority. Source recovery uses `Document`'s parsed selected-source snapshot and literal key segments; source revision changes invalidate metadata after Save/Reload. Operation outcomes have explicit sequences, so repeated identical explicit commands are observable while idle/successful automatic edits are quiet. Feedback posting/expiry and clipboard requests run on owner updates, not UI callbacks.

A disposable net10.0 library probe using SDK 10.0.201 referenced exact published `Ahwoo.BRUTAL.Glfw [2026.10.5]` from `https://api.nuget.org/v3/index.json` and compiled public `Brutal.GlfwApi.Glfw.SetClipboard(string)` with zero warnings/errors. The host reuses this main-thread API for queued owned JSON text; the probe did not run GLFW or disturb the actual clipboard. A separate compile-only net10.0 probe against exact published `Ahwoo.BRUTAL.ImGui [2026.10.3]` from the same source verified public `ImGui.InputTextMultiline(ImString, ReadOnlySpan<byte>, in float2?, ImGuiInputTextFlags, ImGuiInputTextCallback?, Ptr)` with `ReadOnly` (zero warnings/errors). Persistent diagnostics cache sanitized NUL-terminated UTF-8 on text changes and reuse this native selectable/scrolling control in Help; raw managed failures remain intact, while fixed summaries keep short windows reachable. No native probe executable was run. Native property/help/action/theme facilities are reused through the kit's documented published ImGui audit rather than copied Special K code.

Build without editor code, ImGui packages or inspector configuration/asset validation:

```powershell
dotnet restore src/App/BrutalBreakout.App.csproj -p:EnableEditor=false --locked-mode
dotnet build src/App/BrutalBreakout.App.csproj -c Release -p:EnableEditor=false --no-restore
dotnet publish src/App/BrutalBreakout.App.csproj -c Release -p:EnableEditor=false --no-restore -o artifacts/shipping
```

All generated outputs are centralized under ignored `artifacts/`: `bin/<project>/<configuration>/` for binaries, `obj/<project>/` for restore/build intermediates, and `publish/<project>/<configuration>/` for default publish output. Configuration names are lowercase (for example, `debug` and `release`).

The editor-free app uses its own `src/App/packages.shipping.lock.json` and `BrutalBreakout.App.shipping` artifact directories, keeping its binaries and restore intermediates separate from editor-enabled builds. Inspector-only options are rejected in this build. The separate app restore directories allow switching between restored editor-enabled and editor-free builds without overwriting either app's dependency graph. Publish to a fresh directory when changing build variants; incremental publishing does not remove old files.

## Edit CrushKit locally

Public checkout/CI builds use the exact CrushKit source commit in `.github/crushkit-revision`, with `UseCrushKitProjects=true` and an explicit `CrushKitRoot`. The [README source quick start](../../README.md#quick-start-source-build) does not require a local feed. Committed `packages.source.lock.json` files lock this graph; do not regenerate ordinary package locks when changing source references. `.github/scripts/ci.ps1` checks the kit commit, restores in locked mode, builds and runs the headless test baseline. It is source integration evidence, not package-consumer or GPU validation.

The existing package mode remains the default when the source property is absent: `CrushKitImGuiVersion` is `0.1.0-preview.16` and the other modules use `CrushKitVersion` `0.1.0-preview.12`. These packages are unpublished; use the exact existing local packages rather than repacking those immutable versions. Public package hosting/release is deferred.

For coordinated source edits, opt into neighboring project references without changing normal package lock files:

```powershell
dotnet restore -p:UseCrushKitProjects=true -p:NuGetLockFilePath=packages.dev.lock.json
dotnet build --no-restore -p:UseCrushKitProjects=true -p:NuGetLockFilePath=packages.dev.lock.json
```

Set `CrushKitRoot` explicitly if the kit checkout is elsewhere. Use the same properties on test/run commands. Explicit `packages.dev.lock.json` files are ignored for private coordinated edits; omitting that override selects the committed source locks. Switch back to package mode with normal locked restore and the existing local feed. Do not overwrite a shared/published NuGet version: bump it and update consumer lock files. Repacking an unpublished version requires deliberately refreshing only that version's kit cache entries and reevaluating its lock hashes; the kit's `tools/verify-packages.ps1` avoids stale-cache ambiguity with an isolated consumer cache.

The renderer/geometry/shaders and optional inspector now live in CrushKit, not this repository. Breakout retains gameplay, configuration loading/JSON, overlay label/diagnostic content, input bindings/aim priorities, audio cue/event mapping and licensed game assets. CrushKit owns raw input sampling/edge/deadzone helpers and generic clip/device/bounded-voice mechanics; Input/Audio depend on neither graphics nor one another. The renderer no longer knows about balls/bricks; `OverlayHost` supplies those details as text. The kit's Vulkan package alone has no ImGui dependency; this application explicitly configures `ImGuiInspector` with its separately validated `InspectorSettings`; text `OverlaySettings` no longer require any inspector assets. HUD and inspector JSON are independently strict; their immutable settings objects are passed unchanged to their respective consumers.

## Project files and dependencies

- `src/App/Program.cs`: main-thread lifecycle and bounded smoke/self-tests.
- `tools/Showcase/ShapeShowcase.cs`: fitted twelve-panel demo (`--showcase`).
- `src/Runtime/Game/Presentation/BallSandbox.cs`, `src/Runtime/Game/Sessions/BallSandboxSession.cs` and `tests/Rendering/BallSandboxSmoke.cs`: arena geometry, pure session/viewport bridge and hidden scripted scenario.
- `Config/physics.json`, `Config/execution.json`, `Config/gameplay.json`, `Config/input.json`, `src/Runtime/Game/Configuration/`: authoritative production defaults, strict nested loading, field-level live editing, independent CLI overrides and pre-native cross-validation.
- `src/Runtime/Simulation/`: Arch ball/brick/round components, the game dependency graph, immutable publications, validated settings, native arena adapter, gameplay rules, radius-aware casts/preview and fixed-step clock.
- Independent `crushkit-brutal/src/CrushKit.Brutal.Ecs/` and `CrushKit.Brutal.Physics/`: reusable Arch/job ownership and Box2D worker-task integration; no game rules or JSON.
- `src/Runtime/Input/`: Breakout bindings, input schema and aim/action router over CrushKit device sampling/edges.
- `src/Runtime/Audio/`: ten-cue schema and simulation-event policy over CrushKit SDL.Mixer/voice ownership; `src/Runtime/Game/Presentation/Overlay/` and `src/Editor/Inspector/`: game labels and inspector content.
- Independent `crushkit-brutal/src/CrushKit.Brutal.Input/` and `CrushKit.Brutal.Audio/`: reusable input/audio mechanics and headless tests.
- `tests/Runtime/Gameplay/`, `tests/Runtime/Simulation/`, `tests/Runtime/Input/`, `tests/Runtime/Audio/`, `tests/Runtime/Media/`: CPU/offscreen suites; `tests/Rendering/hidden-smoke.ps1`: shared hidden GPU harness.
- Independent `crushkit-brutal/src/CrushKit.Brutal.Draw2D/`: headless geometry/contracts/colors/clips and borrowed buffers; CPU geometry tests live with the kit.
- Independent `crushkit-brutal/src/CrushKit.Brutal.Vulkan/`: lifecycle, embedded shaders, uploads, MSAA, text/texture support and best-effort cleanup.
- Independent `crushkit-brutal/src/CrushKit.Brutal.ImGui/`: optional inspector integration.
- `BrutalBreakout.slnx`, `src/App/BrutalBreakout.App.csproj`, `global.json` and per-project `packages.lock.json` files: dependencies, SDK and locked graph.

Dependency versions are authoritative in the project files and `packages.lock.json`. The application uses BRUTAL Core/GLFW/Vulkan/VMA/ShaderC, SDL.Ttf, Texture abstractions and ImGui backends; `src/Runtime/Audio/` uses SDL.Mixer, `src/Runtime/Simulation/` consumes the Physics module's pinned Box2D2026.10.2, and CrushKit's Draw2D package uses Numerics/Render.Mesh. Native dependencies arrive transitively. The locked graph includes a transitive preview `Microsoft.Extensions.ObjectPool`, not a direct application dependency.

## Repository and release

The public GitHub source repository uses HTTPS: `https://github.com/aefreedman/brutal-breakout.git`. Its compatible source dependency is https://github.com/aefreedman/crushkit-brutal. Source publication retains existing history; normal Git author metadata and historical machine paths are intentionally retained with owner approval.

Original Breakout material is GPL-3.0-or-later with the BRUTAL linking exception; see [licensing scope](../../LICENSING.md). The app copies its licence, exception and scope notice into build/publish output. Release still requires corresponding-source delivery and exact dependency/provenance review; existing asset and third-party licences remain unchanged.

Before distribution review the current [BRUTAL license](https://github.com/ahwootech/BRUTAL) and all third-party notices. BRUTAL requires copyright attribution in credits, its logo before gameplay, and a shipped `NOTICE.txt`; those release assets are not implemented here.
