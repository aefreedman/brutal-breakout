# brutal-breakout

## Repository layout

- `Assets/`: source audio, images, fonts and their provenance/licenses; no application C#.
- `Config/`: designer-facing authoritative JSON; alternate palettes live in `Config/ColorSchemes/`.
- `src/App/`: executable composition and platform/render loop.
- `src/Runtime/Game/`: `BrutalBreakout.Runtime` assembly, configuration loading, game presentation, HUD and sessions.
- `src/Runtime/{Simulation,Input,Audio}/`: existing independent runtime assemblies.
- `src/Editor/`: optional `BrutalBreakout.Editor` assembly, inspector configuration, toggle and capture routing. Runtime has no Editor or ImGui dependency.
- `tools/Showcase/`: primitive-gallery assembly used by development modes.
- `tests/Runtime/`, `tests/Editor/`: CPU/offscreen suites; `tests/Fixtures/`: test-only configuration; `tests/Rendering/`: GPU scripts and scripted gameplay scenario assembly.
- `BrutalBreakout.slnx`: all projects. Package locks live beside their owning projects.

Build/publish output preserves `Config/` and `Assets/`. Default configuration paths resolve beneath the executable's `Config/`, independent of working directory; explicit overrides remain relative to the caller. Asset paths resolve relative to their JSON file.

### Optional editor

Normal development builds include the F1 diagnostics inspector and game-mode controls. HUD text is configured independently in `Config/overlay.json`; editor font, preview image, layout and initial visibility are configured in `Config/inspector.json` (`--inspector-config <path>`). Both settings objects are validated before native initialization and passed unchanged to their consumers.

Build without editor code, ImGui packages or inspector configuration/asset validation:

```powershell
dotnet restore src/App/BrutalBreakout.App.csproj -p:EnableEditor=false --locked-mode
dotnet build src/App/BrutalBreakout.App.csproj -c Release -p:EnableEditor=false --no-restore
dotnet publish src/App/BrutalBreakout.App.csproj -c Release -p:EnableEditor=false --no-restore -o artifacts/shipping
```

All generated outputs are centralized under ignored `artifacts/`: `bin/<project>/<configuration>/` for binaries, `obj/<project>/` for restore/build intermediates, and `publish/<project>/<configuration>/` for default publish output. Configuration names are lowercase (for example, `debug` and `release`).

The editor-free app uses its own `src/App/packages.shipping.lock.json` and `BrutalBreakout.App.shipping` artifact directories, keeping its binaries and restore intermediates separate from editor-enabled builds. Inspector-only options are rejected in this build. The separate app restore directories allow switching between restored editor-enabled and editor-free builds without overwriting either app's dependency graph. Publish to a fresh directory when changing build variants; incremental publishing does not remove old files.

A Windows-first 2D game built with C# and [BRUTAL](https://github.com/ahwootech/BRUTAL). Current milestone: brick clearing with the Unity prototype's catch line and swappable schemes on a small direct Vulkan renderer—not a general engine.

## Requirements

- .NET SDK 10.0.201 (or a newer patch in the same feature band; see `global.json`).
- Windows for the currently tested setup.
- Vulkan 1.3 GPU/driver, combined graphics/present queue, color-attachment swapchain support, B8G8R8A8_SRGB or R8G8B8A8_SRGB surface format, and 4x-MSAA support for the selected color format. There is no non-sRGB or lower-sample-count fallback; unsupported hardware fails clearly.
- Vulkan SDK validation layers for Debug development and smoke tests. Players need the driver runtime, not the SDK.

Breakout consumes local prerelease `CrushKit.Brutal.Draw2D`, `CrushKit.Brutal.Vulkan`, optional-inspector `CrushKit.Brutal.ImGui`, `CrushKit.Brutal.Input`, `CrushKit.Brutal.Audio`, `CrushKit.Brutal.Ecs` and `CrushKit.Brutal.Physics` packages (currently version `0.1.0-preview.5`). BRUTAL packages and native libraries restore transitively from NuGet; no BRUTAL source checkout or external shader build step is required. Embedded GLSL compiles once at startup through ShaderC (no external `glslc`).

## Build and run

Until CrushKit is published, place the independent `crushkit-brutal` checkout beside the coordination repository and build its local feed first:

```powershell
pwsh -NoProfile -File ../../crushkit-brutal/tools/pack-local.ps1
```

`nuget.config` maps `CrushKit.Brutal.*` to that relative local feed and other dependencies to nuget.org. Then, from this repository:

```powershell
dotnet restore --locked-mode
dotnet build --no-restore
dotnet run --project src/App/BrutalBreakout.App.csproj --no-build
```

Normal `dotnet run --project src/App/BrutalBreakout.App.csproj --no-build` opens a resizable **brick-clearing game** at the initial physical size in `Config/window.json` (shipped 1280 x 720). Use `dotnet run --project src/App/BrutalBreakout.App.csproj --no-build -- --showcase` for the existing twelve-panel primitive gallery. These interactive commands open visible windows; automated validation below uses hidden windows only. Debug enables `VK_LAYER_KHRONOS_validation` by default and fails if unavailable; `--no-validation` opts out. Release enables validation with `--validation` or any GPU test mode.

### Edit CrushKit locally

Normal builds consume real packages. For coordinated source edits, opt into neighboring project references without changing normal package lock files:

```powershell
dotnet restore -p:UseCrushKitProjects=true -p:NuGetLockFilePath=packages.dev.lock.json
dotnet build --no-restore -p:UseCrushKitProjects=true -p:NuGetLockFilePath=packages.dev.lock.json
```

Set `CrushKitRoot` explicitly if the kit checkout is elsewhere. Use the same properties on test/run commands. Development lock files are ignored; switch back with normal locked restore. Do not overwrite a shared/published NuGet version: bump it and update consumer lock files. Repacking an unpublished version requires deliberately refreshing only that version's kit cache entries and reevaluating its lock hashes; the kit's `tools/verify-packages.ps1` avoids stale-cache ambiguity with an isolated consumer cache.

