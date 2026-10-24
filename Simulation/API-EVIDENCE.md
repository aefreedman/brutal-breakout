# Published Box2D backend evidence

## Basic breakout and Unity catch line (current seam)

Required constructor is now `GameplaySettings(ArenaSettings arena, BallSettings ball,
BreakoutSettings? breakout)`. Explicit `null` selects the existing physics-reference fixture;
there is no optional argument, default rule set or default layout. The application configuration
must supply a non-null rule set. All older constructor notes below are historical.

New immutable settings:

- `BrickSettings(double x, double y, double width, double height)` uses top-left logical coordinates.
- `BreakoutSettings(int startingBalls, double catchZoneHeightFraction,
  int maxBouncesWithoutBrick, IReadOnlyList<BrickSettings> bricks)` copies the list and exposes a
  `ReadOnlyCollection<BrickSettings>`. Getters have corresponding PascalCase names.
- Starting balls must be positive, fraction finite and strictly between 0 and 1, bounce limit
  nonnegative (zero disables expiry, as in Unity), and layout nonempty with at most 1024 rectangles.
  The brick cap bounds validation/layout work, not the default number of bricks.
- Gameplay cross-validates spawn in the lower zone, full-circle spawn clearance (including native
  float conversion), brick fit inside the walls and above the line, non-overlap (touching edges
  permitted), finite edges and noncollapsing float/render/native rectangle half extents.
- `GameplaySettings.CatchLineY` and `BallSimulation.CatchLineY` use
  `height - height * fraction`, algebraically `height * (1 - fraction)` but avoiding an extra
  rounding of the complementary fraction. The explicit physics reference has NaN for this line.

Read-only simulation outputs: `ReadOnlySpan<BrickState> Bricks`, where
`BrickState(int Id, BrickSettings Bounds, bool Alive)` retains its zero-based layout ID even after
removal; `int RemainingBricks`, `int BallsRemaining` (includes the active ball),
`RoundPhase RoundPhase` (`Playing`, `Won`, `Lost`), and `long BouncesWithoutBrick`.
Existing `Settings`, `Gameplay`, `State`, `Step`, `Query`, `Preview`, and `Dispose` remain.

Reference: read-only PrototypeStudio `GameSettings-Breakout.asset` has BottomThird restriction3,
AlwaysDependent catch4, wall flags13 (top/left/right), MaxSpawns3 and bounce limit12.
`Projectile.cs`193-195 records leaving the shot zone;404-424 requires both leaving and hitting
something since firing. `ProjectileFactory.cs`458-493 restricts the BALL center, not the cursor;
the input-position check is commented out. The implemented Y-down zone boundary is inclusive.
A catch requires a primary edge after both shot facts and return to the lower zone; it freezes the
current snapshot without stepping or relaunching. Launch/catch/reset clear historical facts.
New ball-wall or ball-brick contact qualifies; a wall hit wholly inside the lower zone does not.
Native fixed-step snapshots track leaving the zone, as Unity tracks updated projectile positions.

In breakout mode there is no bottom fixture. A whole circle passing the inner bottom face loses a
ball; surviving bricks remain, and the next shot resets to configured spawn/aim. Wall-only contacts
expire the ball strictly after the configured limit; any brick hit resets that budget. Final brick
wins, final miss loses, terminal rounds freeze and ignore commands other than restart. Restart
recreates all bricks and restores lives and shot state. No paddle, progression or manual reflection,
clamp, per-tick speed repair or catch teleport was added.

Published native API proof: actual installed `Ahwoo.BRUTAL.Box2D 2026.10.2` DLL reflection exposes
`public void B2Body.Dispose()`, not a guessed world DestroyBody helper. A separate console probe at
`C:/Users/aaron/AppData/Local/Temp/brutal-breakout-core-api-probe` references that exact package,
compiles this instance call, and executes create-body/create-polygon/dispose/recreate/dispose/world
cleanup successfully headlessly. Contact structs were also reflected: begin carries ShapeIdA/B
and Manifold; hit carries ShapeIdA/B, Point, Normal, ApproachSpeed. No new imports or package change.
Native brick bodies use the existing polygon creation/material path, category4 against ball2;
wall category1 and ball/query masks1|4 include only surviving obstacles. Both complete begin/hit
buffers are consumed into owned per-shape flags before any body disposal; each body is removed
once. A seam test actually destroys two adjacent bricks in one native step. Restart discards old
handles, recreates bodies and preserves stable application IDs. Queries cast the configured full
circle against the same world and ignore destroyed shapes and the ball.

Component validation: **227/227 Simulation tests in Debug and Release** (179 previous fixture cases
preserved, 48 new breakout/settings cases), locked restores and zero-warning builds. New cases
cover actual bounce/removal/query exclusion, begin/hit deduplication and two-brick seam hit,
catch prerequisites/center boundary/return/catch hold/new-shot facts, whole-ball bottom misses,
configured lives and bounce expiry, brick reset of budget, win/loss/restart, repeated native
restore/dispose beside another world, owner-thread rejection and immutable/invalid settings.
Custom 1800x1000 geometry with radius/speed7/350 and23/950 passes native brick/catch/reset at
30Hz/1,60Hz/2,120Hz/4,240Hz/8. Only Windows x64 headless runtime verified. Representable extreme
settings remain no guarantee of stable physics, nor of observing a zone excursion wholly between
fixed-step snapshots. No root app, GPU, visible-window or controller test was run in this lane.

