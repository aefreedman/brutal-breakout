# Configuration and live tuning

All commands below run from the game repository root. JSON in [`Config/`](../../Config/) is authoritative; tables describe shipped values, not fallback defaults. Presentation colors, style and Shmup layout are covered in [presentation](presentation.md).

## Inspector layout

The inspector has independent movable/resizable Gameplay and Diagnostics windows, plus a Configuration window opened with **Configuration...** in Gameplay. Drag title bars to move windows and edges to resize them. Diagnostics can also collapse to its title bar.

| Window | Initial physical-pixel bounds | Contents |
| --- | --- | --- |
| Gameplay | `(16, 16)`, `360×420` | Running/Paused, Pause/Resume and Restart; selected-mode radios; Shmup widening |
| Diagnostics | `(392, 16)`, `440×480` | Aligned state/run facts/frame-time readings, preview and expandable technical readings |
| Configuration | `(392, 16)`, `560×500` | Live tuning, explicit structural reset, Save changed, Discard and Reload |

Gameplay and Diagnostics fit without overlapping at both 960×540 and 1280×720. Configuration opens over Diagnostics while tuning; move/resize it as needed. Preview starts open; Rendering, Text cache and Font start closed. Labels, values and help wrap rather than forming one large text block. Shmup reports its actual timing, geometry, HP/counts and pause state. Widening is disabled while paused or at maximum width; its stable range cannot narrow the current run.

`Config/inspector.json` owns initial bounds: root `left/top/width/height` configure Diagnostics, and required `gameplayWindow: { "left": 16, "top": 16, "width": 360, "height": 420 }` configures Gameplay. Required `configurationWindow: { "left": 392, "top": 16, "width": 560, "height": 500 }` seeds Configuration. Custom inspector JSON must include both complete window objects. Immutable settings objects are validated before native allocation and passed unchanged to the UI. Bounds are starting positions, not locks: moved/resized bounds and diagnostic section choices survive panel toggles and context recreation within the running application. Smaller framebuffers/DPI changes recover reachable bounds. A new launch starts from JSON again; no docking system or layout files are written.

See [validation and evidence limits](../engineering/validation.md) for layout test coverage; visible inspection requires separate permission.

## Configuration controls

Open **F1 / Select / Share → Gameplay → Configuration...**. Expand file/section groups to edit numeric values, toggles and text. Brick shapes/quarter turns have checkboxes; palette colors have native color editors and semantic roles have palette selectors. Values originate from the selected JSON files, not a second set of UI defaults. Input arrays other than shape/rotation choices currently retain their entry counts.

Valid live edits take effect automatically at the next owner update, outside ImGui drawing. A whole file is no longer treated as requiring a restart: each field shows its effect and any reset/relaunch reason.

| Field effect | Behavior |
| --- | --- |
| Live | Updates the running settings without replacing the session/world or resetting time, progression, entities, pause, input latches or unrelated trail history. Colors/style, input, audio and ordinary gameplay tuning do not need a new run. |
| Future spawns/defaults | Stores new choices/defaults without rewriting existing lives; for example, brick health and shape choices apply to newly spawned bricks. |
| Reset required | Tick frequency/catch-up capacity, execution workers, logical arena/brick topology, pool capacities, brick size/grid pitch and Shmup bounce/sensor mode. **Reset & apply pending** intentionally starts a fresh run and retains mode/manual pause. Inactive-mode geometry can become next-mode settings without resetting the current mode. |
| Relaunch required | Renderer-owned overlay/inspector resource settings remain pending; the current package exposes startup-only configuration. |

Live coverage includes ball speed/radius/damage/rate/wall limits, avatar movement/radius/contact tuning, difficulty/noise, input deadzone, audio, all colors/style/trails, physical window dimensions, and physics substeps/contact stiffness/damping. Current brick health/shape/rotation and appearance remain per-life when spawn defaults change. Lowering avatar health clamps current maximum/HP; raising it never heals or resurrects.

Pending reset/relaunch and unsaved markers are independent. Live candidates merge onto the currently applied structural settings, so pending geometry/resource edits cannot leak into the running world or force an unrelated reset. Invalid intermediate edits retain previous valid settings and report errors. Numeric fields use game units, units/second, seconds or their named ratios; integer-only schema fields reject fractional values. Press **Enter** to commit physical window width/height; intermediate typed digits do not resize the window. Audio changes use the existing silent-output diagnostic if a device is unavailable; unrelated edits do not reopen audio devices.

**Save changed** only persists the staged JSON; it does not reset the run or apply pending structural/relaunch fields. It validates all staged files, preserves relative asset-path spellings, checks for external edits and atomically replaces each changed file. Multi-file failures attempt rollback and report any partial save; this is not a filesystem-wide transaction. **Reload** discards drafts and validates disk files; **Discard** returns to the last loaded/saved source. Their valid live values take effect automatically, while structural/relaunch changes still wait for the appropriate explicit action. No automatic settings or layout writes occur.

