# brutal-breakout

A Windows-first 2D game built with C# and [BRUTAL](https://github.com/ahwootech/BRUTAL). Current milestone: brick clearing with the Unity prototype's catch line and swappable schemes on a small direct Vulkan renderer—not a general engine.

## Requirements

- .NET SDK 10.0.201 (or a newer patch in the same feature band; see `global.json`).
- Windows for the currently tested setup.
- Vulkan 1.3 GPU/driver, combined graphics/present queue, color-attachment swapchain support, B8G8R8A8_SRGB or R8G8B8A8_SRGB surface format, and 4x-MSAA support for the selected color format. There is no non-sRGB or lower-sample-count fallback; unsupported hardware fails clearly.
- Vulkan SDK validation layers for Debug development and smoke tests. Players need the driver runtime, not the SDK.

BRUTAL packages and native libraries restore from NuGet; no framework source checkout or external shader build step is required. Embedded GLSL compiles once at startup through ShaderC (no external `glslc`).

## Build and run

```powershell
dotnet restore --locked-mode
dotnet build --no-restore
dotnet run --no-build
```

Normal `dotnet run --no-build` opens a resizable **brick-clearing game** at the initial physical size in `window.json` (shipped 1280 x 720). Use `dotnet run --no-build -- --showcase` for the existing twelve-panel primitive gallery. These interactive commands open visible windows; automated validation below uses hidden windows only. Debug enables `VK_LAYER_KHRONOS_validation` by default and fails if unavailable; `--no-validation` opts out. Release enables validation with `--validation` or any GPU test mode.

### Breakout rules and controls

- Aim with **WASD / arrows / left stick / mouse movement**. Active keyboard direction wins over stick, then moved/clicked mouse; a stationary unused mouse does not override direction input. Centered stick preserves the last aim. Initial aim comes from `gameplay.json` (the shipped file points up).
- **Space / left mouse button / gamepad south (A)** launches or catches. Shot speed and ball radius come from `gameplay.json` (the shipped values are 600 logical units/second and radius 12), with no gravity or drag; subsequent velocity belongs to the native solver (no per-tick speed normalization). Top and side walls reflect; the bottom is open. Single-hit bricks reflect the ball and disappear.
- After launch, the ball must **leave the lower catch zone and hit a wall or brick**, then return below/on the catch line before it can be caught. The restriction is on the ball center, not the cursor. Crossing the line does not catch automatically. The ball retains its configured disc color when eligible; eligibility does not add a flight halo or recolor.
- Once eligible, press from any cursor position to catch the ball below the line. Catch holds the ball at its actual position, not a spawn point. **Release and press separately** to relaunch; a catch never fires on the same action.
- The shipped game has **40 bricks and three balls**. Missing the bottom consumes a ball; destroyed bricks remain destroyed. More than the configured maximum wall bounces without a brick also consumes a ball. Clear all bricks to win; exhaust balls to lose. The title bar reports status/counts; progress bars and terminal markers provide scene feedback.
- **R / gamepad Start** restores all bricks, balls and initial pose, taking priority over pending actions. Radius-aware native aim previews include live bricks and exclude destroyed ones; ambiguous/no-hit casts show a short direction indicator.
- Unfocused or minimized windows pause simulation and discard accumulated time/pending edges. The first active frame consumes zero elapsed time; held actions on return are baselined, requiring release/repress. Physics continues when rendering is merely unavailable/out-of-date, not minimized.
- The configured logical canvas uniformly fits the physical framebuffer with centered letterboxing. Mouse coordinates convert from logical-window units through framebuffer DPI and fit margins, then aim from the ball center. Zero-size/nonfinite/letterbox targets are ignored.

Rules reference the Unity `Projectile.cs`, `ProjectileFactory.cs` and `GameSettings-Breakout.asset`: bottom-third catch zone, leave-zone plus contact, top/side walls, limited spawns and bounce expiry. No paddle, progression, damage scaling, drag or level/editor framework. Input uses standardized GLFW mappings; polling can miss complete edges or disconnect/reconnect between frames. No physical-controller or visible/image playtest is claimed for this increment. Published API evidence remains in `Platform/Input/API-EVIDENCE.md` and `Simulation/API-EVIDENCE.md`.

### Initial window configuration

`window.json` is the only production default source for initial **physical window** dimensions: required positive integer `width` and `height`. These are independent of `gameplay.json`'s logical canvas; fit/letterboxing still derives from that canvas and the actual framebuffer. Both game and gallery require this file. `--window-config <path>` independently overrides it, using caller cwd for relative paths; default lookup uses the executable directory. Build and publish copy it alongside the other configuration assets. Startup reports selected absolute path, requested dimensions and actual created window/framebuffer dimensions.

The immutable `WindowSettings(int width, int height)` has no defaults or mutable properties. The strict loader rejects missing/unreadable files, absent/null/unknown/duplicate/escaped-duplicate fields, wrong types and nonpositive/out-of-int-range dimensions **before GLFW or a native world**. There is no fallback, partial merge or hot reload. Gallery still loads no implicit physics/game/input/colors/style files; explicit overrides remain validated. `--cleanup-self-test` exits before any configuration lookup or native startup. Positive integer representability does not promise OS/GPU support for arbitrarily huge windows.

### Physics configuration

