# Execution settings and scheduling

[`Config/execution.json`](../../Config/execution.json) owns the explicit CPU budgets. There is no automatic all-core policy. Settings are validated before native startup; execution changes require resetting the owning worlds.

| Setting | Shipped value | Meaning |
| --- | ---: | --- |
| `workerCount` | 2 | Workers for the Arch ECS job scheduler. |
| `maxExpectedConcurrentJobs` | 64 | ECS scheduler handle-pool sizing, not a ball limit, queue limit or throttle on concurrent gameplay objects. |
| `physicsWorkerCount` | 2 | Worker budget used by the Box2D scheduling bridge. Coordinator threads are additional to worker budgets. |

These 2/64/2 values were explicit composition choices for the earlier ECS/native setup, not a measured performance claim for Shmup or a requirement that every scene use two workers. The runtime settings allow worker budgets from 1 through 64; the ECS handle-pool setting allows 1 through 65,536.

## What actually runs in parallel?

Shmup managed simulation is synchronous and owns its native world exclusively. Increasing `workerCount` does not parallelize its ball loops. `physicsWorkerCount` controls the native physics bridge and is the relevant worker budget for native stepping.

Classic `BallSimulation.StepAsync` has an ordered submission contract: accepted ticks and queries are sequenced, and disposal drains accepted work. Its ECS/native jobs are explicitly joined before publication. This asynchronous submission API operates on complete ticks, not one task per ball.

Workers do not make concurrent reads/writes to a stepping world safe. Input/native processing, ECS work and immutable publication retain explicit ownership boundaries.

## Costs and sensible defaults

The physics bridge marshals native task dispatch through a coordinator and executes work on its scheduler workers. Since preview.8, each already-partitioned native range uses a plain scheduler `IJob`; the coordinator flushes once and completion waits for every range handle. This avoids an unnecessary parallel-loop master without replacing the scheduler. Thread dispatch, signaling and completion waits still have a cost, particularly with many small native world steps. A physics worker budget of one still uses the coordinator and scheduler; it is not an inline native path.

The ECS and physics worker pools share CPU time with the render thread. More workers can increase synchronization cost as well as available parallelism. Small-scene comparisons did not establish an advantage for two physics workers; the 500-ball comparison below exposed a bridge-specific penalty. Neither comparison establishes a portable optimum for Breakout's worker settings. The reusable native task bridge is implemented in CrushKit; Breakout supplies its worker budget and owns the gameplay world.

## Controlled high-population finding

The preview.7 bridge wrapped already-partitioned native ranges in `IJobParallelFor`. A controlled 500-ball test showed a large two-worker penalty. An instrumented repeat localized long waits after the last native delegate returned and before the owner observed handle completion. Exact ZeroAllocJobScheduler 1.1.2 DLL inspection found a parallel-master subscriber-poll stage before completion publication. The audit did not establish a lost wakeup or particular OS sleep/descheduling cause.

Preview.8 instead submits those same ranges through existing `Schedule(IJob, ...)`. Partitioning, worker indices, native physics and defaults stay unchanged. Every handle completes before native token/context release; per-task completion is idempotent across Drain/Finish and scheduler-pool reuse. This is not a weaker native-callback-only barrier. Unknown partial submission/flush failures still fail fast, and managed callback faults remain sticky. Upstream scheduler disposal requests shutdown rather than joining worker threads; accepted native work is completed first and the bridge coordinator is joined.

### Equivalent package-level comparison

With 500 active bouncing balls, 40 active square bricks, fixed capacities of 1,024 balls/2,048 bricks, 120 Hz and four intervals × four solver substeps:

| Physics bridge workers | Preview.7 median | Preview.8 median |
| --- | ---: | ---: |
| 1 | 1.20 ms/tick | 1.55 ms/tick |
| 2 | 16.17 ms/tick | 1.53 ms/tick |

These measure whole `NativeShmupArena.Step`, not full-game/frame time or engine-only compute. Each median is over three independent 48-tick sample means after warm-up and one predeclared discarded sample. Release/.NET 10.0.5, tiered compilation disabled, one Windows/i9-9900K machine. Setup, spawning, destruction, snapshot publication and rendering are excluded. Versions were measured in separate sessions; the older probe included a few inner timers while the new probe times only the whole step. The ratios are therefore not a pure causal overhead estimate; the one-worker control was also slower in the newer session.

All new samples matched the saved body/shape/awake/interval counts and aggregate hit/contact facts. Matching aggregates do not establish full trajectory/lifecycle equivalence; separate range/fault/drain and game simulation tests cover those contracts. Lower substeps changed contact facts in the earlier investigation and did not remove its large two-worker penalty, so physics quality was not weakened as the remedy.

**Residual tail limitation:** one retained two-worker sample averaged 4.84 ms/tick and contained a 101.98 ms whole-step outlier (three retained ticks exceeded 10 ms). No extra samples were discarded or rerun. There was no inner completion/GC/CPU/OS trace in this new comparison; the remaining spikes cannot be assigned to the old master-poll stage or a particular cause. The result does not establish that every tick meets 120 Hz, that all completion tails disappeared or that there was a universal 500-ball threshold.

The shipped worker default remains 2. The earlier one-worker override was a reversible preview.7 diagnostic, not a permanent workaround required by preview.8. Changing execution settings resets the owning world and therefore changes population/progression unless the workload is recreated. The current bridge uses its coordinator and scheduler at either worker budget; these comparisons do not measure a scheduling-free native-default or inline path.

## Study-only dedicated-owner protocol

The opt-in [CPU study](performance.md#opt-in-cpu-physics-study-tool) compares direct stepping, a dedicated owner with immediate observation (dispatch-overhead control), and a synthetic one-tick overlap pipeline. Its app-local BCL thread/monitor protocol is not a shared executor or replacement for Classic `StepAsync`. Construction, tuning if used, stepping, diagnostics and disposal stay on the constructing owner thread. There is **one total accepted unit**, running or pending observation—not one running plus one queued. Further admission is rejected until observation. Initialization/step faults wake waiting consumers; disposal drains accepted work and joins the owner rather than disposing native state from another thread.

Every completion carries command sequence/enqueue/start/publication timestamps, that tick's owned immutable snapshot, copied event flags and owner-copied diagnostics. Consumer history captures each completion in order. Overlap presents the previous completed snapshot while one next command runs, explicitly recording input-sequence age, wall snapshot age and backpressure. Initial fill and final drain are included, with one capture/presentation for every completed whole command, including command zero and the final command. A borrowed `ShapeFrame` remains a synchronous consumer-local draw-list view; it never crosses threads or survives reuse as a publication.

This is **not** proof of a real 120 Hz simulation/60 Hz host loop or lower input latency. The separate catch-up experiment uses `FixedStepClock` with an explicit 60 Hz frame/input tape and known stalls, records dropped offered wall time separately from command completion, and does not count less simulated time as faster execution. Production execution policy and existing Classic source are unchanged.

## Sources

- [`ExecutionSettings`](../../src/Runtime/Simulation/ExecutionSettings.cs) and [`ExecutionConfiguration`](../../src/Runtime/Game/Configuration/ExecutionConfiguration.cs)
- [`ShmupSimulation`](../../src/Runtime/Simulation/ShmupSimulation.cs), [`BallSimulation`](../../src/Runtime/Simulation/BallSimulation.cs)
- [Physics quality budget](physics.md), [performance considerations](performance.md)
