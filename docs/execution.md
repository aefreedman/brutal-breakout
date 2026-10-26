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

More workers can help sufficiently large independent tasks, but can also increase synchronization cost or compete with the render thread and other pools. Do not give every pool all cores, infer an optimum from CPU utilization alone or use thread count to compensate for repeated unnecessary work. The earlier small headless comparisons did not establish that two physics workers outperform one; they did not settle the choice for dense 500-plus-ball scenes.

The design questions are task granularity, when dispatch should be inline versus parallel, total CPU budget and the number of native steps—not only worker count. Verify the consumed package's task API and lifetime contracts before changing scheduling. A policy change belongs in the shared physics bridge only when it is genuinely reusable, not as a game-specific duplicate scheduler.

## Sources

- [`ExecutionSettings`](../src/Runtime/Simulation/ExecutionSettings.cs) and [`ExecutionConfiguration`](../src/Runtime/Game/Configuration/ExecutionConfiguration.cs)
- [`ShmupSimulation`](../src/Runtime/Simulation/ShmupSimulation.cs), [`BallSimulation`](../src/Runtime/Simulation/BallSimulation.cs)
- [Physics quality budget](physics.md), [performance considerations](performance.md)