`physics.json` is the **only production default source** for physics timing/tuning. It is copied to build and publish output; normal startup reads the file beside the executable, independent of the caller's working directory. Edit the source file and run/build again, edit the deployed copy, or select a file explicitly:

```powershell
dotnet run -- --physics-config ./physics.json
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

`gameplay.json` and `input.json` are the only production default sources for ball/arena geometry and stick deadzone. Like `physics.json`, they are copied beside the executable on build/publish and loaded before GLFW/native world creation. Edit the source files and build/run again, edit deployed copies, or choose independent overrides:

```powershell
dotnet run -- --gameplay-config ./my-gameplay.json --input-config ./my-input.json
# All three overrides can be combined:
dotnet run -- --physics-config ./my-physics.json --gameplay-config ./my-gameplay.json --input-config ./my-input.json
```

Every section/field is required; there is no partial merge, C# fallback or parameterless settings constructor. Unknown/misspelled, duplicate (including escaped names at nested levels), missing, null or wrong-type fields fail with file-path context. Each option accepts one path; relative paths use caller cwd. Showcase-only modes need neither implicit gameplay nor input files, but validate any explicit overrides (window configuration is always required). Changes apply on next launch, not through hot reload.

| File / section | Required fields and meaning |
| --- | --- |
| `gameplay.json` / `arena` | `width`, `height`: logical canvas; `left`, `top`, `right`, `bottom`: inner wall faces; `wallThickness`: wall thickness in logical units. |
| `gameplay.json` / `ball` | `radius`, `speed`: logical radius and units/second; `spawnX`, `spawnY`: reset center; `initialAimX`, `initialAimY`: finite nonzero direction, normalized once. |
| `gameplay.json` / `breakout` | `startingBalls`, `catchZoneHeightFraction` (canvas fraction measured up from bottom), `maxBouncesWithoutBrick` (0 disables expiry), `bricks`: rectangles with `x`, `y`, `width`, `height`. |
| `input.json` | `stickDeadzone`: radial deadzone in **[0,1)**, validated after float conversion; the boundary is inactive. |

Ordered walls including thickness must fit the canvas, radius must leave a nonempty playable area, and spawn must fit the radius-inset bounds. Invalid/degenerate native float geometry and unsafe speed/timestep combinations reject before a world is allocated; errors for combined physics/gameplay incompatibility identify both paths. Validation establishes numerical representability, not stable physics for every extreme setting.

The same immutable settings drive native walls/circle/velocity, reset/casts, drawing and pointer/DPI fitting. Native speed/radius/positions are **derived via the one unit conversion**, never separately authored meter defaults. Cast reach derives from configured geometry; native maximum linear speed does not silently undercut requested launch speed. Rendering uses the configured inner faces and thickness rather than a second wall layout. The required breakout section validates a nonoverlapping layout inside the walls, above the catch line and clear of the spawn circle. Explicit null breakout settings are only for library physics-reference tests, not a production JSON fallback.

### Swappable color schemes

`colors.json` maps the **active serialized Unity setup**, not just palette assets or camera clear color. `BackgroundUICanvas.prefab` has an inactive untinted image and an active `Image.001`: .22 gray tint multiplied by a vertical coral-bottom/cream-top gradient. UI Color32 tint quantization is 56/255; authored opaque sRGB approximations are **top #33332D / bottom #381B18**. The scene only overrides placement/camera. The gradient covers the physical framebuffer, including letterboxes. Clear-only frames use the configured top `background`, not a full gradient.

The default projectile (including catch-ready flight) and all three normal-brick roles are coral `#FF7D6E`; bricks have rounded fills with no invented keylines. Walls and aim/contact/reflected lines are white. The catch line and held selection ring are cream `#E8E7CB`. Semantic roles remain independent for alternative themes.

```powershell
dotnet run -- --colors-config color-schemes/night.json
```

Combine with any other config overrides. Schemes require `name`, a `palette` of named opaque sRGB `#RRGGBB` colors, and `roles` referencing palette names: `background`, `backgroundBottom`, `heldRing`, `walls`, `ball`, `catchReady`, `catchLine`, `aim`, `brickPrimary`, `brickSecondary`, `brickAccent`, `hud`, `win`, `loss`. Missing/unknown/duplicate fields, bad hex and undefined references fail before platform startup. Edit/select a file for the next launch; no hot reload. Both shipped schemes copy to build/publish. Colors convert from sRGB to linear once; the CPU gradient supplies per-vertex linear colors to the unchanged shader/vertex ABI. Roles may share colors; actual index-range receipts never depend on color identity. The primitive gallery retains its demonstration styling.

### Presentation style configuration

`style.json` is the only production source of new presentation tunables. Use `--style-config <path>` independently of color/game/input/physics overrides. Like those files, all fields are required, unknown/missing/null/duplicate (including escaped-name) fields reject, default lookup is beside the executable, relative overrides use caller cwd, and gallery modes load no implicit style file. Explicit overrides are always validated before GLFW/world creation. Build/publish copies include the style. No merge, optional constructor fallback, hot reload or simulation-timing change.

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

### Bounded remaining-literal audit

Production Simulation, Platform/Input, Presentation (including host/viewport), Program, Vulkan renderer, Shapes and shaders were source-inspected. This is a tuning audit, not a settings framework: tests, showcase artwork and validation scenarios/deadlines are not player defaults. No Simulation/Platform or renderer implementation, package references, locks or backend capabilities changed.