Parent integration remains responsible for new required gameplay JSON rules/layout and caller
migration, presentation and configured palette, real input/session host scenarios, root content
copy/docs and hidden GPU gates. This lane intentionally writes only Simulation and its tests.

## Gameplay/native geometry settings (current seam; supersedes constructors/constants below)

Public required APIs (no defaults or file IO):

- `ArenaSettings(double width, double height, double left, double top, double right, double bottom, double wallThickness)`;
  getter-only `Width`, `Height`, `Left`, `Top`, `Right`, `Bottom`, `WallThickness` (double).
- `BallSettings(double radius, double speed, double spawnX, double spawnY, double initialAimX, double initialAimY)`;
  getter-only `Radius`, `Speed` (double), `Spawn`, `InitialAim` (double2 value copies). Aim normalizes once.
- `GameplaySettings(ArenaSettings arena, BallSettings ball)`; exact-reference `Arena`, `Ball`, derived double
  `MinX`, `MaxX`, `MinY`, `MaxY`. All three settings types are sealed and immutable.
- `BallSimulation(PhysicsSettings physics, GameplaySettings gameplay)`; exact-reference `Settings`, `Gameplay`.
- `BallSandboxSession(PhysicsSettings physics, GameplaySettings gameplay, InputSettings input)`;
  `Settings` and `Simulation` retained; `Router.Settings` is the exact input instance.
- `ArenaViewport(int WindowWidth, int WindowHeight, int Width, int Height, ArenaSettings Arena)`;
  the first two sizes are logical window dimensions, the next two physical framebuffer dimensions.
  `Advance` rejects viewports whose Arena is not the session's exact arena object, before routing input.

Validation before native allocation: finite positive canvas/thickness/radius/speed, finite ordered wall faces,
finite spawn, finite nonzero robustly normalized aim; walls including thickness fit canvas; radius-inset area
is nonempty and contains spawn (inclusive). Canvas is limited by the existing renderer 2^20 coordinate
contract, not a new arbitrary gameplay range. Float rendering/native faces, insets, body positions and
half extents must not collapse. Native radius/speed/cast reach must be positive finite floats; native
squared speed must be positive finite; wall squared lengths and inverse, circle mass/inertia and inverse,
and cast squared length must be finite/nondegenerate. Speed times configured timestep and its square
must remain finite, checked before CreateWorld. These checks establish numeric representability, not
stable/accurate physics for every representable combination of scale, speed and timestep.

One unit definition remains: 100 logical units per native meter. Walls are four rectangles matching the
actual drawn union: top/bottom span outer side edges; side rectangles span inner top/bottom faces.
Native circle, launch speed, spawn/reset, origin limits and full-circle query all derive from Gameplay.
Cast reach is canvas diagonal plus twice radius. No per-step speed repair/clamp/custom reflection exists.
Catch retains the exact application snapshot; reset restores the exact configured application spawn/aim,
with its corresponding float native transform, rather than copying rounded native state into the snapshot.

Published symbol proof: installed `Ahwoo.BRUTAL.Box2D 2026.10.2` XML entry
`F:Brutal.Box2DApi.B2WorldDef.MaximumLinearSpeed` documents native meters/sec; reflection test confirms
actual public field type System.Single and production assignment compiles against that DLL.
`B2WorldDef.Default.MaximumLinearSpeed` is **400 m/s** on tested assets. World definition sets it to
at least configured native speed. Actual first-step **80000 logical units/sec (800 m/s)** remains uncapped
in a 100000x80000 canvas; full-circle cast reaches the configured far face, far beyond the former 16m.
No package/API additions, custom P/Invoke or solver changes were needed.

Headless Debug and Release: **173/173 tests each** (143 preserved +30 new), zero build warnings/errors,
locked restores for standalone Simulation and tests/Simulation. Tests serialize native allocation and
explicitly dispose worlds. New fixtures prove radius/speed **7/350** and **23/950**, canvas1800x1000,
inner faces80/120/1600/850, thickness24, spawn420/600, initial aim3/-4 -> .6/-.8. Actual native initial
motion agrees within .01 logical units/velocity units, contact unlocks catch, contact speed within .1,
cast radius-plane agrees within .51 (documented native slop), hold and reset snapshot are exact.
Custom ownership cycles prove independent filters and repeated disposal. Actual linked CPU geometry
proves wall extents56..1624/96..874, radius23 disc and radius+9 halo (+1.5 stroke halfwidth), actual indexed
categories, 2x DPI fitting/letterbox rejection and session pointer mapping on the same arena instance.
Native and float precision failures are tested without creating a world.

Integration intentionally outstanding: Program.cs must provide gameplay/input to session and its arena
to viewport; Presentation/BallSandboxSmoke.cs must replace old constructor/static Arena bounds and
literal spawn/speed assumptions; tests/Gameplay requires explicit settings fixtures and migrated callers.
Root JSON/loaders/CLI/assets/locks/README and GPU validation are integration-owned, untouched here.
No root build, visible window, graphics call, GPU test, push or merge was run. Prior native slop,
contact tolerances, owner-thread/lifetime rules and query callback policies remain unchanged.

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
