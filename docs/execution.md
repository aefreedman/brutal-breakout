# Execution settings and scheduling

[`Config/execution.json`](../Config/execution.json) owns the explicit CPU budgets. There is no automatic all-core policy. Settings are validated before native startup; execution changes require resetting the owning worlds.

| Setting | Shipped value | Meaning |
| --- | ---: | --- |
| `workerCount` | 2 | Workers for the Arch ECS job scheduler. |
| `maxExpectedConcurrentJobs` | 64 | ECS scheduler handle-pool sizing, not a ball limit, queue limit or throttle on concurrent gameplay objects. |
| `physicsWorkerCount` | 2 | Worker budget used by the Box2D scheduling bridge. Coordinator threads are additional to worker budgets. |

These 2/64/2 values were explicit composition choices for the earlier ECS/native setup, not a measured performance claim for Shmup or a requirement that every scene use two workers. The runtime settings allow worker budgets from 1 through 64; the ECS handle-pool setting allows 1 through 65,536.

## What actually runs in parallel?

Shmup managed simulation is synchronous and owns its native world exclusively. Increasing `workerCount` does not parallelize its ball loops. `physicsWorkerCount` controls the native physics bridge and is the relevant worker budget for native stepping.

Classic `BallSimulation.StepAsync` has an ordered submission contract: accepted ticks and queries are sequenced, and disposal drains accepted work. Its ECS/native jobs are explicitly joined before publication. Preserve that contract; performance-by-default rejects per-ball task fanout, not asynchronous APIs in general.

Workers do not make concurrent reads/writes to a stepping world safe. Input/native processing, ECS work and immutable publication retain explicit ownership boundaries.

## Costs and sensible defaults

The physics bridge marshals native task dispatch through a coordinator and executes work on its scheduler workers. Since preview.8, each already-partitioned native range uses a plain scheduler `IJob`; the coordinator flushes once and completion waits for every range handle. This avoids an unnecessary parallel-loop master without replacing the scheduler. Thread dispatch, signaling and completion waits still have a cost, particularly with many small native world steps. A physics worker budget of one still uses the bridge; do not equate it with a scheduling-free native inline path.

More workers can help sufficiently large independent tasks, but can also increase synchronization cost or compete with the render thread and other pools. Do not give every pool all cores, infer an optimum from CPU utilization alone or use thread count to compensate for repeated unnecessary work. Small-scene comparisons did not establish an advantage for two physics workers. A controlled 500-ball comparison subsequently found a substantial interaction; see below. Neither result establishes a portable optimum.

The design questions are task granularity, when dispatch should be inline versus parallel, total CPU budget and the number of native steps—not only worker count. Verify the consumed package's task API and lifetime contracts before changing scheduling. A policy change belongs in the shared physics bridge only when it is genuinely reusable, not as a game-specific duplicate scheduler.

## Controlled high-population finding

The preview.7 bridge wrapped already-partitioned native ranges in `IJobParallelFor`. A controlled 500-ball test showed a large two-worker penalty. An instrumented repeat localized long waits after the last native delegate returned and before the owner observed handle completion. Exact ZeroAllocJobScheduler 1.1.2 DLL inspection found a parallel-master subscriber-poll stage before completion publication. The audit did not establish a lost wakeup or particular OS sleep/descheduling cause.

Preview.8 instead submits those same ranges through existing `Schedule(IJob, ...)`. Partitioning, worker indices, native physics and defaults stay unchanged. Every handle completes before native token/context release; per-task completion is idempotent across Drain/Finish and scheduler-pool reuse. This is not a weaker native-callback-only barrier. Unknown partial submission/flush failures still fail fast, and managed callback faults remain sticky. Upstream scheduler disposal requests shutdown rather than joining worker threads; accepted native work is completed first and the bridge coordinator is joined.

### Equivalent package-level comparison

With 500 active bouncing balls, 40 active square bricks, fixed capacities of 1,024 balls/2,048 bricks, 120 Hz and four intervals × four solver substeps:

| Physics bridge workers | Preview.7 median | Preview.8 median |
| --- | ---: | ---: |
| 1 | 1.20 ms/tick | 1.55 ms/tick |
| 2 | 16.17 ms/tick | 1.53 ms/tick |

These measure whole `NativeShmupArena.Step`, not full-game/frame time or engine-only compute. Each median is over three independent 48-tick sample means after warm-up and one predeclared discarded sample. Release/.NET 10.0.5, tiered compilation disabled, one Windows/i9-9900K machine. Setup, spawning, destruction, snapshot publication and rendering are excluded. Versions were measured in separate sessions; the older probe included a few inner timers while the new probe times only the whole step. Thus the ratios are not a pure causal overhead estimate, and the one-worker increase is not hidden.

All new samples matched the saved body/shape/awake/interval counts and aggregate hit/contact facts. Matching aggregates do not establish full trajectory/lifecycle equivalence; separate range/fault/drain and game simulation tests cover those contracts. Lower substeps changed contact facts in the earlier investigation and did not remove its large two-worker penalty, so physics quality was not weakened as the remedy.

**Residual tail limitation:** one retained two-worker sample averaged 4.84 ms/tick and contained a 101.98 ms whole-step outlier (three retained ticks exceeded 10 ms). No extra samples were discarded or rerun. There was no inner completion/GC/CPU/OS trace in this new comparison; the remaining spikes cannot be assigned to the old master-poll stage or a particular cause. Do not claim every tick meets 120 Hz, all completion tails disappeared, or a universal 500-ball threshold was removed.

The shipped worker default remains 2. The earlier one-worker override was a reversible preview.7 diagnostic, not a permanent workaround required by preview.8. Compare equivalent recreated workloads when changing execution settings because the required reset changes population/progression. A scheduling-free native-default/inline path is still unproven here; do not infer one merely from a single worker or absent custom callbacks, or invent hybrid worker-zero ownership.

## Sources

- [`ExecutionSettings`](../src/Runtime/Simulation/ExecutionSettings.cs) and [`ExecutionConfiguration`](../src/Runtime/Game/Configuration/ExecutionConfiguration.cs)
- [`ShmupSimulation`](../src/Runtime/Simulation/ShmupSimulation.cs), [`BallSimulation`](../src/Runtime/Simulation/BallSimulation.cs)
- [Physics quality budget](physics.md), [performance considerations](performance.md)