| Capability / literal family | Classification / rationale |
| --- | --- |
| Initial window 1280×720 | **Configured:** `window.json`; same validated immutable settings supply game/gallery `CreateInfo.Size`. Logical arena remains independent. |
| HUD bars 120/.25/6/1/4/.5; terminal 28/.125/4 | **Configured:** the nine required `style.json` / `hud` fields above; same immutable object drives emitted geometry. |
| Fixed rate, solver substeps, catch-up cap, contact Hz/damping | **Already configured:** five `physics.json` fields; reciprocal timestep is derived, not a second default. |
| Arena/wall/ball/spawn/aim, brick layout, balls/catch zone/bounce expiry | **Already configured:** `gameplay.json`; zero disabling bounce expiry is schema semantics, not a hidden default. |
| Stick deadzone | **Already configured:** `input.json`; keyboard zero threshold is digital direction semantics. |
| Colors, ring/aim/reflection/catch-line/brick styling | **Already configured:** semantic schemes and the 13 existing scalar style fields; palette's three brick roles are a schema contract. |
| Native units 100 logical units/meter | **Retained:** shared unit definition, not gameplay tuning. |
| Friction0, restitution1, gravity0, damping0; bullet/fixed rotation, density1 | **Retained:** current frictionless elastic mode and exact-reflection preview model. Exposing forces/material response would invalidate preview or require an unrequested physics framework; density has no game-feel purpose with direct velocity against static colliders. |
| Native workerCount0, sleeping/events/filter bits, wall counts3/4 | **Retained:** synchronous ownership, collision routing, contact facts and open/closed-arena topology contracts; not performance/gameplay defaults. |
| Native cast tie1e-6, normal .5..1.5/.999, event thresholds0 | **Retained:** native contact/query validity and ambiguity numerics, not aim behavior sliders. |
| Secondary query skin1; contact allowance2 + roundoff.001 | **Retained:** documented native slop/query separation and bounded validation evidence, not collision-radius or feel settings. |
| Half extents/centers, radius clearances, checkmark quarter/half points, Tau | **Retained:** geometry/artwork/math definitions. |
| GLFW pad IDs0..15, input signs/axis/button mappings | **Retained:** published API IDs and current control contract; no new binding framework. |
| Vulkan1.3, sRGB, MSAA4, FIFO, one graphics/present queue | **Retained:** supported backend capability contract, not promised quality/vsync choices. |
| Shader locations/24-byte vertex/8-byte push constants, attachment/index counts | **Retained:** GPU ABI and API resource topology, not styling. |
| Renderer wait .05s / acquire100ms, image min+1, buffer growth | **Retained:** bounded idle/acquire lifecycle and resource allocation policy; no pacing framework. |
| Shapes 2^20 envelope, 1024 source points/bricks, 4096 patterns/tessellation, 4M work, 1M/3M default buffers | **Retained:** precision/safety/complexity budgets, not player quality settings. |
| Tessellation .25-.09-r×1e-6, epsilon64×double epsilon, sRGB transfer constants | **Retained:** accuracy bounds/roundoff and standard color conversion definitions. |
| Hidden1280→960 resize, forced1281 stale producer, frame/stage counts/deadlines; showcase artwork | **Retained test fixtures:** explicitly independent of editable production defaults, not normal startup configuration. |

No additional genuine user knob was found in this bounded scan. Native-model/backend changes require separate scope/preview work, not misleading configuration switches. Existing behavior expectations were retained; constructor/schema fixtures now explicitly include HUD data.

### Native physics and ownership

Published **Ahwoo.BRUTAL.Box2D 2026.10.2** replaces the custom collision solver: one native world owns three static arena walls, live static brick bodies and a dynamic circle with bullet CCD. Published `B2Body.Dispose()` was DLL-inspected and headless compile/runtime-probed before use. Events are consumed and deduplicated before destroying hit bodies. The fixed clock, input staging and catch/aim/restart policy remain our game code. The conversion is **100 logical units per meter**, stepping at the configured fixed frequency/substep count, synchronously on the construction thread. Each disposable session owns its simulation/world; the hidden gameplay script owns its own session. Both normal and exceptional app exits dispose these before draw-list/renderer teardown. Showcase and renderer-only smoke modes create no physics world. Snapshot state remains readable after disposal; native operations and session advances do not.

Native contacts are not analytic radius-inset guarantees: bounded checks allow **2 logical units of contact penetration plus a separately named .001 coordinate-roundoff margin**, without clamping positions. At the original explicit 120Hz/four-substep test fixture, the measured worst was 2.000179291 units over 100,000 ticks/ten shots; speed drift was .000190735 units/second. Those measurements are not a universal tolerance guarantee for every configuration. Native cast contact centers have about .5-unit slop, so post-impact paths are not the old exact time-of-impact trajectories. Corners need not yield a combined two-axis normal; initial overlap/zero-normal or ambiguous casts can legitimately have no hit. The presentation's secondary full-radius cast uses a **one-unit query-only separation skin** along the first native normal; it does not move the ball or shrink collision geometry, and clips the fixed short reflected leg when the next obstruction is available. See the linked physics evidence for measured gates and exact policies.

Published native assets cover **win-x64, linux-x64 and osx-arm64 only**; only Windows x64 runtime behavior is verified here. Native worlds are explicitly disposed on their construction thread, with no finalizer or shared global world. Native world allocation tests are serialized.

