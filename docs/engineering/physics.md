# Physics settings and quality budget

[`Config/physics.json`](../../Config/physics.json) is authoritative. All five settings are required, strictly validated before platform/native allocation, and passed through the same immutable `PhysicsSettings` object. There are no C# fallback defaults. `--physics-config <path>` selects another file; relative paths resolve against the caller's working directory.

## Settings

| Setting | Shipped value | Meaning |
| --- | ---: | --- |
| `updateHz` | 120 | Fixed gameplay ticks per second; each tick spans `1 / updateHz` seconds (8.33 ms at 120 Hz). Fractional positive rates are supported when native arithmetic can represent them. |
| `substeps` | 4 | Solver substeps in each Box2D world step. They are not extra gameplay commands. In Shmup there can be several world steps per gameplay tick. |
| `maxCatchUpSteps` | 8 | Maximum gameplay ticks in one host advance. Excess new elapsed time is discarded while the prior fractional tick is preserved; there is no unbounded catch-up debt. |
| `contactHertz` | 120 | Native contact stiffness frequency, not a simulation update rate or iteration count. It is explicit tuning, not automatically matched to `updateHz`. |
| `contactDampingRatio` | 1 | Native contact damping ratio; 1 denotes critical damping. Material restitution separately controls bounce. |

Changing `updateHz` or catch-up capacity requires an explicit reset. Substeps and contact tuning apply live at the owner boundary. External file edits are not watched; use Reload in the configuration UI or launch with the file. Reload/Discard may apply valid live fields, while structural fields remain pending. Strict validation establishes numeric representability and application constraints, not useful physics for every extreme combination.

## Two levels of subdivision in Shmup

Classic Breakout advances its native world once per flying tick with the configured solver substeps; paused/held updates do not imply a native step. Shmup currently adds application-controlled native intervals:

```text
intervals = max(1, ceil((ballSpeed + descentSpeed)
                       * tickSeconds / (ballRadius / 2)))
worldStepSeconds = tickSeconds / intervals
solverSubstepsPerTick = intervals * substeps
```

The interval count depends on speed, radius and descent, not ball population. With shipped speed 576, radius 2.52 and initial descent 21.6, there are four intervals per tick; maximum descent 86.4 produces five. At 120 Hz with four solver substeps, that means 480–600 native world-step calls and 1,920–2,400 solver subdivisions per simulated second. Catch-up can run several complete ticks before the next rendered frame. Solver subdivisions are internal to each native world step; gameplay updates occur once per fixed tick.

Intervals preserve application contact/expiry chronology and short post-wall query paths, particularly for piercing balls. Native sweeps detect thin crossed bricks, and Box2D continuous collision detection is also enabled. These mechanisms serve overlapping but not identical purposes. The interval loop currently runs for bouncing balls too. It is not yet proven that every mode needs the same subdivision policy.

## Why these defaults?

The 120 Hz / four-substep settings predate Shmup's interval loop. They are a fidelity-biased inherited baseline, not a demonstrated optimum for hundreds of balls. [Upstream Box2D guidance](https://box2d.org/documentation/md_simulation.html) recommends four solver substeps generally and describes 60 Hz as usually high quality; that does not independently justify four substeps inside each of our four-to-five application intervals. Contact settings affect collision behavior; they are not interchangeable speed presets. Eight catch-up ticks cap work, but can still allow a long rendered frame when each tick is expensive.

Tick rate, application intervals and solver substeps jointly determine Shmup's quality budget. The [controlled worker correction](execution.md#controlled-high-population-finding) in preview.8 removed the large prior median penalty without changing this quality budget. Reducing solver substeps had not removed that fixture's two-worker slowdown and changed contact/hit results. Residual whole-step outliers remain unlocalized, so the measurements do not identify reduced physics quality as their remedy. Reducing update rate alone does not necessarily reduce Shmup cost proportionally: larger ticks can require more intervals. Reducing radius or increasing speed also increases the interval count. Raising worker counts does not remove this work; see [execution](execution.md).

Breakout's interval/substep behavior supports compound seam deduplication, genuine reentry, piercing sweeps, moving-brick contacts, chronological wall expiry, same-interval brick precedence, earlier hits surviving later expiry and fixed-tick events/trails. Box2D owns collision response. Throughput measurements alone do not establish that these gameplay behaviors are unchanged.

## Observed settings sensitivities

