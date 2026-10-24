# brutal-breakout

A Windows-first 2D game built with C# and [BRUTAL](https://github.com/ahwootech/BRUTAL). Current milestone: a small, direct Vulkan primitive renderer, not gameplay or a general engine.

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

Expect a resizable 1280 x 720 **Brutal Breakout** window with nine panels of abstract shapes. The wide canvas fits the live physical framebuffer with uniform scale and centered margins, not a phone/paddle layout. Resize/minimize/restore, then close normally. Debug enables `VK_LAYER_KHRONOS_validation` by default and fails if unavailable; `--no-validation` opts out. Release enables validation with `--validation`, `--smoke-test`, `--lifecycle-smoke-test`, or `--visible-resize-stress`.

### Validation

Run GPU commands sequentially, with no other lane using the GPU:

```powershell
dotnet restore --locked-mode
dotnet restore Rendering/Shapes/BrutalBreakout.Shapes.csproj --locked-mode
dotnet restore tests/Shapes/Shapes.Tests.csproj --locked-mode
dotnet test tests/Shapes/Shapes.Tests.csproj -c Debug --no-restore
dotnet test tests/Shapes/Shapes.Tests.csproj -c Release --no-restore
dotnet build -c Debug --no-restore
dotnet run -c Debug --no-build -- --smoke-test
dotnet build -c Release --no-restore
dotnet run -c Release --no-build -- --smoke-test
dotnet run -c Debug --no-build -- --cleanup-self-test
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Debug
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Release
```

Routine `--smoke-test` enables validation and renders in a **hidden GLFW window**. It submits the actual 53-call showcase (14 solid outlines and 8 patterned paths, plus filled/transform/clip/blend references and panel/marker shapes), reports expected shape counts and nonempty geometry at both 1280 x 720 and 960 x 540, asserts minimum vertex/index counts and five clip-command runs, requires presentations after resize, and has a 15-second deadline. This is GPU validation, not headless CPU, screenshot comparison, or real minimize/restore coverage. Hidden presentation is verified on this Windows/RTX 5070 Ti setup; other platforms/drivers remain unverified. The metadata regression above suppresses console creation too, samples foreground/window visibility every 10ms, and asserts unchanged foreground/no visible test windows with a 25-second process deadline. It does not move user focus; external user focus activity invalidates the observation. Sampling cannot exclude shorter transients. CPU generation skips iconified/zero-size windows; `Draw(null)` retains backend throttling. Validation errors cause nonzero exit. The three known Galaxy/GOG layer-naming warnings are upstream loader warnings, not validation errors; report other warnings separately.

Cleanup injects resource-lifetime helper failures, not actual device loss. CPU tests validate geometry/ABI contracts, not GPU image correctness. Visible tests are **separate explicit opt-ins**; obtain user permission for desktop/focus disturbance before running:

```powershell
# Visible resize/minimize/idle/restore/resumed presentation; 15-second app deadline:
dotnet run -c Debug --no-build -- --lifecycle-smoke-test
# Visible external resize stress (bounded, per-iteration cleanup):
pwsh -NoProfile -File tests/Rendering/resize-smoke.ps1 -Configuration Debug
pwsh -NoProfile -File tests/Rendering/resize-smoke.ps1 -Configuration Release
```

All test-created windows request `Focused=false` and `FocusOnShow=false`; external resize uses `SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS`. Native lifecycle restore can still activate; **no portable zero-focus guarantee** is made for visible lifecycle mode. No test calls focus/foreground activation APIs. Normal interactive `dotnet run` is unchanged. With permission, also inspect/close the visible demo and repeat startup/shutdown. The Windows-only external-resize regression changes the native window asynchronously during rendering, including 40 rapid resizes per iteration, and retains logs under the system temp directory. This catches acquire/present out-of-date exceptions that between-frame smoke resizing can miss.

## Showcase guide

Panels left to right, top to bottom, without font dependencies:

1. Filled cyan disc, gold ring, coral rectangle, cyan rounded rectangle and gold triangle.
2. Solid circle, rectangle, rounded rectangle and triangle outlines.
3. Inside, center and outside circle outlines, left to right; thin white strokes mark their input boundaries.
4. Two nonuniform scales: cyan Local widths above, gold Framebuffer widths below (all width 6). Framebuffer widths remain physical pixels even as the design canvas fits the window.
5. Basic/butt, square and round dashes, then genuine circular dots. Dash rows share occupied centerline lengths and allow space for cap extensions.
6. Continuous dash phase across a corner; three closed rings demonstrate phased fixed-period dashes, closure-fitted natural dashes, and fixed dots.
7. Rotated rounded rectangle, scaled/sheared ring, reflected/sheared triangle and white open solid bevel path.
8. Nested framebuffer scissors: disc under the outer clip, coral/gold under the inner clip, white segment under only the outer clip.
9. Ordered half-alpha linear red/blue over black: blue-over-red above, red-over-blue below; a linear 0.5 gray bar at right. Red/green top markers retain orientation cues.

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
var frame = draw.Finish(); // synchronously renderer.Draw(frame) before next BeginFrame/disposal
```

Outline width is full thickness; `Center` (default) puts half on either side of the input boundary, `Inside` preserves outer bounds and `Outside` preserves the inner boundary, independent of winding/reflection. Rectangles, triangles and convex closed polylines use miter corners. Rounded rectangles clamp radius to half the shorter extent; eroded radii clamp to zero. A consumed hole produces a solid outer silhouette, not inverted triangles.

`StrokeSpace.Local` (default) measures offsets, widths and pattern lengths before local/world transforms; nonuniform transforms also distort local circular dots. `Framebuffer` transforms the boundary/centerline first and then measures offsets and lengths in physical pixels, keeping width constant under scaling/shear. Transformed circles use sampled contours (including ellipses), not an analytic ellipse-arclength promise. Patterned strokes follow their centerline; they do not take an alignment argument.

Pattern calls: `StrokePatternedSegment`, `StrokePatternedPolyline(points, width, pattern, color, closed=false, cap=Butt, space=Local)` and `StrokePatternedCircle`. Factories:

- `StrokePattern.Dashes(length, gap, offsetCycles=0, fitClosed=false)` and `RelativeDashes(lengthMultiples, gapMultiples, offsetCycles=0, fitClosed=false)`.
- `StrokePattern.Dots(spacing, offsetCycles=0, fitClosed=false)` and `RelativeDots(spacingMultiples, offsetCycles=0, fitClosed=false)`.
- `StrokePattern.FixedDashes(count, gapFraction=0.5, offsetCycles=0)` and `FixedDots(count, offsetCycles=0)`.

Absolute lengths use the selected space; relative lengths multiply full width. Dash length is the **occupied centerline interval**, not the visible tip-to-tip length: square/round caps extend half a width past each interval. Gaps must allow these extensions and turns; touching/overlapping fragment geometry rejects rather than blending twice. Zero gap can become a continuous solid path. Dots are discs of diameter width, not short round dashes; cap appearance is ignored for dots but the enum is still validated.

Positive phase moves the lattice forward by a fraction of a period; phase wraps periodically, including negative offsets (`-.25` equals `.75`, `1` equals `0`). Arc-length phase continues across vertices; closed seam fragments are stitched rather than capped twice. `FixedDashes` fits **count complete periods**, not necessarily count disconnected visible islands: phase may split/clip endpoint fragments. `FixedDots` places count sites over `[0,length)`, never duplicating the far endpoint. Natural dots include an open far endpoint only when it is a lattice site; closed paths never duplicate seam dots. `fitClosed` rescales natural spacing to the nearest whole period count (midpoints upward, minimum one), preserving dash duty cycle; fixed-count patterns inherently fit. Open end-to-end snapping and decorative angled/chevron styles are not supported.

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
- GLFW creates the window, not graphics. The main thread polls and renders; minimization waits briefly for events. Future simulation needs explicit fixed-step timing.
- Vulkan acquires an image, records a render pass and ordered indexed draws, submits and presents. A fence protects command/upload reuse; semaphores coordinate acquisition/presentation.
- BRUTAL is a framework; scene/entity/loading policy remains our responsibility. Add only what the game needs.

## Project files and dependencies

- `Program.cs`: main-thread lifecycle and bounded smoke/self-tests.
- `Presentation/ShapeShowcase.cs`: fitted nine-panel demo.
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

Primitive rendering is implemented. Future gamepad input and timed simulation require interaction decisions. Gameplay direction is direct ball catch/aim/relaunch, informed by read-only PrototypeStudio reference, not an assumed paddle game. Text needs a verified managed wrapper first.

Planning lives outside the repo at `C:\Users\aaron\obs-mono\vaults\personal\brutal-breakout`, using `YY-MM-DD-kebab-case.md`. Avoid building a general engine ahead of the game.
