using System.Numerics;

namespace Galaxus.Core.Ships;

/// <summary>Solid shapes for things you can fly into: the station, and planets and stars as spheres.</summary>
public static class Collision
{
    /// <summary>
    /// Signed distance from a point (in the station's own space, metres) to the station's surface:
    /// negative inside. The station is a box hub along the Z axis and a ring 150 m out, matching
    /// MeshFactory.BuildStation. The spokes are thin enough to leave out.
    /// </summary>
    public static float StationDistance(Vector3 p, out Vector3 normal)
    {
        // Hub: a box from the docking bay at the front (-Z) to the cap at the back.
        var hubCentre = new Vector3(0, 0, 9.5f);
        var hubHalf = new Vector3(31, 31, 65.5f);
        float hub = BoxDistance(p - hubCentre, hubHalf, out var hubNormal);

        // Ring: a torus round the Z axis.
        const float ringRadius = 150f, tube = 16f;
        var flat = new Vector2(p.X, p.Y);
        float along = flat.Length();
        var radial = along > 1e-4f ? flat / along : Vector2.UnitX;
        var q = new Vector2(along - ringRadius, p.Z);
        float ringDist = q.Length() - tube;
        var qn = q.Length() > 1e-4f ? q / q.Length() : Vector2.UnitX;
        var ringNormal = new Vector3(radial.X * qn.X, radial.Y * qn.X, qn.Y);

        if (hub < ringDist) { normal = hubNormal; return hub; }
        normal = ringNormal;
        return ringDist;
    }

    private static float BoxDistance(Vector3 p, Vector3 half, out Vector3 normal)
    {
        var d = Vector3.Abs(p) - half;
        var outside = Vector3.Max(d, Vector3.Zero);
        float inside = MathF.Min(MathF.Max(d.X, MathF.Max(d.Y, d.Z)), 0);
        if (outside.LengthSquared() > 0)
        {
            var n = new Vector3(MathF.Sign(p.X) * outside.X, MathF.Sign(p.Y) * outside.Y, MathF.Sign(p.Z) * outside.Z);
            normal = Vector3.Normalize(n);
        }
        else
        {
            // Inside: push out through the nearest face.
            normal = d.X >= d.Y && d.X >= d.Z ? new Vector3(MathF.Sign(p.X) is 0 ? 1 : MathF.Sign(p.X), 0, 0)
                : d.Y >= d.Z ? new Vector3(0, MathF.Sign(p.Y) is 0 ? 1 : MathF.Sign(p.Y), 0)
                : new Vector3(0, 0, MathF.Sign(p.Z) is 0 ? 1 : MathF.Sign(p.Z));
        }
        return outside.Length() + inside;
    }

    /// <summary>
    /// Where along a straight move (0 at the start, 1 at the end) you first come within a radius of a
    /// point, or null if you don't. Moving away from something you're already inside doesn't count, so
    /// you can always leave. Works in doubles for solar-system distances.
    /// </summary>
    public static double? FirstContact((double X, double Y, double Z) start, (double X, double Y, double Z) end,
        (double X, double Y, double Z) centre, double radius)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
        double fx = start.X - centre.X, fy = start.Y - centre.Y, fz = start.Z - centre.Z;
        double a = dx * dx + dy * dy + dz * dz;
        double b = 2 * (fx * dx + fy * dy + fz * dz);
        double c = fx * fx + fy * fy + fz * fz - radius * radius;
        if (c <= 0) return b < 0 ? 0 : null;
        if (a < 1e-18) return null;
        double disc = b * b - 4 * a * c;
        if (disc < 0) return null;
        double t = (-b - Math.Sqrt(disc)) / (2 * a);
        return t is >= 0 and <= 1 ? t : null;
    }

    /// <summary>Hull damage from hitting something at a closing speed in m/s. Gentle bumps are free.</summary>
    public static float ImpactDamage(float closingSpeed) => closingSpeed <= 12 ? 0 : (closingSpeed - 12) * 1.6f;
}
