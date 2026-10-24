# Published Box2D backend evidence

## Configurable core seam (supersedes fixed timing/public constructor notes below)

`PhysicsSettings(double updateHz, int substeps, int maxCatchUpSteps, double contactHertz,
double contactDampingRatio)` requires all five values. It is sealed with getter-only properties;
contact values are validated and stored as native floats. `StepSeconds` derives from `UpdateHz`.
`FixedStepClock(settings)`, `BallSimulation(settings)`, internal `NativeArena(settings)` and
`BallSandboxSession(settings)` require the same immutable instance. Clock and simulation expose
`Settings` and instance `StepSeconds`; the clock exposes instance `MaxStepsPerAdvance`.
No production fallback/default settings are provided. File/app configuration remains integration-owned.

Validation rejects nonpositive/nonfinite frequency, nonpositive counts, negative/nonfinite/overflowing
contact values (including positive values that underflow to zero), and native float step/substep or
inverse conversions that become zero or nonfinite. There is no arbitrary frequency range. Validation
is numeric representability, not a promise of meaningful physics at every representable extreme.
Box2D controls contact response; no new native tuning constraints or response corrections were added.

Headless tests cover actual native first displacement and independently stepped published-world
agreement at 30/1, 60/2, 120/4, 144/3 and 240/8 Hz/substeps. One simulated second agrees with direct
native-backed simulation at all five rates across 30/60/144/1000 render partitions, using custom
caps and contact tuning. Tests also cover cap 1 through int.MaxValue, huge elapsed fraction retention,
custom-rate session pending edges/pause reset, and zero warmed managed allocation at 30/1, 144/3,
240/8. Historical contact tolerances below remain scoped to the original explicit test fixture.

The following sections record the preceding fixed-settings backend migration, not current defaults.

- Exact NuGet `Ahwoo.BRUTAL.Box2D [2026.10.2]`; transitive Native 2026.10.2, Core 2026.10.4, Numerics 2026.10.2 (locks checked in).
- Managed nupkg SHA256: `63B9CBA50264C8E23AFAB06A108CFFCC18F43CC59703374A8BB78C8E655E2294`.
- API provenance: verified-published-api.md in audit output `9cba6cb7-9450-49f3-9bb6-04c1f52974cd/box2d` (2026-10-24). Actual DLL exports instance B2World/B2Body/B2Shape methods and Default fields, not XML static spellings.
- Reused: CreateWorld, default definitions, polygon/circle shapes, bullet CCD, contact/hit buffers, CastShape circle proxy, query filters, float2.Reflect and Dispose. No missing facility requiring an analytic solver.
- Windows x64 headless native create/Step/CastShape/Dispose test passed Debug and Release, .NET 10.0, SDK 10.0.201. No manual native install, custom P/Invoke, graphics or window activity.
- Scale: 100 logical units/meter; fixed 1f/120f step, four substeps, zero workers/gravity/damping/friction; restitution 1 and threshold 0. Native velocities are never renormalized.
- Query returns center translation, not callback surface point. Zero-fraction/invalid normal gives no hit. Distinct normals at fractions within 1e-6 are ambiguous: no hit rather than inventing a corner reflection. Callback continues through all hits to make tie policy independent of order. No exceptions cross the callback boundary.
- World tests serialized because native global world allocation is shared. Worlds are independently owned and explicitly disposed on their creating thread.

## Migration policies (approved after native measurement)

ContactHertz=120 and ContactDampingRatio=1 (critical damping) are set on the default world definition. Published XML documents Hz as stiffness and 1 as critical damping, but supplies no numeric range; 120 Hz is a conservative quarter of the 480 Hz substep frequency. This one bounded tuning trial improved sampled contact-plane preview agreement; it did not remove CCD penetration. No further tuning/search or application correction is used.

The initial <=1-unit bounds target failed with native CCD penetration of 1.259117 units. Supervisor approved a **2 logical-unit (.02m) native contact bound** for bounded tests, not an analytic inset guarantee. First-contact cast center is about .50004 unit past the ideal circle-center plane (native slop). Sampled closest native approach agrees with preview plane within 1 unit in the tested non-corner shots; exact TOI plus remaining-distance trajectory is deliberately not promised.

