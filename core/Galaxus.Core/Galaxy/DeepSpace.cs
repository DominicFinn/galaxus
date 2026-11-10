using System.Numerics;

namespace Galaxus.Core.Galaxy;

/// <summary>A star in deep space, generated on demand. Not stored unless someone goes there.</summary>
public readonly record struct DeepStar(ulong Key, Vector3 PositionLy, ulong Seed);

/// <summary>
/// The rest of the galaxy: every star that isn't a gate system, about two billion of them,
/// generated on demand from the galaxy seed in columns ten light years square. Star density
/// follows the spiral arms and thins towards the rim, much as in a real galaxy. Only stars the
/// player visits get registered in the Galaxy (and saved); everything else is regenerated
/// whenever it's looked at.
/// </summary>
public static class DeepSpace
{
    /// <summary>Gate systems' map positions are in galaxy units; one unit is this many light years.</summary>
    public const float LyPerUnit = 40f;
    public const float ColumnLy = 10f;
    /// <summary>Half the thickness of the disc that the game models, in light years.</summary>
    public const float HalfThicknessLy = 60f;
    /// <summary>Stars per cubic light year in the densest parts of the arms.</summary>
    private const float PeakDensity = 0.01f;
    private const float RadiusLy = 1000f * LyPerUnit;

    /// <summary>Stars per cubic light year at a point on the disc: a bright core, four arms, a fading rim.</summary>
    public static float Density(float xLy, float yLy)
    {
        float r = MathF.Sqrt(xLy * xLy + yLy * yLy) / LyPerUnit; // galaxy units, 0 to ~1000
        if (r > 1080) return 0;
        float angle = MathF.Atan2(yLy, xLy);
        float arm = 0;
        for (int k = 0; k < 4; k++)
        {
            float armAngle = k * MathF.PI / 2 + MathF.Log(r / 60f + 1) * 1.6f;
            float gap = MathF.Abs(MathF.IEEERemainder(angle - armAngle, MathF.Tau));
            arm = MathF.Max(arm, MathF.Exp(-gap * gap / 0.08f));
        }
        float core = MathF.Exp(-r / 120f) * 2.5f;
        float disc = MathF.Exp(-r / 420f);
        float rimFade = r > 950 ? MathF.Max(0, 1 - (r - 950) / 130f) : 1;
        return PeakDensity * (0.25f * disc + 0.75f * disc * arm + core) * rimFade;
    }

    /// <summary>All the stars in one ten-light-year column of the disc. Same seed, same stars.</summary>
    public static List<DeepStar> Column(ulong galaxySeed, int cx, int cy)
    {
        float x0 = cx * ColumnLy, y0 = cy * ColumnLy;
        float expected = Density(x0 + ColumnLy / 2, y0 + ColumnLy / 2) * ColumnLy * ColumnLy * HalfThicknessLy * 2;
        var rng = new Rng(galaxySeed ^ Rng.Hash($"col:{cx}:{cy}"));
        // Round the expected count up or down at random, so sparse regions still get the odd star.
        int count = (int)expected + (rng.NextFloat() < expected - (int)expected ? 1 : 0);
        var stars = new List<DeepStar>(count);
        for (int i = 0; i < count && i < 255; i++)
        {
            var position = new Vector3(x0 + rng.NextFloat() * ColumnLy, y0 + rng.NextFloat() * ColumnLy,
                (rng.NextFloat() - 0.5f) * HalfThicknessLy * 2 * rng.NextFloat());
            stars.Add(new DeepStar(Key(cx, cy, i), position, rng.NextULong()));
        }
        return stars;
    }

    /// <summary>Stars within a radius of a point, nearest first.</summary>
    public static List<(DeepStar Star, float DistanceLy)> Near(ulong galaxySeed, Vector3 positionLy, float radiusLy)
    {
        var result = new List<(DeepStar, float)>();
        int minX = (int)MathF.Floor((positionLy.X - radiusLy) / ColumnLy), maxX = (int)MathF.Floor((positionLy.X + radiusLy) / ColumnLy);
        int minY = (int)MathF.Floor((positionLy.Y - radiusLy) / ColumnLy), maxY = (int)MathF.Floor((positionLy.Y + radiusLy) / ColumnLy);
        for (int cx = minX; cx <= maxX; cx++)
        for (int cy = minY; cy <= maxY; cy++)
            foreach (var star in Column(galaxySeed, cx, cy))
            {
                float d = Vector3.Distance(star.PositionLy, positionLy);
                if (d <= radiusLy && d > 0.01f) result.Add((star, d));
            }
        result.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return result;
    }

    /// <summary>
    /// Rough count of stars in the galaxy, by summing density over a coarse grid. About two
    /// billion with the default numbers.
    /// </summary>
    public static double EstimateStarCount()
    {
        double total = 0;
        const float step = 400f;
        for (float x = -RadiusLy; x <= RadiusLy; x += step)
        for (float y = -RadiusLy; y <= RadiusLy; y += step)
            total += Density(x, y) * step * step * HalfThicknessLy * 2;
        return total;
    }

    public static ulong Key(int cx, int cy, int index) =>
        ((ulong)(uint)(cx + (1 << 27)) << 36) | ((ulong)(uint)(cy + (1 << 27)) << 8) | (uint)index;

    public static (int Cx, int Cy, int Index) Unpack(ulong key) =>
        ((int)(key >> 36) - (1 << 27), (int)((key >> 8) & 0xFFFFFFF) - (1 << 27), (int)(key & 0xFF));

    public static DeepStar Find(ulong galaxySeed, ulong key)
    {
        var (cx, cy, index) = Unpack(key);
        var column = Column(galaxySeed, cx, cy);
        return index < column.Count ? column[index] : throw new KeyNotFoundException($"No deep star {key}.");
    }

    /// <summary>A catalogue name, from the column, like "Deep Sector JR-4471 c3".</summary>
    public static string Name(ulong key)
    {
        var (cx, cy, index) = Unpack(key);
        char a = (char)('A' + Math.Abs(cx) % 26), b = (char)('A' + Math.Abs(cy) % 26);
        int number = Math.Abs(cx * 73 + cy * 131) % 10000;
        return $"Sector {a}{b}-{number:D4} {(char)('a' + index % 26)}{index / 26 + 1}";
    }

    public static string Region(Vector3 positionLy)
    {
        float r = new Vector2(positionLy.X, positionLy.Y).Length() / LyPerUnit;
        return r < 180 ? "core" : r < 450 ? "inner" : r < 800 ? "outer" : "rim";
    }

    /// <summary>A gate system's position in light years: its map position scaled up, on the galactic plane.</summary>
    public static Vector3 PositionLy(StarSystem system) =>
        system.DeepKey != 0 ? system.PositionLyCache : new Vector3(system.Position.X * LyPerUnit, system.Position.Y * LyPerUnit, 0);
}