The renderer/geometry/shaders and optional inspector now live in CrushKit, not this repository. Breakout retains gameplay, configuration loading/JSON, overlay label/diagnostic content, input bindings/aim priorities, audio cue/event mapping and licensed game assets. CrushKit owns raw input sampling/edge/deadzone helpers and generic clip/device/bounded-voice mechanics; Input/Audio depend on neither graphics nor one another. The renderer no longer knows about balls/bricks; `OverlayHost` supplies those details as text. The kit's Vulkan package alone has no ImGui dependency; this application explicitly configures `ImGuiInspector` with its separately validated `InspectorSettings`; text `OverlaySettings` no longer require any inspector assets. HUD and inspector JSON are independently strict; their immutable settings objects are passed unchanged to their respective consumers.

### Arch ECS and jobs

Breakout uses **Arch 2.1.0** through `CrushKit.Brutal.Ecs`, with `ZeroAllocJobScheduler 1.1.2` for explicit dependencies. `CrushKit.Brutal.Physics` owns the reusable Box2D worker/task bridge; neither package knows about balls, bricks, catch rules or game JSON. Breakout owns those components/systems, the native arena layout, tick graph and immutable render publications.

`Config/execution.json` is required for gameplay, copied into the executable's `Config/` directory and validated before platform/native startup. `--execution-config <path>` selects an override. Its three required positive-integer fields are `workerCount` (ECS graph workers), `maxExpectedConcurrentJobs` (scheduler handle-pool sizing, not a queue limit) and `physicsWorkerCount` (Box2D workers). Shipped budgets are 2/64/2; they are explicit composition choices, not a single-thread restriction or a performance claim. Coordinator threads are additional to these worker budgets. Tune total CPU use rather than giving every pool all cores.

Each tick runs input/native physics first, then independent ball/round and brick ECS jobs, then joins both before publishing. The physics engine, not an ECS movement system, owns flight integration and collisions. No world reads/writes overlap its native step; native events are fully copied/consumed before destroying bodies. Structural ECS changes occur only at exclusive boundaries. The pinned Arch query scheduler is static, so the shared host deliberately does not replace it or run nested `ParallelQuery` calls: graph-level jobs provide concurrency over disjoint archetypes instead.

`BallSimulation.StepAsync` queues ticks from any caller thread and returns that tick's immutable `SimulationSnapshot`; snapshots can be retained while work continues. Queries queue behind accepted ticks. Disposal drains accepted work before releasing native worlds, worker pools and ECS storage. `Step` is a blocking compatibility wrapper; the current input/render host joins ticks before drawing, not a requirement that gameplay or physics execute on the render thread. Host input staging remains host-thread-owned. Async I/O, further jobs and snapshot-based render overlap remain possible without changing collision ownership. Dependency graphs, tasks and retained snapshots currently allocate; the scheduler's name does not imply allocation-free ticks or a measured speedup.

### Breakout rules and controls

- Aim with **WASD / arrows / left stick / mouse movement**. Active keyboard direction wins over stick, then moved/clicked mouse; a stationary unused mouse does not override direction input. Centered stick preserves the last aim. Initial aim comes from `Config/gameplay.json` (the shipped file points up).
- **Space / left mouse button / gamepad south (A)** launches or catches. Shot speed and ball radius come from `Config/gameplay.json` (the shipped values are 600 logical units/second and radius 12), with no gravity or drag; subsequent velocity belongs to the native solver (no per-tick speed normalization). Top and side walls reflect; the bottom is open. Single-hit bricks reflect the ball and disappear.
- After launch, the ball must **leave the lower catch zone and hit a wall or brick**, then return below/on the catch line before it can be caught. The restriction is on the ball center, not the cursor. Crossing the line does not catch automatically. The ball retains its configured disc color when eligible; eligibility does not add a flight halo or recolor.
- Once eligible, press from any cursor position to catch the ball below the line. Catch holds the ball at its actual position, not a spawn point. **Release and press separately** to relaunch; a catch never fires on the same action.
- The shipped game has **40 bricks and three balls**. Missing the bottom consumes a ball; destroyed bricks remain destroyed. More than the configured maximum wall bounces without a brick also consumes a ball. Clear all bricks to win; exhaust balls to lose. The title bar reports status/counts; progress bars and terminal markers provide scene feedback.
- **R / gamepad Start** restores all bricks, balls and initial pose, taking priority over pending actions. Radius-aware native aim previews include live bricks and exclude destroyed ones; ambiguous/no-hit casts show a short direction indicator.
- Unfocused or minimized windows pause simulation and discard accumulated time/pending edges. The first active frame consumes zero elapsed time; held actions on return are baselined, requiring release/repress. Physics continues when rendering is merely unavailable/out-of-date, not minimized.
- The configured logical canvas uniformly fits the physical framebuffer with centered letterboxing. Mouse coordinates convert from logical-window units through framebuffer DPI and fit margins, then aim from the ball center. Zero-size/nonfinite/letterbox targets are ignored.

Rules reference the Unity `Projectile.cs`, `ProjectileFactory.cs` and `GameSettings-Breakout.asset`: bottom-third catch zone, leave-zone plus contact, top/side walls, limited spawns and bounce expiry. No paddle, progression, damage scaling, drag or level/editor framework. Input uses standardized GLFW mappings; polling can miss complete edges or disconnect/reconnect between frames. No physical-controller or visible/image playtest is claimed for this increment.

### Shmup-breakout mode

The optional F1 inspector provides a mode selector for **Breakout** and **Shmup**, plus a widening-only play-area control. Mode changes start a fresh isolated run; the original Breakout controls and catch/relaunch rules are unchanged. `--shmup` starts directly in the hybrid mode, including editor-free builds.

