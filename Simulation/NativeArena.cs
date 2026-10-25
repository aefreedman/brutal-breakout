using Brutal.Box2DApi;
using Brutal.Numerics;
using CrushKit.Brutal.Physics;

namespace BrutalBreakout.Simulation;

/// <summary>Game-specific native arena. The job graph guarantees exclusive native access;
/// Box2D may dispatch internal collision/solver tasks. All native distances are meters.</summary>
internal sealed class NativeArena : IDisposable
{
    internal const float UnitsPerMeter = 100;
    private readonly B2World world;
    private readonly Box2DTaskScheduler tasks;
    private readonly B2Body ball;
    private readonly B2Shape ballShape;
    private readonly B2Shape[] walls = new B2Shape[4];
    private readonly bool[] wallHit = new bool[4];
    private readonly int wallCount;
    private readonly B2Body[] brickBodies;
    private readonly B2Shape[] brickShapes;
    private readonly bool[] brickAlive;
    private readonly bool[] brickHit;
    private readonly B2ShapeDef brickDefinition;
    private bool disposed;

    private readonly PhysicsSettings settings;
    private readonly GameplaySettings gameplay;

    internal NativeArena(PhysicsSettings settings, GameplaySettings gameplay, int physicsWorkerCount)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(gameplay);
        this.settings = settings;
        this.gameplay = gameplay;
        wallCount = gameplay.Breakout is null ? 4 : 3;
        int brickCount = gameplay.Breakout?.Bricks.Count ?? 0;
        brickBodies = new B2Body[brickCount];
        brickShapes = new B2Shape[brickCount];
        brickAlive = new bool[brickCount];
        brickHit = new bool[brickCount];
        gameplay.ValidatePhysics(settings);
        var definition = B2WorldDef.Default;
        definition.MaximumLinearSpeed = Math.Max(definition.MaximumLinearSpeed, gameplay.NativeSpeed);
        definition.Gravity = default;
        definition.EnableSleep = false;
        definition.EnableContinuous = true;
        // The published binding needs callbacks as well as a worker budget.
        tasks = new(physicsWorkerCount);
        definition.RestitutionThreshold = 0;
        definition.HitEventThreshold = 0;
        // Published contact tuning: stiffness in Hz, 1 = critical damping.
        definition.ContactHertz = settings.ContactHertz;
        definition.ContactDampingRatio = settings.ContactDampingRatio;
        try
        {
            tasks.Configure(ref definition);
            world = Box2DWorldLifetime.CreateWorld(in definition);
        }
        catch { tasks.Dispose(); throw; }
        try
        {
            var shape = B2ShapeDef.Default;
            var material = B2SurfaceMaterial.Default;
            material.Friction = 0;
            material.Restitution = 1;
            shape.Material = material;
            shape.EnableContactEvents = true;
            shape.EnableHitEvents = true;
            shape.Filter.CategoryBits = 1;
            shape.Filter.MaskBits = 2;
            void Wall(int index, float x, float y, float hx, float hy)
            {
                var bodyDefinition = B2BodyDef.Default;
                bodyDefinition.Type = B2BodyType.Static;
                bodyDefinition.Position = new(x, y);
                var body = world.CreateBody(in bodyDefinition);
                var box = Box2D.MakeBox(hx, hy);
                walls[index] = body.CreatePolygonShape(in shape, in box);
            }
            var a = gameplay.Arena;
            double t = a.WallThickness;
            float M(double value) => Geometry.Meters(value);
            Wall(0, M(a.Left - t / 2), M((a.Top + a.Bottom) / 2), M(t / 2), M((a.Bottom - a.Top) / 2));
            Wall(1, M(a.Right + t / 2), M((a.Top + a.Bottom) / 2), M(t / 2), M((a.Bottom - a.Top) / 2));
            Wall(2, M((a.Left + a.Right) / 2), M(a.Top - t / 2), M((a.Right - a.Left) / 2 + t), M(t / 2));
            if (wallCount == 4)
                Wall(3, M((a.Left + a.Right) / 2), M(a.Bottom + t / 2), M((a.Right - a.Left) / 2 + t), M(t / 2));
            shape.Filter.CategoryBits = 4;
            brickDefinition = shape;
            for (int i = 0; i < brickCount; i++) CreateBrick(i);
            var bd = B2BodyDef.Default;
            bd.Type = B2BodyType.Dynamic;
            bd.Position = gameplay.NativeSpawn;
            bd.IsBullet = true;
            bd.FixedRotation = true;
            bd.EnableSleep = false;
            bd.IsAwake = true;
            bd.LinearDamping = 0;
            bd.AngularDamping = 0;
            ball = world.CreateBody(in bd);
            shape.Filter.CategoryBits = 2;
            shape.Filter.MaskBits = 1 | 4;
            shape.Density = 1;
            var circle = new B2Circle { Radius = gameplay.NativeRadius };
            ballShape = ball.CreateCircleShape(in shape, in circle);
            Freeze();
        }
        catch
        {
            try { Box2DWorldLifetime.DestroyWorld(world); }
            finally { tasks.Dispose(); }
            throw;
        }
    }

    internal void Guard()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        tasks.ThrowIfFaulted();
    }

    internal double2 Position { get { Guard(); return Logical(ball.GetPosition()); } }
    internal double2 Velocity { get { Guard(); return Logical(ball.GetLinearVelocity()); } }
    private static double2 Logical(float2 value) => new((double)value.X * UnitsPerMeter, (double)value.Y * UnitsPerMeter);
    internal void Freeze()
    {
        Guard();
        ball.SetLinearVelocity(default);
        ball.SetType(B2BodyType.Static);
    }
    internal void Launch(double2 direction)
    {
        Guard();
        ball.SetType(B2BodyType.Dynamic);
        ball.SetLinearVelocity(new((float)(direction.X * gameplay.NativeSpeed), (float)(direction.Y * gameplay.NativeSpeed)));
    }
    internal void Reset()
    {
        Freeze();
        ball.SetTransform(gameplay.NativeSpawn, B2Rot.Identity);
    }
    private void CreateBrick(int index)
    {
        var rectangle = gameplay.Breakout!.Bricks[index];
        var definition = B2BodyDef.Default;
        definition.Type = B2BodyType.Static;
        definition.Position = new(Geometry.Meters(rectangle.X + rectangle.Width / 2), Geometry.Meters(rectangle.Y + rectangle.Height / 2));
        brickBodies[index] = world.CreateBody(in definition);
        try
        {
            var box = Box2D.MakeBox(Geometry.Meters(rectangle.Width / 2), Geometry.Meters(rectangle.Height / 2));
            brickShapes[index] = brickBodies[index].CreatePolygonShape(in brickDefinition, in box);
            brickAlive[index] = true;
        }
        catch
        {
            brickBodies[index].Dispose();
            throw;
        }
    }
    internal bool IsBrickAlive(int index) { Guard(); return brickAlive[index]; }
    internal void ResetBricks()
    {
        Guard();
        for (int i = 0; i < brickAlive.Length; i++)
        {
            if (brickAlive[i]) brickBodies[i].Dispose();
            brickAlive[i] = false;
        }
        for (int i = 0; i < brickAlive.Length; i++) CreateBrick(i);
    }
    private bool IsWall(B2Shape shape)
    {
        for (int i = 0; i < wallCount; i++) if (walls[i].Equals(shape)) return true;
        return false;
    }
    private bool IsLiveBrick(B2Shape shape)
    {
        for (int i = 0; i < brickAlive.Length; i++) if (brickAlive[i] && brickShapes[i].Equals(shape)) return true;
        return false;
    }
    private void RecordContact(B2Shape a, B2Shape b)
    {
        B2Shape other;
        if (ballShape.Equals(a)) other = b;
        else if (ballShape.Equals(b)) other = a;
        else return;
        for (int i = 0; i < wallCount; i++) if (walls[i].Equals(other)) wallHit[i] = true;
        for (int i = 0; i < brickAlive.Length; i++)
            if (brickAlive[i] && brickShapes[i].Equals(other)) brickHit[i] = true;
    }
    internal readonly record struct StepResult(int WallContacts, int BricksDestroyed)
    {
        internal bool HasContact => WallContacts > 0 || BricksDestroyed > 0;
    }
    internal unsafe StepResult Step()
    {
        Guard();
        Array.Clear(wallHit); Array.Clear(brickHit);
        world.Step((float)settings.StepSeconds, settings.Substeps);
        tasks.ThrowIfFaulted();
        // First consume both complete native buffers into owned flags. Never mutate bodies while
        // traversing borrowed events. Begin + hit (or multiple hits) identify each shape only once.
        var events = world.GetContactEvents();
        for (int i = 0; i < events.BeginCount; i++)
        {
            var e = events.BeginEvents[i];
            RecordContact(e.ShapeIdA, e.ShapeIdB);
        }
        for (int i = 0; i < events.HitCount; i++)
        {
            var e = events.HitEvents[i];
            RecordContact(e.ShapeIdA, e.ShapeIdB);
        }
        int contacts = 0, destroyed = 0;
        for (int i = 0; i < wallCount; i++) if (wallHit[i]) contacts++;
        for (int i = 0; i < brickAlive.Length; i++)
        {
            if (!brickHit[i]) continue;
            brickBodies[i].Dispose();
            brickAlive[i] = false;
            destroyed++;
        }
        return new(contacts, destroyed);
    }

    internal WallContact Query(double2 origin, double2 direction)
    {
        Guard();
        if (!Arena.Finite(origin) || origin.X < 0 || origin.X > gameplay.Arena.Width || origin.Y < 0 || origin.Y > gameplay.Arena.Height)
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (!Arena.Finite(direction) || (direction.X == 0 && direction.Y == 0))
            throw new ArgumentOutOfRangeException(nameof(direction));
        var unit = Arena.Normalize(direction);
        // Longer than the arena diagonal, finite even for extreme input direction magnitudes.
        float castMeters = gameplay.CastMeters;
        float2 translation = new((float)unit.X * castMeters, (float)unit.Y * castMeters);
        var proxy = new B2ShapeProxy { Count = 1, Radius = gameplay.NativeRadius };
        proxy.Points[0] = new((float)(origin.X / UnitsPerMeter), (float)(origin.Y / UnitsPerMeter));
        var filter = B2QueryFilter.Default;
        filter.CategoryBits = 2;
        filter.MaskBits = 1 | 4;
        float nearest = float.PositiveInfinity;
        float2 normal = default;
        bool meaningful = false, ambiguous = false;
        // Never throw through the native callback. Continue rather than clip so equal-distance
        // corner normals can be detected independent of broad-phase visitation order.
        B2CastResultFcn callback = (shape, point, n, fraction, context) =>
        {
            if ((!IsWall(shape) && !IsLiveBrick(shape)) || !float.IsFinite(fraction) || fraction < 0 || fraction > 1) return 1;
            const float tie = 1e-6f;
            float lengthSquared = n.X * n.X + n.Y * n.Y;
            bool valid = float.IsFinite(lengthSquared) && lengthSquared > .5f && lengthSquared < 1.5f && fraction > 0;
            if (fraction < nearest - tie)
            {
                nearest = fraction;
                normal = n;
                meaningful = valid;
                ambiguous = false;
            }
            else if (Math.Abs(fraction - nearest) <= tie)
            {
                if (!valid || !meaningful || n.X * normal.X + n.Y * normal.Y < .999f) ambiguous = true;
            }
            return 1;
        };
        world.CastShape(in proxy, translation, filter, callback, nint.Zero);
        GC.KeepAlive(callback);
        if (!meaningful || ambiguous) return default;
        normal = float2.Normalize(normal);
        float2 reflected = float2.Reflect(new((float)unit.X, (float)unit.Y), normal);
        var displacement = Logical(translation) * nearest;
        return new(double2.Distance(default, displacement), origin + displacement, new(reflected.X, reflected.Y))
        { Normal = new(normal.X, normal.Y) };
    }

    public void Dispose()
    {
        if (disposed) return;
        try { tasks.Drain(); }
        finally
        {
            try { Box2DWorldLifetime.DestroyWorld(world); }
            finally
            {
                disposed = true;
                tasks.Dispose();
            }
        }
    }
}
