using Godot;
using Galaxus.Core;
using Galaxus.Core.Galaxy;

namespace Galaxus;

public enum PlanetKind { Terran, Ocean, Desert, Ice, Lava, Barren, Toxic, GasGiant, IceGiant }

public enum StarKind { Yellow, Orange, Red, White, Blue }

/// <summary>
/// Builds the bodies you see in a system: its star, its main planet (with atmosphere and rings
/// where it has them) and a moon or two. Everything comes from the system seed, so a system
/// always looks the same, and settled systems lean towards habitable worlds.
/// </summary>
public static class PlanetFactory
{
    public static StarKind PickStar(Rng rng) => rng.NextFloat() switch
    {
        < 0.35f => StarKind.Yellow,
        < 0.6f => StarKind.Orange,
        < 0.8f => StarKind.Red,
        < 0.93f => StarKind.White,
        _ => StarKind.Blue,
    };

    public static (Color Disc, Color Light, float Energy, float Size) StarLook(StarKind kind) => kind switch
    {
        StarKind.Yellow => (new Color(1f, 0.93f, 0.75f), new Color(1f, 0.95f, 0.85f), 1.3f, 3000),
        StarKind.Orange => (new Color(1f, 0.72f, 0.42f), new Color(1f, 0.82f, 0.62f), 1.15f, 2600),
        StarKind.Red => (new Color(1f, 0.45f, 0.3f), new Color(1f, 0.62f, 0.5f), 0.95f, 2200),
        StarKind.White => (new Color(0.95f, 0.97f, 1f), new Color(0.95f, 0.97f, 1f), 1.45f, 2800),
        _ => (new Color(0.7f, 0.82f, 1f), new Color(0.78f, 0.86f, 1f), 1.6f, 3600),
    };

    public static PlanetKind PickPlanet(StarSystem system, Rng rng)
    {
        float roll = rng.NextFloat();
        if (system.Settled && roll < 0.7f)
        {
            // People settle where they can live, or at least breathe through a mask.
            return rng.NextFloat() switch
            {
                < 0.35f => PlanetKind.Terran,
                < 0.55f => PlanetKind.Ocean,
                < 0.75f => PlanetKind.Desert,
                < 0.9f => PlanetKind.Ice,
                _ => PlanetKind.Barren,
            };
        }
        bool far = system.Region is "outer" or "rim";
        return rng.NextFloat() switch
        {
            < 0.25f => PlanetKind.GasGiant,
            < 0.37f => PlanetKind.IceGiant,
            < 0.55f => PlanetKind.Barren,
            < 0.67f => far ? PlanetKind.Ice : PlanetKind.Desert,
            < 0.77f => PlanetKind.Lava,
            < 0.87f => PlanetKind.Toxic,
            < 0.94f => PlanetKind.Ice,
            _ => PlanetKind.Desert,
        };
    }

    /// <summary>The planet node: surface, optional atmosphere shell, optional rings, and moons.</summary>
    public static Node3D BuildPlanet(PlanetKind kind, Rng rng)
    {
        bool giant = kind is PlanetKind.GasGiant or PlanetKind.IceGiant;
        float radius = giant ? rng.Range(18000, 26000) : rng.Range(7000, 13000);
        int seed = (int)(rng.NextULong() & 0x7fffffff);

        var root = new Node3D();
        root.AddChild(new MeshInstance3D { Mesh = MeshFactory.Icosphere(radius, giant ? 4 : 3, dir => SurfaceColour(kind, dir, seed)) });

        if (Atmosphere(kind) is { } air)
            root.AddChild(new MeshInstance3D { Mesh = AtmosphereMesh(radius * (giant ? 1.03f : 1.06f), air) });

        if (giant && rng.NextFloat() < 0.55f)
        {
            var ring = new MeshInstance3D
            {
                Mesh = RingMesh(radius * 1.35f, radius * rng.Range(2f, 2.6f), RingColour(kind, rng), rng.Fork("ring")),
                RotationDegrees = new Vector3(rng.Range(-25, 25), 0, rng.Range(-30, 30)),
            };
            root.AddChild(ring);
        }

        // A moon or two, airless and cratered, close enough to frame the planet.
        int moons = giant ? 1 + rng.Next(2) : rng.Next(2);
        for (int i = 0; i < moons; i++)
        {
            var mrng = rng.Fork($"moon-{i}");
            float moonRadius = radius * mrng.Range(0.08f, 0.2f);
            int moonSeed = (int)(mrng.NextULong() & 0x7fffffff);
            var moonKind = mrng.NextFloat() < 0.75f ? PlanetKind.Barren : PlanetKind.Ice;
            var dir = new Vector3(mrng.Range(-1, 1), mrng.Range(-0.3f, 0.3f), mrng.Range(-1, 1)).Normalized();
            root.AddChild(new MeshInstance3D
            {
                Mesh = MeshFactory.Icosphere(moonRadius, 2, d => SurfaceColour(moonKind, d, moonSeed)),
                Position = dir * radius * mrng.Range(2.4f, 3.6f),
            });
        }
        return root;
    }