### Validation

Run GPU commands sequentially, with no other lane using the GPU:

```powershell
dotnet restore --locked-mode
dotnet restore Rendering/Shapes/BrutalBreakout.Shapes.csproj --locked-mode
dotnet restore Simulation/BrutalBreakout.Simulation.csproj --locked-mode
dotnet restore Platform/Input/BrutalBreakout.Input.csproj --locked-mode
dotnet restore tests/Shapes/Shapes.Tests.csproj --locked-mode
dotnet restore tests/Simulation/Simulation.Tests.csproj --locked-mode
dotnet restore tests/Input/BrutalBreakout.Input.Tests.csproj --locked-mode
dotnet restore tests/Gameplay/Gameplay.Tests.csproj --locked-mode
dotnet test tests/Shapes/Shapes.Tests.csproj -c Debug --no-restore
dotnet test tests/Shapes/Shapes.Tests.csproj -c Release --no-restore
foreach ($configuration in 'Debug', 'Release') {
    dotnet test tests/Simulation/Simulation.Tests.csproj -c $configuration --no-restore
    dotnet test tests/Input/BrutalBreakout.Input.Tests.csproj -c $configuration --no-restore
    dotnet test tests/Gameplay/Gameplay.Tests.csproj -c $configuration --no-restore
}
dotnet build -c Debug --no-restore
dotnet build -c Release --no-restore
dotnet run -c Debug --no-build -- --cleanup-self-test
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Debug
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Release
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Debug -Mode Gameplay
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Release -Mode Gameplay
foreach ($configuration in 'Debug', 'Release') {
    pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration $configuration -Mode Gameplay -PhysicsConfig tests/Gameplay/Fixtures/physics-60hz.json -GameplayConfig tests/Gameplay/Fixtures/gameplay-custom.json -InputConfig tests/Gameplay/Fixtures/input-custom.json -ColorsConfig color-schemes/night.json -StyleConfig tests/Gameplay/Fixtures/style-custom.json
}
```

Routine `--smoke-test` enables validation and renders in a **hidden GLFW window**. It submits the actual 64-call showcase (14 solid outlines, 8 Basic/dot patterned paths, and 4 angled + 4 chevron paths, plus filled/transform/clip/blend references and panel/marker shapes), reports expected shape counts and nonempty geometry at both 1280 x 720 and 960 x 540, asserts minimum vertex/index counts and five clip-command runs, plus per-appearance counters and nonempty indexed geometry tagged by dedicated showcase colors, requires presentations after resize, and has a 15-second deadline. This is GPU validation, not headless CPU, screenshot comparison, or real minimize/restore coverage. Hidden presentation is verified on this Windows/RTX 5070 Ti setup; other platforms/drivers remain unverified. The metadata regression above suppresses console creation too, samples foreground/window visibility every 10ms, and asserts unchanged foreground/no visible test windows with a 25-second process deadline. It does not move user focus; external user focus activity invalidates the observation. Sampling cannot exclude shorter transients. CPU generation skips iconified/zero-size windows; `Draw(null, background)` retains backend throttling. Validation errors cause nonzero exit. The three known Galaxy/GOG layer-naming warnings are upstream loader warnings, not validation errors; report other warnings separately.

`-Mode Showcase` is the hidden harness default and retains all gallery geometry/resize checks. `-Mode Gameplay` passes `--gameplay-smoke-test`: controlled fake raw snapshots traverse the **same InputRouter and BallSandboxSession** as live polling, using advances derived from the configured timestep (two ticks, bounded by the configured catch-up cap), without OS input injection. The harness uses explicit test fixtures rather than the user's editable production files and verifies the actual startup paths, timing/contact settings, complete logical gameplay geometry/normalized aim and deadzone against the selected JSON. `-PhysicsConfig`, `-GameplayConfig`, `-InputConfig`, `-ColorsConfig` and `-StyleConfig` select independent fixture files. Both modes pass `tests/Gameplay/Fixtures/window-default.json`; `-WindowConfig` selects another path but this receipt harness explicitly requires 1280×720 to preserve its fixed stale-extent regression. CPU tests prove custom800×600 values construct the published `GlfwWindow.CreateInfo.Size` independently of the logical arena. The harness checks selected window path, effective startup window/framebuffer, all nested HUD fields and actual indexed first-bar bounds; custom-HUD CPU cases compare bar and win/loss-marker vertices, exercising both max-size and fraction-limited branches. A real-router inside/outside deadzone probe and measured native launch speed prove the loaded values are used; reset checks use the configured spawn. Critical stages advance only after a true indexed-geometry presentation receipt (not merely a clear-only presentation): initial aim, launch, blocked above-line catch, brick destruction and return-below-line eligibility, catch at the preserved position, six held frames, separate release/repress relaunch, restart, and six resumed 960 x 540 presentations. It asserts states, bounds and position preservation, and reports actual indexed arena/bricks/catch-line/HUD/ball/aim/background/held-ring geometry at both 1280 x 720 and 960 x 540. Both hidden modes always request `Visible=0`, `Focused=0`, `FocusOnShow=0`; the shared harness uses `CreateNoWindow=true`. GPU modes and `--showcase` are mutually exclusive. Before the scenario, it deliberately builds a 1281 x 720 producer frame for the actual 1280 x 720 framebuffer until the renderer discards it and successfully presents a clear-only frame. The required `Gameplay receipt regression PASS` log proves `PresentedFrames` increased while the geometry receipt was false and nonempty-frame/viewports/stage evidence remained at zero/zero/Initial; the harness requires this before any gameplay viewport or stage log. It then resumes correct-extent geometry and all existing stages. There is no minimize/restore or hardware-input coverage in hidden gameplay.

