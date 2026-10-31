# brutal-breakout

Breakout inspired 2D game prototypes built with C# and [BRUTAL](https://github.com/ahwootech/BRUTAL).

## Requirements

- .NET SDK **10.0.201** (or a newer patch in the same feature band; see [`global.json`](global.json)).
- Windows for the currently tested setup. Only Windows x64 runtime behavior is verified here.
- Vulkan 1.3 GPU/driver, combined graphics/present queue, color-attachment swapchain support, B8G8R8A8_SRGB or R8G8B8A8_SRGB surface format, and 4x MSAA for the selected color format. There is no non-sRGB or lower-sample fallback.
- Vulkan SDK validation layers for Debug development and smoke tests. Players need the driver runtime, not the SDK.
- The independent **[CrushKit source checkout](https://github.com/aefreedman/crushkit-brutal)** at the exact commit in [`.github/crushkit-revision`](.github/crushkit-revision). No published CrushKit package or local package feed is needed for the recommended source build.

BRUTAL packages and native libraries restore transitively from NuGet; no BRUTAL source checkout or external shader build step is required. Embedded GLSL compiles once at startup through ShaderC (no external `glslc`).

## Quick start (source build)

Clone the two public repositories as siblings, then run the .NET commands from the game repository root. The pinned kit commit, not a floating `main`, defines the compatible source dependency:

```powershell
git clone https://github.com/aefreedman/crushkit-brutal.git
git clone https://github.com/aefreedman/brutal-breakout.git
cd brutal-breakout
$kit = (Resolve-Path ../crushkit-brutal).Path
$revision = (Get-Content .github/crushkit-revision -Raw).Trim()
git -C $kit checkout --detach $revision
$source = @('-p:UseCrushKitProjects=true', "-p:CrushKitRoot=$kit")
dotnet restore --locked-mode @source
dotnet build --no-restore @source
dotnet run --project src/App/BrutalBreakout.App.csproj --no-build @source
```

For an existing coordinated checkout, set `$kit` to the actual kit location (normally `../crushkit-brutal` from this game's root). Preserve local kit changes before checking out another revision. Source builds use committed `packages.source.lock.json` files; existing package locks and ignored private development locks remain separate. The legacy local-package mode is still available without `UseCrushKitProjects`, but requires the exact unpublished packages; package hosting/publication is intentionally deferred.

This opens a resizable brick-clearing game (shipped initial physical size: 1280×720). Optional modes:

```powershell
# Shmup hybrid:
dotnet run --project src/App/BrutalBreakout.App.csproj --no-build @source -- --shmup
# Twelve-panel primitive gallery:
dotnet run --project src/App/BrutalBreakout.App.csproj --no-build @source -- --showcase
```

These commands open **visible windows**. Automated GPU validation uses a separate hidden harness; visible lifecycle/resize tests can disturb desktop focus. See [test coverage](docs/engineering/validation.md).

Debug enables `VK_LAYER_KHRONOS_validation` by default and fails if unavailable; `--no-validation` opts out. Release enables validation with `--validation` or any GPU test mode.

## Documentation

Start with the [audience index](docs/README.md):

- **Players:** [controls, catch/relaunch rules and Shmup](docs/player/guide.md).
- **Designers:** [configuration and live tuning](docs/design/configuration.md), [colors, style, trails and brick layout](docs/design/presentation.md).
- **Engineers:** [development, packages and repository layout](docs/engineering/development.md), [architecture and native ownership](docs/engineering/architecture.md), [rendering/media](docs/engineering/media.md), [validation and evidence limits](docs/engineering/validation.md).
- **Performance/physics:** [performance design](docs/engineering/performance.md), [physics quality budget](docs/engineering/physics.md), [execution and worker scheduling](docs/engineering/execution.md).

[`Config/`](Config/) is the authoritative source of defaults. The optional **F1 / Select/Share** inspector provides configuration controls with field-level live edits, explicit structural resets and explicit saves. External files are not watched. For durable source-file editing, launch from this root with `--project-configs`; normal launches select executable-directory copies.

## GitHub CI

One Windows Debug job builds source and runs headless tests on pushes/PRs. Run the same baseline locally with `pwsh -File .github/scripts/ci.ps1 -CrushKitRoot $kit`. It verifies the kit revision and locked source graph; it does not validate the packaged-consumer path, open game windows, run GPU tests/benchmarks or publish anything. Actions updates are grouped monthly. NuGet update automation waits for an immutable public kit feed; native/API/dependency updates require manual review. See [contributing](CONTRIBUTING.md) and [security reporting](SECURITY.md).

## Release and legal status

Original Breakout material is **GPL-3.0-or-later**, with a BRUTAL linking exception. See [LICENSE](LICENSE), [LICENSE-EXCEPTION](LICENSE-EXCEPTION) and [licensing scope](LICENSING.md). Assets and third-party components retain their existing licences. Commercial builds and donations are permitted; choosing a licence is not release clearance.

Before distribution, review the [BRUTAL license](https://github.com/ahwootech/BRUTAL) and third-party notices. BRUTAL requires copyright attribution in credits, its logo before gameplay and a shipped `NOTICE.txt`; those release assets are **not implemented here**. Copying dependency notices alone is not complete release compliance. See [development/release notes](docs/engineering/development.md#repository-and-release) and [media provenance](docs/engineering/media.md).