In Shmup, move the circular avatar with the **left stick**, aim with the **right stick**, and hold **right trigger** to fire a stream of small balls. Balls bounce off side walls and, by default, bricks; brick bouncing is configurable. Crossing the top or bottom screen edge despawns balls. Twenty consecutive side-wall contacts without a brick interaction expire a ball; brick interactions reset that configurable counter. Native intervals resolve entries and wall expiry chronologically, deduplicating contact/sweep facts without merging separate reentries. Brick contact takes precedence only within the same interval; a later brick cannot rescue an already expired ball.

Shmup aiming uses the original mode's long line, impact circle and short reflected bounce indicator, backed by an instantaneous projectile-radius native cast against the current walls and live bricks. Piercing mode ignores bricks as bounce targets. Open top/bottom exits show an unreflected line to the screen edge; ambiguous or overlapping queries retain the short fallback rather than inventing a bounce. This is current-pose feedback, not prediction of future moving-brick positions.

Ball trails retain bounded per-life world-space history at fixed ticks, fading and tapering behind live balls. They reset on despawn, pool reuse, restart and mode switch. Pause freezes simulation-time aging; resizing transforms stored points without changing simulation. `Config/trails.json` (`--trails-config <path>`) supplies the immutable startup settings and storage/draw budgets. The shipped trail settings use 24 points per ball, a 0.18-second lifetime, radius-relative width 1.5, opacity 0.55, and at most 16,384 rendered segments. Histories are limited by both age and point count; draw saturation shortens old tails and shares the budget across live balls. Bends within one fixed tick are approximated by the sampled polyline. Camera/display behavior is unchanged.

Square bricks descend in continuously generated rows with seeded Perlin-noise gaps. Spawn frequency and descent speed increase over gameplay time to configured caps. Bricks have 100 HP and balls deal 10 damage by default. The player has 100 HP; escaped bricks cost 10 HP each, while avatar contact causes damage followed by brief invulnerability. At zero HP the mode stops until explicitly restarted. The top/bottom screen edges have no rendered border.

`Config/shmup.json` is the authoritative tuning source, validated before native startup. It includes canvas/initial width, avatar, balls, bricks, difficulty, noise and explicit capacity limits. `--shmup-config <path>` selects an override. Runtime width changes preserve the screen center and cannot narrow the live play area. The shipped capacities retain 512 ball slots and 2048 brick slots: 2560 Arch entities and 2562 native bodies including the two walls. Saturation drops new spawns (with separate ball/brick counters), without a deferred burst. Reused lives reset HP, contact state and wall streaks; slot IDs include life generations. Immutable snapshot arrays still allocate, so pooling is not a claim of allocation-free ticks. For both bounce and piercing, native interval counts derive from configured speed, descent and ball radius; settings requiring more than 4096 intervals per tick are rejected before allocation. Smaller balls/higher speeds therefore trade CPU time for contact fidelity. The native solver handles collision response, with ball speed restored to its configured magnitude after each fixed tick. Shmup simulation remains independent of input, rendering and ImGui. Shmup has short firing, brick-hit, brick-destruction and player-damage cues, and reuses the loss cue for game over. Events are captured per fixed tick, including catch-up ticks; destruction takes precedence over hit feedback, and defeat cuts other tails and plays once. Mute, focus loss, restart and mode changes stop stale feedback.

Keyboard/mouse fallback: **WASD/arrows** move, **mouse movement** aims, and hold **Space/left click** to fire; **R/Start** restarts. Neutral aim retains the last direction. After focus loss, input reset or controller replacement, release fire before pressing again.

After building, `pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Debug -Mode Shmup` (also Release) exercises synthetic input, widening, resize, fresh mode-switch state and inspector callback failure/retry in a hidden window with Vulkan validation and foreground/visibility sampling. This is not a visible playtest or physical-controller test.

### BRUTAL audio, text, textures and diagnostics

Normal gameplay now shows count/status/control labels through **SDL.Ttf -> BRUTAL.Texture/GenericTexture -> direct Vulkan textured quads**. `TextureLoader` uses BRUTAL Stb internally for the PNG path; there is no second image decoder or alternate graphics stack. ImGui supplies **only the optional diagnostics inspector and game-mode controls**, not the game text/image compositor. The existing 24-byte solid-shape pipeline is unchanged; textured vertices use a separate 32-byte UV material, with linear-premultiplied texels/tints, sRGB output and the same 4x MSAA render pass.

- `Config/overlay.json` is required in game and gallery modes (`--overlay-config <path>` overrides it). Its `fontPath` selects the proportional game font. Editor-enabled builds separately require `Config/inspector.json` (`--inspector-config <path>`), whose `fontPath` selects the monospaced inspector font and whose `imagePath` selects the preview. Asset paths resolve relative to the JSON containing them. Font sizes, label spacing and inspector dimensions are **physical framebuffer pixels**. Cache/label/texture/encoded-byte limits are explicit; required fields/assets and the host's three-label capacity are validated before GLFW. Defaults exist only in JSON, with no partial merge or hot reload. Labels cache unchanged strings; cache eviction and GPU texture/descriptor lifetime follow the shared frame fence.
- Press **F1** to open/close the inspector at runtime, including while it captures keyboard input. Holding F1 does not repeat; a held key on focus regain is baselined. No CLI argument is required. `visible` in `Config/inspector.json` seeds the initial state; `--debug-ui` remains an optional initially-open shortcut. It shows state/counts, timing, geometry/presentation/cache statistics, font/framebuffer/raster-density metrics and an authored PNG preview. Its game-controls window selects the mode and widens the active Shmup play area; the remaining diagnostics are read-only. With it closed, the game does not initialize ImGui or install its GLFW callbacks. Capture transitions clear pending input edges; captured keyboard/mouse sources are suppressed without taking gamepad control away.
- `Config/audio.json` is required in gameplay (`--audio-config <path>` overrides it). It configures enabled state, master gain, a bounded voice count and ten WAV paths: the original launch/catch/brick/miss/win/loss cues plus shmupFire/shmupHit/shmupDestroy/shmupDamage. Custom audio JSON must supply all ten cue paths. Cues are collected per fixed tick, not inferred once per render frame. Terminal cues replace same-tick brick/miss feedback. Tracks are reused first-idle; saturation drops the new cue, never queues it. Focus loss/mute/restart stops feedback and prevents stale replay.
- Audio uses **BRUTAL SDL.Mixer** and cached, predecoded clips. `--mute` avoids creating an audio device entirely. Disabled audio also creates no device. Missing/unavailable native audio devices/libraries give one diagnostic and silent output, without stopping gameplay. Configuration or asset validation errors remain startup errors. The supplied sounds are original simple placeholder tones (CC0); their offline authoring script is included, not a runtime dependency. Custom cue files currently require canonical mono PCM16 WAV, not every format SDL.Mixer can decode.
- Hidden GPU modes and showcase never open an audio device. Headless tests exercise cue mapping, saturation and unavailable output through fakes; no audible quality/latency/device-shutdown acceptance is claimed.