Final milestone counts are recorded in the dated results note. Coverage includes strict nested breakout/color schemas, native brick lifecycle and casts, catch-line facts and boundaries, host catch/miss/restart at 60/120/240Hz, terminal geometry and identical-color schemes. Prior native timing/partition and ownership tests remain. Build/publish asset copies are verified.

Hidden checks retain the stale-frame clear-only receipt regression, two framebuffer sizes and focus/visibility sampling. The breakout scenario proves an above-line press does not catch, a native brick disappears, below-line catch preserves position, held input does not relaunch, separate release/repress launches, and restart restores the level. Alternate gameplay/input/physics plus Night colors exercise independent overrides and effective linear roles. GPU checks prove submission/behavior, not appearance or hardware-controller feel; known Galaxy/GOG naming warnings are reported separately.

CPU host tests preserve edge latching/one-time consumption, catch/relaunch, restart precedence, focus loss/resume with no catch-up or held-action replay, direction mapping, pointer/DPI/letterbox/zero-framebuffer behavior and phase geometry. Eight new ownership cases cover normal/exceptional scope teardown before outer dependencies, repeated disposal, zero-tick/inactive use-after-dispose rejection, wrong-thread rejection and independent repeated host lifetimes. Every session/direct simulation fixture is disposed; Gameplay native tests are serialized to avoid global allocator create/destroy races. Gameplay tests reference the executable assembly without executing its entry point.

Cleanup injects resource-lifetime helper failures, not actual device loss. CPU tests validate geometry/ABI contracts, not GPU image correctness. Visible tests are **separate explicit opt-ins**; obtain user permission for desktop/focus disturbance before running:

```powershell
# Visible resize/minimize/idle/restore/resumed presentation; 15-second app deadline:
dotnet run -c Debug --no-build -- --lifecycle-smoke-test
# Visible external resize stress (bounded, per-iteration cleanup):
pwsh -NoProfile -File tests/Rendering/resize-smoke.ps1 -Configuration Debug
pwsh -NoProfile -File tests/Rendering/resize-smoke.ps1 -Configuration Release
```

All test-created windows request `Focused=false` and `FocusOnShow=false`; external resize uses `SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS`. Native lifecycle restore can still activate; **no portable zero-focus guarantee** is made for visible lifecycle mode. No test calls focus/foreground activation APIs. Normal interactive `dotnet run` now selects the ball sandbox; `--showcase` selects the gallery. With permission, also inspect/close the visible demo and repeat startup/shutdown. The Windows-only external-resize regression changes the native window asynchronously during rendering, including 40 rapid resizes per iteration, and retains logs under the system temp directory. This catches acquire/present out-of-date exceptions that between-frame smoke resizing can miss.

## Showcase guide

Four columns by three rows, left to right then top to bottom, without font dependencies:

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

Prior to this outline/pattern layout, targeted client-area samples on the RTX 5070 Ti measured red-only `(188,0,0)`, blue-only `(0,0,188)`, blue-over-red `(138,0,188)`, red-over-blue `(188,0,138)`, gray `(188,188,188)`. These historical blend/sRGB checks are not new visual evidence for this layout, image goldens, or evidence for other devices. Hidden GPU tests validate submission and resize, not visual appearance or minimize/restore.

## Shape API and limits

Use a disposable `ShapeDrawList`, `BeginFrame(width, height[, worldToPixel])`, primitive calls, then `Finish()` and synchronously `VulkanRenderer.Draw(frame)` before the next `BeginFrame`. Frames borrow producer buffers: never retain after the next frame or disposal. The demo disposes the list, including BRUTAL mesh native scratch, before renderer shutdown. GLFW, frame production and graphics stay on the main thread. `Draw` returns true only when at least one indexed draw from the supplied current-extent frame was recorded and presentation returned Success/Suboptimal. It returns false for clear-only, mismatched-extent, empty/all-clipped frames and recoverable unavailable/out-of-date presentation paths; existing fatal errors still throw. `PresentedFrames` continues counting all successful presentations, including clear-only. Callers may ignore the return. `Draw(null, background)` clears only; skip `BeginFrame` for zero extents.

Coordinates are physical framebuffer pixels, origin top-left, +Y down. `SetTransform` applies local-to-world before world-to-pixel; local radii/widths transform too. `PushClip` intersects a half-open integer framebuffer scissor with its parent; balance with `PopClip`. Clips are not transformed local geometry. Submission order is preserved, merging only adjacent equal-scissor commands. `LinearColor` accepts straight linear RGBA in [0,1]; use `FromSrgb` for sRGB source colors. Vertices premultiply once; blending uses ONE / ONE_MINUS_SRC_ALPHA and the sRGB attachment encodes once.

