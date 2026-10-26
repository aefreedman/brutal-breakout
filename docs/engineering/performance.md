# Performance design and limits

Performance is a design constraint from the first implementation. Avoid obviously unnecessary work by design. Use profiling to verify assumptions, investigate unexpected costs and guide optimizations whose benefits are uncertain. Prefer simple, bounded designs over both naive hot loops and elaborate speculative optimizations.

## Design and review defaults

For a changed hot path, identify its frequency, work scaling, storage/ownership, invalidation, saturation behavior and an appropriate deterministic regression.

- Work should follow active objects, observed interactions or changed inputs—not reserved capacity products. Dense indexed lookup storage and justified linear capacity scans remain useful.
- Reuse bounded owner-private scratch. Avoid unnecessary intermediate lists, per-object captured callbacks and repeated sorting/materialization or JSON conversions.
- Recompute stable configuration/derived data at change boundaries. Every mutation and lifecycle transition must participate in invalidation; public mutable aliases must not bypass revisions.
- Preserve independently owned immutable publications and the same authoritative immutable settings objects. Do not recycle published buffers without a reader-release contract.
- Keep native physics authoritative and simulation independent of presentation/platform work. Preserve fixed-tick events and trail capture during catch-up; do not silently skip them to reduce rendering work.
- Use coarse-grained simulation work instead of one task per object; preserve existing ordered asynchronous APIs. Batch graphics work and reuse resources through verified facilities rather than inventing new cache/pool/scheduler frameworks.
- Bound work, memory, queues and saturation behavior. Remove structural waste rather than compensating for it with thread counts, GC/JIT modes, pool limits or reduced physics fidelity. Evaluate tuning separately with representative measurements and explicit behavior tradeoffs.

Owned snapshot arrays, construction-time bounded buffers, change-triggered validation and explicit saves are deliberate costs. This is not a blanket zero-allocation policy. Prefer deterministic work-count, invalidation and retained-ownership tests to wall-time gates or runtime-specific exact byte totals.

## Current implementation

Configuration documents own mutable JSON privately, expose cached immutable read views and accept explicit edits. Idle owner updates and repeated status reads do not parse, serialize or revalidate unchanged documents. Invalid attempts retry only after an input/context change. Save remains explicit and conflict-aware.

Shmup reconciles sparse logical contact candidates and resolves only actual hit pairs. Marked entries are cleared instead of complete ball×brick matrices. Publication reuses private staging bounded by configured capacities, then copies active entries into independently owned output arrays in stable ID order. Native ball overlap/sweep callbacks are instance-owned and reused with per-call ball-index context. Physics preview.8 submits already-partitioned native ranges as plain scheduler jobs rather than wrapping them in another parallel-loop master; all range handles still complete before native token release.

## Why hundreds of live balls still cost more

There is no intentional 500-ball cliff. The sparse fix removed unnecessary pair traversal; it did not make active physics or presentation free.

- Every active ball remains an awake, continuous-collision native body. Balls collide with walls/bricks, **not other balls**. Dense placement can still increase native broad-phase/contact/query work; do not infer the responsible stage without evidence.
- Shmup's [interval policy](physics.md) performs several world steps per gameplay tick, each with solver substeps, per-ball overlap queries and speed restoration. Piercing adds pre-step overlap and sweep queries. These multipliers matter even after management overhead is improved.
- At the shipped initial four intervals and 120 Hz, 500 bouncing balls cause about 240,000 per-ball overlap calls per simulated second, in addition to collision response and velocity access. This is arithmetic from the implementation, not a timing prediction.
- Snapshot output, trail history and trail geometry scale with live population. Trails have 24 points per ball and a 16,384 rendered-segment limit in shipped [`trails.json`](../../Config/trails.json); the render limit does not disable simulation history capture.
- Increasing **capacity alone** reserves more bodies, fixtures, staging and dense lookup storage. The current pair lookup cells use roughly nine bytes per configured ball×brick slot, excluding array/native/other overhead. At 512×2,048 that is about nine MiB; doubling ball capacity doubles that storage. Full pair traversal is gone, but some single-pass pool scans remain.
- Expensive ticks trigger catch-up work before rendering. Once the fixed-step budget is exceeded, apparent frame degradation can be much sharper than the extra cost of one ball.

High fire rate increases population and spatial density; it is not equivalent to increasing an otherwise-unused cap. Full-game frame time also includes presentation, native scheduling, GPU submission and waits. Headless CPU evidence cannot attribute the user's frame-time limit to one of those stages.

## Investigating without abandoning the defaults

Keep build and validation modes explicit: normal Debug launches enable Vulkan validation, while normal Release launches do not unless requested. Those costs are outside a headless physics test. Do not present Debug-versus-Release or validation differences as proof of the cause of a particular live-ball limit.

Start with source-backed work/ownership reasoning. For uncertain design tradeoffs, compare controlled equal populations/capacities and separate active-work, collision-density, scheduling and quality-budget effects. Label CPU-only synthetic results honestly. Use representative full-frame/tail-latency evidence when the question concerns rendering or perceived responsiveness. Take targeted measurements where needed; routine cleanups do not require a new benchmark framework.

The [execution/worker correction](execution.md#controlled-high-population-finding) addresses the large preview.7 median penalty without lowering caps or physics quality. The equivalent 500-ball native-only comparison fell from 16.17 to 1.53 ms/tick with two workers in preview.8. This is not a full-frame or universal-threshold claim: a retained sample still contained a roughly 102 ms whole-step outlier, with no established cause. Keep residual tail latency and the combined [physics quality budget](physics.md) as separate questions, and behavioral correctness separate from throughput comparisons.

## Validation

Use changed Debug filters, one affected-domain integration batch and an app build. Add an affected hidden GPU scenario only for actual presentation/rendering changes. Visible tests require separate permission. Escalate for genuine shared API/lifecycle risk; avoid routine full matrices and unrelated smoke repeats.

See [validation](validation.md) for commands, [configuration](../design/configuration.md) for live editing and the [player guide](../player/guide.md) for controls.
