using Brutal.Box2DApi;
using Brutal.Numerics;

namespace BrutalBreakout.Simulation;

/// <summary>Shared native closest-circlecast policy, including tied/invalid normal rejection.</summary>
internal static class NativeAimQuery
{
    internal static WallContact Cast(B2World world, in B2ShapeProxy proxy, float2 translation,
        B2QueryFilter filter, Func<B2Shape, bool> obstacle, double2 origin, double2 unit, out bool clear)
    {
        float nearest = float.PositiveInfinity;
        float2 normal = default;
        bool meaningful = false, ambiguous = false;
        // Continue rather than clip so equal-distance corner normals are visitation-order independent.
        B2CastResultFcn callback = (shape, point, n, fraction, context) =>
        {
            if (!obstacle(shape) || !float.IsFinite(fraction) || fraction < 0 || fraction > 1) return 1;
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
        clear = float.IsPositiveInfinity(nearest);
        if (!meaningful || ambiguous) return default;
        normal = float2.Normalize(normal);
        float2 reflected = float2.Reflect(new((float)unit.X, (float)unit.Y), normal);
        var displacement = new double2((double)translation.X * NativeArena.UnitsPerMeter,
            (double)translation.Y * NativeArena.UnitsPerMeter) * nearest;
        return new(double2.Distance(default, displacement), origin + displacement, new(reflected.X, reflected.Y))
        { Normal = new(normal.X, normal.Y) };
    }
}
