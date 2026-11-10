using Godot;
using Galaxus.Core;

namespace Galaxus;

/// <summary>Cheap managed value noise. Used to paint the sky and planets once at startup.</summary>
public static class Noise
{
    private static float Hash(int x, int y, int z, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + z * 2147483647 + seed * 144665);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    /// <summary>2D value noise that wraps every <paramref name="period"/> cells in X, so the sky has no seam.</summary>
    public static float Value2D(float x, float y, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = Smooth(x - x0), fy = Smooth(y - y0);
        int xa = Mod(x0, period), xb = Mod(x0 + 1, period);
        float a = Mathf.Lerp(Hash(xa, y0, 0, seed), Hash(xb, y0, 0, seed), fx);
        float b = Mathf.Lerp(Hash(xa, y0 + 1, 0, seed), Hash(xb, y0 + 1, 0, seed), fx);
        return Mathf.Lerp(a, b, fy);
    }

    public static float Value3D(Vector3 p, int seed)
    {
        int x0 = Mathf.FloorToInt(p.X), y0 = Mathf.FloorToInt(p.Y), z0 = Mathf.FloorToInt(p.Z);
        float fx = Smooth(p.X - x0), fy = Smooth(p.Y - y0), fz = Smooth(p.Z - z0);
        float Layer(int z)
        {
            float a = Mathf.Lerp(Hash(x0, y0, z, seed), Hash(x0 + 1, y0, z, seed), fx);
            float b = Mathf.Lerp(Hash(x0, y0 + 1, z, seed), Hash(x0 + 1, y0 + 1, z, seed), fx);
            return Mathf.Lerp(a, b, fy);
        }
        return Mathf.Lerp(Layer(z0), Layer(z0 + 1), fz);
    }

    public static float Fbm2D(float x, float y, int period, int seed, int octaves = 5)
    {
        float sum = 0, amplitude = 0.5f, total = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += Value2D(x, y, period, seed + i) * amplitude;
            total += amplitude;
            x *= 2; y *= 2; period *= 2; amplitude *= 0.5f;
        }
        return sum / total;
    }

    public static float Fbm3D(Vector3 p, int seed, int octaves = 4)
    {
        float sum = 0, amplitude = 0.5f, total = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += Value3D(p, seed + i) * amplitude;
            total += amplitude;
            p *= 2; amplitude *= 0.5f;
        }
        return sum / total;
    }

    private static int Mod(int a, int m) => ((a % m) + m) % m;
}

/// <summary>The backdrop: a painted starfield sky, a sun, a planet, and dust motes that show your speed.</summary>
public partial class Space : Node3D
{
    private const int DustCount = 360;
    private const float DustBox = 160f;

    private MultiMeshInstance3D _dust = null!;
    private readonly Vector3[] _dustPositions = new Vector3[DustCount];

    public Vector3 SunDirection { get; private set; } = new Vector3(0.6f, 0.25f, -0.75f).Normalized();

    public PlanetKind Planet { get; private set; }
    public StarKind Star { get; private set; }

    /// <param name="system">The system to show. Null gives a random backdrop from the seed (used by the menu).</param>
    public void Build(ulong seed, Galaxus.Core.Galaxy.StarSystem? system = null)
    {
        var rng = new Rng(seed);

        var sky = new Sky { SkyMaterial = new PanoramaSkyMaterial { Panorama = PaintSky(rng.Fork("sky")), Filter = false } };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.35f, 0.4f, 0.55f),
            AmbientLightEnergy = 0.35f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        // The star: its colour tints the light on everything in the system.
        var bodies = rng.Fork("bodies");
        Star = PlanetFactory.PickStar(bodies.Fork("star"));
        var (disc, light, energy, size) = PlanetFactory.StarLook(Star);
        var sunDir = new Vector3(bodies.Range(-1, 1), bodies.Range(-0.2f, 0.45f), bodies.Range(-1, -0.2f)).Normalized();
        SunDirection = sunDir;
        AddChild(new DirectionalLight3D
        {
            Transform = Transform3D.Identity.LookingAt(-SunDirection, Vector3.Up),
            LightColor = light,
            LightEnergy = energy,
        });
        AddChild(new MeshInstance3D
        {
            Mesh = MeshFactory.Icosphere(size, 2, _ => disc, MeshFactory.Unshaded(disc)),
            Position = SunDirection * 90000,
        });

        // The main planet, off to one side so the station and the planet frame each other.
        var prng = bodies.Fork("planet");
        Planet = system != null ? PlanetFactory.PickPlanet(system, prng) : (PlanetKind)prng.Next(9);
        var planet = PlanetFactory.BuildPlanet(Planet, prng.Fork("build"));
        float angle = prng.Range(0.5f, 2.6f) * (prng.NextFloat() < 0.5f ? -1 : 1);
        float distance = prng.Range(42000, 60000);
        planet.Position = new Vector3(Mathf.Sin(angle) * distance, prng.Range(-12000, 9000), -Mathf.Cos(angle) * distance);
        planet.RotationDegrees = new Vector3(prng.Range(-20, 20), prng.Range(0, 360), prng.Range(-15, 15));
        AddChild(planet);

