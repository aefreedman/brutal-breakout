# brutal-breakout

A Windows-first 2D game built with C# and [BRUTAL](https://github.com/ahwootech/BRUTAL). Current milestone: a first playable, wide single-ball catch/aim/relaunch sandbox on a small direct Vulkan renderer, not a general engine.

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

Normal `dotnet run --no-build` opens a resizable 1280 x 720 **ball arena**. Use `dotnet run --no-build -- --showcase` for the existing twelve-panel primitive gallery. These interactive commands open visible windows; automated validation below uses hidden windows only. Debug enables `VK_LAYER_KHRONOS_validation` by default and fails if unavailable; `--no-validation` opts out. Release enables validation with `--validation` or any GPU test mode.

### Ball sandbox: provisional rules and controls

- Aim with **WASD / arrows / left stick / mouse movement**. Active keyboard direction wins over stick, then moved/clicked mouse; a stationary unused mouse does not override direction input. Centered stick preserves the last aim. Initial aim is up.
- **Space / left mouse button / gamepad south (A)** launches or catches. Shots have fixed speed (600 logical units/second), no gravity or drag. Four reflecting wall faces enclose the arena; the ball has radius 12.
- Launch locks catching until the first wall bounce. The ball turns **cyan** when catch-eligible; gold indicates aiming or catch-locked flight. Aiming has a larger halo and white radius-aware first-contact circle/path plus a short reflected leg (clipped at the next contact).
- Once eligible, catch from anywhere. Catch holds the ball at its actual position, not a spawn point. **Release and press separately** to relaunch; a catch never fires on the same action.
- **R / gamepad Start** restarts to the initial position/aim, taking priority over a pending action.
- Unfocused or minimized windows pause simulation and discard accumulated time/pending edges. The first active frame consumes zero elapsed time; held actions on return are baselined, requiring release/repress. Physics continues when rendering is merely unavailable/out-of-date, not minimized.
- The logical 1280 x 720 arena uniformly fits the physical framebuffer with centered letterboxing. Mouse coordinates convert from logical-window units through framebuffer DPI and fit margins, then aim from the ball center. Zero-size/nonfinite/letterbox targets are ignored.

These are provisional sandbox choices, not exact old-game feel: no paddle, bricks, damage, failure, progression, power, restricted catch zone, or drag. Input uses standardized GLFW gamepad mappings. Polling can miss a full press/release or disconnect/reconnect between frames (including replacement at the same pad ID). No physical controller compatibility, hardware playtest, visible gameplay inspection, or screenshot correctness is claimed. Published input API evidence is in `Platform/Input/API-EVIDENCE.md`; the simulation's published Numerics reuse/gap audit is in `Simulation/Arena.cs`.

### Validation

Run GPU commands sequentially, with no other lane using the GPU:

```powershell
dotnet restore --locked-mode
dotnet restore Rendering/Shapes/BrutalBreakout.Shapes.csproj --locked-mode
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
```

Routine `--smoke-test` enables validation and renders in a **hidden GLFW window**. It submits the actual 64-call showcase (14 solid outlines, 8 Basic/dot patterned paths, and 4 angled + 4 chevron paths, plus filled/transform/clip/blend references and panel/marker shapes), reports expected shape counts and nonempty geometry at both 1280 x 720 and 960 x 540, asserts minimum vertex/index counts and five clip-command runs, plus per-appearance counters and nonempty indexed geometry tagged by dedicated showcase colors, requires presentations after resize, and has a 15-second deadline. This is GPU validation, not headless CPU, screenshot comparison, or real minimize/restore coverage. Hidden presentation is verified on this Windows/RTX 5070 Ti setup; other platforms/drivers remain unverified. The metadata regression above suppresses console creation too, samples foreground/window visibility every 10ms, and asserts unchanged foreground/no visible test windows with a 25-second process deadline. It does not move user focus; external user focus activity invalidates the observation. Sampling cannot exclude shorter transients. CPU generation skips iconified/zero-size windows; `Draw(null)` retains backend throttling. Validation errors cause nonzero exit. The three known Galaxy/GOG layer-naming warnings are upstream loader warnings, not validation errors; report other warnings separately.

`-Mode Showcase` is the hidden harness default and retains all gallery geometry/resize checks. `-Mode Gameplay` passes `--gameplay-smoke-test`: controlled fake raw snapshots traverse the **same InputRouter and BallSandboxSession** as live polling, at 1/60-second advances (two 120Hz ticks), without OS input injection. Critical stages advance only after actual presentations: initial aim, launch, wall/catch eligibility, catch at the preserved position, six held frames, separate release/repress relaunch, restart, and six resumed 960 x 540 presentations. It asserts states, bounds and position preservation, and reports actual indexed arena/ball-plus-halo/aim geometry at both 1280 x 720 and 960 x 540. Both hidden modes always request `Visible=0`, `Focused=0`, `FocusOnShow=0`; the shared harness uses `CreateNoWindow=true`. GPU modes and `--showcase` are mutually exclusive. There is no minimize/restore or hardware-input coverage in hidden gameplay.

