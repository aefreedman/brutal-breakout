# Presentation and Shmup layout

All commands below run from the game repository root. [`Config/`](../../Config/) remains authoritative; examples below describe shipped values. See [configuration controls](configuration.md#configuration-controls) for live, future-spawn, reset and relaunch behavior.

## Swappable color schemes

`Config/colors.json` maps the **active serialized Unity setup**, not just palette assets or camera clear color. `BackgroundUICanvas.prefab` has an inactive untinted image and an active `Image.001`: .22 gray tint multiplied by a vertical coral-bottom/cream-top gradient. UI Color32 tint quantization is 56/255; authored opaque sRGB approximations are **top #33332D / bottom #381B18**. The scene only overrides placement/camera. The gradient covers the physical framebuffer, including letterboxes. Clear-only frames use the configured top `background`, not a full gradient.

The default projectile (including catch-ready flight) and all three normal-brick roles are coral `#FF7D6E`; bricks have rounded fills with no invented keylines. Walls and aim/contact/reflected lines are white. The catch line and held selection ring are cream `#E8E7CB`. Semantic roles remain independent for alternative themes.

```powershell
dotnet run --project src/App/BrutalBreakout.App.csproj -- --colors-config Config/ColorSchemes/night.json
```

Combine with any other config overrides. Schemes require `name`, a `palette` of named opaque sRGB `#RRGGBB` colors, and `roles` referencing palette names: `background`, `backgroundBottom`, `heldRing`, `walls`, `ball`, `trail`, `catchReady`, `catchLine`, `aim`, `brickPrimary`, `brickSecondary`, `brickAccent`, `hud`, `win`, `loss`. Missing/unknown/duplicate fields, bad hex and undefined references fail before platform startup. Colors apply live through the Configuration panel; external files are not watched. Both shipped schemes copy to build/publish. Colors convert from sRGB to linear once; the CPU gradient supplies per-vertex linear colors to the unchanged shader/vertex ABI. Roles may share colors; actual index-range receipts never depend on color identity. The primitive gallery retains its demonstration styling.

## Presentation style configuration

`Config/style.json` is the only production source of new presentation tunables. Use `--style-config <path>` independently of color/game/input/physics overrides. Like those files, all fields are required, unknown/missing/null/duplicate (including escaped-name) fields reject, default lookup is beneath the executable's `Config/` directory, relative overrides use caller cwd, and gallery modes load no implicit style file. Explicit overrides are always validated before GLFW/world creation. Build/publish copies include the style. No merge or optional constructor fallback. Style applies live through the Configuration panel without a simulation-timing change; external files are not watched.

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

## Trails

Ball trails are white in the shipped palettes, independently of ball color through the required `roles.trail` palette role. Custom color schemes must supply that role. Trails retain bounded per-life world-space history at fixed ticks, fading and tapering behind live balls. They reset on despawn, pool reuse, restart and mode switch. Pause freezes simulation-time aging; resizing transforms stored points without changing simulation. `Config/trails.json` (`--trails-config <path>`) supplies the immutable settings, editable live through the Configuration panel and storage/draw budgets. The shipped trail settings use 24 points per ball, a 0.18-second lifetime, radius-relative width 1.5, opacity 0.55, and at most 16,384 rendered segments. Histories are limited by both age and point count; draw saturation shortens old tails and shares the budget across live balls. Bends within one fixed tick are approximated by the sampled polyline. Camera/display behavior is unchanged.

## Shmup brick layout and shape choices

Mixed square, **2×1**, **2×2**, four-cell **L** and four-cell **T** bricks descend in continuously generated rows with seeded Perlin-noise gaps. Shapes support 0°, 90°, 180° and 270° placements. A compound shape is one brick with shared HP and one escape/destruction event; its L/T cutouts remain open to balls, avatar contact and aim casts. Rendering and native convex fixtures consume the same immutable rotated geometry.

Brick anchors use a fixed world-centered lattice with `brick.columnPitch`; widening unlocks columns without changing pitch, shifting existing bricks or recentering the grid by column count. Side margins are left unused. Compound shapes occupy the same grid cells as ordinary squares, with the gaps between connected cells filled. For square size `S`, pitch `P` and an `N`-cell extent, the footprint is `(N - 1) * P + S`, not `N * S`. At the shipped size 28.8 and pitch 36, a 2×1 is 64.8×28.8, a 2×2 is 64.8×64.8, an unrotated L is 64.8×100.8 and T is 100.8×64.8. Placement aligns occupied cell centers, so even-cell extents use half-pitch bounding-center offsets. Descending waves share a moving vertical grid phase; spawn deadlines may leave empty rows rather than compressing spacing. Conservative reservations skip blocked candidates. This is deliberately loose placement, not tight packing.

`Config/shmup.json` is the authoritative tuning source, validated before native startup. It includes canvas/initial width, avatar, balls, bricks, difficulty, noise and explicit capacity limits. Required `brick.shapes` lists enabled names (`"square"`, `"2x1"`, `"2x2"`, `"l"`, `"t"`); required `brick.quarterTurns` lists enabled rotations (`0`–`3`). The shipped JSON enables all five shapes and all four rotations. Lists must be nonempty, unique and valid; custom Shmup JSON must include both. Shape/rotation choices are seeded from global column and row indices. `--shmup-config <path>` selects an override. Runtime width changes preserve the screen center and cannot narrow the live play area.