    private static Color SurfaceColour(PlanetKind kind, Vector3 dir, int seed)
    {
        float n = Noise.Fbm3D(dir * 2.4f + new Vector3(11, 7, 3), seed);
        float fine = Noise.Fbm3D(dir * 7f + new Vector3(3, 9, 1), seed + 40);
        float lat = Mathf.Abs(dir.Y);

        switch (kind)
        {
            case PlanetKind.Terran:
                if (lat > 0.84f + (n - 0.5f) * 0.25f) return new Color(0.92f, 0.94f, 0.98f);
                if (n < 0.5f) return new Color(0.1f, 0.24f, 0.45f).Lerp(new Color(0.16f, 0.36f, 0.56f), n * 2);
                if (n < 0.58f) return new Color(0.3f, 0.46f, 0.22f);
                if (n < 0.66f) return new Color(0.42f, 0.42f, 0.26f);
                return new Color(0.5f, 0.42f, 0.32f);

            case PlanetKind.Ocean:
                if (lat > 0.9f) return new Color(0.9f, 0.94f, 0.98f);
                if (n > 0.64f) return new Color(0.36f, 0.5f, 0.28f);
                return new Color(0.06f, 0.2f, 0.42f).Lerp(new Color(0.12f, 0.34f, 0.55f), fine);

            case PlanetKind.Desert:
            {
                float dunes = Mathf.Sin(dir.Y * 18f + n * 9f) * 0.5f + 0.5f;
                var sand = new Color(0.78f, 0.6f, 0.38f).Lerp(new Color(0.62f, 0.42f, 0.26f), dunes * 0.6f + fine * 0.4f);
                return n > 0.66f ? new Color(0.48f, 0.34f, 0.24f) : sand;
            }

            case PlanetKind.Ice:
            {
                bool crack = Mathf.Abs(fine - 0.5f) < 0.025f;
                if (crack) return new Color(0.45f, 0.62f, 0.75f);
                return new Color(0.82f, 0.88f, 0.94f).Lerp(new Color(0.66f, 0.78f, 0.88f), n);
            }

            case PlanetKind.Lava:
            {
                bool crack = Mathf.Abs(fine - 0.5f) < 0.04f || n > 0.7f;
                return crack ? new Color(1f, 0.42f, 0.1f) : new Color(0.16f, 0.12f, 0.11f).Lerp(new Color(0.28f, 0.2f, 0.17f), n);
            }

            case PlanetKind.Toxic:
            {
                float swirl = Mathf.Sin(dir.Y * 10f + n * 7f) * 0.5f + 0.5f;
                return new Color(0.62f, 0.62f, 0.22f).Lerp(new Color(0.42f, 0.5f, 0.2f), swirl).Lerp(new Color(0.76f, 0.7f, 0.36f), fine * 0.4f);
            }

            case PlanetKind.GasGiant:
            {
                float bands = Mathf.Sin(dir.Y * 22f + n * 6f + fine * 2f) * 0.5f + 0.5f;
                var c = new Color(0.82f, 0.68f, 0.5f).Lerp(new Color(0.6f, 0.42f, 0.3f), bands);
                // A great storm somewhere south of the equator.
                float storm = (dir - new Vector3(0.6f, -0.3f, 0.74f).Normalized()).Length();
                if (storm < 0.16f) c = new Color(0.78f, 0.4f, 0.28f).Lerp(c, storm / 0.16f);
                return c.Lerp(new Color(0.92f, 0.86f, 0.74f), Mathf.Max(0, lat - 0.85f) * 3);
            }

            case PlanetKind.IceGiant:
            {
                float bands = Mathf.Sin(dir.Y * 14f + n * 4f) * 0.5f + 0.5f;
                return new Color(0.5f, 0.74f, 0.82f).Lerp(new Color(0.34f, 0.56f, 0.72f), bands * 0.7f + fine * 0.3f);
            }

            default: // Barren: grey rock with craters
            {
                var rock = new Color(0.42f, 0.4f, 0.39f).Lerp(new Color(0.58f, 0.56f, 0.53f), n);
                float crater = Crater(dir, seed);
                if (crater < 0) return rock * 0.62f;              // crater floor
                if (crater > 0) return rock.Lerp(Colors.White, 0.18f); // crater rim
                return rock;
            }
        }
    }

