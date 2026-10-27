# Validation and evidence limits

All commands below run from the game repository root. See [development](development.md) for restore/build variants. This page describes Breakout's tests, renderer harnesses and their coverage limits.

## CPU and offscreen tests

The project suites live under `tests/Runtime/` and `tests/Editor/`:

- **Simulation:** native contacts and casts, brick lifecycle, catch-line boundaries, Shmup chronology, fixed-step behavior, retained snapshots and queued-tick/disposal contracts.
- **Gameplay:** host input staging, catch/miss/restart, pause and focus transitions, mode switching, trail history, configuration integration and HUD geometry. Native gameplay fixtures serialize world creation/destruction to protect native global allocation state.
- **Input:** bindings, deadzones, direction mapping and input-state transitions using synthetic snapshots.
- **Audio/media:** cue mapping, bounded voices, unavailable-output handling and asset/text contracts. Audio tests use fakes rather than judging audible quality.
- **Editor:** configuration edits, live/reset/relaunch effects, invalidation, Save/Reload and offscreen inspector behavior. Native CPU layout cases exercise wide/sidebar and narrow/stacked categories, long field labels, independently scrolling bodies with reachable navigation, actual shape/rotation checkbox mouse edits, balanced scopes and consumer selection/bounds across context recreation. These cases do not create GLFW windows or establish visual/focus quality.

For example, the Shmup-related simulation and gameplay tests can be selected with:

```powershell
dotnet test tests/Runtime/Simulation/Simulation.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~Shmup'
dotnet test tests/Runtime/Gameplay/Gameplay.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~Shmup|FullyQualifiedName~BallTrail|FullyQualifiedName~HostPause'
```

The Gameplay tests reference the executable assembly without executing its entry point. Session fixtures cover normal/exceptional teardown, repeated disposal, use-after-dispose rejection, host-input thread ownership and independent host lifetimes. Host tests include 60/120/240 Hz timing, pointer/DPI/letterbox handling, terminal geometry and identical-color schemes.

## App build and cleanup self-test

After the [locked restore](../../README.md#quick-start):

```powershell
dotnet build src/App/BrutalBreakout.App.csproj -c Release --no-restore
dotnet run --project src/App/BrutalBreakout.App.csproj -c Release --no-build -- --cleanup-self-test
```

The cleanup self-test exits before configuration lookup or native startup. It injects resource-lifetime helper failures, not actual device loss.

## Hidden GPU harness

`tests/Rendering/hidden-smoke.ps1` runs the app with Vulkan validation and a hidden GLFW window. It samples foreground/window visibility every 10 ms, checks for unchanged foreground/no visible test windows and enforces a process deadline. External focus changes invalidate that observation; sampling cannot exclude shorter visibility transients.

```powershell
# Primitive gallery (the default mode):
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Release
# Classic Breakout:
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Release -Mode Gameplay
# Shmup:
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Release -Mode Shmup
# HUD, inspector and texture-preview checks:
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Debug -Mode Gameplay -Overlays
```

The GPU modes and interactive `--showcase` mode are mutually exclusive. Hidden modes request `Visible=0`, `Focused=0` and `FocusOnShow=0`; the harness also suppresses console creation. Validation errors cause a nonzero exit. Three known Galaxy/GOG layer-naming warnings are upstream loader warnings rather than Vulkan validation errors.

### Showcase coverage

The showcase submits the twelve-panel, 64-call gallery, including solid/patterned outlines, angled and chevron paths, fills, transforms, clips and blends. Checks cover shape/appearance counts, nonempty indexed geometry, five clip-command runs and presentations at 1280×720 and 960×540. This is GPU submission/resize coverage, not screenshot comparison or real minimize/restore coverage.

### Gameplay coverage

Synthetic raw snapshots traverse the same `InputRouter` and `BallSandboxSession` as live input, without OS input injection. The harness checks the selected configuration paths, loaded timing/geometry/deadzone values, native launch speed, HUD bounds and actual indexed presentation at two framebuffer sizes.

The scripted sequence covers initial aim, launch, an above-line press that cannot catch, brick destruction, return-below-line eligibility, catch at the preserved position, held input without relaunch, separate release/repress, restart and resumed resized presentation.

A stale-frame regression deliberately supplies 1281×720 geometry to a 1280×720 framebuffer. The renderer discards it and presents a clear-only frame. `Gameplay receipt regression PASS` confirms that an increased presentation count alone does not count as indexed-geometry success or advance the scenario. Normal stages resume only after valid geometry is presented.

`-PhysicsConfig`, `-GameplayConfig`, `-InputConfig`, `-ColorsConfig` and `-StyleConfig` select independent fixtures. The harness defaults to `tests/Fixtures/window-default.json`; although `-WindowConfig` accepts another path, this scenario requires 1280×720 for its stale-extent regression. Separate CPU tests cover custom 800×600 window creation independently of logical arena size.

### Shmup coverage

The Shmup scenario exercises synthetic input, widening, resize, fresh mode-switch state, pause/restart and inspector callback failure/retry. Its configuration fixture explicitly selects the Shmup category and opens live Ball fields. CPU/native ImGui tests cover layout mechanics; the separate overlay smoke opts into an explicit diagnostic PNG fixture and requires actual indexed image/diagnostics receipts at both extents. Normal inspector JSON has no preview fields. Host elapsed history observations occur outside drawing, with monotonic IDs independent of callback cancellation; they are not GPU timing measurements.

## Visible lifecycle and resize tests

These tests open visible windows, resize/minimize/restore them and can disturb desktop focus:

```powershell
# Resize/minimize/idle/restore/resumed presentation; 15-second app deadline:
dotnet run --project src/App/BrutalBreakout.App.csproj -c Debug --no-build -- --lifecycle-smoke-test
# Windows-only external resize stress:
pwsh -NoProfile -File tests/Rendering/resize-smoke.ps1 -Configuration Debug
pwsh -NoProfile -File tests/Rendering/resize-smoke.ps1 -Configuration Release
```

Test-created windows request `Focused=false` and `FocusOnShow=false`; external resize uses `SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS`. Native lifecycle restore can still activate a window. The tests do not call focus/foreground activation APIs, but there is no portable zero-focus guarantee.

The external-resize test changes the native window asynchronously during rendering, including 40 rapid resizes per iteration, and retains logs under the system temp directory. It exercises acquire/present out-of-date paths that between-frame resizing can miss.

## Coverage limits

- Hidden GPU checks verify submission and scripted behavior, not image correctness, subjective readability or mixed-DPI sharpness.
- Controller input uses synthetic snapshots rather than physical controller hardware.
- Audio tests do not establish audible quality, latency or real-device shutdown behavior.
- CPU geometry/ABI checks and cleanup failure injection do not establish GPU image correctness or device-loss recovery.
- Runtime/hidden presentation behavior has been verified on one Windows GPU setup; other platforms/drivers remain unverified.
