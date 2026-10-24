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

Expect a resizable 1280 x 720 **Brutal Breakout** window with six panels of abstract shapes. The wide canvas fits the live physical framebuffer with uniform scale and centered margins, not a phone/paddle layout. Resize/minimize/restore, then close normally. Debug enables `VK_LAYER_KHRONOS_validation` by default and fails if unavailable; `--no-validation` opts out. Release enables validation with `--validation` or `--smoke-test`.

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
```

Smoke enables validation, submits the actual 31-shape showcase (asserting minimum vertex/index counts and five clip-command runs), requires presentations after resize/restore, and checks idle minimization, with a 15-second lifecycle deadline. CPU generation skips iconified/zero-size windows; `Draw(null)` retains backend throttling. Validation errors cause nonzero exit. The three known Galaxy/GOG layer-naming warnings are upstream loader warnings, not validation errors; report other warnings separately.

Cleanup injects resource-lifetime helper failures, not actual device loss. CPU tests validate geometry/ABI contracts, not GPU image correctness. Also inspect the visible demo, close a normal run, and repeat startup/shutdown. Smoke is not a screenshot comparison.

## Showcase guide

Panels left to right, top to bottom, without font dependencies:

1. Cyan disc and gold ring with an empty center.
2. Coral rectangle, cyan rounded rectangle, gold triangle.
3. Equal centerline lengths with butt, square, then round segment caps.
4. Rotated rounded rectangle, nonuniformly scaled/sheared ring, reflected/sheared triangle, white open butt-cap bevel polyline.
5. Nested hard scissors: cyan disc cut at outer left edge; coral/gold shapes bounded by a smaller inner rectangle; white segment clipped only by the outer scissor.
6. Half-alpha linear red/blue on black: blue-over-red above, red-over-blue below. Overlaps must differ. Right gray bar is linear 0.5 encoded to sRGB (about 188/255), not 128. Red/green upper left/right markers expose orientation errors.

On the tested RTX 5070 Ti, client-area capture sampling gave red-only `(188,0,0)`, blue-only `(0,0,188)`, blue-over-red `(138,0,188)`, red-over-blue `(188,0,138)`, gray `(188,188,188)`. These are targeted blend/sRGB checks, not comprehensive goldens or evidence for other devices.

## Shape API and limits

Use a disposable `ShapeDrawList`, `BeginFrame(width, height[, worldToPixel])`, primitive calls, then `Finish()` and synchronously `VulkanRenderer.Draw(frame)` before the next `BeginFrame`. Frames borrow producer buffers: never retain after the next frame or disposal. The demo disposes the list, including BRUTAL mesh native scratch, before renderer shutdown. GLFW, frame production and graphics stay on the main thread. `Draw(null)` clears only; skip `BeginFrame` for zero extents.

Coordinates are physical framebuffer pixels, origin top-left, +Y down. `SetTransform` applies local-to-world before world-to-pixel; local radii/widths transform too. `PushClip` intersects a half-open integer framebuffer scissor with its parent; balance with `PopClip`. Clips are not transformed local geometry. Submission order is preserved, merging only adjacent equal-scissor commands. `LinearColor` accepts straight linear RGBA in [0,1]; use `FromSrgb` for sRGB source colors. Vertices premultiply once; blending uses ONE / ONE_MINUS_SRC_ALPHA and the sRGB attachment encodes once.

Calls: `FillDisc`, `FillRing`, `FillRect`, `FillRoundedRect`, `FillTriangle`, `StrokeSegment` (butt/square/round), `StrokePolyline`. Polylines are open, butt-cap, bevel-join, at most 1024 input points; consecutive duplicates/forward-collinear runs are removed. Reversals and crossing/touching/overlapping outlines throw instead of double blending. Closed/round/miter joins and general self-crossing unions are deferred.

Curves target <=0.25 physical-pixel chord error (8–4096 full-circle segments). Framebuffer coordinate magnitude is bounded by 2^20; default budgets are 1,000,000 vertices / 3,000,000 indices. Invalid/nonfinite/negative input or state throws. Valid zero-size/alpha/empty-clip/singular-transform shapes emit nothing. Reflections are supported, but finite geometry can be unrepresentable in float32: actual inverted triangles or touching/crossing quantized polyline boundaries are rejected transactionally, not repaired. Budget/tessellation failures also throw without partial shape output.

Backend: one fence-protected in-flight upload frame, dynamic viewport/scissors, indexed triangles, classic render pass, 4x-MSAA resolve into sRGB swapchain, no depth/culling, VMA-backed buffers/images. Resize rebuilds attachment/pipeline resources. Actual device loss, other platforms/drivers, and unsupported-format/sample-count failure paths have not been exercised in integration.

Text is deferred: the published FreeType package contains native libraries but no managed bindings. No speculative font/texture pipeline is included.

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
- `Presentation/ShapeShowcase.cs`: fitted six-panel demo.
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