        BuildDust(rng.Fork("dust"));
    }

    /// <summary>Paints an equirectangular sky: a nebula band along the galactic plane, a bright core, and pixel stars.</summary>
    private static ImageTexture PaintSky(Rng rng)
    {
        // Stars are drawn at full resolution so they come out about a pixel wide on screen.
        // The nebula is painted at half resolution and doubled up, which suits the pixel look.
        const int width = 2048, height = 1024, cloudScale = 2;
        var data = new byte[width * height * 3];
        int seed = (int)(rng.NextULong() & 0x7fffffff);
        var violet = new Color(0.22f, 0.07f, 0.3f);
        var teal = new Color(0.04f, 0.2f, 0.26f);
        var coreGlow = new Color(0.9f, 0.7f, 0.45f);

        for (int cy = 0; cy < height / cloudScale; cy++)
        {
            float v = cy * cloudScale / (float)height;
            for (int cx = 0; cx < width / cloudScale; cx++)
            {
                float u = cx * cloudScale / (float)width;
                float wobble = (Noise.Fbm2D(u * 6, 3.3f, 6, seed + 50, 3) - 0.5f) * 0.12f;
                float band = Mathf.Exp(-Mathf.Pow((v - 0.5f - wobble) / 0.11f, 2));
                float cloud = Noise.Fbm2D(u * 16, v * 8, 16, seed);
                float tint = Noise.Fbm2D(u * 8, v * 4, 8, seed + 20);
                float density = Mathf.Clamp((cloud - 0.38f) * 2.2f, 0, 1) * band;

                // The galactic core, the bright bulge everyone flies away from to find the rim.
                float du = Mathf.Min(Mathf.Abs(u - 0.25f), 1 - Mathf.Abs(u - 0.25f));
                float core = Mathf.Exp(-(du * du / 0.004f + (v - 0.5f) * (v - 0.5f) / 0.0025f));

                var colour = violet.Lerp(teal, tint) * density * 0.9f + coreGlow * core * 0.55f;
                byte r = ToByte(colour.R), g = ToByte(colour.G), b = ToByte(colour.B);
                for (int oy = 0; oy < cloudScale; oy++)
                for (int ox = 0; ox < cloudScale; ox++)
                {
                    int i = ((cy * cloudScale + oy) * width + cx * cloudScale + ox) * 3;
                    data[i] = r; data[i + 1] = g; data[i + 2] = b;
                }
            }
        }

        // Stars: a scatter everywhere and many more along the band.
        for (int s = 0; s < 7000; s++)
        {
            int x = rng.Next(width);
            float v = s % 3 == 0 ? Mathf.Clamp(0.5f + (rng.NextFloat() - 0.5f) * 0.25f, 0, 0.999f) : rng.NextFloat();
            int y = (int)(v * height);
            float brightness = Mathf.Pow(rng.NextFloat(), 3f) * 0.85f + 0.15f;
            var tint = rng.Next(5) switch
            {
                0 => new Color(0.75f, 0.85f, 1f),
                1 => new Color(1f, 0.85f, 0.7f),
                _ => new Color(1f, 1f, 1f),
            };
            int i = (y * width + x) * 3;
            data[i] = (byte)Mathf.Max(data[i], ToByte(tint.R * brightness));
            data[i + 1] = (byte)Mathf.Max(data[i + 1], ToByte(tint.G * brightness));
            data[i + 2] = (byte)Mathf.Max(data[i + 2], ToByte(tint.B * brightness));
        }

        var image = Image.CreateFromData(width, height, false, Image.Format.Rgb8, data);
        return ImageTexture.CreateFromImage(image);
    }

    private static byte ToByte(float value) => (byte)Mathf.Clamp(value * 255f, 0, 255);

    private void BuildDust(Rng rng)
    {
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.12f, 0.12f), Material = MeshFactory.Unshaded(new Color(0.75f, 0.78f, 0.85f)) },
            InstanceCount = DustCount,
        };
        for (int i = 0; i < DustCount; i++)
            _dustPositions[i] = new Vector3(rng.Range(-1, 1), rng.Range(-1, 1), rng.Range(-1, 1)) * DustBox / 2;
        _dust = new MultiMeshInstance3D { Multimesh = multimesh };
        AddChild(_dust);
    }

    /// <summary>Keeps the dust in a box around the camera and stretches it into streaks along your velocity.</summary>
    public void UpdateDust(Vector3 cameraPosition, Vector3 velocity, Vector3 up)
    {
        float speed = velocity.Length();
        var basis = Basis.Identity;
        if (speed > 5f)
        {
            var direction = velocity / speed;
            var safeUp = Mathf.Abs(direction.Dot(up)) > 0.98f ? Vector3.Right : up;
            // Scale along the local look axis so each mote becomes a streak in the direction of travel.
            basis = Basis.LookingAt(direction, safeUp) * Basis.FromScale(new Vector3(1, 1, 1 + speed * 0.08f));
        }

        var multimesh = _dust.Multimesh;
        float half = DustBox / 2;
        for (int i = 0; i < DustCount; i++)
        {
            var p = _dustPositions[i] - cameraPosition;
            p = new Vector3(Wrap(p.X, half), Wrap(p.Y, half), Wrap(p.Z, half));
            _dustPositions[i] = p + cameraPosition;
            multimesh.SetInstanceTransform(i, new Transform3D(basis, _dustPositions[i]));
        }
    }

    private static float Wrap(float value, float half) => Mathf.PosMod(value + half, half * 2) - half;
}
