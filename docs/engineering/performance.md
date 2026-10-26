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

## Opt-in CPU physics study tool

[`tools/Performance/BrutalBreakout.Performance.csproj`](../../tools/Performance/BrutalBreakout.Performance.csproj) is a headless console study, not a production execution mode or benchmark-framework dependency. It references the public runtime presentation assembly and production configuration loaders. Simulation grants only the named tool friend access; cached, read-only reflection obtains the native adapter once per whole world for owner-thread diagnostics. Private production state is not modified by reflection.

From the game repository, after locked restore using the appropriate absolute NuGet configuration (see [development](development.md)):

```powershell
dotnet test tests/Performance/BrutalBreakout.Performance.Tests.csproj -c Debug --no-restore
# Build Debug only for a bounded smoke; one tick in each of the 28 isolated cases:
dotnet build tools/Performance/BrutalBreakout.Performance.csproj -c Debug --no-restore
dotnet artifacts/bin/BrutalBreakout.Performance/debug/BrutalBreakout.Performance.dll campaign --config Config --manifest tools/Performance/study.json --output <ignored-output-directory>/smoke --smoke-ticks 1
# Resolve/list without allocating a native world:
dotnet artifacts/bin/BrutalBreakout.Performance/debug/BrutalBreakout.Performance.dll list --config Config --manifest tools/Performance/study.json
# Deliberate intensive run, normal Release JIT (build separately):
dotnet artifacts/bin/BrutalBreakout.Performance/release/BrutalBreakout.Performance.dll campaign --config Config --manifest tools/Performance/study.json --output <ignored-output-directory>/campaign
```

The intensive command rejects Debug and explicitly disabled tiering. Two simulated seconds of warmup do **not** establish JIT stability. `case` is an internal child-process entry, not an alternative way around the campaign deadline. No window, GPU submission or rendering backend is created; `ShmupPresentation.Build` generates CPU geometry at 1280×720.

### Workloads and owned inputs

`study.json` declares tool policy, order and overrides, not game defaults. Authoritative JSON is validated before overrides and again after immutable settings construction. Campaign startup freezes owned source files and the manifest into the output directory; children use those copies. Resolved JSON/settings, layout/seed, budgets and build/runtime identities are retained. Each world has 4,096 ball/1,024 brick slots and an explicitly widened 1,280-unit play area. Forty mixed, quarter-turned compounds occupy a disjoint upper region. All 4,000 initial ball circles and rotated compound bounds are validated against current walls, open exits, the actual reset avatar and each other. Seeded 100-site stratified layers give uniform-coverage prefixes at 500/1,000/2,000/4,000; they are not an offscreen extension of the old fixture.

Default families (28, no Cartesian sweep): native and whole at four loads; piercing; spawn/reuse; physics workers 1/4/8 with whole-2000 as the 2-worker anchor; 60/240 Hz with 120 Hz anchor; solver 1/2 with 4 anchor; combined 60 Hz/.7 contact preset against 120/1; interval pressure (4× ball speed); trails absent/draw-disabled against enabled; immediate-owner/overlap against direct; recreated-500; end baseline repeat; scripted catch-up 1/4/8. Worker variants do not change ECS worker count. Other one-factor cases preserve geometry, speed, radius and command tape; interval pressure deliberately changes speed. Production loaders, not duplicated game fallback defaults, supply unspecified settings.

Controlled cases declare synthetic high brick/avatar HP, high wall-hit limit, noise threshold 1 (row suppression), and bounded validated refill. Ball and brick refill attempts, retire/clear work and accounting are included end-to-end and separately measured as maintenance. Failed placement remains an observed population deficit. The spawn/reuse case is **seeded churn in a controlled environment**, not fully natural gameplay or stationary 4,000-ball throughput: starts near cap, retains authoritative wall-hit lifetime, fires at a validated 60 Hz, performs no ball refill, and reports actual populations, admissions, drops and generations. Synthetic HP/noise/brick maintenance still apply.

Native scope times `NativeShmupArena.Step` plus separately labelled maintenance; it excludes gameplay resolution/publication. Whole scope includes actual `ShmupSimulation.Step`, owned publication, every completion's consumer capture, and CPU geometry. Native and whole timings/counters are **not automatically subtractable**. Absent trails allocate/capture no history; draw-disabled trails retain capture/storage but suppress trail geometry; enabled trails include both.

### Budgets and interpretation

- At most 2 simulated warm seconds plus 20 measured seconds: 4,800 **measured** ticks at 240 Hz plus 480 warm ticks (5,280 total). Scripted clock cases instead offer a 20-second 60 Hz host tape with declared stalls; dropped time means less simulated work and is not a speedup. Planned admissions, actual accepted/completed commands, input frame identities and elapsed simulated seconds are distinct. Smoke is explicitly un-warmed and limited to 1–120 ticks per case.
- Each child has a 30-second external wall deadline including startup/setup/warmup/teardown/output. Cooperative checks are between ticks. A blocked native call is handled only by killing that case process, never by foreign-thread disposal. Campaign ceiling is 20 minutes including setup; skips/truncations/timeouts remain recorded in declared order. No outlier is discarded or winner auto-selected.
- A sampled 1 GiB process-private soft ceiling, current available physical RAM and headroom preflight guard allocation. Windows-only `GlobalMemoryStatusEx` fails closed if unavailable. Sparse samples (startup and every 60 completed ticks) record memory pressure and observer cost. This is not a hard memory reservation. Planning storage includes full pair-matrix/list capacity, native bodies/shapes/dictionaries/ECS, two retained snapshots, trails, draw buffers and raw records—not only the roughly 36 MiB matrices. No forced GC, application closure or OS tuning is performed.
- Raw bounded value records are formatted into JSONL only after stepping and owner teardown. Each final case file contains a summary followed by tick records; parent `campaign.jsonl` records exit/timeout/missing-output separately. Partial files cannot stand in for final results. Setup/warm/measurement/teardown failures, warm/measurement truncation and memory skips remain distinct. Truncated warmup has no steady statistics.
- p50/p95/p99/max and finite N are descriptive nearest-rank samples, with low-N flags below 100. Actual work/population and completion counts must accompany comparisons; equal duration does not imply equal trajectories, quality or work. Wall and process CPU include setup/teardown; per-phase timings remain separately labelled. Global allocation/GC generation/pause counters are correlations, not pause-cause attribution. Tests do not enforce exact runtime byte/time thresholds.

Whole-step `DamagePairVisits` measures exact logical **pair visits**, not all reentry hit entries. Events are coalesced gameplay facts. Post-resolution native hit/track counts describe retained/surviving resources: clearing a retired resource removes some native facts. Whole exact native expiry/hit/wall totals are therefore unavailable (`null`), not zero. Retired outside/inside-screen poses are observational categories, not exclusive exit/expiry causes. Native-only exact hit/wall/expiry observations are copied before maintenance. Restart/frozen ticks do not reuse prior interval/hit work. See [execution](execution.md#study-only-dedicated-owner-protocol) for ownership and [physics](physics.md#study-quality-and-gpu-feasibility) for quality/GPU scope.

See [validation](validation.md) for project test commands and coverage, [configuration](../design/configuration.md) for live editing and the [player guide](../player/guide.md) for controls.
