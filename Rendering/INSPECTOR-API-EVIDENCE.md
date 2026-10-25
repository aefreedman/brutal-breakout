# Runtime inspector and fonts: published/native evidence

Exact package versions/source URLs, literal probe project/source/output, metadata inspector and selected DLL IL, native test evidence and pinned font archive hashes are in `INSPECTOR-API-EVIDENCE.json`.

| Capability | Verified API/evidence | Reuse / boundary |
| --- | --- | --- |
| Runtime host toggle | Existing published GLFW `GlfwWindow.GetKey(GlfwKey.F1)` / `GlfwKeyAction.Press`, exact compile probe | Raw snapshot precedes ImGui capture. Small edge tracker baselines first/focus-regained/restore held F1; no keyboard repeat or gameplay intent. No binding framework. |
| Explicit inspector font | ImGui2026.10.3 `ImFontAtlasExtensions.AddFontFromFileTTF(ImFontAtlasPtr,ImString,float,ImFontConfigPtr,ReadOnlySpan<uint>)`, `ImGuiIOPtr.FontDefault` | Configured standard Iosevka Regular, no implicit default. Native atlas owns loaded font bytes/lifetime; context teardown releases it. |
| Density | Published GLFW backend IL assigns `io.DisplayFramebufferScale` from framebuffer/window size and registers per-viewport framebuffer-scale callback. Native `ImFontPtr.GetFontBaked(size,density)`, `ImGui.GetFontBaked()` verified | ImGui1.92 RendererHasTextures automatically rasterizes at framebuffer density. Convert physical requested fontSize to logical once; no global bitmap upscale or extra resolution knob. |
| Font source/metrics | Published `ImFontPtr.Sources`, `ImFontConfigPtr.FontDataSize/PixelSnapH/OversampleH/V`, `ImFontBakedPtr.Size/RasterizerDensity/FindGlyph` | Offscreen native tests compare previous ProggyClean and the actual configured TTF and verify monospaced inspector advances. Inspector displays actual logical/physical size and density. |
| Game font | Existing SDL.Ttf settings.FontPath | Iosevka Aile Regular remains proportional game font; separate required debugFontPath selects monospaced inspector. No accidental switch of game typography. |

## Actual low-resolution finding

The old inspector never loaded the app font. Packaged native ImGui selects ProggyClean.ttf with a13px source/default, horizontal pixel snapping and1x oversampling. **It is not a fixed13px bitmap merely enlarged**: with ImGui1.92 dynamic textures and requested22px, headless native evidence shows a22px bake. Thus no framebuffer-resolution bug was established; the implicit pixel-style face/oversampling was the concrete typography issue. The new explicitly loaded regular TTF has horizontal/vertical pixel snapping disabled and uses native auto oversampling.

Native tests cover framebuffer scales1,1.25,1.5,2 and return to1 in one context. Rasterizer density tracks each scale without a device/window. Native logical font size is rounded to a pixel and has a1logical-pixel minimum; actual physical size may differ by up to half a logical pixel times framebuffer scale. The inspector reports actual values; fontSize in overlay.json stays the physical-pixel target. No screenshot/physical-monitor claim follows from these tests.

## Assets and startup schema

Exactly TWO unmodified official v34.9.1 hinted Regular TTFs:
- Game `IosevkaAile-Regular.ttf`:11,878,496bytes.
- Inspector `Iosevka-Regular.ttf`:11,613,712bytes.
- Total23,492,208bytes(22.40MiB), shared OFL/provenance; no Abel, extra variants/collections or ZIPs.

Required overlay.json `fontPath` and `debugFontPath` are independently absolute-resolved/header-validated before native initialization, including when inspector starts closed. Every mode loads overlay settings so F1 needs no CLI arg; ordinary gallery retains its original geometry without extra HUD labels. `--debug-ui` is only an initial-visible shortcut, not a forced-visible state, and F1 can close it. Existing hidden inspector phase override stays separate from runtime key samples.

## Verification boundary

Final component focused tests: Media48passed, Input48passed,0failed/skipped, no build warnings. No new package references/lock changes. Headless native ImGui/font and existing image/font CPU tests only. Parent owns hidden GPU acceptance, integration review and physical UI/DPI acceptance. Exact probe temporary tree/archives are removed after this evidence is preserved.