At the 2,000-ball anchor, 60/120/240 Hz realized 8/4/2 native intervals per tick: all three executed **480 native intervals per simulated second**. Lower per-tick cost at 240 Hz did not make it a cheaper simulated second. The 60 Hz case completed twenty measured simulated seconds, while 240 Hz reached only 11.44 before the work-budget limit. The whole-study scope also generates CPU geometry once per tick, so rate comparisons include a changed presentation frequency; the real render cadence was not measured.

Two solver substeps had a similar step median to four (4.65 versus 4.68 ms). One substep was not a demonstrated faster equivalent: its case truncated at 17.43 simulated seconds and recorded work/lifecycle facts diverged early. The combined 60 Hz-stiffness/.7-damping preset completed its duration with a 4.70 ms median, but likewise changed recorded facts. Those differences are not by themselves semantic failures, nor proof that CCD/compound/expiry invariants remain equivalent under the alternatives.

Four-times ball speed raised realized native intervals from 480 to 1,920 per simulated second; that case reached 9.44 measured simulated seconds with a 15.06 ms step median before truncation. Piercing, with its additional overlap/sweep queries, reached 14.81 seconds at an 8.36 ms median. Their shorter completed durations are retained overload observations, not matched-duration winners.

### Catch-up admission

The same scripted twenty-second, 60 Hz host tape with declared stalls produced:

| Catch-up cap | Completed simulated time | Dropped offered time |
| ---: | ---: | ---: |
| 1 | 9.600 s | 10.400 s |
| 4 | 19.292 s | 0.700 s |
| 8 | 19.492 s | 0.500 s |

The latter two retain a fractional 0.00833-second tick. Every admitted command completed. A cap of one took less work because it simulated much less offered time, not because it accelerated the solver. These are synthetic host-admission results, separate from asynchronous backpressure and real frame/device latency.

## Study quality and GPU feasibility

The [CPU study tool](performance.md#opt-in-cpu-physics-study-tool) varies tick/solver/contact/worker budgets without changing production defaults or response code. Its higher-speed interval-pressure case exercises the existing adaptive interval formula. Interval and solver work are reported per completed simulated duration. Changed trajectories under different quality budgets are not automatically failures; throughput is not evidence of quality equivalence. Required CCD, compound seam/reentry, chronological expiry/brick precedence, fixed-tick event/trail and immutable-publication contracts must pass separately before interpreting any configuration as a safe alternative. Small deterministic native fixtures and scheduler gates are correctness tests, not a 4,000-ball benchmark or exact timing/byte gates.

GPU feasibility is conceptual, **not measured**. [Box2D's 3.0 release description](https://box2d.org/posts/2024/08/releasing-box2d-3.0/) describes CPU multithreading, graph coloring and SIMD/AVX2. The current CPU Box2D integration has no selected GPU backend. Independent projectile work could suit a GPU-resident implementation, but that alone does not preserve Breakout's collision/expiry chronology, compound queries or CPU publication latency.

[PhysX 5.5.1 GPU simulation](https://nvidia-omniverse.github.io/PhysX/physx/5.5.1/docs/GPURigidBodies.html) is a different CUDA-based SDK with supported-hardware/platform and fixed-buffer constraints. Its documentation distinguishes GPU broad phase/contact/body/solver work from CPU facilities including CCD and triggers. [Direct GPU API](https://nvidia-omniverse.github.io/PhysX/physx/5.5.1/docs/DirectGPUAPI.html) can remove automatic exposed-state CPU copies; CPU poses may then be stale, and CPU contact reporting, pose-dependent queries and sweep CCD have limitations. Readback, synchronization, aim queries and snapshot ownership are design costs, not free compatibility. A device model name (including RTX 5070 Ti) is not SDK compatibility proof.

Selecting another SDK or custom Vulkan compute would require an explicit backend/CCD/compound/contact chronology/query/readback/latency/portability decision and applicable quality tests. The study adds no GPU solver, shader, backend, SDK dependency, GPU test or throughput claim.

## Sources

- [`PhysicsSettings`](../../src/Runtime/Simulation/PhysicsSettings.cs), [`FixedStepClock`](../../src/Runtime/Simulation/FixedStepClock.cs)
- [`NativeArena`](../../src/Runtime/Simulation/NativeArena.cs), [`NativeShmupArena`](../../src/Runtime/Simulation/NativeShmupArena.cs)
- [Performance considerations](performance.md)
- [Upstream simulation concepts](https://box2d.org/documentation/md_simulation.html) and [world settings](https://box2d.org/documentation/group__world.html). These upstream links explain simulation concepts; newer upstream APIs and features may not exist in Breakout's locked BRUTAL package.
