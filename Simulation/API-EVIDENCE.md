# Published Box2D backend evidence

- Exact NuGet `Ahwoo.BRUTAL.Box2D [2026.10.2]`; transitive Native 2026.10.2, Core 2026.10.4, Numerics 2026.10.2 (locks checked in).
- Managed nupkg SHA256: `63B9CBA50264C8E23AFAB06A108CFFCC18F43CC59703374A8BB78C8E655E2294`.
- API provenance: verified-published-api.md in audit output `9cba6cb7-9450-49f3-9bb6-04c1f52974cd/box2d` (2026-10-24). Actual DLL exports instance B2World/B2Body/B2Shape methods and Default fields, not XML static spellings.
- Reused: CreateWorld, default definitions, polygon/circle shapes, bullet CCD, contact/hit buffers, CastShape circle proxy, query filters, float2.Reflect and Dispose. No missing facility requiring an analytic solver.
- Windows x64 headless native create/Step/CastShape/Dispose test passed Debug and Release, .NET 10.0, SDK 10.0.201. No manual native install, custom P/Invoke, graphics or window activity.
- Scale: 100 logical units/meter; fixed 1f/120f step, four substeps, zero workers/gravity/damping/friction; restitution 1 and threshold 0. Native velocities are never renormalized.
- Query returns center translation, not callback surface point. Zero-fraction/invalid normal gives no hit. Distinct normals at fractions within 1e-6 are ambiguous: no hit rather than inventing a corner reflection. Callback continues through all hits to make tie policy independent of order. No exceptions cross the callback boundary.
- World tests serialized because native global world allocation is shared. Worlds are independently owned and explicitly disposed on their creating thread.
