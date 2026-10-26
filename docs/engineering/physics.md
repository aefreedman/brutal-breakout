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

The interval count depends on speed, radius and descent, not ball population. With shipped speed 576, radius 2.52 and initial descent 21.6, there are four intervals per tick; maximum descent 86.4 produces five. At 120 Hz with four solver substeps, that means 480–600 native world-step calls and 1,920–2,400 solver subdivisions per simulated second. Catch-up can run several complete ticks before the next rendered frame. Do not confuse solver subdivisions with full gameplay updates or native world-step calls.

Intervals preserve application contact/expiry chronology and short post-wall query paths, particularly for piercing balls. Native sweeps detect thin crossed bricks, and Box2D continuous collision detection is also enabled. These mechanisms serve overlapping but not identical purposes. The interval loop currently runs for bouncing balls too. It is not yet proven that every mode needs the same subdivision policy.

## Why these defaults?

The 120 Hz / four-substep settings predate Shmup's interval loop. They are a fidelity-biased inherited baseline, not a demonstrated optimum for hundreds of balls. [Upstream Box2D guidance](https://box2d.org/documentation/md_simulation.html) recommends four solver substeps generally and describes 60 Hz as usually high quality; that does not independently justify four substeps inside each of our four-to-five application intervals. Contact settings are explicit behavior choices, not throughput controls. Eight catch-up ticks cap work, but can still allow a long rendered frame when each tick is expensive.

A sound change should establish a deliberate combined quality budget, rather than independently accumulating conservative settings. The [controlled worker correction](execution.md#controlled-high-population-finding) in preview.8 removed the large prior median penalty without changing this quality budget. Reducing solver substeps had not removed that fixture's two-worker slowdown and changed contact/hit results. Residual whole-step outliers remain unlocalized; do not weaken quality as their presumed remedy. Reducing update rate alone does not necessarily reduce Shmup cost proportionally: larger ticks can require more intervals. Reducing radius or increasing speed also increases the interval count. Raising worker counts does not remove this work; see [execution](execution.md).

Before changing interval/substep policy, preserve compound seam deduplication, genuine reentry, piercing sweeps, moving-brick contacts, chronological wall expiry, same-interval brick precedence, earlier hits surviving later expiry and fixed-tick events/trails. Keep native collision authority. A faster timing result alone does not establish equivalent gameplay.

## Sources

- [`PhysicsSettings`](../../src/Runtime/Simulation/PhysicsSettings.cs), [`FixedStepClock`](../../src/Runtime/Simulation/FixedStepClock.cs)
- [`NativeArena`](../../src/Runtime/Simulation/NativeArena.cs), [`NativeShmupArena`](../../src/Runtime/Simulation/NativeShmupArena.cs)
- [Performance considerations](performance.md)
- [Upstream simulation concepts](https://box2d.org/documentation/md_simulation.html) and [world settings](https://box2d.org/documentation/group__world.html). Current upstream documentation is conceptual guidance, not proof that every API or feature exists in the locked BRUTAL package; published-symbol/compile evidence owns that question.
