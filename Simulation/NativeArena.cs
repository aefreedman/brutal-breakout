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

    internal NativeArena()
    {
        var definition = B2WorldDef.Default;
        definition.Gravity = default;
        definition.EnableSleep = false;
        definition.EnableContinuous = true;
        definition.WorkerCount = 0;
        definition.RestitutionThreshold = 0;
        definition.HitEventThreshold = 0;
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
            Wall(0, .38f, 3.60f, .10f, 3.22f);
            Wall(1, 12.42f, 3.60f, .10f, 3.22f);
            Wall(2, 6.40f, .38f, 6.12f, .10f);
            Wall(3, 6.40f, 6.82f, 6.12f, .10f);
            var bd = B2BodyDef.Default;
            bd.Type = B2BodyType.Dynamic;
            bd.Position = new(6.4f, 6.12f);
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
            var circle = new B2Circle { Radius = .12f };
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
        ball.SetLinearVelocity(new((float)(direction.X * 6), (float)(direction.Y * 6)));
    }
    internal void Reset()
    {
        Freeze();
        ball.SetTransform(new(6.4f, 6.12f), B2Rot.Identity);
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
        world.Step(1f / 120f, 4);
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
        if (!Arena.Finite(origin) || origin.X < 0 || origin.X > Arena.Width || origin.Y < 0 || origin.Y > Arena.Height)
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (!Arena.Finite(direction) || (direction.X == 0 && direction.Y == 0))
            throw new ArgumentOutOfRangeException(nameof(direction));
        var unit = Arena.Normalize(direction);
        // Longer than the arena diagonal, finite even for extreme input direction magnitudes.
        const float castMeters = 16;
        float2 translation = new((float)unit.X * castMeters, (float)unit.Y * castMeters);
        var proxy = new B2ShapeProxy { Count = 1, Radius = .12f };
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
        return new(double2.Distance(default, displacement), origin + displacement, new(reflected.X, reflected.Y));
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