Filled calls: `FillDisc`, `FillRing`, `FillRect`, `FillRoundedRect`, `FillTriangle`. Solid open calls: `StrokeSegment` (butt/square/round caps), `StrokePolyline` (butt-cap, bevel-join). Solid closed calls: `StrokeCircle`, `StrokeRect`, `StrokeRoundedRect`, `StrokeTriangle`, `StrokeClosedPolyline`.

```csharp
using Brutal.Numerics;
using BrutalBreakout.Rendering.Shapes;

using var draw = new ShapeDrawList();
draw.BeginFrame(1280, 720);
var cyan = LinearColor.FromSrgb(.24f, .84f, .85f);
draw.StrokeRoundedRect(new(40, 40), new(200, 100), 20, 8, cyan,
    alignment: StrokeAlignment.Inside, space: StrokeSpace.Framebuffer);
draw.StrokePatternedSegment(new(40, 200), new(320, 200), 10,
    StrokePattern.Dashes(24, 18, offsetCycles: -.25), cyan, LineCap.Round);
draw.StrokePatternedCircle(new(440, 200), 60, 8,
    StrokePattern.FixedDots(16), cyan);
var pattern = StrokePattern.Dashes(36, 28, offsetCycles: .15);
draw.StrokePatternedSegment(new(40, 300), new(328, 300), 18,
    pattern.WithShape(DashShape.Angled, -1), cyan); // Butt only
draw.StrokePatternedSegment(new(40, 380), new(328, 380), 18,
    pattern.WithShape(DashShape.Chevron, .5), cyan);
var frame = draw.Finish(); // synchronously renderer.Draw(frame) before next BeginFrame/disposal
```

Outline width is full thickness; `Center` (default) puts half on either side of the input boundary, `Inside` preserves outer bounds and `Outside` preserves the inner boundary, independent of winding/reflection. Rectangles, triangles and convex closed polylines use miter corners. Rounded rectangles clamp radius to half the shorter extent; eroded radii clamp to zero. A consumed hole produces a solid outer silhouette, not inverted triangles.

`StrokeSpace.Local` (default) measures offsets, widths and pattern lengths before local/world transforms; nonuniform transforms also distort local circular dots. `Framebuffer` transforms the boundary/centerline first and then measures offsets and lengths in physical pixels, keeping width constant under scaling/shear. Transformed circles use sampled contours (including ellipses), not an analytic ellipse-arclength promise. Patterned strokes follow their centerline; they do not take an alignment argument.

Pattern calls: `StrokePatternedSegment`, `StrokePatternedPolyline(points, width, pattern, color, closed=false, cap=Butt, space=Local)` and `StrokePatternedCircle`. Factories:

- `StrokePattern.Dashes(length, gap, offsetCycles=0, fitClosed=false)` and `RelativeDashes(lengthMultiples, gapMultiples, offsetCycles=0, fitClosed=false)`.
- `StrokePattern.Dots(spacing, offsetCycles=0, fitClosed=false)` and `RelativeDots(spacingMultiples, offsetCycles=0, fitClosed=false)`.
- `StrokePattern.FixedDashes(count, gapFraction=0.5, offsetCycles=0)` and `FixedDots(count, offsetCycles=0)`.

Absolute lengths use the selected space; relative lengths multiply full width. Dash length is the **occupied centerline interval**, not the visible tip-to-tip length: square/round caps extend half a width past each interval. Gaps must allow these extensions and turns; touching/overlapping fragment geometry rejects rather than blending twice. Zero gap can become a continuous solid path. Dots are discs of diameter width, not short round dashes; cap appearance is ignored for dots but the enum is still validated.

Positive phase moves the lattice forward by a fraction of a period; phase wraps periodically, including negative offsets (`-.25` equals `.75`, `1` equals `0`). Arc-length phase continues across vertices; closed seam fragments are stitched rather than capped twice. `FixedDashes` fits **count complete periods**, not necessarily count disconnected visible islands: phase may split/clip endpoint fragments. `FixedDots` places count sites over `[0,length)`, never duplicating the far endpoint. Natural dots include an open far endpoint only when it is a lattice site; closed paths never duplicate seam dots. `fitClosed` rescales natural spacing to the nearest whole period count (midpoints upward, minimum one), preserving dash duty cycle; fixed-count patterns inherently fit. Open end-to-end snapping is not supported.

`pattern.WithShape(DashShape.Angled, modifier)` and `pattern.WithShape(DashShape.Chevron, modifier)` (modifier defaults to 1) return immutable styles retaining sizing, phase and fitting; factories remain `Basic` by default. Modifiers must be finite in **[-1, 1]**. Angled ends are slanted stripes; Chevron mirrors the slant across the width into concave V-shaped ends. Sign mirrors direction against supplied path order in the chosen Local/Framebuffer space (including reflections); magnitude sets depth. At magnitude 1 a straight angled boundary shifts one full width across the stroke (45-degree slope); each chevron half shifts half a width. Exactly zero uses Basic geometry, and Basic ignores a validated modifier. Dots reject non-Basic shapes, even with modifier zero.

