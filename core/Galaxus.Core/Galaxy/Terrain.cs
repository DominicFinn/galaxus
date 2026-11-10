using System.Numerics;

namespace Galaxus.Core.Galaxy;

/// <summary>What the ground is at a point, for colour and for where ports can go.</summary>
public enum Ground { DeepSea, Sea, Shore, Grass, Forest, Scrub, Sand, Rock, Snow, Ice, Lava, Dust, CraterFloor, Acid }

public readonly record struct TerrainSample(float HeightKm, Ground Ground, float R, float G, float B)
{
    public bool Wet => Ground is Ground.DeepSea or Ground.Sea or Ground.Lava or Ground.Acid;
}

/// <summary>Seeded 3D gradient noise (improved Perlin). Same seed, same values, on any machine.</summary>
public sealed class Noise3
{
    private readonly int[] _perm = new int[512];

    public Noise3(ulong seed)
    {
        var rng = new Rng(seed);
        var p = Enumerable.Range(0, 256).ToArray();
        for (int i = 255; i > 0; i--) { int j = rng.Next(i + 1); (p[i], p[j]) = (p[j], p[i]); }
        for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
    }

    /// <summary>Roughly -1 to 1.</summary>
    public float At(float x, float y, float z)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y), zi = (int)MathF.Floor(z);
        float xf = x - xi, yf = y - yi, zf = z - zi;
        int X = xi & 255, Y = yi & 255, Z = zi & 255;
        float u = Fade(xf), v = Fade(yf), w = Fade(zf);
        int a = _perm[X] + Y, aa = _perm[a] + Z, ab = _perm[a + 1] + Z;
        int b = _perm[X + 1] + Y, ba = _perm[b] + Z, bb = _perm[b + 1] + Z;
        return Lerp(w,
            Lerp(v, Lerp(u, Grad(_perm[aa], xf, yf, zf), Grad(_perm[ba], xf - 1, yf, zf)),
                    Lerp(u, Grad(_perm[ab], xf, yf - 1, zf), Grad(_perm[bb], xf - 1, yf - 1, zf))),
            Lerp(v, Lerp(u, Grad(_perm[aa + 1], xf, yf, zf - 1), Grad(_perm[ba + 1], xf - 1, yf, zf - 1)),
                    Lerp(u, Grad(_perm[ab + 1], xf, yf - 1, zf - 1), Grad(_perm[bb + 1], xf - 1, yf - 1, zf - 1))));
    }

    public float At(Vector3 p) => At(p.X, p.Y, p.Z);

    /// <summary>Layered noise, roughly -1 to 1.</summary>
    public float Fbm(Vector3 p, int octaves, float lacunarity = 2.03f, float gain = 0.5f)
    {
        float sum = 0, amp = 1, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += At(p) * amp;
            norm += amp;
            amp *= gain;
            p = p * lacunarity + new Vector3(17.3f, -9.1f, 4.7f);
        }
        return sum / norm;
    }

    /// <summary>Ridged noise for mountain chains: 0 in the valleys, 1 along the ridges.</summary>
    public float Ridged(Vector3 p, int octaves)
    {
        float sum = 0, amp = 1, norm = 0, weight = 1;
        for (int i = 0; i < octaves; i++)
        {
            float n = 1 - MathF.Abs(At(p));
            n *= n * weight;
            weight = Math.Clamp(n * 2, 0, 1);
            sum += n * amp;
            norm += amp;
            amp *= 0.5f;
            p = p * 2.1f + new Vector3(-3.3f, 8.9f, 1.1f);
        }
        return sum / norm;
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static float Lerp(float t, float a, float b) => a + t * (b - a);

    private static float Grad(int hash, float x, float y, float z)
    {
        int h = hash & 15;
        float u = h < 8 ? x : y, v = h < 4 ? y : h is 12 or 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
}

/// <summary>
/// The whole surface of a rocky world, worked out from its seed whenever it's needed: continents and
/// seas, mountain chains, dunes, craters, ice caps. Nothing is stored. Heights are in km above the
/// body's radius; sea level, where a world has seas, is 0.
/// </summary>
public sealed class PlanetTerrain
{
    public BodyKind Kind { get; }
    public double RadiusKm { get; }
    /// <summary>False for gas and ice giants, which have no surface to land on.</summary>
    public bool Solid { get; }
    public bool HasSea => Kind is BodyKind.Terran or BodyKind.Ocean or BodyKind.Molten or BodyKind.Toxic;
    /// <summary>The highest the ground can reach, for knowing when you're safely above it.</summary>
    public float MaxHeightKm { get; }

    private readonly Noise3 _noise;
    private readonly Noise3 _detail;
    private readonly float _seaBias;

    public PlanetTerrain(Body body) : this(body.Kind, body.Radius * SolarSystem.KmPerEarthRadius, body.Seed) { }

    public PlanetTerrain(BodyKind kind, double radiusKm, ulong seed)
    {
        Kind = kind;
        RadiusKm = radiusKm;
        Solid = kind is not (BodyKind.GasGiant or BodyKind.IceGiant or BodyKind.Belt);
        _noise = new Noise3(seed ^ 0x7E44A1);
        _detail = new Noise3(seed ^ 0x2D37C9);
        // How much of the world is under water: Earth-like worlds about two thirds, ocean worlds nearly all.
        _seaBias = kind switch { BodyKind.Ocean => -0.38f, BodyKind.Terran => -0.06f, BodyKind.Molten => -0.12f, BodyKind.Toxic => -0.2f, _ => 0 };
        MaxHeightKm = kind switch
        {
            BodyKind.Terran or BodyKind.Ocean => 9, BodyKind.Desert => 6, BodyKind.Barren => 7,
            BodyKind.Ice => 4, BodyKind.Molten => 5, BodyKind.Toxic => 5, _ => 0,
        };
    }

    /// <summary>Ground height in km at a direction from the centre (any length).</summary>
    public float HeightKm(Vector3 direction) => Solid ? Shape(Vector3.Normalize(direction), out _, out _) : 0;

    public TerrainSample Sample(Vector3 direction)
    {
        var dir = Vector3.Normalize(direction);
        if (!Solid) return new TerrainSample(0, Ground.Rock, 0.5f, 0.5f, 0.5f);
        float height = Shape(dir, out float ridge, out float moisture);
        var ground = GroundAt(dir, height, ridge, moisture);
        var (r, g, b) = Colour(ground, height, moisture);
        // A little fine variation so large areas don't look flat.
        float grain = 1 + _detail.At(dir * 140) * 0.06f;
        return new TerrainSample(height, ground, r * grain, g * grain, b * grain);
    }

    private float Shape(Vector3 dir, out float ridge, out float moisture)
    {
        moisture = _detail.Fbm(dir * 2.2f + new Vector3(40, 0, 0), 3) * 0.5f + 0.5f;
        float continents = _noise.Fbm(dir * 1.7f, 5) + _seaBias;
        float land = Smooth(-0.02f, 0.12f, continents);
        float hills = _noise.Fbm(dir * 9f + new Vector3(5, 5, 5), 5);
        float fine = _detail.Fbm(dir * 60f, 4);
        ridge = _noise.Ridged(dir * 4.2f + new Vector3(-7, 3, 2), 6);

        switch (Kind)
        {
            case BodyKind.Terran:
            case BodyKind.Ocean:
            {
                float mountains = ridge * ridge * land * Smooth(0.05f, 0.35f, continents + hills * 0.15f);
                return continents * 4.5f + mountains * 8f + hills * 0.6f * land + fine * 0.15f;
            }
            case BodyKind.Desert:
            {
                // Plateaus stepped into mesas, wide basins, and dunes.
                float plateau = continents * 3f + hills * 0.8f;
                float stepped = MathF.Floor(plateau * 2.5f) / 2.5f;
                float mesa = plateau + (stepped - plateau) * Smooth(0.2f, 0.6f, hills + 0.4f);
                float dunes = MathF.Sin(Vector3.Dot(dir, new Vector3(0.3f, 0.2f, 0.93f)) * 900 + fine * 6) * 0.04f;
                return mesa + ridge * ridge * 3.5f + dunes;
            }
            case BodyKind.Barren:
            {
                float maria = Smooth(-0.1f, 0.15f, continents) * 1.5f;
                return maria + hills * 0.8f + ridge * 1.6f + Craters(dir, 3) + fine * 0.1f;
            }
            case BodyKind.Ice:
            {
                float cracks = MathF.Pow(1 - MathF.Abs(_detail.At(dir * 18)), 12) * 0.6f;
                return continents * 1.2f + hills * 0.4f + cracks + Craters(dir, 2) * 0.5f;
            }
            case BodyKind.Molten:
                return continents * 3f + ridge * 2.5f + hills * 0.6f + fine * 0.15f;
            default: // Toxic
                return continents * 3f + hills * 0.9f + ridge * 1.5f + fine * 0.1f;
        }
    }

    /// <summary>Craters at several sizes: a bowl, a raised rim, and flat ground beyond.</summary>
    private float Craters(Vector3 dir, int scales)
    {
        float total = 0;
        for (int s = 0; s < scales; s++)
        {
            float freq = 6f * MathF.Pow(3.2f, s);
            float depth = 2.4f / MathF.Pow(2.2f, s);
            var p = dir * freq;
            int cx = (int)MathF.Floor(p.X), cy = (int)MathF.Floor(p.Y), cz = (int)MathF.Floor(p.Z);
            for (int x = cx - 1; x <= cx + 1; x++)
            for (int y = cy - 1; y <= cy + 1; y++)
            for (int z = cz - 1; z <= cz + 1; z++)
            {
                uint h = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791 ^ s * 2654435761);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                if ((h & 3) != 0) continue; // only some cells have a crater
                var centre = new Vector3(x + (h >> 4 & 255) / 255f, y + (h >> 12 & 255) / 255f, z + (h >> 20 & 255) / 255f);
                float radius = 0.25f + (h >> 28) / 15f * 0.35f;
                float d = Vector3.Distance(p, centre) / radius;
                if (d >= 1.5f) continue;
                total += d < 1 ? (d * d - 1) * depth : (1 - (d - 1) / 0.5f) * (1 - (d - 1) / 0.5f) * depth * 0.35f;
            }
        }
        return total;
    }

    private Ground GroundAt(Vector3 dir, float height, float ridge, float moisture)
    {
        float lat = MathF.Abs(dir.Y);
        switch (Kind)
        {
            case BodyKind.Terran:
            case BodyKind.Ocean:
                if (height < -1.2f) return lat > 0.88f ? Ground.Ice : Ground.DeepSea;
                if (height < 0) return lat > 0.85f ? Ground.Ice : Ground.Sea;
                if (lat > 0.82f - height * 0.02f || height > 5.5f - lat * 3) return Ground.Snow;
                if (height < 0.12f) return Ground.Shore;
                if (ridge > 0.55f && height > 2.2f) return Ground.Rock;
                // Dry belts either side of the equator, wet tropics and temperate zones.
                float dryness = (1 - moisture) + (MathF.Abs(lat - 0.3f) < 0.12f ? 0.25f : 0);
                return dryness > 0.78f ? Ground.Sand : dryness > 0.58f ? Ground.Scrub : moisture > 0.55f ? Ground.Forest : Ground.Grass;
            case BodyKind.Desert:
                return ridge > 0.6f ? Ground.Rock : lat > 0.93f ? Ground.Ice : Ground.Sand;
            case BodyKind.Ice:
                return ridge > 0.7f ? Ground.Rock : Ground.Ice;
            case BodyKind.Molten:
                return height < 0 ? Ground.Lava : Ground.Rock;
            case BodyKind.Toxic:
                return height < 0 ? Ground.Acid : ridge > 0.6f ? Ground.Rock : Ground.Dust;
            default:
                return height < -0.6f ? Ground.CraterFloor : Ground.Dust;
        }
    }

    private (float, float, float) Colour(Ground ground, float height, float moisture) => ground switch
    {
        Ground.DeepSea => Mix((0.04f, 0.13f, 0.32f), (0.07f, 0.2f, 0.42f), Math.Clamp(1 + height / 4, 0, 1)),
        Ground.Sea => Mix((0.07f, 0.2f, 0.42f), (0.13f, 0.38f, 0.55f), Math.Clamp(1 + height / 1.2f, 0, 1)),
        Ground.Shore => (0.76f, 0.7f, 0.5f),
        Ground.Grass => Mix((0.3f, 0.5f, 0.2f), (0.42f, 0.52f, 0.26f), Math.Clamp(height / 2, 0, 1)),
        Ground.Forest => Mix((0.13f, 0.32f, 0.14f), (0.2f, 0.36f, 0.18f), moisture),
        Ground.Scrub => (0.52f, 0.5f, 0.3f),
        Ground.Sand => Kind == BodyKind.Desert
            ? Mix((0.8f, 0.6f, 0.38f), (0.6f, 0.4f, 0.25f), Math.Clamp(height / 4, 0, 1))
            : (0.78f, 0.68f, 0.45f),
        Ground.Rock => Kind switch
        {
            BodyKind.Molten => (0.18f, 0.13f, 0.12f),
            BodyKind.Desert => (0.5f, 0.34f, 0.24f),
            BodyKind.Toxic => (0.4f, 0.4f, 0.24f),
            _ => Mix((0.42f, 0.38f, 0.34f), (0.55f, 0.53f, 0.5f), Math.Clamp(height / 6, 0, 1)),
        },
        Ground.Snow => (0.93f, 0.95f, 0.98f),
        Ground.Ice => Kind == BodyKind.Ice ? Mix((0.66f, 0.77f, 0.87f), (0.86f, 0.9f, 0.95f), Math.Clamp(height / 2 + 0.5f, 0, 1)) : (0.88f, 0.92f, 0.96f),
        Ground.Lava => Mix((1f, 0.3f, 0.06f), (1f, 0.58f, 0.16f), moisture),
        Ground.Acid => (0.48f, 0.56f, 0.16f),
        Ground.CraterFloor => (0.32f, 0.31f, 0.3f),
        _ => Kind == BodyKind.Toxic
            ? Mix((0.62f, 0.6f, 0.26f), (0.74f, 0.68f, 0.36f), moisture)
            : Mix((0.44f, 0.42f, 0.4f), (0.6f, 0.58f, 0.55f), Math.Clamp(height / 4 + 0.5f, 0, 1)),
    };

    private static (float, float, float) Mix((float R, float G, float B) a, (float R, float G, float B) b, float t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    private static float Smooth(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// A flat, dry spot for a surface port, searched for from the seed: above sea level, not on a
    /// mountain, and not at the poles. Falls back to the best spot found.
    /// </summary>
    public Vector3 FindPortSite(Rng rng)
    {
        Vector3 best = Vector3.UnitX;
        float bestScore = float.MinValue;
        for (int i = 0; i < 300; i++)
        {
            var dir = Vector3.Normalize(new Vector3(rng.Range(-1, 1), rng.Range(-0.7f, 0.7f), rng.Range(-1, 1)));
            if (float.IsNaN(dir.X)) continue;
            var sample = Sample(dir);
            if (sample.Wet) continue;
            // Flatness: compare with neighbours about 20 km away.
            float step = (float)(20 / RadiusKm);
            float slope = MathF.Abs(HeightKm(dir + new Vector3(step, 0, 0)) - sample.HeightKm)
                        + MathF.Abs(HeightKm(dir + new Vector3(0, 0, step)) - sample.HeightKm);
            float score = -slope * 4 - MathF.Abs(sample.HeightKm - 0.6f) * 0.3f - MathF.Abs(dir.Y);
            if (sample.Ground is Ground.Snow or Ground.Ice) score -= 2;
            if (score > bestScore) { bestScore = score; best = dir; }
        }
        return best;
    }
}