Actual corner casts generally report one meaningful wall normal; return that, never synthesize a combined normal. If the callback does report distinct equal-fraction normals, return no hit. Exact touching/overlapping cast origins may return zero normal/no hit, including a reflected cast directly from the previous native cast center. Presentation uses a named **1 logical-unit query-only skin along the native surface normal** for the second leg; no world/body position or circle radius is changed. If the second cast is unavailable the reflected segment is omitted. No-hit first leg shows only a short aim indicator.

Launch clears eligibility before stepping; an actual same-step new ball-wall begin/hit event may legitimately set it again on a tangential relaunch at contact. Catch still returns without stepping/relaunching. Event buffers are read immediately after each native step and never retained across mutations.

## Final bounded runtime gates and integration handoff

Windows 10.0.26300 win-x64, SDK 10.0.201, .NET runtime 10.0.5; actual published box2d.dll loaded from NuGet runtime assets. Locked restores and standalone library builds succeeded with zero warnings/errors in Debug and Release. **70/70 simulation/backend/CPU-consumer tests**, unchanged **147/147 shape tests**, and unchanged **28/28 input tests** passed in each configuration. Actual `Presentation/BallSandbox.cs` and its existing session/viewport source are linked into the simulation test project, without referencing/restoring the root app; no test creates the still-integration-owned session. World tests are serialized.

Ten shots, 10,000 steps each (four cardinal, four corner-targeting, diagonal `(1,-.731)`, grazing `(1,1e-6)`) had no escape/stall/energy failure. Worst values were identical in Debug/Release:

| Measurement | Observed worst | Test allowance |
| --- | ---: | ---: |
| Native center penetration past ideal radius-inset bounds | 2.000179291 logical units (straight down) | 2 native contact + .001 coordinate roundoff |
| Speed drift from launch 600 | .000190735 logical units/sec | 1 |
| Seven non-corner first-contact preview-plane closest approaches | .500005960 logical units | 1 |
| 50 catch/hold/relaunch/reset cycles penetration | 1.259117126 logical units | 2 |
| Same cycles speed drift | .000047684 logical units/sec | 1 |
| 10,000 warmed native Step calls managed allocation (owner thread) | 0 bytes | measurement only, not a binding-wide guarantee |

The **.001 logical-unit coordinate-roundoff allowance is separate** from the 2-unit native-contact allowance, specifically approved after observing 2.000179291. Measurements are not truncated to claim penetration <=2. No solver tuning beyond the one contact Hz/damping trial, and no clamps, custom reflections, speed normalization, per-query worlds, or analytic cast fallback.

Additional tests: 100 create/step/query/destroy cycles beside an independent held world; wrong-thread Step/Query/Preview/Dispose rejected; idempotent destruction and use-after-disposal rejected; fixed elapsed travel; robust extreme finite aims/casts; zero/nonfinite argument policies; overlap/default no-hit; radius/filter correctness; exact-origin reflected no-hit vs separated second native cast; actual CPU geometry in two viewports across aiming/corner/flying/caught/reset. All loops bounded, all worlds disposed.

Public seam:
- `BallSimulation : IDisposable`, `Step(SimulationCommand command = default)`, `const double StepSeconds`, snapshot `State` and `Preview()` retained.
- New `WallContact Query(double2 origin, double2 direction)` on simulation; static `Arena.Query` and custom `QueryUnit` deleted.
- `WallContact` retains positional `(double Distance, double2 Position, double2 ReflectedDirection)` and adds `HasHit` (default false; positional construction true) and native `Normal` (toward free space).
- Native operations (Step, queries, Dispose) require construction thread. Snapshot State may be read after disposal. No finalizer/global world; partial-construction failures dispose the created world.

Integration owner still must update **root and Gameplay transitive locks**, make `BallSandboxSession`/smoke owners disposable and dispose every app/session/direct test simulation on the native owner thread, adapt old strict/8-decimal analytic assertions to documented engine tolerances, and run root/Gameplay/hidden renderer gates. No root app build or GPU/window activity was attempted here. Native assets only cover win-x64/linux-x64/osx-arm64; only Windows x64 runtime is proved. This backend does not claim exact simultaneous both-axis corner reversal or analytic elapsed-distance post-impact identity. The one-unit second-cast query skin is an explicitly approximate drawing aid, never physical motion.
