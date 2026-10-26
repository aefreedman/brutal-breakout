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

The physics bridge marshals native task dispatch through a coordinator and executes work on its scheduler workers. Thread dispatch, signaling and joins have a cost, particularly with many small native world steps. A physics worker budget of one still uses the bridge; do not equate it with a scheduling-free native inline path.

More workers can help sufficiently large independent tasks, but can also increase synchronization cost or compete with the render thread and other pools. Do not give every pool all cores, infer an optimum from CPU utilization alone or use thread count to compensate for repeated unnecessary work. Small-scene comparisons did not establish an advantage for two physics workers. A controlled 500-ball comparison subsequently found a substantial interaction; see below. Neither result establishes a portable optimum.

The design questions are task granularity, when dispatch should be inline versus parallel, total CPU budget and the number of native steps—not only worker count. Verify the consumed package's task API and lifetime contracts before changing scheduling. A policy change belongs in the shared physics bridge only when it is genuinely reusable, not as a game-specific duplicate scheduler.

## Controlled high-population finding

With 500 active bouncing balls, 40 active square bricks, fixed capacities of 1,024 balls/2,048 bricks, 120 Hz and four intervals × four solver substeps, a headless native-arena test measured:

| Physics bridge workers | Median arena-step elapsed time |
| --- | ---: |
| 1 | 1.20 ms/tick |
| 2 | 16.17 ms/tick |

These are medians of three 48-tick sample means after warm-up, Release/.NET 10.0.5 with tiered compilation disabled on one Windows/i9-9900K machine. They exclude setup, spawning, destruction, managed snapshot publication and rendering. The two-worker excess persisted with one/two/four solver substeps and was inside elapsed `world.Step`, which includes native execution, bridge callbacks and waits. It is not established as bridge-only overhead or native compute time.

Aggregate hit/contact facts matched across worker choices; changing substeps changed those facts. Aggregate agreement is not full trajectory/lifecycle equivalence. This prioritizes execution investigation before reducing physics quality; it does not establish a universal 500-ball threshold, the cause of a particular live-game slowdown or a shipped-default recommendation.

For a reversible stress-session diagnostic, try `physicsWorkerCount: 1` with the required owner reset and unchanged physics settings. Recreate comparable populations/layouts: comparing a fresh reset against an older overloaded run is not a controlled comparison. One worker still uses the bridge, and improved headless time does not guarantee a particular frame rate. The shipped default remains 2.

A separate instrumented paired repeat of the same 500-ball/four-substep fixture narrowed the excess to completion-return waits **after the last recorded native callback exit**. With two workers, 59 tasks had post-callback tails over 10 ms; with one worker, none exceeded 1 ms. All long tails belonged to two-range tasks. Schedule/Flush and native callback elapsed were not the large bucket in that repeat.

This is a completion/wakeup-path lead, not proof of a particular race, sleep policy or OS cause. The observation boundary is native callback exit—not completion of all scheduler wrapper/finalization work—and owner descheduling is not excluded. Instrumentation and run variance materially changed absolute time relative to the earlier test; do not mix the two timing sets or add overlapping parallel wall-time buckets as CPU time.

The next engineering step is a focused two-range completion-publication/wait regression against the consumed scheduler, followed only by a narrowly evidenced correction. Preserve exact-once ranges, exclusive concurrent worker indices, accepted-work draining and sticky faults; verify prompt completion as well as correct physics/lifetime behavior. Do not lower quality or replace the scheduler to conceal the tail. A native-default-task-fields comparison still requires exact-version creation/step/destruction proof; do not call it single-thread/inline merely because custom callbacks are absent, or invent hybrid worker-zero ownership.

## Sources

- [`ExecutionSettings`](../src/Runtime/Simulation/ExecutionSettings.cs) and [`ExecutionConfiguration`](../src/Runtime/Game/Configuration/ExecutionConfiguration.cs)
- [`ShmupSimulation`](../src/Runtime/Simulation/ShmupSimulation.cs), [`BallSimulation`](../src/Runtime/Simulation/BallSimulation.cs)
- [Physics quality budget](physics.md), [performance considerations](performance.md)
