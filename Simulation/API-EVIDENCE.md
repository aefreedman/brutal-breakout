# Published Box2D backend evidence

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