Exactly two pinned, unmodified **Iosevka v34.9.1 Regular** TTFs are included: **Iosevka Aile** (proportional sans) for game labels, and standard **Iosevka** (monospaced, not Term/Etoile) for the inspector. They were acquired from the official be5invis/Iosevka release, not supplied by BRUTAL or installed from the OS. Aile is 11,878,496 bytes; inspector Regular is 11,613,712 bytes: **23,492,208 bytes (22.40 MiB)** total. No other weights/italics/collections/download archives are committed, and Abel was removed. The shared SIL OFL1.1 and exact source/archive/font hashes are in `Assets/Fonts/OFL.txt` and `PROVENANCE.txt`. Fonts, authored PNG/sounds and provenance copy to build/publish output. The old inspector implicitly chose ImGui's pixel-style ProggyClean font; it now explicitly loads the configured Iosevka TTF. ImGui1.92's dynamic-font backend follows framebuffer rasterizer density rather than stretching an old fixed 13px bitmap. `fontSize` remains the shared physical-pixel target: the inspector converts it to logical size once, and native ImGui rounds to a logical pixel (up to half a logical pixel of size quantization, with a one-logical-pixel minimum). No extra UI-resolution downscale is used; game SDL.Ttf rendering remains separate. Actual visible/DPI sharpness still needs user acceptance.

Dependency notices are under `Licenses/Media`, module-qualified `Licenses/CrushKit/Input` and `Licenses/CrushKit/Audio`, plus game audio notices in `Assets/Audio/ThirdParty`. Module-qualified paths prevent shared-dependency collisions in publish output. BRUTAL's bundled license also requires product credits, its pre-game logo (subject to stated permission/exception), and NOTICE distribution before end-user distribution; copying notices alone is not a complete release-compliance claim.

Hidden checks can additionally exercise HUD-only rendering, inspector open/close, texture preview and resize, without input injection:

```powershell
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Debug -Mode Gameplay -Overlays
```

### Initial window configuration

`Config/window.json` is the only production default source for initial **physical window** dimensions: required positive integer `width` and `height`. These are independent of `Config/gameplay.json`'s logical canvas; fit/letterboxing still derives from that canvas and the actual framebuffer. Both game and gallery require this file. `--window-config <path>` independently overrides it, using caller cwd for relative paths; default lookup uses the executable directory. Build and publish copy it alongside the other configuration assets. Startup reports selected absolute path, requested dimensions and actual created window/framebuffer dimensions.

The immutable `WindowSettings(int width, int height)` has no defaults or mutable properties. The strict loader rejects missing/unreadable files, absent/null/unknown/duplicate/escaped-duplicate fields, wrong types and nonpositive/out-of-int-range dimensions **before GLFW or a native world**. There is no fallback, partial merge or hot reload. Gallery still loads no implicit physics/game/input/colors/style files; explicit overrides remain validated. `--cleanup-self-test` exits before any configuration lookup or native startup. Positive integer representability does not promise OS/GPU support for arbitrarily huge windows.

### Physics configuration

`Config/physics.json` is the **only production default source** for physics timing/tuning. It is copied to build and publish output; normal startup reads the file beneath the executable's `Config/` directory, independent of the caller's working directory. Edit the source file and run/build again, edit the deployed copy, or select a file explicitly:

```powershell
dotnet run --project src/App/BrutalBreakout.App.csproj -- --physics-config ./physics.json
```

An explicit relative path resolves against the caller's working directory. The file is read and validated **before GLFW or a native physics world starts**. All five fields are required; unknown/misspelled/duplicate fields, wrong types, malformed/nonfinite/unrepresentable numbers and missing/unreadable files fail with path context. There is no C# fallback configuration, partial merge, or last-wins duplicate handling. Showcase-only modes do not load an implicit physics file; an explicit override is still validated. `--physics-config` must occur once with a path. Changes take effect on the **next launch**, not through hot reload.

| Field | Meaning |
| --- | --- |
| `updateHz` | Fixed gameplay/physics updates per second; timestep is derived as `1 / updateHz`. Positive finite fractional rates are allowed when native step/substep arithmetic can represent them. |
| `substeps` | Positive integer Box2D solver substeps within each fixed update; this is not an extra game-command update. |
| `maxCatchUpSteps` | Positive integer maximum fixed ticks per host advance. Excess new elapsed time is discarded while the previous fractional tick remains. |
| `contactHertz` | Nonnegative finite native contact stiffness frequency. Explicit, independent tuning—not automatically changed when `updateHz` changes. |
| `contactDampingRatio` | Nonnegative finite native contact damping ratio; 1 is critical damping. |

One immutable validated `PhysicsSettings` instance is shared by session, clock, simulation and native adapter. Startup prints the loaded absolute path, timestep and all five effective settings. Numeric representability does **not** guarantee useful or stable physics for every extreme combination; tune/test frequency, substeps and contact settings together. Unit conversion and arena geometry are not part of this timing configuration.

### Gameplay and input configuration

`Config/gameplay.json` and `Config/input.json` are the only production default sources for ball/arena geometry and stick deadzone. Like `Config/physics.json`, they are copied into the executable's `Config/` directory on build/publish and loaded before GLFW/native world creation. Edit the source files and build/run again, edit deployed copies, or choose independent overrides:

