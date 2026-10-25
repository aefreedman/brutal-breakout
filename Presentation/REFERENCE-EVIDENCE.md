# Bounded prototype presentation reference

Authority: the parent's serialized source/GUID/active-state trace in the external `26-10-24-prototype-visual-fidelity-plan.md`. Unity remains read-only; no addon code, tessellation or shaders were copied. This is geometry/configuration fidelity, not an image-parity claim.

| Reference chain / active setup | Implemented consequence |
| --- | --- |
| `p01.BreakoutGame.unity` -> BackgroundUICanvas GUID `c6702e2d5f4ee438d8423f0114e5af5e`; inactive first Image, active `Image.001`; scene overrides camera/placement only | Full physical-frame vertical gradient, not camera cream |
| Active Image tint .22 gray; UIGradient Vertical=1, Multiply=2; bottom coral / top cream; Color32 tint 56/255 | Authored approximate opaque sRGB endpoints top `#33332D`, bottom `#381B18`, converted to linear once |
| GameSettings -> ProjectileFactory -> Projectile facade/runtime `_initColor`; base Brick runtime `_initColor`, regular quad roundness .2 | Coral ball even when catch-ready; all shipped normal-brick roles coral; corner radius .1 short brick side, no keylines |
| EdgeColliders; Projectile trajectory plain Lines; TrajectoryLineEnd; MyBounceIndicator | White walls/shaft/contact ring/reflection; .25r shaft width, r contact radius/.1r width, r clearance then 2r reflected length |
| `SelectionIndicatorPrefab` GUID `fcdadf3f24c8241b9bb28479ac970153` -> `SpecialFX/P01.DashedDiscRotatingFX.prefab` (not disabled JuggleIndicator) | Playing+Aiming only: cream 16 rounded dashes, .5 gap fraction, 2 pattern cycles/sec |
| Shapes ThicknessSpace=2 Noots; project `NOOTS_ACROSS_SCREEN=100` | Held radius20/width2 = .20/.02 short physical framebuffer extent, not pixels or thousandths |
| HorizontalLineScreenWidth / ShootBelowLineVisual inheritance | Cream catch line, width .01 short framebuffer extent, rounded dash length3/gap3 widths |

The BRUTAL dependencies remain the existing locked published packages: Numerics 2026.10.2 vectors/transforms and Render.Mesh 2026.10.2 disk generation. Existing `StrokePattern.FixedDashes`, `StrokePattern.RelativeDashes` and round-cap patterned paths provide the required dashes without new tessellation. The gradient adds only a four-corner CPU rectangle with the existing `ShapeVertex` colors; raster interpolation is supplied by the existing shader. It needs no new interpolation helper/API, shader ABI, native import, dependency or gradient framework.

## Evidence gates

- Required `BackgroundBottom` / `HeldRing` roles and all required style fields: strict load, CLI/path/gallery behavior, constructor validity, pre-platform rejection and shipped-role tests.
- CPU geometry: transformed gradient endpoints/center/shared diagonal, premultiplication, transactional validation/budget/envelope errors; screen-relative held ring and catch width, dash gaps/round caps, render-only rotation, flight disc/no recolor, terminal suppression, native shaft/contact/reflection clearances, no-hit fallback, palette-independent exact category sums.
- Final Debug/Release: 150 Shapes + 227 Simulation + 48 Input + 400 Gameplay tests per configuration; root builds with zero warnings, eight locked restores. No simulation/platform implementation changes.
- Six serial hidden GPU checks: each configuration runs Showcase, default Unity gameplay, and custom style+custom arena/input+60Hz+Night. All pass both viewport receipts and stale-frame clear-only regression where applicable, with zero validation errors. Three known Galaxy layer-name loader warnings per process. Foreground unchanged and no visible test handles observed at 10ms sampling; sampling cannot exclude shorter transients.
- Source/build/publish SHA-256 equality verified for physics, gameplay, input, colors, style and Night assets in both configurations. Full logs/hash receipt are outside the checkout in `%TEMP%/prototype-fidelity`; no screenshots, visible lifecycle/stress checks, desktop input injection or appearance claims.