The selected absolute paths are shown in the panel. Direct application launches normally select executable-directory `Config/` copies; saving those copies is not durable across clean/redeployment. From the project directory, **`--project-configs`** selects source `Config/` files for implicit paths. The coordination workspace's **`play.cmd`** supplies this flag automatically. Explicit per-file `--*-config <path>` overrides still win and saves target exactly those files. Apply uses in-memory production parsing/cross-validation and does not need write access to the source directory.

## Initial window configuration

`Config/window.json` is the only production default source for initial **physical window** dimensions: required positive integer `width` and `height`. These are independent of `Config/gameplay.json`'s logical canvas; fit/letterboxing still derives from that canvas and the actual framebuffer. Both game and gallery require this file. `--window-config <path>` independently overrides it, using caller cwd for relative paths; default lookup uses the executable directory. Build and publish copy it alongside the other configuration assets. Startup reports selected absolute path, requested dimensions and actual created window/framebuffer dimensions.

The immutable `WindowSettings(int width, int height)` has no defaults or mutable properties. The strict loader rejects missing/unreadable files, absent/null/unknown/duplicate/escaped-duplicate fields, wrong types and nonpositive/out-of-int-range dimensions **before GLFW or a native world**. There is no fallback or partial merge. Physical dimensions can be edited live through the Configuration panel; external files are not watched. Gallery still loads no implicit physics/game/input/colors/style files; explicit overrides remain validated. `--cleanup-self-test` exits before any configuration lookup or native startup. Positive integer representability does not promise OS/GPU support for arbitrarily huge windows.

## Physics configuration

`Config/physics.json` is the only production default source for physics timing/tuning and is copied into build/publish output. Select another file with `--physics-config <path>`; explicit relative paths resolve against the caller's working directory. Showcase-only modes do not load implicit physics, but validate an explicit override.

See the [physics guide](../engineering/physics.md) for every setting, shipped values, strict validation, live-versus-reset behavior, adaptive Shmup intervals and their interaction with solver substeps. External files are not automatically watched; the Configuration panel's Reload/Discard and explicit reset rules apply as described above.

## Gameplay and input configuration

`Config/gameplay.json` and `Config/input.json` are the only production default sources for ball/arena geometry and stick deadzone. Like `Config/physics.json`, they are copied into the executable's `Config/` directory on build/publish and loaded before GLFW/native world creation. Edit the source files and build/run again, edit deployed copies, or choose independent overrides:

```powershell
dotnet run --project src/App/BrutalBreakout.App.csproj -- --gameplay-config ./my-gameplay.json --input-config ./my-input.json
# All three overrides can be combined:
dotnet run --project src/App/BrutalBreakout.App.csproj -- --physics-config ./my-physics.json --gameplay-config ./my-gameplay.json --input-config ./my-input.json
```

Every section/field is required; there is no partial merge, C# fallback or parameterless settings constructor. Unknown/misspelled, duplicate (including escaped names at nested levels), missing, null or wrong-type fields fail with file-path context. Each option accepts one path; relative paths use caller cwd. Showcase-only modes need neither implicit gameplay nor input files, but validate any explicit overrides (window configuration is always required). External files are not watched. Use the Configuration panel for field-level live edits or Reload; structural fields require explicit reset.

| File / section | Required fields and meaning |
| --- | --- |
| `Config/gameplay.json` / `arena` | `width`, `height`: logical canvas; `left`, `top`, `right`, `bottom`: inner wall faces; `wallThickness`: wall thickness in logical units. |
| `Config/gameplay.json` / `ball` | `radius`, `speed`: logical radius and units/second; `spawnX`, `spawnY`: reset center; `initialAimX`, `initialAimY`: finite nonzero direction, normalized once. |
| `Config/gameplay.json` / `breakout` | `startingBalls`, `catchZoneHeightFraction` (canvas fraction measured up from bottom), `maxBouncesWithoutBrick` (0 disables expiry), `bricks`: rectangles with `x`, `y`, `width`, `height`. |
| `Config/input.json` | `stickDeadzone`: radial deadzone in **[0,1)**, validated after float conversion; the boundary is inactive. |

Ordered walls including thickness must fit the canvas, radius must leave a nonempty playable area, and spawn must fit the radius-inset bounds. Invalid/degenerate native float geometry and unsafe speed/timestep combinations reject before a world is allocated; errors for combined physics/gameplay incompatibility identify both paths. Validation establishes numerical representability, not stable physics for every extreme setting.

The same immutable settings drive native walls/circle/velocity, reset/casts, drawing and pointer/DPI fitting. Native speed/radius/positions are **derived via the one unit conversion**, never separately authored meter defaults. Cast reach derives from configured geometry; native maximum linear speed does not silently undercut requested launch speed. Rendering uses the configured inner faces and thickness rather than a second wall layout. The required breakout section validates a nonoverlapping layout inside the walls, above the catch line and clear of the spawn circle. Explicit null breakout settings are only for library physics-reference tests, not a production JSON fallback.