Decorative styles own their end boundary and require `LineCap.Butt`, including modifier zero; Square/Round reject rather than being combined or ignored. Exactly-zero modifiers use Basic geometry, not an exemption from style/cap validation. Compatibility is validated even for invisible/no-output calls. Open decorative endpoints clip to the path's arclength domain with **no cap extension**; unlike Basic caps, the slanted tip is not extended beyond the supplied endpoints. Decoration continues across corners and the closed seam using a piecewise-affine **miter ribbon**, whereas Basic patterned open joins use bevels. Sharp/wide turns that invert this ribbon reject; there is no general path-union repair or guarantee of identical corner geometry as a nonzero modifier approaches zero. Zero-gap continuous patterns fall back to Basic solid geometry (including its joins), so decorative ends are indistinguishable; decorative cap restrictions still apply.

Decorative clipping has a strict float32 precision limit: even moderate-size paths can generate tiny clipped fragments near sampled curve edges, seams or chevron half boundaries. Inverted/overlapping actual-float tiles or loss of an entire occupied glyph reject the whole call transactionally rather than silently dropping a glyph, changing the modifier, or repairing geometry. Zero-area quantized clipping slivers may disappear if the glyph retains geometry. Sampled circles are supported, but not every radius/width/phase combination is representable under these checks; no universal safe circle recipe or epsilon-based repair is promised. The showcase uses closed rectangular paths as a robust corner/seam example, not a fallback after a caught circle failure. Existing coordinate, tessellation, complexity and output bounds still apply.

Paths accept at most 1024 input points. Consecutive duplicates, repeated closing points on closed paths, and forward-collinear runs are removed. Closed outlines/patterned paths require simple **convex** contours, either winding; nonconvex/self-crossing paths are not a general polygon-union facility. Open paths reject reversals, crossings, touching or overlapping geometry. Pattern output is limited to 4096 intervals/sites and pairwise validation to 4,000,000 candidates, including broad-phase tests; excessive complexity rejects even below the output limit. Zero-length patterned paths are validated no-ops, including round caps.

The Unity Shapes public interfaces in read-only PrototypeStudio were a **conceptual reference only**, not copied tessellation/shaders or a parity claim. Published BRUTAL packages were checked first: Numerics supplies vectors/transforms and Render.Mesh `GenerateDisk` supplies the tested disk adapter/rims; no complete planar outline/pattern facility was found. CPU tessellation fills that gap and submits the unchanged GPU triangle ABI; there is no new rendering backend or font dependency.

Curves target <=0.25 physical-pixel chord error (8-4096 full-circle segments). Framebuffer coordinate magnitude is bounded by 2^20; default budgets are 1,000,000 vertices / 3,000,000 indices. Invalid/nonfinite/negative input or state throws. Valid zero-size/alpha/empty-clip/singular-transform shapes emit nothing. Reflections are supported, but finite geometry can be unrepresentable in float32: actual inverted triangles or touching/crossing quantized polyline boundaries are rejected transactionally, not repaired. Budget/tessellation/pattern-complexity failures also throw without partial shape output, including command merging: each failing shape call leaves previously published vertices, indices and commands unchanged. This does not extend borrowed-frame lifetime.

Backend: one fence-protected in-flight upload frame, dynamic viewport/scissors, indexed triangles, classic render pass, 4x-MSAA resolve into sRGB swapchain, no depth/culling, VMA-backed buffers/images. Resize rebuilds attachment/pipeline resources. Actual device loss, other platforms/drivers, and unsupported-format/sample-count failure paths have not been exercised in integration.

Text is deferred: the published FreeType package contains native libraries but no managed bindings. No speculative font/texture pipeline is included.

## Project-local Pi support

Review `.pi/` before granting project trust. Start Pi **in this repository/worktree** and approve trusted project resources; use `/reload` after edits. A session started in `C:\Users\aaron\dev` does not automatically discover this child repo's resources: restart here, or explicitly load `.pi/extensions/brutal-support.ts` with `--extension` and each skill directory with `--skill` (absolute paths from another cwd). No settings, global packages, or auto-run hooks are installed.

- `/skill:brutal-package-audit`: published NuGet first, with required symbol/compile-probe evidence and a reuse/gap table. Metadata/assets alone are **not** API verification; no automatic reference edits.
- `/skill:validate-renderer`: doctor, locked restore/build, headless cleanup test, and hidden GPU smoke; visible lifecycle/stress needs separate explicit permission. Always report the 3 known GOG naming warnings separately from validation errors; observed counts vary. Doctor may also show unrelated loader warnings.
- Optional `brutal_support` tool: the same six bounded operations below, only on user request; `smoke` runs hidden GPU validation with SDK layers. The allowlist exposes no visible lifecycle/stress operation.

Scripts also work without Pi, from any cwd (Windows, PowerShell 7):

```powershell
pwsh -NoProfile -File <repo>/.pi/scripts/support.ps1 -Operation doctor
pwsh -NoProfile -File <repo>/.pi/scripts/support.ps1 -Operation build
pwsh -NoProfile -File <repo>/.pi/scripts/support.ps1 -Operation cleanup
pwsh -NoProfile -File <repo>/.pi/scripts/support.ps1 -Operation smoke # hidden GPU shapes/resize; coordinate GPU use
pwsh -NoProfile -File <repo>/.pi/scripts/support.ps1 -Operation package-versions -PackageId Ahwoo.BRUTAL.Core
pwsh -NoProfile -File <repo>/.pi/scripts/support.ps1 -Operation package-assets -PackageId Ahwoo.BRUTAL.Core -Version 2026.10.4
```

