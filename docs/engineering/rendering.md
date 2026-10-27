# Rendering architecture and performance

Rendering is separate from native physics and managed gameplay. The host may complete several fixed ticks before building one render frame. A simulation tick, CPU geometry build, Vulkan submission, GPU command interval and displayed frame are different units of work. Standalone phase medians are not automatically additive frame-time predictions.

## Presentation controls

Configuration → **WINDOW** exposes live **VSync** and **Frame rate cap (0 unlimited)** controls. The cap commits on Enter; Save persists it. Authoritative `Config/window.json` requires `width`, `height`, boolean `vsync` and integer `frameRateLimit` (0–1000). Defaults remain VSync on with no additional software cap. Custom window files must provide all four fields.

VSync on requests FIFO; off requests supported Immediate, with explicit rejection rather than a silent fallback if unavailable. Changes recreate only the swapchain at the owner boundary, not the gameplay world. The software cap subtracts time already spent doing work or waiting on presentation, then pumps events in bounded waits; it does not add another whole frame interval. Zero cap does not disable VSync. Very low caps can reach the existing fixed-clock catch-up/drop limit without changing physics Hz. See [configuration](../design/configuration.md#initial-window-configuration) for persistence and timing details.

## Batching and instancing

The shared CrushKit renderer already batches solid shapes. `ShapeDrawList` appends vertices and indices to reusable arrays and merges adjacent ranges with the same framebuffer scissor. Colors are per vertex, so changing ball/brick/trail colors does not require a material draw call. Ordering is retained for premultiplied-alpha blending; different clips can split batches.

The cached-triangle reference scenes at 500/1,000/2,000/4,000 balls produced **one shape command and one indexed shape draw per frame**. This is not one draw per ball. Letterboxing/nested clips can produce more commands. HUD and inspector commands are separate from these shape counts.

**Shmup uses shared GPU meshes by default since preview.11.** Balls and compound bricks reuse immutable local vertices/indices with dynamic position, scale and color instances. `Config/rendering.json` and the ImGui configuration window retain the explicit **CPU-built triangles** alternative. Trails and other changing geometry remain dynamic; classic Breakout rendering is unchanged.

The CPU-built-triangle path uses per-vertex binding and an instance count of one. Since preview.10, its `DiskMeshAdapter` retains the last generated/welded unit-disc resolution and native storage; a resolution transition regenerates it. This is instance-owned one-entry reuse, distinct from persistent shared GPU meshes.

Shared meshes target repeated CPU expansion and vertex/index uploads, not an already single triangle draw. Ordered instances retain curve precision, clipping, alpha order, framebuffer scaling and ownership contracts; adjacent compatible instances batch without sorting transparent objects.

## Submission and synchronization

The CPU-built-triangle Vulkan path has:

- One shared submission fence, command buffer and shape vertex/index buffer set; presentation semaphores are indexed by acquired swapchain image.
- Full vertex/index/command validation on every `Draw`, including normal Release. This is separate from optional Vulkan validation layers.
- Host-visible, CPU-preferred vertex/index buffers, mapped/copied/flushed each frame. There is no shape staging-to-device-local upload path or persistent instance buffer.
- Since preview.10, power-of-two private upload capacity growth while copying/flushing only actual payload bytes and drawing only logical counts. Shrink/reuse does not replace buffers. Capacity is less than twice the growth request; above 2³⁰ bytes the helper uses the exact requirement to avoid integer overflow. Retained capacity trades bounded extra storage for fewer replacements; frozen warmed scenes do not measure growth costs.
- Four-sample MSAA, sRGB output and premultiplied-alpha blending; configurable FIFO/Immediate presentation. GPU resources and swapchain are not recreated each ordinary frame.

After CPU frame validation, the renderer waits its previous submission fence, uploads geometry, acquires an image, records commands, submits and presents. The next host iteration can do CPU work while the preceding GPU submission executes; one in-flight buffer set does not imply zero CPU/GPU overlap. `Draw` wall time may include fence/acquire/presentation pacing and is not GPU execution time.

HUD labels use cached whole-string textures and separate indexed textured quads, one per visible label. Changing label text can require rasterization and texture upload; it is not a persistent glyph-atlas instance path. The optional ImGui inspector has its own native backend and draw commands. The shape-only measurements below exclude both, as well as audio and game input handling. See [media](media.md) for their implementation and ownership.

## CPU geometry study

The opt-in performance tool has a **native-world-free** mode:

```powershell
dotnet build tools/Performance/BrutalBreakout.Performance.csproj -c Release --no-restore
dotnet artifacts/bin/BrutalBreakout.Performance/release/BrutalBreakout.Performance.dll render-cpu --config Config --manifest tools/Performance/study.json --output <ignored-output-directory>/render-cpu
```

This reuses validated owned settings and the seeded forty-compound layout, not the physics campaign's measured ticks. Synthetic immutable publications contain horizontal reflected paths and fixed-tick trail samples; they are not recorded physics trajectories. Setup/history capture is excluded. Each case performs 200 warm and 400 measured synchronous `ShmupPresentation.Build` calls. `--smoke-ticks 3` instead means three un-warmed geometry builds per case, not physics ticks.

A fixed-order Release pass on the i9-9900K/.NET 10.0.5 produced:

| Balls | Trails enabled p50 / p99 | History absent p50 / p99 |
| ---: | ---: | ---: |
| 500 | 2.00 / 3.47 ms | 0.50 / 0.76 ms |
| 1,000 | 3.30 / 5.62 ms | 0.98 / 1.79 ms |
| 2,000 | 4.25 / 6.67 ms | 1.96 / 3.91 ms |
| 4,000 | 6.40 / 9.82 ms | 3.93 / 6.90 ms |

These scenes use 1280×720. At 2,000 balls with trails, geometry was 118,311 vertices / 150,447 indices, about **3.28 MiB** of vertex/index upload per frame, despite only one shape draw. Without trails it was about 1.45 MiB. Higher framebuffer resolution increases disc tessellation: the same 2,000-ball trail scene took 4.69 ms median at 1920×1080 and 5.81 ms at 3840×2160. Those are CPU-only results, not GPU timings at those extents.

All thirteen cases completed 400 samples, with outliers retained. The final repeated 2,000-ball/720p case had identical frozen-frame bytes and a 4.26 ms median. Measured builds recorded zero current-thread managed allocation after warm-up; this does not imply no native allocation or allocation-free simulation/publication. Normal tiered JIT and a finite warm-up are not proof of complete JIT stability.

The tool freezes its six source configurations, resolved settings, raw timing arrays, counts and SHA256-hashed frame exports into the output directory. Its little-endian frame format stores a 24-byte header, 24-byte vertices, uint32 indices and six-int commands. Export occurs synchronously before borrowed draw-list buffers are reused; exported arrays can be consumed independently by a renderer-only probe. The tool itself creates no Vulkan renderer and reports no GPU time. Bounds, memory observations, truncations and smoke state remain explicit.

### Unit-disc reuse and upload capacity growth

A normal-Release comparison of preview.9 against the reviewed cache/growth changes used the same thirteen frozen configurations/scenes, 200 warm builds and 400 measurements per case. All thirteen frame hashes and vertex/index counts matched exactly. At 720p:

| Balls | Trails: before → cached CPU build p50 | No trails: before → cached CPU build p50 |
| ---: | ---: | ---: |
| 2,000 | 4.24 → 3.85 ms | 1.96 → 1.39 ms |
| 4,000 | 6.42 → 5.32 ms | 3.92 → 2.80 ms |

This single paired comparison observed roughly 9%/17% lower trail-enabled medians and 29% lower no-trail medians at these loads. It is descriptive CPU-preparation evidence, not a full-game/GPU speedup. The original table above records the earlier uncached study.

CPU geometry studies do not execute Vulkan buffer allocation. Separate capacity-policy tests cover ramps, reuse, jumps and integer boundaries. A hidden validation-enabled fixture exercised changing geometry, three actual upload replacement pairs, shrinking/reuse and resize with zero Vulkan errors and the three known layer-naming warnings. It validates buffer ownership and submission, not a quantified frame-time improvement or pixel equivalence. Copy, flush, fence protection and logical draw counts remain unchanged.

### Instancing prototype: not adopted

An isolated ordered prototype compared cached triangles (A), instanced balls (B), and instanced balls plus trails (C). It shared the existing trail selection/fading and presentation ordering, retained tessellation and logical primitive counts, and preserved triangle fallback. This is not part of the shipping packages.

One predeclared 27-case CPU pass used 200 warm builds and 400 measured builds per case. Directly timed preparation plus validation regressed with C: at 2,000 balls/trails, **4.32 → 5.53 ms**; at 4,000, **6.55 → 8.26 ms**. The repeated 2,000-ball control likewise regressed. These are midpoint medians for this experiment, rather than the earlier study's nearest-rank percentiles.

In six separate frozen, no-present GPU cases (180 measured submissions each), C reduced upload payload from 3.44 MB to 0.73 MB at 2,000 balls, and GPU command span from 0.72 ms to 0.13 ms. At 4,000 it reduced 4.95 MB to 0.81 MB and 0.87 ms to 0.12 ms. Those GPU gains do not establish a whole-frame benefit: no live combined preparation-to-submission distribution was collected, and the CPU preparation result was worse.

The prototype conservatively reconstructs reference disc vertices/triangles and segment corners to check eligibility/precision. That is a source-backed explanation worth examining, not profiled proof of the regression's cause or proof that instancing inherently loses. A future implementation could investigate cached mesh invariants and cheaper justified per-instance checks without dropping necessary validation.

CPU contracts and hidden Vulkan validation passed with unchanged selected trail counts and no Vulkan errors. GPU image/readback equivalence, translucent edges/seams and overlay pixel overlap were not established. **Instancing remains unpromoted** because this implementation regressed CPU cost and lacks image-quality acceptance. Its complete source and negative-result evidence are retained outside maintained documentation for future investigation.

### Shared meshes: default Shmup rendering path

Preview.11 retains immutable local ball and Shmup brick meshes with compact dynamic instances. Brick variants use the same immutable pitch-aware geometry, including quarter-turns and open L/T cutouts. Movement, health and tint changes reuse geometry; geometry revisions or physical-radius LOD changes select/rebuild only affected keys. CPU geometry, selection/revision storage and GPU residency are bounded. Cached invariants and narrow positive-uniform-scale/translation precision checks avoid rebuilding reference geometry per instance. Unsupported/uncertain cases retain ordered triangle fallback. Trails keep their existing traversal, fading and budgets; rounded Breakout bricks remain dynamic.

`Config/rendering.json` defaults to `sharedMeshes`. The ImGui **Configuration → RENDERING** choice switches live between **Shared meshes** and **CPU-built triangles**, including while paused, without changing the run, time or progression. Save remains explicit; Reload/Discard restore source choices. Resource budgets are validated before native allocation and require relaunch to change. The same immutable startup limits object reaches the producer and renderer; switching paths retains warm geometry. See [rendering configuration](../design/configuration.md#shmup-rendering-path).

Independent precision/lifetime review and matching-target hidden GPU readback preceded performance. Eight 1280x720 sRGB/4x-MSAA A/B/A scenes, including translucent overlaps, cutouts, clips, fallback placement and HUD/ImGui, were byte-identical. Resize, instance growth/shrink/reuse, VSync recreation and disposal passed with zero Vulkan errors; known upstream layer-name warnings were recorded separately. This is evidence for those fixtures/device, not universal visible-window, 4K or driver-fault acceptance.

A single matched CPU pass compared cached triangles, baked balls/bricks with dynamic trails, and the recovered conservative balls/trails prototype on the same owned inputs. Direct preparation-plus-validation medians at 2k trails were 4.3083/3.1350/5.3246 ms; at 4k, 6.1099/3.7044/7.9964 ms. The conservative control is contemporaneous here; the older negative-control numbers above remain historical. All 27 CPU cells retained 400 measurements after 200 warm calls. The 500/no-trails candidate showed little combined gain and worse packing p95; finite warm-up/tiering is an unprofiled uncertainty, not grounds for a selective rerun.

A separate 18-cell, same-binary hidden held-image comparison used one setup, 60 warm and 180 measured submissions per cell, with capture off and ordinary CPU guards on. Directly measured preparation-to-submit medians at 2k/4k trails improved 4.8498→3.4205 and 6.6804→3.9708 ms. Median GPU command spans improved 0.7624→0.3834 and 1.0532→0.4024 ms; payload bytes fell 3,441,252→2,004,132 and 4,953,252→2,084,132. Measured mesh rebuild/upload and buffer replacement counts were zero. Each cell actually acquired once, submitted/read 241 query pairs and never presented or signaled an unused presentation semaphore; final queries drained after their protecting fence.

The gains are not uniform: 1k/2k no-trails preparation-to-submit medians regressed by about **0.26/0.30 ms** (0.9933→1.2578 and 1.8373→2.1334 ms). Several individual phase p95s also regressed, including recording and prior-submission fence waits. Primary combined CPU/GPU-span p95 endpoints stayed within the proposed 5% bound, but the original blanket all-phase threshold was not met. Adoption explicitly accepts these small absolute low-load tradeoffs for the larger high-load savings; it does not reinterpret the threshold as passed. Original maxima and the repeated anchor remain retained; no whole-game FPS or sums of independent medians are inferred. Independent source/configuration review and the final hidden default/live-switch scenario passed. Measurement-only query/held-image hooks were excluded from preview.11 packages.

## GPU and presentation evidence

The following historical renderer-only/pacing study is separate from the newer baked-mesh comparison above; its capture and extent limitations are not claims that the newer matching-target images were unavailable.

GPU timing requires actual device timestamps, not subtracting CPU geometry from `Draw` or calling presentation waits shader work. Renderer-only experiments consume the frozen frames without physics stepping or CPU tessellation. A hidden window can validate commands and measure that environment's GPU interval; it does not establish visible desktop frame rate, compositor latency, image quality or input-to-display latency.

A separate temporary instrumented build of the actual CrushKit renderer consumed the exported meshes on the RTX 5070 Ti. It preserved the original validation, upload, draw and synchronization paths. Published locked BRUTAL Vulkan 2026.10.3 query wrappers were verified by DLL reflection and compile probes; no replacement Vulkan wrapper or dependency was introduced. Timestamp pairs bracketed the real command buffer through the end of its render pass and were read after the associated submission fence. Explicit drains kept setup/warm/measured boundaries separate.

Each of thirteen completed normal cases used sixty warm and 180 measured frames, with Vulkan validation layers **off**. At 1280×720:

| Balls | GPU command span, trails p50 / p99 | GPU command span, no trails p50 / p99 |
| ---: | ---: | ---: |
| 500 | 0.28 / 0.55 ms | 0.07 / 0.12 ms |
| 1,000 | 0.62 / 0.92 ms | 0.14 / 0.25 ms |
| 2,000 | 0.65 / 0.96 ms | 0.26 / 0.37 ms |
| 4,000 | 0.98 / 3.30 ms | 0.56 / 2.90 ms |

The 2,000-ball trail span at 1920×1080 was 0.74 ms median / 3.36 ms p99. Repeating the 720p anchor gave 0.64 ms median; all outliers remained. These GPU intervals include relevant command-stage stalls, not only shader arithmetic. They exclude queue delay outside the timestamped commands and presentation/display completion.

At 2,000 balls/trails at 720p, renderer CPU medians were 0.67 ms full frame validation, 0.30 ms upload and 0.046 ms command recording. At 4,000 they were 0.99/0.48/0.047 ms. These are separate from the CPU geometry builds above and from physics. They indicate that **CPU geometry preparation deserves investigation before assuming draw-call count or GPU execution is the bottleneck** in this fixture; they do not prove an instancing speedup or full-game bottleneck.

Whole `Draw` medians were roughly 11.76 ms even for clear-only controls. Previous-submission fence waits accounted for about 10–11.6 ms, while clear-only GPU spans were 0.011 ms at 720p and 0.019 ms at 1080p. The waiting/pacing is not equivalent to GPU render work. A fence-wait sample refers to the preceding submission, whereas its reported GPU span belongs to its own submitted commands. Adding those phase medians, or adding standalone `Draw` wall time to host simulation time, would misrepresent overlap and pacing.

### Presentation pacing control

A focused follow-up used the same frozen 720p clear/2,000-ball/4,000-ball trail scenes under FIFO, supported-and-selected Immediate, and a no-present diagnostic. Each normal case had sixty warm and 180 measured submissions; three preceding validation-enabled heavy-scene probes passed with zero Vulkan errors and the same three known layer-naming warnings each.

| Scene | FIFO Draw p50 | Immediate Draw p50 | No-present Draw p50 | No-present GPU span p50 |
| --- | ---: | ---: | ---: | ---: |
| Clear | 11.73 ms | 0.76 ms | 0.10 ms | 0.011 ms |
| 2,000 balls + trails | 11.77 ms | 3.07 ms | 1.14 ms | 0.76 ms |
| 4,000 balls + trails | 11.74 ms | 3.43 ms | 1.54 ms | 1.04 ms |

The large FIFO prior-submission fence wait disappeared in these controls. **The roughly 11.76 ms interval was presentation-associated pacing, not equivalent GPU rendering cost.** Immediate still spent CPU time in presentation; it is not proof that every compositor/driver/global limiter was absent. Monitor refresh and GPU clocks/power were not sampled, so the precise mechanism or display frequency is not established.

The no-present mode acquired one swapchain image during setup and retained it, reusing its framebuffer after each submission fence. Warm/measured loops made no further acquire or present calls and signaled no unconsumed presentation semaphores. Every such process had 241 GPU submissions/query pairs, one acquisition and zero presentations. This is rendering to a held acquired image, **not** a surface-free offscreen renderer or a production execution mode. Validation, uploads, shaders, render pass, MSAA and geometry remained unchanged. Its 4,000-ball GPU p99/max were 3.59/4.72 ms; outliers were retained.

The independent CPU geometry study creates no Vulkan device or swapchain: its 4.25 ms geometry cost cannot be a vsync wait. The earlier roughly 209 ms headless simulation-step outlier likewise contained no Vulkan presentation loop and remains a separate unresolved tail. None of these controls changes production vsync defaults or establishes full-game frame rate, input latency, or an instancing benefit.

### Coverage limits

Requested 3840×2160 hidden-window geometry could not be tested: the actual surface was 3840×1581, and the unchanged renderer correctly rejected the mismatched frame. That process's extent failure was retained, and the other 2160 GPU cases were not attempted. CPU 2160 geometry results do not fill this GPU evidence gap. No resizing workaround or visible-window test was used.

Two preceding hidden validation-enabled probes, at 500 and 4,000 balls, completed with zero Vulkan errors and the three known upstream GalaxyOverlayVkLayer naming warnings each. No visible process windows were sampled. One validation probe observed unrelated foreground-window changes, so its nonactivation evidence is inconclusive; hidden metadata sampling is not a universal focus-disturbance guarantee. Normal performance samples themselves do not establish validation-layer cleanliness.

This evidence excludes dynamic population-growth buffer reallocations, HUD/inspector costs, real host cadence, physical input/display latency and image-quality verification. Unit-disc reuse and bounded upload capacity growth are implemented in preview.10. Instancing remains experimental rather than a production default. Physics, execution and presentation defaults/backend policy are unchanged; temporary GPU timing hooks are not a shipping API.

See [performance](performance.md) for simulation scaling, [execution](execution.md) for physics-worker/coarse-owner studies and [validation](validation.md) for hidden versus visible GPU coverage.