```powershell
dotnet run --project src/App/BrutalBreakout.App.csproj -- --gameplay-config ./my-gameplay.json --input-config ./my-input.json
# All three overrides can be combined:
dotnet run --project src/App/BrutalBreakout.App.csproj -- --physics-config ./my-physics.json --gameplay-config ./my-gameplay.json --input-config ./my-input.json
```

Every section/field is required; there is no partial merge, C# fallback or parameterless settings constructor. Unknown/misspelled, duplicate (including escaped names at nested levels), missing, null or wrong-type fields fail with file-path context. Each option accepts one path; relative paths use caller cwd. Showcase-only modes need neither implicit gameplay nor input files, but validate any explicit overrides (window configuration is always required). Changes apply on next launch, not through hot reload.

| File / section | Required fields and meaning |
| --- | --- |
| `Config/gameplay.json` / `arena` | `width`, `height`: logical canvas; `left`, `top`, `right`, `bottom`: inner wall faces; `wallThickness`: wall thickness in logical units. |
| `Config/gameplay.json` / `ball` | `radius`, `speed`: logical radius and units/second; `spawnX`, `spawnY`: reset center; `initialAimX`, `initialAimY`: finite nonzero direction, normalized once. |
| `Config/gameplay.json` / `breakout` | `startingBalls`, `catchZoneHeightFraction` (canvas fraction measured up from bottom), `maxBouncesWithoutBrick` (0 disables expiry), `bricks`: rectangles with `x`, `y`, `width`, `height`. |
| `Config/input.json` | `stickDeadzone`: radial deadzone in **[0,1)**, validated after float conversion; the boundary is inactive. |

Ordered walls including thickness must fit the canvas, radius must leave a nonempty playable area, and spawn must fit the radius-inset bounds. Invalid/degenerate native float geometry and unsafe speed/timestep combinations reject before a world is allocated; errors for combined physics/gameplay incompatibility identify both paths. Validation establishes numerical representability, not stable physics for every extreme setting.

The same immutable settings drive native walls/circle/velocity, reset/casts, drawing and pointer/DPI fitting. Native speed/radius/positions are **derived via the one unit conversion**, never separately authored meter defaults. Cast reach derives from configured geometry; native maximum linear speed does not silently undercut requested launch speed. Rendering uses the configured inner faces and thickness rather than a second wall layout. The required breakout section validates a nonoverlapping layout inside the walls, above the catch line and clear of the spawn circle. Explicit null breakout settings are only for library physics-reference tests, not a production JSON fallback.

### Swappable color schemes

`Config/colors.json` maps the **active serialized Unity setup**, not just palette assets or camera clear color. `BackgroundUICanvas.prefab` has an inactive untinted image and an active `Image.001`: .22 gray tint multiplied by a vertical coral-bottom/cream-top gradient. UI Color32 tint quantization is 56/255; authored opaque sRGB approximations are **top #33332D / bottom #381B18**. The scene only overrides placement/camera. The gradient covers the physical framebuffer, including letterboxes. Clear-only frames use the configured top `background`, not a full gradient.

The default projectile (including catch-ready flight) and all three normal-brick roles are coral `#FF7D6E`; bricks have rounded fills with no invented keylines. Walls and aim/contact/reflected lines are white. The catch line and held selection ring are cream `#E8E7CB`. Semantic roles remain independent for alternative themes.

```powershell
dotnet run --project src/App/BrutalBreakout.App.csproj -- --colors-config Config/ColorSchemes/night.json
```

Combine with any other config overrides. Schemes require `name`, a `palette` of named opaque sRGB `#RRGGBB` colors, and `roles` referencing palette names: `background`, `backgroundBottom`, `heldRing`, `walls`, `ball`, `catchReady`, `catchLine`, `aim`, `brickPrimary`, `brickSecondary`, `brickAccent`, `hud`, `win`, `loss`. Missing/unknown/duplicate fields, bad hex and undefined references fail before platform startup. Edit/select a file for the next launch; no hot reload. Both shipped schemes copy to build/publish. Colors convert from sRGB to linear once; the CPU gradient supplies per-vertex linear colors to the unchanged shader/vertex ABI. Roles may share colors; actual index-range receipts never depend on color identity. The primitive gallery retains its demonstration styling.

### Presentation style configuration

`Config/style.json` is the only production source of new presentation tunables. Use `--style-config <path>` independently of color/game/input/physics overrides. Like those files, all fields are required, unknown/missing/null/duplicate (including escaped-name) fields reject, default lookup is beneath the executable's `Config/` directory, relative overrides use caller cwd, and gallery modes load no implicit style file. Explicit overrides are always validated before GLFW/world creation. Build/publish copies include the style. No merge, optional constructor fallback, hot reload or simulation-timing change.

| Required style fields | Shipped values / units |
| --- | --- |
| `heldRingRadiusScreenFraction`, `heldRingWidthScreenFraction` | .20 / .02 of shorter **physical framebuffer** extent |
| `heldRingDashCount`, `heldRingGapFraction`, `heldRingCyclesPerSecond` | 16 / .5 / 2 pattern cycles per render-animation second |
| `aimWidthRadiusRatio`, `impactRingWidthRadiusRatio` | .25 / .1 of ball radius |
| `reflectedLengthRadiusRatio`, `fallbackAimLengthRadiusRatio` | 2 / 4 ball radii |
| `brickCornerFraction` | .1 of shorter brick side |
| `catchLineWidthScreenFraction` | .01 of shorter physical framebuffer extent |
| `catchLineDashLengthMultiples`, `catchLineGapMultiples` | 3 / 3 full stroke widths, rounded caps |
| `hud.barMaxWidth`, `hud.barMaxArenaWidthFraction` | 120 logical units / .25 of inner arena width; smaller wins |
| `hud.barHeight`, `hud.barOutlineWidth` | 6 / 1 logical units |
| `hud.barMinCenterY`, `hud.barTopMarginFraction` | 4 logical units / .5 of the space above the outer top wall; larger wins |
| `hud.terminalMaxSize`, `hud.terminalMaxArenaSizeFraction` | 28 logical units / .125 of shorter inner arena extent; smaller wins |
| `hud.terminalMarkerWidth` | 4 logical units |