`cleanup` includes build; `smoke` includes cleanup. JSON reports root/caller cwd, bounded output tails, timeouts, and nonzero failures. Processes are bounded per stage; the tool adds a six-minute deadline and cancellation of the Windows process tree. Package downloads use public NuGet, are disposable, and require network access. Doctor checks PATH before existing SDK executable paths; it changes no machine configuration. Enumeration does not replace a GPU smoke test. Abrupt process-tree cancellation may leave a temporary download directory. Output truncation means incomplete evidence; there is no full-log artifact or automatic API probe execution. Run the CPU test commands above separately; support operations do not run them.

Support regression checks (no interactive Pi, GPU, or new dependencies):

```powershell
pwsh -NoProfile -File <repo>/.pi/tests/process.tests.ps1
node <repo>/.pi/tests/extension.test.mjs <installed-pi-package-root>
```

The second check uses the installed Pi loader/API and runs doctor plus cancellation checks; it requires a configured local Vulkan toolchain. Validated with Pi 1.4.0; it is not a standalone TypeScript compiler check.

## How this differs from Unity

- `Program.cs` owns initialization, OS event processing and shutdown; no Unity player lifecycle.
- GLFW creates the window, not graphics. The main thread polls and renders; minimization waits briefly for events. The host session uses a synchronous accumulator with the shared configured fixed frequency and catch-up limit; input edges apply only on the first tick, not once per render or catch-up tick.
- Vulkan acquires an image, records a render pass and ordered indexed draws, submits and presents. A fence protects command/upload reuse; semaphores coordinate acquisition/presentation.
- `BallSimulation.Step` is the small explicit equivalent of gameplay `FixedUpdate`; state is plain data, not a GameObject/MonoBehaviour. Input snapshots are the equivalent of sampled player controls, and `BallSandbox.Build` is a state-to-draw-list presentation pass. There are no per-ball tasks or awaited frame continuations.
- BRUTAL is a framework; scene/entity/loading policy remains our responsibility. Add only what the game needs.

## Project files and dependencies

- `Program.cs`: main-thread lifecycle and bounded smoke/self-tests.
- `Presentation/ShapeShowcase.cs`: fitted twelve-panel demo (`--showcase`).
- `Presentation/BallSandbox*.cs`: arena geometry, pure session/viewport bridge and hidden scripted scenario.
- `physics.json`, `gameplay.json`, `input.json`, `Configuration/`: authoritative production defaults, strict nested startup-only loading, independent CLI overrides and pre-native cross-validation.
- `Simulation/`: immutable validated physics settings, native Box2D world adapter, gameplay rules, radius-aware casts/preview and fixed-step clock.
- `Platform/Input/`: owner-thread GLFW poller, pure input router and published API evidence.
- `tests/Gameplay/`, `tests/Simulation/`, `tests/Input/`: standalone CPU suites; `tests/Rendering/hidden-smoke.ps1`: shared hidden GPU harness.
- `Rendering/Shapes/`: headless geometry/contracts/colors/clips and borrowed buffers.
- `tests/Shapes/`: CPU geometry and ABI tests.
- `Rendering/VulkanRenderer.cs`, `Rendering/VulkanRenderer.Shapes.cs`, `Rendering/Shaders/`: lifecycle, pipeline, uploads, MSAA and disposal.
- `Rendering/ResourceLifetime.cs`: best-effort cleanup preserving original failures.
- `brutal-breakout.csproj`, `global.json`, `packages.lock.json`: dependencies, SDK and locked graph.

App direct packages: `Ahwoo.BRUTAL.Core` 2026.10.4, `Ahwoo.BRUTAL.Glfw` 2026.10.5, and `Ahwoo.BRUTAL.ShaderC`, `Ahwoo.BRUTAL.Vulkan`, `Ahwoo.BRUTAL.Vulkan.Abstractions`, `Ahwoo.BRUTAL.Vulkan.Vma` 2026.10.3. The simulation directly pins `Ahwoo.BRUTAL.Box2D` exactly to 2026.10.2 (Native 2026.10.2, Core 2026.10.4 and Numerics 2026.10.2 transitively). The shapes library directly references `Ahwoo.BRUTAL.Numerics` 2026.10.2 and `Ahwoo.BRUTAL.Render.Mesh` 2026.10.2. BRUTAL `GenerateDisk` supplies discs through a tested adapter; custom tessellation fills genuine planar ring/rounded-rectangle/stroke gaps. GLFW native libraries arrive transitively. The locked graph includes a transitive preview `Microsoft.Extensions.ObjectPool`, not a direct addition.

## Git and release

Use feature branches; do not merge into `main` without approval. The private GitHub remote uses HTTPS: `https://github.com/aefreedman/brutal-breakout.git`. Use focused implementation and independent read-only review, one writer per tree.

Before distribution review the current [BRUTAL license](https://github.com/ahwootech/BRUTAL) and all third-party notices. BRUTAL requires copyright attribution in credits, its logo before gameplay, and a shipped `NOTICE.txt`; those release assets are not implemented here.

## Next milestone

The first ball interaction slice is implemented with provisional rules. Next: independently review integration and, only with user permission, inspect the visible arena and try real controls. Further gameplay choices remain unapproved. Text needs a verified managed wrapper first.

Planning lives outside the repo at `C:\Users\aaron\obs-mono\vaults\personal\brutal-breakout`, using `YY-MM-DD-kebab-case.md`. Avoid building a general engine ahead of the game.
