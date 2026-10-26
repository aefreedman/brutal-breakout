# brutal-breakout

A Windows-first 2D game built with C# and [BRUTAL](https://github.com/ahwootech/BRUTAL): brick clearing with the Unity prototype's **catch line → aim → separate relaunch** interaction, plus an optional Shmup hybrid mode. It uses a small direct Vulkan renderer, not a general engine.

## Requirements

- .NET SDK **10.0.201** (or a newer patch in the same feature band; see [`global.json`](global.json)).
- Windows for the currently tested setup. Only Windows x64 runtime behavior is verified here.
- Vulkan 1.3 GPU/driver, combined graphics/present queue, color-attachment swapchain support, B8G8R8A8_SRGB or R8G8B8A8_SRGB surface format, and 4x MSAA for the selected color format. There is no non-sRGB or lower-sample fallback.
- Vulkan SDK validation layers for Debug development and smoke tests. Players need the driver runtime, not the SDK.
- The independent **`crushkit-brutal` checkout and its local package feed**: CrushKit packages are not yet published. The locked version is currently `0.1.0-preview.8`.

BRUTAL packages and native libraries restore transitively from NuGet; no BRUTAL source checkout or external shader build step is required. Embedded GLSL compiles once at startup through ShaderC (no external `glslc`).

## Quick start

Run **all commands from this game repository's root**. In the coordinated layout, this repository is `brutal-coord/breakout/` and the independent `crushkit-brutal/` checkout sits beside `brutal-coord/`.

Build the kit's local feed first:

```powershell
pwsh -NoProfile -File ../../crushkit-brutal/tools/pack-local.ps1
```

[`nuget.config`](nuget.config) maps `CrushKit.Brutal.*` to that relative local feed and other dependencies to nuget.org. Then:

```powershell
dotnet restore --locked-mode
dotnet build --no-restore
dotnet run --project src/App/BrutalBreakout.App.csproj --no-build
```

This opens a resizable brick-clearing game (shipped initial physical size: 1280×720). Optional modes:

```powershell
# Shmup hybrid:
dotnet run --project src/App/BrutalBreakout.App.csproj --no-build -- --shmup
# Twelve-panel primitive gallery:
dotnet run --project src/App/BrutalBreakout.App.csproj --no-build -- --showcase
```

These commands open **visible windows**. Automated GPU validation uses a separate hidden harness; visible lifecycle/resize tests require explicit user permission.

Debug enables `VK_LAYER_KHRONOS_validation` by default and fails if unavailable; `--no-validation` opts out. Release enables validation with `--validation` or any GPU test mode.

## Documentation

Start with the [audience index](docs/README.md):

- **Players:** [controls, catch/relaunch rules and Shmup](docs/player/guide.md).
- **Designers:** [configuration and live tuning](docs/design/configuration.md), [colors, style, trails and brick layout](docs/design/presentation.md).
- **Engineers:** [development, packages and repository layout](docs/engineering/development.md), [architecture and native ownership](docs/engineering/architecture.md), [rendering/media](docs/engineering/media.md), [validation and evidence limits](docs/engineering/validation.md).
- **Performance/physics:** [performance design](docs/engineering/performance.md), [physics quality budget](docs/engineering/physics.md), [execution and worker scheduling](docs/engineering/execution.md).

[`Config/`](Config/) is the authoritative source of defaults. The optional **F1 / Select/Share** inspector provides configuration controls with field-level live edits, explicit structural resets and explicit saves. External files are not watched. For durable source-file editing, launch from this root with `--project-configs`; normal launches select executable-directory copies.

## Release and legal status

Before distribution, review the [BRUTAL license](https://github.com/ahwootech/BRUTAL) and third-party notices. BRUTAL requires copyright attribution in credits, its logo before gameplay and a shipped `NOTICE.txt`; those release assets are **not implemented here**. Copying dependency notices alone is not complete release compliance. See [development/release notes](docs/engineering/development.md#repository-and-release) and [media provenance](docs/engineering/media.md).
