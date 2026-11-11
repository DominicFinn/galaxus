using Galaxus.Core.Galaxy;

namespace Galaxus.Core.Mining;

/// <summary>One asteroid: where it sits, how big it is, and what it's made of.</summary>
/// <param name="Id">Stable across visits, so what you've mined and scanned stays done.</param>
/// <param name="Km">Position in km from the star.</param>
/// <param name="RadiusM">Size in metres.</param>
/// <param name="OreId">The commodity it yields.</param>
/// <param name="Tonnes">What it holds before anyone mines it.</param>
/// <param name="Richness">How fast it gives up its ore, about 0.5 to 1.6.</param>
public sealed record Rock(string Id, (double X, double Y, double Z) Km, float RadiusM, string OreId, int Tonnes, float Richness);

/// <summary>
/// Asteroid belts as places you can fly to and mine. A belt is a wide ring in the plane of the
/// system; anywhere in it, rocks drift in clumps worked out from the belt's seed and a 20 km grid,
/// so the same rocks are always in the same place. Warm belts are stony and metallic, cold belts
/// icy and carbon-rich, and a few rocks anywhere carry rarer metals.
/// </summary>
public static class Asteroids
{
    public const double CellKm = 20;
    /// <summary>How far above and below the belt's plane rocks are found.</summary>
    public const double ThicknessKm = 6;

    /// <summary>The belt's inner and outer edges, in km from the star.</summary>
    public static (double Inner, double Outer) Edges(Body belt) =>
        ((belt.Orbit - belt.Radius) * SolarSystem.KmPerAu, (belt.Orbit + belt.Radius) * SolarSystem.KmPerAu);

    /// <summary>The nearest point in the belt to where you are, in its plane.</summary>
    public static (double X, double Y, double Z) NearestKm(Body belt, (double X, double Y, double Z) from)
    {
        var (inner, outer) = Edges(belt);
        double r = Math.Sqrt(from.X * from.X + from.Z * from.Z);
        double target = Math.Clamp(r, inner + CellKm, outer - CellKm);
        if (r < 1) return (target, 0, 0);
        return (from.X / r * target, 0, from.Z / r * target);
    }

    /// <summary>Where to arrive in the belt: by the nearest clump of rocks to the nearest part of it.</summary>
    public static (double X, double Y, double Z) Landing(Body belt, (double X, double Y, double Z) from)
    {
        var point = NearestKm(belt, from);
        var rock = Near(belt, point, CellKm * 1.5).MinBy(r => Square(r.Km, point));
        return rock == null ? point : rock.Km;
    }

    private static double Square((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);

    public static bool Inside(Body belt, (double X, double Y, double Z) at)
    {
        var (inner, outer) = Edges(belt);
        double r = Math.Sqrt(at.X * at.X + at.Z * at.Z);
        return r >= inner && r <= outer && Math.Abs(at.Y) <= ThicknessKm * 2;
    }

    /// <summary>Rocks within a distance of a point, if the point is in the belt.</summary>
    public static List<Rock> Near(Body belt, (double X, double Y, double Z) at, double withinKm)
    {
        var rocks = new List<Rock>();
        if (belt.Kind != BodyKind.Belt || !Inside(belt, at)) return rocks;
        long cx0 = (long)Math.Floor((at.X - withinKm) / CellKm), cx1 = (long)Math.Floor((at.X + withinKm) / CellKm);
        long cz0 = (long)Math.Floor((at.Z - withinKm) / CellKm), cz1 = (long)Math.Floor((at.Z + withinKm) / CellKm);
        for (long cx = cx0; cx <= cx1; cx++)
            for (long cz = cz0; cz <= cz1; cz++)
                foreach (var rock in Cell(belt, cx, cz))
                {
                    double dx = rock.Km.X - at.X, dy = rock.Km.Y - at.Y, dz = rock.Km.Z - at.Z;
                    if (dx * dx + dy * dy + dz * dz <= withinKm * withinKm) rocks.Add(rock);
                }
        return rocks;
    }

    /// <summary>The rocks in one grid cell: a clump or two, or nothing at all.</summary>
    public static List<Rock> Cell(Body belt, long cx, long cz)
    {
        var rocks = new List<Rock>();
        var rng = new Rng(belt.Seed ^ Rng.Hash($"cell:{cx}:{cz}"));
        var (inner, outer) = Edges(belt);
        int clumps = rng.NextFloat() < 0.25f ? 0 : 1 + rng.Next(2);
        for (int c = 0; c < clumps; c++)
        {
            double centreX = (cx + rng.NextDouble()) * CellKm, centreZ = (cz + rng.NextDouble()) * CellKm;
            double r = Math.Sqrt(centreX * centreX + centreZ * centreZ);
            if (r < inner || r > outer) continue;
            double centreY = (rng.NextDouble() - 0.5) * ThicknessKm;
            int count = 5 + rng.Next(10);
            for (int i = 0; i < count; i++)
            {
                // Clumps a couple of km across, most rocks small and a few big.
                double spread = 1.6;
                var km = (centreX + (rng.NextDouble() - 0.5) * spread * 2, centreY + (rng.NextDouble() - 0.5) * spread, centreZ + (rng.NextDouble() - 0.5) * spread * 2);
                float size = 8 + 70 * MathF.Pow(rng.NextFloat(), 2.2f);
                string ore = Ore(belt, rng);
                int tonnes = (int)(size * size * 0.12f) + 10;
                float richness = rng.Range(0.5f, 1.6f);
                rocks.Add(new Rock($"{belt.Seed:x}:{cx}:{cz}:{c}:{i}", km, size, ore, tonnes, richness));
            }
        }
        return rocks;
    }

    /// <summary>The ore most rocks in this belt yield: what a mining job asks for.</summary>
    public static string MainOre(Body belt) => Warm(belt) ? "iron-ore" : "ice";

    private static bool Warm(Body belt) => belt.Temperature > 170;

    private static string Ore(Body belt, Rng rng)
    {
        float roll = rng.NextFloat();
        if (roll < 0.04f) return rng.Pick(Rare);
        return Warm(belt)
            ? roll < 0.5f ? "iron-ore" : roll < 0.68f ? "nickel-ore" : roll < 0.8f ? "silica-sand" : roll < 0.9f ? "copper-ore" : "titanium-ore"
            : roll < 0.5f ? "ice" : roll < 0.75f ? "carbonaceous-rock" : roll < 0.85f ? "iron-ore" : roll < 0.93f ? "nickel-ore" : "heavy-water";
    }

    private static readonly string[] Rare = ["rare-earth-ore", "tungsten-ore", "lithium-ore"];

    /// <summary>The belt in a system, if it has one.</summary>
    public static int BeltIndex(SolarSystem solar) => solar.Bodies.FindIndex(b => b.Kind == BodyKind.Belt);
}
