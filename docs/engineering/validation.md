# Validation and evidence limits

All commands below run from the game repository root. See [development](development.md) for restore/build variants.

## Validation

Keep the default loop change-scoped: run only the changed Debug test class/filter during iteration, then one affected-domain batch at integration. Reuse the existing restore graph unless dependencies change. For example, Shmup work can use:

```powershell
dotnet test tests/Runtime/Simulation/Simulation.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~Shmup'
dotnet test tests/Runtime/Gameplay/Gameplay.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~Shmup|FullyQualifiedName~BallTrail|FullyQualifiedName~HostPause'
dotnet build src/App/BrutalBreakout.App.csproj -c Release --no-restore
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Release -Mode Shmup
```

Use smaller filters while editing and small capacities in new physics fixtures. Do not rerun every suite/configuration, editor-free builds, package verification or unrelated GPU modes for each local change. Escalate for failures, shared contracts, package changes or renderer lifecycle risk. Run any GPU commands sequentially, with no other lane using the GPU.

The following broader regression matrix is reference material for those escalations, **not the routine iteration gate**:

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

Coverage includes strict nested breakout/color schemas, native brick lifecycle and casts, catch-line facts and boundaries, host catch/miss/restart at 60/120/240Hz, terminal geometry and identical-color schemes. Prior native timing/partition and ownership tests remain. Build/publish asset copies are verified.

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


## Shmup and media checks

CPU/native ImGui and hidden GPU checks verify layout mechanics and real geometry/preview receipts, not subjective readability or visible mixed-DPI quality. Visible inspection remains a separate permission-gated check.

After a Release build, `pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Release -Mode Shmup` exercises synthetic input, widening, resize, fresh mode-switch state and inspector callback failure/retry in a hidden window with Vulkan validation and foreground/visibility sampling. This is not a visible playtest or physical-controller test.

Physical controller mappings are tested with synthetic snapshots, not controller hardware.