The required nested `hud` is one immutable `HudStyle` on `PresentationStyle.Hud`, not an optional/default constructor argument. All nine nested fields are required with recursive unknown/duplicate/escaped-duplicate/null/type rejection. HUD dimensions are positive, finite and float-safe within the Shapes 2^20 coordinate envelope; its three fractions are at most one. Pure game/style cross-validation rejects collapsed float coordinates/strokes, out-of-envelope geometry and terminal stroke widths exceeding the derived marker size before native startup. Half-height bar centering and checkmark point fractions are geometry definitions, not tunables. Defaults preserve the prior scene geometry.

Dimensions must be finite, positive and float-safe; ring count is 1..4096, gap fraction [0,1), with additional spacing checks for disjoint round caps. Cross-game dimension/pattern checks run before native startup. Very extreme viewports/transforms remain subject to the Shapes precision/envelope/budget contract.

The selected `SelectionIndicatorPrefab` resolves to `SpecialFX/P01.DashedDiscRotatingFX.prefab`, **not** the disabled inner JuggleIndicator. Shapes space 2 is Noots, and this Unity project defines 100 Noots across the short screen extent: radius20 / width2 Noots therefore means .20 / .02, not pixels or thousandths. Only Playing+Aiming draws the cream rotating rounded-dash ring (including initial hold); flight and terminal rounds have none. At 1280x720 these are radius144px / width14.4px / catch width7.2px; at 960x540, 108px / 10.8px / 5.4px. Explicit finite render-animation time changes the dash phase on render-only frames without stepping simulation.

Native full-radius preview centers/normals remain authoritative. The plain white shaft begins at the ball surface and ends one radius before contact center; the contact ring has ball radius and .1-radius width. The short reflected leg starts one radius beyond contact and extends two radii, clipped by an available next-obstruction query. No meaningful first hit means only a bounded plain direction segment—no fabricated ring, normal, reflection or arrowhead. Geometry receipts separately count background and held ring even when every role shares one RGB value. Source tracing and CPU/hidden GPU validation establish geometry/configuration, **not exact image matching**; no screenshots or visible playtest were taken.

### Native physics and ownership

Published **Ahwoo.BRUTAL.Box2D 2026.10.2** replaces the custom collision solver: one native world owns three static arena walls, live static brick bodies and a dynamic circle with bullet CCD. Published `B2Body.Dispose()` was DLL-inspected and headless compile/runtime-probed before use. Events are consumed and deduplicated before destroying hit bodies. The fixed clock, input staging and catch/aim/restart policy remain our game code. The conversion is **100 logical units per meter**, stepping at the configured fixed frequency/substep count through the dependency-job pipeline and Box2D task bridge. Each disposable session owns its simulation/world; the hidden gameplay script owns its own session. Both normal and exceptional app exits dispose these before draw-list/renderer teardown. Showcase and renderer-only smoke modes create no physics world. Snapshot state remains readable after disposal; native operations and session advances do not.

Native contacts are not analytic radius-inset guarantees: bounded checks allow **2 logical units of contact penetration plus a separately named .001 coordinate-roundoff margin**, without clamping positions. At the original explicit 120Hz/four-substep test fixture, the measured worst was 2.000179291 units over 100,000 ticks/ten shots; speed drift was .000190735 units/second. Those measurements are not a universal tolerance guarantee for every configuration. Native cast contact centers have about .5-unit slop, so post-impact paths are not the old exact time-of-impact trajectories. Corners need not yield a combined two-axis normal; initial overlap/zero-normal or ambiguous casts can legitimately have no hit. The presentation's secondary full-radius cast uses a **one-unit query-only separation skin** along the first native normal; it does not move the ball or shrink collision geometry, and clips the fixed short reflected leg when the next obstruction is available. Maintained integration constraints are in the coordination workspace's `docs/reference/brutal-apis.md`; executable checks live in `tests/Runtime/Simulation/`.

Published native assets cover **win-x64, linux-x64 and osx-arm64 only**; only Windows x64 runtime behavior is verified here. Native worlds are explicitly drained and disposed through their execution host, with no finalizer or shared global world. Shared world creation/destruction is serialized to protect native global allocation state; separate worlds may advance concurrently.

### Validation

Run GPU commands sequentially, with no other lane using the GPU:

```powershell
dotnet restore --locked-mode
dotnet restore ../../crushkit-brutal/src/CrushKit.Brutal.Draw2D/CrushKit.Brutal.Draw2D.csproj --locked-mode
dotnet restore src/Runtime/Simulation/BrutalBreakout.Simulation.csproj --locked-mode
dotnet restore src/Runtime/Input/BrutalBreakout.Input.csproj --locked-mode
dotnet restore ../../crushkit-brutal/tests/Draw2D/Draw2D.Tests.csproj --locked-mode
dotnet restore tests/Runtime/Simulation/Simulation.Tests.csproj --locked-mode
dotnet restore tests/Runtime/Input/BrutalBreakout.Input.Tests.csproj --locked-mode
dotnet restore tests/Runtime/Gameplay/Gameplay.Tests.csproj --locked-mode
dotnet test ../../crushkit-brutal/tests/Draw2D/Draw2D.Tests.csproj -c Debug --no-restore
dotnet test ../../crushkit-brutal/tests/Draw2D/Draw2D.Tests.csproj -c Release --no-restore
foreach ($configuration in 'Debug', 'Release') {
    dotnet test tests/Runtime/Simulation/Simulation.Tests.csproj -c $configuration --no-restore
    dotnet test tests/Runtime/Input/BrutalBreakout.Input.Tests.csproj -c $configuration --no-restore
    dotnet test tests/Runtime/Gameplay/Gameplay.Tests.csproj -c $configuration --no-restore
}
dotnet build -c Debug --no-restore
dotnet build -c Release --no-restore
dotnet run --project src/App/BrutalBreakout.App.csproj -c Debug --no-build -- --cleanup-self-test
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Debug
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Release
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Debug -Mode Gameplay
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Release -Mode Gameplay
foreach ($configuration in 'Debug', 'Release') {
    pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration $configuration -Mode Gameplay -PhysicsConfig tests/Fixtures/physics-60hz.json -GameplayConfig tests/Fixtures/gameplay-custom.json -InputConfig tests/Fixtures/input-custom.json -ColorsConfig Config/ColorSchemes/night.json -StyleConfig tests/Fixtures/style-custom.json
}
```

