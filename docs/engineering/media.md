# Rendering, media and diagnostics

All commands below run from the game repository root.

## BRUTAL audio, text, textures and diagnostics

Normal gameplay now shows count/status/control labels through **SDL.Ttf -> BRUTAL.Texture/GenericTexture -> direct Vulkan textured quads**. `TextureLoader` uses BRUTAL Stb internally for the PNG path; there is no second image decoder or alternate graphics stack. ImGui supplies **optional diagnostics, gameplay and configuration controls**, not the game text/image compositor. The existing 24-byte solid-shape pipeline is unchanged; textured vertices use a separate 32-byte UV material, with linear-premultiplied texels/tints, sRGB output and the same 4x MSAA render pass.

- `Config/overlay.json` is required in game and gallery modes (`--overlay-config <path>` overrides it). Its `fontPath` selects the proportional game font. Editor-enabled builds separately require `Config/inspector.json` (`--inspector-config <path>`), whose `fontPath` selects the monospaced inspector font and whose `imagePath` selects the preview. Asset paths resolve relative to the JSON containing them. Font sizes, label spacing and inspector dimensions are **physical framebuffer pixels**. Cache/label/texture/encoded-byte limits are explicit; required fields/assets and the host's three-label capacity are validated before GLFW. Defaults exist only in JSON, with no partial merge; renderer-owned resources require relaunch (see [configuration controls](../design/configuration.md#configuration-controls)). Labels cache unchanged strings; cache eviction and GPU texture/descriptor lifetime follow the shared frame fence.
- Press **F1 / Select / Share** to open/close the inspector at runtime, including while it captures input. Holding the key/button does not repeat; focus/device changes baseline held controls. No CLI argument is required. `visible` in `Config/inspector.json` seeds the initial state; `--debug-ui` remains an optional initially-open shortcut. It shows state/counts, timing, geometry/presentation/cache statistics, font/framebuffer/raster-density metrics and an authored PNG preview. Its game-controls window selects the mode and widens the active Shmup play area; the remaining diagnostics are read-only; field-level tuning is in the separate Configuration window. With it closed, the game does not initialize ImGui or install its GLFW callbacks. Capture transitions clear pending input edges; captured keyboard/mouse sources are suppressed without taking gamepad control away.
- `Config/audio.json` is required in gameplay (`--audio-config <path>` overrides it). It configures enabled state, master gain, a bounded voice count and ten WAV paths: the original launch/catch/brick/miss/win/loss cues plus shmupFire/shmupHit/shmupDestroy/shmupDamage. Custom audio JSON must supply all ten cue paths. Cues are collected per fixed tick, not inferred once per render frame. Terminal cues replace same-tick brick/miss feedback. Tracks are reused first-idle; saturation drops the new cue, never queues it. Focus loss/mute/restart stops feedback and prevents stale replay.
- Audio uses **BRUTAL SDL.Mixer** and cached, predecoded clips. `--mute` avoids creating an audio device entirely. Disabled audio also creates no device. Missing/unavailable native audio devices/libraries give one diagnostic and silent output, without stopping gameplay. Configuration or asset validation errors remain startup errors. The supplied sounds are original simple placeholder tones (CC0); their offline authoring script is included, not a runtime dependency. Custom cue files currently require canonical mono PCM16 WAV, not every format SDL.Mixer can decode.
- Hidden GPU modes and showcase never open an audio device. Headless tests exercise cue mapping, saturation and unavailable output through fakes; no audible quality/latency/device-shutdown acceptance is claimed.

Exactly two pinned, unmodified **Iosevka v34.9.1 Regular** TTFs are included: **Iosevka Aile** (proportional sans) for game labels, and standard **Iosevka** (monospaced, not Term/Etoile) for the inspector. They were acquired from the official be5invis/Iosevka release, not supplied by BRUTAL or installed from the OS. Aile is 11,878,496 bytes; inspector Regular is 11,613,712 bytes: **23,492,208 bytes (22.40 MiB)** total. No other weights/italics/collections/download archives are committed, and Abel was removed. The shared SIL OFL1.1 and exact source/archive/font hashes are in `Assets/Fonts/OFL.txt` and `PROVENANCE.txt`. Fonts, authored PNG/sounds and provenance copy to build/publish output. The old inspector implicitly chose ImGui's pixel-style ProggyClean font; it now explicitly loads the configured Iosevka TTF. ImGui1.92's dynamic-font backend follows framebuffer rasterizer density rather than stretching an old fixed 13px bitmap. `fontSize` remains the shared physical-pixel target: the inspector converts it to logical size once, and native ImGui rounds to a logical pixel (up to half a logical pixel of size quantization, with a one-logical-pixel minimum). No extra UI-resolution downscale is used; game SDL.Ttf rendering remains separate. Actual visible/DPI sharpness still needs user acceptance.

Dependency notices are under `Licenses/Media`, module-qualified `Licenses/CrushKit/Input` and `Licenses/CrushKit/Audio`, plus game audio notices in `Assets/Audio/ThirdParty`. Module-qualified paths prevent shared-dependency collisions in publish output. BRUTAL's bundled license also requires product credits, its pre-game logo (subject to stated permission/exception), and NOTICE distribution before end-user distribution; copying notices alone is not a complete release-compliance claim.

Hidden checks can additionally exercise HUD-only rendering, inspector open/close, texture preview and resize, without input injection:

```powershell
pwsh -NoProfile -File tests/Rendering/hidden-smoke.ps1 -Configuration Debug -Mode Gameplay -Overlays
```

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

The maintained drawing API, color/borrowed-frame contracts, stroke patterns and geometry limits are documented in [CrushKit Draw2D](../../../../crushkit-brutal/docs/draw2d.md). Breakout's showcase remains a game-owned integration demo of that API.


## Shmup aim and audio feedback

Shmup aiming uses the original mode's long line, impact circle and short reflected bounce indicator, backed by an instantaneous projectile-radius native cast against the current walls and live bricks. Piercing mode ignores bricks as bounce targets. Open top/bottom exits show an unreflected line to the screen edge; ambiguous or overlapping queries retain the short fallback rather than inventing a bounce. This is current-pose feedback, not prediction of future moving-brick positions.

Shmup has short firing, brick-hit, brick-destruction and player-damage cues, and reuses the loss cue for game over. Events are captured per fixed tick, including catch-up ticks; destruction takes precedence over hit feedback, and defeat cuts other tails and plays once. Mute, focus loss, restart and mode changes stop stale feedback.