CPU suites preserve 147 shape, 51 simulation and 28 input tests, and add 17 host integration cases covering edge latching/one-time consumption, catch/relaunch, restart precedence, focus loss/resume, direction mapping, pointer/DPI/letterbox/zero-framebuffer behavior and phase geometry. Gameplay tests reference the executable assembly without executing its entry point.

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

Use a disposable `ShapeDrawList`, `BeginFrame(width, height[, worldToPixel])`, primitive calls, then `Finish()` and synchronously `VulkanRenderer.Draw(frame)` before the next `BeginFrame`. Frames borrow producer buffers: never retain after the next frame or disposal. The demo disposes the list, including BRUTAL mesh native scratch, before renderer shutdown. GLFW, frame production and graphics stay on the main thread. `Draw(null)` clears only; skip `BeginFrame` for zero extents.

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

Curves target <=0.25 physical-pixel chord error (8–4096 full-circle segments). Framebuffer coordinate magnitude is bounded by 2^20; default budgets are 1,000,000 vertices / 3,000,000 indices. Invalid/nonfinite/negative input or state throws. Valid zero-size/alpha/empty-clip/singular-transform shapes emit nothing. Reflections are supported, but finite geometry can be unrepresentable in float32: actual inverted triangles or touching/crossing quantized polyline boundaries are rejected transactionally, not repaired. Budget/tessellation/pattern-complexity failures also throw without partial shape output, including command merging: each failing shape call leaves previously published vertices, indices and commands unchanged. This does not extend borrowed-frame lifetime.

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
- GLFW creates the window, not graphics. The main thread polls and renders; minimization waits briefly for events. The host session uses a synchronous fixed 120Hz accumulator, bounded to eight catch-up ticks; input edges apply only on the first tick, not once per render or catch-up tick.
- Vulkan acquires an image, records a render pass and ordered indexed draws, submits and presents. A fence protects command/upload reuse; semaphores coordinate acquisition/presentation.
- `BallSimulation.Step` is the small explicit equivalent of gameplay `FixedUpdate`; state is plain data, not a GameObject/MonoBehaviour. Input snapshots are the equivalent of sampled player controls, and `BallSandbox.Build` is a state-to-draw-list presentation pass. There are no per-ball tasks or awaited frame continuations.
- BRUTAL is a framework; scene/entity/loading policy remains our responsibility. Add only what the game needs.

## Project files and dependencies

- `Program.cs`: main-thread lifecycle and bounded smoke/self-tests.
- `Presentation/ShapeShowcase.cs`: fitted twelve-panel demo (`--showcase`).
- `Presentation/BallSandbox*.cs`: arena geometry, pure session/viewport bridge and hidden scripted scenario.
- `Simulation/`: standalone pure rules, radius-aware sweep/preview and fixed-step clock.
- `Platform/Input/`: owner-thread GLFW poller, pure input router and published API evidence.
- `tests/Gameplay/`, `tests/Simulation/`, `tests/Input/`: standalone CPU suites; `tests/Rendering/hidden-smoke.ps1`: shared hidden GPU harness.
- `Rendering/Shapes/`: headless geometry/contracts/colors/clips and borrowed buffers.
- `tests/Shapes/`: CPU geometry and ABI tests.
- `Rendering/VulkanRenderer.cs`, `Rendering/VulkanRenderer.Shapes.cs`, `Rendering/Shaders/`: lifecycle, pipeline, uploads, MSAA and disposal.
- `Rendering/ResourceLifetime.cs`: best-effort cleanup preserving original failures.
- `brutal-breakout.csproj`, `global.json`, `packages.lock.json`: dependencies, SDK and locked graph.

App direct packages: `Ahwoo.BRUTAL.Core` 2026.10.4, `Ahwoo.BRUTAL.Glfw` 2026.10.5, and `Ahwoo.BRUTAL.ShaderC`, `Ahwoo.BRUTAL.Vulkan`, `Ahwoo.BRUTAL.Vulkan.Abstractions`, `Ahwoo.BRUTAL.Vulkan.Vma` 2026.10.3. The headless library directly references `Ahwoo.BRUTAL.Numerics` 2026.10.2 and `Ahwoo.BRUTAL.Render.Mesh` 2026.10.2. BRUTAL `GenerateDisk` supplies discs through a tested adapter; custom tessellation fills genuine planar ring/rounded-rectangle/stroke gaps. GLFW native libraries arrive transitively. The locked graph includes a transitive preview `Microsoft.Extensions.ObjectPool`, not a direct addition.

## Git and release

Use feature branches; do not merge into `main` without approval. The private GitHub remote uses HTTPS: `https://github.com/aefreedman/brutal-breakout.git`. Use focused implementation and independent read-only review, one writer per tree.

Before distribution review the current [BRUTAL license](https://github.com/ahwootech/BRUTAL) and all third-party notices. BRUTAL requires copyright attribution in credits, its logo before gameplay, and a shipped `NOTICE.txt`; those release assets are not implemented here.

## Next milestone

The first ball interaction slice is implemented with provisional rules. Next: independently review integration and, only with user permission, inspect the visible arena and try real controls. Further gameplay choices remain unapproved. Text needs a verified managed wrapper first.

Planning lives outside the repo at `C:\Users\aaron\obs-mono\vaults\personal\brutal-breakout`, using `YY-MM-DD-kebab-case.md`. Avoid building a general engine ahead of the game.