Routine `--smoke-test` enables validation and renders in a **hidden GLFW window**. It submits the actual 64-call showcase (14 solid outlines, 8 Basic/dot patterned paths, and 4 angled + 4 chevron paths, plus filled/transform/clip/blend references and panel/marker shapes), reports expected shape counts and nonempty geometry at both 1280 x 720 and 960 x 540, asserts minimum vertex/index counts and five clip-command runs, plus per-appearance counters and nonempty indexed geometry tagged by dedicated showcase colors, requires presentations after resize, and has a 15-second deadline. This is GPU validation, not headless CPU, screenshot comparison, or real minimize/restore coverage. Hidden presentation is verified on one Windows GPU setup (model omitted for publication); other platforms/drivers remain unverified. The metadata regression above suppresses console creation too, samples foreground/window visibility every 10ms, and asserts unchanged foreground/no visible test windows with a 25-second process deadline. It does not move user focus; external user focus activity invalidates the observation. Sampling cannot exclude shorter transients. CPU generation skips iconified/zero-size windows; `Draw(null, background)` retains backend throttling. Validation errors cause nonzero exit. The three known Galaxy/GOG layer-naming warnings are upstream loader warnings, not validation errors; report other warnings separately.

`-Mode Showcase` is the hidden harness default and retains all gallery geometry/resize checks. `-Mode Gameplay` passes `--gameplay-smoke-test`: controlled fake raw snapshots traverse the **same InputRouter and BallSandboxSession** as live polling, using advances derived from the configured timestep (two ticks, bounded by the configured catch-up cap), without OS input injection. The harness uses explicit test fixtures rather than the user's editable production files and verifies the actual startup paths, timing/contact settings, complete logical gameplay geometry/normalized aim and deadzone against the selected JSON. `-PhysicsConfig`, `-GameplayConfig`, `-InputConfig`, `-ColorsConfig` and `-StyleConfig` select independent fixture files. Both modes pass `tests/Fixtures/window-default.json`; `-WindowConfig` selects another path but this receipt harness explicitly requires 1280×720 to preserve its fixed stale-extent regression. CPU tests prove custom800×600 values construct the published `GlfwWindow.CreateInfo.Size` independently of the logical arena. The harness checks selected window path, effective startup window/framebuffer, all nested HUD fields and actual indexed first-bar bounds; custom-HUD CPU cases compare bar and win/loss-marker vertices, exercising both max-size and fraction-limited branches. A real-router inside/outside deadzone probe and measured native launch speed prove the loaded values are used; reset checks use the configured spawn. Critical stages advance only after a true indexed-geometry presentation receipt (not merely a clear-only presentation): initial aim, launch, blocked above-line catch, brick destruction and return-below-line eligibility, catch at the preserved position, six held frames, separate release/repress relaunch, restart, and six resumed 960 x 540 presentations. It asserts states, bounds and position preservation, and reports actual indexed arena/bricks/catch-line/HUD/ball/aim/background/held-ring geometry at both 1280 x 720 and 960 x 540. Both hidden modes always request `Visible=0`, `Focused=0`, `FocusOnShow=0`; the shared harness uses `CreateNoWindow=true`. GPU modes and `--showcase` are mutually exclusive. Before the scenario, it deliberately builds a 1281 x 720 producer frame for the actual 1280 x 720 framebuffer until the renderer discards it and successfully presents a clear-only frame. The required `Gameplay receipt regression PASS` log proves `PresentedFrames` increased while the geometry receipt was false and nonempty-frame/viewports/stage evidence remained at zero/zero/Initial; the harness requires this before any gameplay viewport or stage log. It then resumes correct-extent geometry and all existing stages. There is no minimize/restore or hardware-input coverage in hidden gameplay.

Final milestone counts are recorded in the dated results note. Coverage includes strict nested breakout/color schemas, native brick lifecycle and casts, catch-line facts and boundaries, host catch/miss/restart at 60/120/240Hz, terminal geometry and identical-color schemes. Prior native timing/partition and ownership tests remain. Build/publish asset copies are verified.

Hidden checks retain the stale-frame clear-only receipt regression, two framebuffer sizes and focus/visibility sampling. The breakout scenario proves an above-line press does not catch, a native brick disappears, below-line catch preserves position, held input does not relaunch, separate release/repress launches, and restart restores the level. Alternate gameplay/input/physics plus Night colors exercise independent overrides and effective linear roles. GPU checks prove submission/behavior, not appearance or hardware-controller feel; known Galaxy/GOG naming warnings are reported separately.

CPU host tests preserve edge latching/one-time consumption, catch/relaunch, restart precedence, focus loss/resume with no catch-up or held-action replay, direction mapping, pointer/DPI/letterbox/zero-framebuffer behavior and phase geometry. Eight new ownership cases cover normal/exceptional scope teardown before outer dependencies, repeated disposal, zero-tick/inactive use-after-dispose rejection, host-input wrong-thread rejection and independent repeated host lifetimes. Direct simulation submissions are thread-independent; queued-tick, snapshot and drain tests cover the job-based execution path. Every session/direct simulation fixture is disposed; Gameplay native tests are serialized to avoid global allocator create/destroy races. Gameplay tests reference the executable assembly without executing its entry point.

