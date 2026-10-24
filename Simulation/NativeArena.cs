using Brutal.Box2DApi;
using Brutal.Numerics;

namespace BrutalBreakout.Simulation;

/// <summary>One synchronous, thread-affine native world. All native distances are meters.</summary>
internal sealed class NativeArena : IDisposable
{
    internal const float UnitsPerMeter = 100;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly B2World world;
    private readonly B2Body ball;
    private readonly B2Shape ballShape;
    private readonly B2Shape[] walls = new B2Shape[4];
    private bool disposed;

    private readonly PhysicsSettings settings;
    private readonly GameplaySettings gameplay;

    internal NativeArena(PhysicsSettings settings, GameplaySettings gameplay)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(gameplay);
        this.settings = settings;
        this.gameplay = gameplay;
        gameplay.ValidatePhysics(settings);
        var definition = B2WorldDef.Default;
        definition.MaximumLinearSpeed = Math.Max(definition.MaximumLinearSpeed, gameplay.NativeSpeed);
        definition.Gravity = default;
        definition.EnableSleep = false;
        definition.EnableContinuous = true;
        definition.WorkerCount = 0;
        definition.RestitutionThreshold = 0;
        definition.HitEventThreshold = 0;
        // Published contact tuning: stiffness in Hz, 1 = critical damping.
        definition.ContactHertz = settings.ContactHertz;
        definition.ContactDampingRatio = settings.ContactDampingRatio;
        world = Box2D.CreateWorld(in definition);
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
            Wall(3, M((a.Left + a.Right) / 2), M(a.Bottom + t / 2), M((a.Right - a.Left) / 2 + t), M(t / 2));
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
            shape.Filter.MaskBits = 1;
            shape.Density = 1;
            var circle = new B2Circle { Radius = gameplay.NativeRadius };
            ballShape = ball.CreateCircleShape(in shape, in circle);
            Freeze();
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    internal void Guard()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("The native world must be accessed on its owner thread.");
        ObjectDisposedException.ThrowIf(disposed, this);
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
    private bool IsWall(B2Shape shape)
    {
        foreach (var wall in walls) if (wall.Equals(shape)) return true;
        return false;
    }
    private bool IsBallWall(B2Shape a, B2Shape b) =>
        (ballShape.Equals(a) && IsWall(b)) || (ballShape.Equals(b) && IsWall(a));

    internal unsafe bool Step()
    {
        Guard();
        world.Step((float)settings.StepSeconds, settings.Substeps);
        // Event buffers belong to this step. Consume all needed facts before any mutation.
        var events = world.GetContactEvents();
        bool contact = false;
        for (int i = 0; i < events.BeginCount; i++)
        {
            var e = events.BeginEvents[i];
            contact |= IsBallWall(e.ShapeIdA, e.ShapeIdB);
        }
        for (int i = 0; i < events.HitCount; i++)
        {
            var e = events.HitEvents[i];
            contact |= IsBallWall(e.ShapeIdA, e.ShapeIdB);
        }
        return contact;
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
        filter.MaskBits = 1;
        float nearest = float.PositiveInfinity;
        float2 normal = default;
        bool meaningful = false, ambiguous = false;
        // Never throw through the native callback. Continue rather than clip so equal-distance
        // corner normals can be detected independent of broad-phase visitation order.
        B2CastResultFcn callback = (shape, point, n, fraction, context) =>
        {
            if (!IsWall(shape) || !float.IsFinite(fraction) || fraction < 0 || fraction > 1) return 1;
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
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("The native world must be disposed on its owner thread.");
        if (disposed) return;
        world.Dispose();
        disposed = true;
    }
}
