# Performance design and limits

Breakout's CPU cost includes fixed-tick simulation, native physics and queries, snapshot publication, trail history and render-geometry generation. GPU submission and presentation add separate costs. This guide describes the implementation choices, remaining scaling costs and measured limits of this project.

## Implemented improvements

- **Sparse collision bookkeeping.** Shmup reconciles observed logical contact candidates and resolves actual hit pairs. Marked entries are cleared instead of scanning or clearing complete ball×brick matrices. Contact chronology, compound seams and separate reentries retain their gameplay meaning.
- **Change-driven configuration.** Configuration documents privately own mutable JSON and expose cached immutable read views. Idle updates and repeated status reads do not parse, serialize or revalidate unchanged documents. Invalid attempts retry after an input/context change; Save remains explicit and conflict-aware.
- **Private staging, owned publications.** Shmup reuses bounded private staging, then copies active entries into independently owned snapshot arrays in stable ID order. Readers can retain snapshots across later updates, so published arrays are not recycled. Required events and trail samples are captured at fixed ticks, including catch-up ticks.
- **Cached native callbacks.** Ball overlap/sweep delegates are instance-owned and reused with a per-call ball-index context instead of constructing a callback for each ball query.
- **Simpler native range dispatch.** Physics preview.8 submits already-partitioned native ranges as plain scheduler jobs instead of wrapping them in another parallel-loop master. All range handles still complete before native token release; see [execution](execution.md#controlled-high-population-finding).

Simulation snapshots still allocate, and some single-pass pool scans remain. Pooling and scratch reuse reduce repeated work without making the whole tick allocation-free or independent of configured capacity.

## Why hundreds of live balls still cost more

There is no intentional 500-ball cliff. The sparse fix removed unnecessary pair traversal; it did not make active physics or presentation free.

- Every active ball remains an awake, continuous-collision native body. Balls collide with walls/bricks, **not other balls**. Dense placement can still increase native broad-phase/contact/query work.
- Shmup's [interval policy](physics.md) performs several world steps per gameplay tick, each with solver substeps, per-ball overlap queries and speed restoration. Piercing adds pre-step overlap and sweep queries.
- At the shipped initial four intervals and 120 Hz, 500 bouncing balls cause about 240,000 per-ball overlap calls per simulated second, in addition to collision response and velocity access. This is arithmetic from the implementation, not a timing prediction.
- Snapshot output, trail history and trail geometry scale with live population. Trails have 24 points per ball and a 16,384 rendered-segment limit in shipped [`trails.json`](../../Config/trails.json); the render limit does not disable simulation history capture.
- Increasing **capacity alone** reserves more bodies, fixtures, staging and dense lookup storage. The current pair lookup cells use roughly nine bytes per configured ball×brick slot, excluding array/native/other overhead. At 512×2,048 that is about nine MiB; doubling ball capacity doubles that storage. Full pair traversal is gone, but some single-pass pool scans remain.
- Expensive ticks trigger catch-up work before rendering. Once the fixed-step budget is exceeded, frame degradation can be much sharper than the extra cost of one ball.

High fire rate increases population and spatial density; it is not equivalent to increasing an otherwise-unused cap.

## Measurements and remaining limits

The [execution/worker correction](execution.md#controlled-high-population-finding) addresses the large preview.7 median penalty without lowering caps or physics quality. The equivalent 500-ball native-only comparison fell from 16.17 to 1.53 ms/tick with two workers in preview.8. A retained sample still contained a roughly 102 ms whole-step outlier, with no established cause. The execution guide records both worker controls, workload, sampling method and limitations.

These headless measurements exclude snapshot publication, presentation and GPU work, so they do not establish full-game frame rate or the cause of a particular live-game slowdown. Native completion waits, the combined [physics quality budget](physics.md), trail/presentation costs and residual tick outliers are distinct contributors.

Normal Debug launches enable Vulkan validation; normal Release launches do not unless requested. Hidden GPU tests enable it in either configuration. These renderer costs are absent from the headless native-arena comparison. Execution-setting changes reset the owning world, which also changes population and progression unless the workload is recreated.

See [validation](validation.md) for project test commands and coverage, [configuration](../design/configuration.md) for live editing and the [player guide](../player/guide.md) for controls.