Cleanup injects resource-lifetime helper failures, not actual device loss. CPU tests validate geometry/ABI contracts, not GPU image correctness. Visible tests are **separate explicit opt-ins**; obtain user permission for desktop/focus disturbance before running:

```powershell
# Visible resize/minimize/idle/restore/resumed presentation; 15-second app deadline:
dotnet run --project src/App/BrutalBreakout.App.csproj -c Debug --no-build -- --lifecycle-smoke-test
# Visible external resize stress (bounded, per-iteration cleanup):
pwsh -NoProfile -File tests/Rendering/resize-smoke.ps1 -Configuration Debug
pwsh -NoProfile -File tests/Rendering/resize-smoke.ps1 -Configuration Release
```

All test-created windows request `Focused=false` and `FocusOnShow=false`; external resize uses `SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS`. Native lifecycle restore can still activate; **no portable zero-focus guarantee** is made for visible lifecycle mode. No test calls focus/foreground activation APIs. Normal interactive `dotnet run --project src/App/BrutalBreakout.App.csproj` now selects the ball sandbox; `--showcase` selects the gallery. With permission, also inspect/close the visible demo and repeat startup/shutdown. The Windows-only external-resize regression changes the native window asynchronously during rendering, including 40 rapid resizes per iteration, and retains logs under the system temp directory. This catches acquire/present out-of-date exceptions that between-frame smoke resizing can miss.

## Showcase guide

Four columns by three rows, left to right then top to bottom. The primitive geometry itself uses no fonts; startup overlay assets are required for the F1 inspector, and ordinary gallery artwork remains free of HUD labels:

1. Filled cyan disc, gold ring, coral rectangle, cyan rounded rectangle and gold triangle.
2. Solid circle, rectangle, rounded rectangle and triangle outlines.
3. Inside, center and outside circle outlines, left to right; thin white strokes mark their input boundaries.
4. Mint angled dashes: modifiers +1, -1, +0.5 from top to bottom.
5. Two nonuniform scales: cyan Local widths above, gold Framebuffer widths below (all width 6). Framebuffer widths remain physical pixels even as the design canvas fits the window.
6. Basic/butt, square and round dashes, then genuine circular dots. Dash rows share occupied centerline lengths and allow space for cap extensions.
7. Continuous dash phase across a corner; three closed rings demonstrate phased fixed-period dashes, closure-fitted natural dashes, and fixed dots.
8. Lilac chevron dashes: modifiers +1, -1, +0.5 from top to bottom, with concave V-shaped ends rather than slanted stripes.
9. Rotated rounded rectangle, scaled/sheared ring, reflected/sheared triangle and white open solid bevel path.
10. Nested framebuffer scissors: disc under the outer clip, coral/gold under the inner clip, white segment under only the outer clip.
11. Ordered half-alpha linear red/blue over black: blue-over-red above, red-over-blue below; a linear 0.5 gray bar at right.
12. Closed, corner-crossing mint angled (-1) and lilac chevron (+1) paths, using fixed counts and periodic phase. Red/green top markers retain orientation cues.

Both decorative appearances use the same actual submissions in `--showcase` and hidden renderer smoke. The new layout/visuals have **not been capture-checked**; the user can inspect them interactively later. Hidden tests are not image goldens or minimize/restore tests.

Prior to this outline/pattern layout, targeted client-area samples on the tested GPU (model redacted for publication) measured red-only `(188,0,0)`, blue-only `(0,0,188)`, blue-over-red `(138,0,188)`, red-over-blue `(188,0,138)`, gray `(188,188,188)`. These historical blend/sRGB checks are not new visual evidence for this layout, image goldens, or evidence for other devices. Hidden GPU tests validate submission and resize, not visual appearance or minimize/restore.

## Shape API and limits

The maintained drawing API, color/borrowed-frame contracts, stroke patterns and geometry limits are documented in [CrushKit Draw2D](../../crushkit-brutal/docs/draw2d.md). Breakout's showcase remains a game-owned integration demo of that API.

## Coordination and investigation records

Shared agent tooling and maintained usage/reference documentation live in the separate `brutal-coord` workspace. In the coordinated checkout, see `../docs/README.md`, `../docs/usage/pi-support.md` and `../tasks/README.md`. Unfinished work belongs in its `tasks/`; disposable logs/probes go in its ignored `.local/`, not a permanent investigation archive. Start Pi from the coordination root; ordinary game build/run/test commands above still run from this repository.

## How this differs from Unity

- `src/App/Program.cs` owns initialization, OS event processing and shutdown; no Unity player lifecycle.
- GLFW creates the window, not graphics. The main thread polls and renders; minimization waits briefly for events. The host session uses a synchronous accumulator with the shared configured fixed frequency and catch-up limit; input edges apply only on the first tick, not once per render or catch-up tick.
- Vulkan acquires an image, records a render pass and ordered indexed draws, submits and presents. A fence protects command/upload reuse; semaphores coordinate acquisition/presentation.
- `BallSimulation.Step` is the small explicit equivalent of gameplay `FixedUpdate`; state is plain data, not a GameObject/MonoBehaviour. Input snapshots are the equivalent of sampled player controls, and `BallSandbox.Build` is a state-to-draw-list presentation pass. There are no per-ball tasks or awaited frame continuations.
- BRUTAL is a framework; scene/entity/loading policy remains our responsibility. Add only what the game needs.

## Project files and dependencies

- `src/App/Program.cs`: main-thread lifecycle and bounded smoke/self-tests.
- `tools/Showcase/ShapeShowcase.cs`: fitted twelve-panel demo (`--showcase`).
- `src/Runtime/Game/Presentation/BallSandbox.cs`, `src/Runtime/Game/Sessions/BallSandboxSession.cs` and `tests/Rendering/BallSandboxSmoke.cs`: arena geometry, pure session/viewport bridge and hidden scripted scenario.
- `Config/physics.json`, `Config/execution.json`, `Config/gameplay.json`, `Config/input.json`, `src/Runtime/Game/Configuration/`: authoritative production defaults, strict nested startup-only loading, independent CLI overrides and pre-native cross-validation.
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
