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

Build without editor code, ImGui packages or inspector configuration/asset validation:

```powershell
dotnet restore src/App/BrutalBreakout.App.csproj -p:EnableEditor=false --locked-mode
dotnet build src/App/BrutalBreakout.App.csproj -c Release -p:EnableEditor=false --no-restore
dotnet publish src/App/BrutalBreakout.App.csproj -c Release -p:EnableEditor=false --no-restore -o artifacts/shipping
```

All generated outputs are centralized under ignored `artifacts/`: `bin/<project>/<configuration>/` for binaries, `obj/<project>/` for restore/build intermediates, and `publish/<project>/<configuration>/` for default publish output. Configuration names are lowercase (for example, `debug` and `release`).

The editor-free app uses its own `src/App/packages.shipping.lock.json` and `BrutalBreakout.App.shipping` artifact directories, keeping its binaries and restore intermediates separate from editor-enabled builds. Inspector-only options are rejected in this build. The separate app restore directories allow switching between restored editor-enabled and editor-free builds without overwriting either app's dependency graph. Publish to a fresh directory when changing build variants; incremental publishing does not remove old files.

## Edit CrushKit locally

Normal builds consume real packages. The changed ImGui module is pinned through `CrushKitImGuiVersion` (`0.1.0-preview.13`); the other kit modules use `CrushKitVersion` (`0.1.0-preview.12`). Build the matching kit branch's packages into the configured local feed before normal locked restore. During isolated worktree development, an explicit temporary NuGet configuration may point at the owned kit package output; never overwrite another session's shared preview packages.

For coordinated source edits, opt into neighboring project references without changing normal package lock files:

```powershell
dotnet restore -p:UseCrushKitProjects=true -p:NuGetLockFilePath=packages.dev.lock.json
dotnet build --no-restore -p:UseCrushKitProjects=true -p:NuGetLockFilePath=packages.dev.lock.json
```

Set `CrushKitRoot` explicitly if the kit checkout is elsewhere. Use the same properties on test/run commands. Development lock files are ignored; switch back with normal locked restore. Do not overwrite a shared/published NuGet version: bump it and update consumer lock files. Repacking an unpublished version requires deliberately refreshing only that version's kit cache entries and reevaluating its lock hashes; the kit's `tools/verify-packages.ps1` avoids stale-cache ambiguity with an isolated consumer cache.

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

The private GitHub remote uses HTTPS: `https://github.com/aefreedman/brutal-breakout.git`.

Before distribution review the current [BRUTAL license](https://github.com/ahwootech/BRUTAL) and all third-party notices. BRUTAL requires copyright attribution in credits, its logo before gameplay, and a shipped `NOTICE.txt`; those release assets are not implemented here.
