using Brutal.Box2DApi;
using Brutal.Numerics;
using CrushKit.Brutal.Physics;

namespace BrutalBreakout.Simulation;

/// <summary>Exclusive synchronous native owner. All narrow-phase queries and response use Box2D.
/// Bodies/shapes are allocated once, disabled while idle, and reset before enabling a new life.</summary>
internal sealed class NativeShmupArena : IDisposable
{
    private readonly B2World world;
    private readonly Box2DTaskScheduler tasks;
    private readonly PhysicsSettings physics;
    private readonly ShmupSettings settings;
    private readonly B2Body[] balls, bricks;
    private readonly B2Body[] walls = new B2Body[2];
    private readonly Dictionary<B2Shape, int> ballIds = new(), brickIds = new(), wallIds = new();
    private readonly bool[,] touching;
    private readonly bool[,] wallTouching;
    private readonly bool[,] overlaps;
    private bool disposed;
    internal bool[,] BrickHits { get; }
    internal int[] WallHits { get; }
    internal bool AvatarContact { get; private set; }
    internal int BodyCount => balls.Length + bricks.Length + walls.Length;

    internal NativeShmupArena(PhysicsSettings physics, ShmupSettings settings, ExecutionSettings execution)
    {
        settings.ValidatePhysics(physics);
        this.physics = physics; this.settings = settings;
        balls = new B2Body[settings.Capacity.MaxBalls]; bricks = new B2Body[settings.Capacity.MaxBricks];
        touching = new bool[balls.Length, bricks.Length]; overlaps = new bool[balls.Length, bricks.Length];
        wallTouching = new bool[balls.Length, 2]; BrickHits = new bool[balls.Length, bricks.Length]; WallHits = new int[balls.Length];
        tasks = new(execution.PhysicsWorkerCount);
        var wd = B2WorldDef.Default;
        wd.Gravity = default; wd.EnableSleep = false; wd.EnableContinuous = true;
        wd.MaximumLinearSpeed = Math.Max(wd.MaximumLinearSpeed, Geometry.Meters(Math.Max(settings.Ball.Speed, settings.Difficulty.MaximumDescentSpeed)));
        wd.RestitutionThreshold = 0; wd.HitEventThreshold = 0;
        wd.ContactHertz = physics.ContactHertz; wd.ContactDampingRatio = physics.ContactDampingRatio;
        try { tasks.Configure(ref wd); world = Box2DWorldLifetime.CreateWorld(in wd); }
        catch { tasks.Dispose(); throw; }
        try
        {
            var sd = B2ShapeDef.Default;
            var material = B2SurfaceMaterial.Default;
            material.Friction = 0; material.Restitution = 1; sd.Material = material;
            sd.EnableContactEvents = true; sd.EnableHitEvents = true;
            sd.Filter.CategoryBits = 1; sd.Filter.MaskBits = 2;
            // Wall thickness is construction geometry derived from the configured ball, not tuning.
            var box = Box2D.MakeBox(Geometry.Meters(settings.Ball.Radius), Geometry.Meters(settings.Arena.Height / 2 + settings.Ball.Radius));
            for (int i = 0; i < 2; i++)
            {
                var bd = B2BodyDef.Default; bd.Type = B2BodyType.Static;
                walls[i] = world.CreateBody(in bd);
                wallIds.Add(walls[i].CreatePolygonShape(in sd, in box), i);
            }
            Widen(settings.Arena.InitialPlayAreaWidth);
            sd.Filter.CategoryBits = 4; sd.Filter.MaskBits = 2;
            sd.IsSensor = !settings.Ball.BounceOnBricks;
            sd.EnableSensorEvents = !settings.Ball.BounceOnBricks;
            box = Box2D.MakeBox(Geometry.Meters(settings.Brick.Size / 2), Geometry.Meters(settings.Brick.Size / 2));
            for (int i = 0; i < bricks.Length; i++)
            {
                var bd = B2BodyDef.Default; bd.Type = B2BodyType.Kinematic; bd.IsEnabled = false;
                bricks[i] = world.CreateBody(in bd);
                brickIds.Add(bricks[i].CreatePolygonShape(in sd, in box), i);
            }
            sd.IsSensor = false; sd.EnableSensorEvents = true;
            sd.Filter.CategoryBits = 2; sd.Filter.MaskBits = 1 | 4; sd.Density = 1;
            var circle = new B2Circle { Radius = Geometry.Meters(settings.Ball.Radius) };
            for (int i = 0; i < balls.Length; i++)
            {
                var bd = B2BodyDef.Default; bd.Type = B2BodyType.Dynamic; bd.IsEnabled = false;
                bd.IsBullet = true; bd.FixedRotation = true; bd.EnableSleep = false;
                balls[i] = world.CreateBody(in bd);
                ballIds.Add(balls[i].CreateCircleShape(in sd, in circle), i);
            }
        }
        catch { Dispose(); throw; }
    }
    internal void Guard() { ObjectDisposedException.ThrowIf(disposed, this); tasks.ThrowIfFaulted(); }
    private static float2 Native(double2 p) => new(Geometry.Meters(p.X), Geometry.Meters(p.Y));
    private static double2 Logical(float2 p) => new((double)p.X * NativeArena.UnitsPerMeter, (double)p.Y * NativeArena.UnitsPerMeter);
    internal double2 BallPosition(int id) => Logical(balls[id].GetPosition());
    internal double2 BallVelocity(int id) => Logical(balls[id].GetLinearVelocity());
    internal double2 BrickPosition(int id) => Logical(bricks[id].GetPosition());
    internal void Widen(double width)
    {
        Guard();
        double center = settings.Arena.Width / 2, radius = settings.Ball.Radius;
        walls[0].SetTransform(Native(new(center - width / 2 - radius, settings.Arena.Height / 2)), B2Rot.Identity);
        walls[1].SetTransform(Native(new(center + width / 2 + radius, settings.Arena.Height / 2)), B2Rot.Identity);
        Array.Clear(wallTouching);
    }
    internal void ActivateBall(int id, double2 position, double2 direction)
    {
        Guard(); ClearBall(id);
        balls[id].SetTransform(Native(position), B2Rot.Identity);
        balls[id].SetLinearVelocity(Native(direction * settings.Ball.Speed)); balls[id].Enable();
    }
    internal void ActivateBrick(int id, double2 position)
    {
        Guard(); ClearBrick(id);
        bricks[id].SetTransform(Native(position), B2Rot.Identity); bricks[id].SetLinearVelocity(default); bricks[id].Enable();
    }
    internal void ClearBall(int id)
    {
        balls[id].Disable(); balls[id].SetLinearVelocity(default);
        for (int j = 0; j < bricks.Length; j++) touching[id, j] = overlaps[id, j] = BrickHits[id, j] = false;
        for (int j = 0; j < 2; j++) wallTouching[id, j] = false;
        WallHits[id] = 0;
    }
    internal void ClearBrick(int id)
    {
        bricks[id].Disable(); bricks[id].SetLinearVelocity(default);
        for (int i = 0; i < balls.Length; i++) touching[i, id] = overlaps[i, id] = BrickHits[i, id] = false;
    }
    internal void Reset()
    {
        Guard();
        for (int i = 0; i < balls.Length; i++) ClearBall(i);
        for (int i = 0; i < bricks.Length; i++) ClearBrick(i);
        AvatarContact = false;
    }
    private void Contact(B2Shape a, B2Shape b, bool end)
    {
        if (!ballIds.TryGetValue(a, out int ball))
        {
            if (!ballIds.TryGetValue(b, out ball)) return;
            (a, b) = (b, a);
        }
        if (!balls[ball].IsEnabled()) return;
        if (wallIds.TryGetValue(b, out int wall))
        {
            if (end) wallTouching[ball, wall] = false;
            else if (!wallTouching[ball, wall]) { WallHits[ball]++; wallTouching[ball, wall] = true; }
        }
        else if (settings.Ball.BounceOnBricks && brickIds.TryGetValue(b, out int brick) && bricks[brick].IsEnabled())
        {
            if (end) touching[ball, brick] = false;
            else if (!touching[ball, brick]) { BrickHits[ball, brick] = true; touching[ball, brick] = true; }
        }
    }
    private static B2ShapeProxy Circle(double2 p, double radius)
    {
        var proxy = new B2ShapeProxy { Count = 1, Radius = Geometry.Meters(radius) };
        proxy.Points[0] = Native(p); return proxy;
    }
    private B2QueryFilter BrickFilter()
    {
        var filter = B2QueryFilter.Default; filter.CategoryBits = 2; filter.MaskBits = 4; return filter;
    }
    private void OverlapBall(int ball, bool recordHits)
    {
        var proxy = Circle(BallPosition(ball), settings.Ball.Radius);
        B2OverlapResultFcn callback = (shape, context) =>
        {
            if (brickIds.TryGetValue(shape, out int brick) && bricks[brick].IsEnabled())
            {
                overlaps[ball, brick] = true;
                if (recordHits && !touching[ball, brick]) BrickHits[ball, brick] = true;
            }
            return true;
        };
        world.OverlapShape(in proxy, BrickFilter(), callback, nint.Zero); GC.KeepAlive(callback);
    }
    private void SweepBall(int ball, double dt, double descentSpeed)
    {
        // Query at the pre-step brick poses using relative motion; return 1 to collect ALL crossed bricks.
        var proxy = Circle(BallPosition(ball), settings.Ball.Radius);
        double2 v = BallVelocity(ball) - new double2(0, descentSpeed);
        B2CastResultFcn callback = (shape, point, normal, fraction, context) =>
        {
            if (brickIds.TryGetValue(shape, out int brick) && bricks[brick].IsEnabled() && !touching[ball, brick])
                BrickHits[ball, brick] = true;
            return 1;
        };
        world.CastShape(in proxy, Native(v * dt), BrickFilter(), callback, nint.Zero); GC.KeepAlive(callback);
    }
    internal unsafe void Step(double descentSpeed, double2 avatar)
    {
        Guard(); Array.Clear(BrickHits); Array.Clear(WallHits);
        for (int j = 0; j < bricks.Length; j++) if (bricks[j].IsEnabled()) bricks[j].SetLinearVelocity(Native(new(0, descentSpeed)));
        // Small native intervals keep post-wall piercing paths within a circle overlap. Native sweeps
        // still detect arbitrarily thin crossed bricks, including relative kinematic movement.
        int intervals = settings.Ball.BounceOnBricks ? 1 : (int)Math.Ceiling(
            (settings.Ball.Speed + descentSpeed) * physics.StepSeconds / (settings.Ball.Radius / 2));
        double dt = physics.StepSeconds / Math.Max(1, intervals);
        for (int step = 0; step < Math.Max(1, intervals); step++)
        {
            if (!settings.Ball.BounceOnBricks)
            {
                Array.Clear(overlaps);
                for (int i = 0; i < balls.Length; i++) if (balls[i].IsEnabled()) { OverlapBall(i, true); SweepBall(i, dt, descentSpeed); }
            }
            world.Step((float)dt, physics.Substeps); tasks.ThrowIfFaulted();
            var events = world.GetContactEvents();
            // End before begin/hit: a pair can end and begin again within a step.
            for (int i = 0; i < events.EndCount; i++) { var e = events.EndEvents[i]; Contact(e.ShapeIdA, e.ShapeIdB, true); }
            for (int i = 0; i < events.BeginCount; i++) { var e = events.BeginEvents[i]; Contact(e.ShapeIdA, e.ShapeIdB, false); }
            for (int i = 0; i < events.HitCount; i++) { var e = events.HitEvents[i]; Contact(e.ShapeIdA, e.ShapeIdB, false); }
            if (!settings.Ball.BounceOnBricks)
            {
                Array.Clear(overlaps);
                for (int i = 0; i < balls.Length; i++) if (balls[i].IsEnabled()) OverlapBall(i, true);
                Array.Copy(overlaps, touching, overlaps.Length);
            }
        }
        // Exact native overlap each tick, rather than begin-only damage, supports recurrent contact.
        AvatarContact = false;
        var player = Circle(avatar, settings.Avatar.Radius);
        B2OverlapResultFcn playerCallback = (shape, context) =>
        {
            if (brickIds.TryGetValue(shape, out int brick) && bricks[brick].IsEnabled()) AvatarContact = true;
            return true;
        };
        world.OverlapShape(in player, BrickFilter(), playerCallback, nint.Zero); GC.KeepAlive(playerCallback);
        // Restore constant configured ball speed after moving-brick restitution transfers energy.
        for (int i = 0; i < balls.Length; i++) if (balls[i].IsEnabled())
        {
            var v = BallVelocity(i);
            if (Arena.Finite(v) && (v.X != 0 || v.Y != 0)) balls[i].SetLinearVelocity(Native(Arena.Normalize(v) * settings.Ball.Speed));
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        try { tasks.Drain(); }
        finally { try { Box2DWorldLifetime.DestroyWorld(world); } finally { disposed = true; tasks.Dispose(); } }
    }
}