    /// <summary>-1 inside a crater, 1 on its rim, 0 elsewhere. Craters are placed from the seed.</summary>
    private static float Crater(Vector3 dir, int seed)
    {
        var rng = new Rng((ulong)seed);
        for (int i = 0; i < 18; i++)
        {
            var centre = new Vector3(rng.Range(-1, 1), rng.Range(-1, 1), rng.Range(-1, 1)).Normalized();
            float size = rng.Range(0.06f, 0.28f);
            float d = dir.DistanceTo(centre);
            if (d < size * 0.8f) return -1;
            if (d < size) return 1;
        }
        return 0;
    }

    private static Color? Atmosphere(PlanetKind kind) => kind switch
    {
        PlanetKind.Terran or PlanetKind.Ocean => new Color(0.45f, 0.65f, 1f, 0.28f),
        PlanetKind.Desert => new Color(0.95f, 0.7f, 0.45f, 0.18f),
        PlanetKind.Toxic => new Color(0.85f, 0.85f, 0.3f, 0.35f),
        PlanetKind.Ice => new Color(0.7f, 0.9f, 1f, 0.12f),
        PlanetKind.Lava => new Color(1f, 0.4f, 0.2f, 0.12f),
        PlanetKind.GasGiant => new Color(0.95f, 0.85f, 0.7f, 0.12f),
        PlanetKind.IceGiant => new Color(0.6f, 0.85f, 1f, 0.15f),
        _ => null,
    };

    /// <summary>
    /// A slightly larger shell drawn from the inside only, so it shows as a glowing rim around
    /// the planet's edge and a faint tint across its face.
    /// </summary>
    private static ArrayMesh AtmosphereMesh(float radius, Color colour)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = colour,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Front,
        };
        return MeshFactory.Icosphere(radius, 3, _ => Colors.White, material);
    }

    private static (Color Inner, Color Outer) RingColour(PlanetKind kind, Rng rng) => kind == PlanetKind.IceGiant
        ? (new Color(0.7f, 0.8f, 0.85f), new Color(0.5f, 0.6f, 0.7f))
        : (new Color(0.82f, 0.74f, 0.6f), new Color(0.6f, 0.5f, 0.42f).Lerp(new Color(0.75f, 0.7f, 0.65f), rng.NextFloat()));

    /// <summary>Flat banded ring in the XZ plane, with gaps. Lit, double-sided and slightly transparent.</summary>
    private static ArrayMesh RingMesh(float inner, float outer, (Color Inner, Color Outer) colours, Rng rng)
    {
        const int segments = 96, bands = 14;
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var cols = new List<Color>();
        var bandAlpha = Enumerable.Range(0, bands).Select(_ => rng.NextFloat() < 0.18f ? 0f : rng.Range(0.35f, 0.85f)).ToArray();

        for (int b = 0; b < bands; b++)
        {
            if (bandAlpha[b] <= 0) continue;
            float r0 = Mathf.Lerp(inner, outer, b / (float)bands), r1 = Mathf.Lerp(inner, outer, (b + 1) / (float)bands);
            var c = colours.Inner.Lerp(colours.Outer, b / (float)bands);
            c.A = bandAlpha[b];
            for (int s = 0; s < segments; s++)
            {
                float a0 = s * Mathf.Tau / segments, a1 = (s + 1) * Mathf.Tau / segments;
                var p00 = new Vector3(Mathf.Cos(a0) * r0, 0, Mathf.Sin(a0) * r0);
                var p01 = new Vector3(Mathf.Cos(a1) * r0, 0, Mathf.Sin(a1) * r0);
                var p10 = new Vector3(Mathf.Cos(a0) * r1, 0, Mathf.Sin(a0) * r1);
                var p11 = new Vector3(Mathf.Cos(a1) * r1, 0, Mathf.Sin(a1) * r1);
                foreach (var v in new[] { p00, p10, p11, p00, p11, p01 })
                {
                    verts.Add(v);
                    normals.Add(Vector3.Up);
                    cols.Add(c);
                }
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = cols.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 1,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        });
        return mesh;
    }
}
