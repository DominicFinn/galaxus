using System.Numerics;

namespace Galaxus.Core.Portraits;

/// <summary>
/// Painted-style portraits in the manner of late-80s and early-90s home computer games: a head
/// sculpted from smooth 3D shapes, lit hard from one side, then drawn with a short colour ramp
/// per material and ordered dithering on black. The same pools of features, outfits and genders
/// drive it as the flat builder, so every person still comes from their seed.
/// </summary>
public static class SculptedPortrait
{
    public const int Width = 96, Height = 112;

    private enum Mat { None, Skin, Lip, Hair, Cloth, Trim, EyeWhite, Iris, Metal, Lens, Ivory, Gold, Glow, HiVis, Chassis }

    /// <summary>Everything that decides how one face looks, rolled from the person's seed.</summary>
    public sealed class Face
    {
        public bool Machine;
        public Vector3 Skin, Hair, Eye, Cloth, Trim, Chassis, Light;
        public float HeadWidth = 1, Jaw = 1, Brow = 1, Nose = 1, NoseLength = 1, Lips = 1, Mouth = 1, Cheek = 1, Chin = 1;
        public float Yaw;
        public string Ears = "round", HairStyle = "crop";
        public bool Beard, Moustache, Tusks, GlowEyes;
        public bool Scar, Freckles, Tattoo, Earring, Cyber, Eyepatch, Seams, Grime;
        public string Headwear = "none", Eyewear = "none", Collar = "plain";
        public bool Lamp, Badge, Antenna;
        public int VisorLights = 2;
    }

    public static Face Describe(PortraitStyleDef style, ulong seed, string? clothing, Attire? attire, string gender)
    {
        attire ??= new Attire();
        var rng = new Rng(seed ^ 0x5C0FFEE);
        Vector3 Colour(List<string> options, string fallback) => Rgb(options.Count > 0 ? rng.Pick(options) : fallback);
        bool woman = gender == "woman", man = gender == "man";
        float Roll(float min, float max) => rng.Range(min, max);
        bool Mark(string name) => rng.NextFloat() < style.Marks.GetValueOrDefault(name);

        var f = new Face
        {
            Machine = style.Machine,
            Skin = Colour(style.Skin, "#c89a7a"),
            Hair = Colour(style.Hair, "#3a2a20"),
            Eye = Colour(style.Eyes, "#4a6a8a"),
            Cloth = clothing != null ? Rgb(clothing) : Colour(style.Clothing, "#445566"),
            Yaw = Roll(-0.32f, 0.32f),
            Ears = style.Ears,
            Tusks = style.Tusks,
            GlowEyes = style.GlowEyes,
            Headwear = attire.Headwear,
            Eyewear = attire.Eyewear,
            Collar = attire.Collar,
            Lamp = attire.Extras.Contains("lamp"),
            Badge = attire.Extras.Contains("badge"),
            Grime = attire.Extras.Contains("grime"),
        };
        f.Trim = Vector3.Lerp(f.Cloth, Vector3.One, 0.25f);
        f.Chassis = f.Skin;
        f.Light = f.Eye;

        // Head shape from the race's ranges, nudged by gender.
        float width = (Roll(style.HeadWidth[0], style.HeadWidth[1]) - 12) / 12f; // 0 to 1
        f.HeadWidth = 0.86f + width * 0.3f;
        float jawT = woman ? Roll(0, 0.55f) : man ? Roll(0.45f, 1) : rng.NextFloat();
        f.Jaw = 0.7f + (style.Jaw[0] + (style.Jaw[1] - style.Jaw[0]) * jawT - 0.5f) * 0.75f;
        f.Brow = woman ? Roll(0.3f, 0.6f) : man ? Roll(0.8f, 1.3f) : Roll(0.4f, 1);
        f.Nose = (woman ? Roll(0.75f, 0.95f) : Roll(0.9f, 1.25f)) * (style.Tusks ? 1.35f : 1);
        f.NoseLength = Roll(0.85f, 1.2f) * (style.Ears == "long" ? 1.1f : 1);
        f.Lips = woman ? Roll(1.15f, 1.5f) : Roll(0.8f, 1.1f);
        f.Mouth = Roll(0.85f, 1.15f) * (style.Tusks ? 1.2f : 1);
        f.Cheek = Roll(0.8f, 1.2f) * (woman ? 1.1f : 1);
        f.Chin = Roll(0.8f, 1.2f);

        string hair = style.HairStyles.Count > 0 ? rng.Pick(style.HairStyles) : "crop";
        if (woman && hair is "bald" or "shaved" or "crop" && rng.NextFloat() < 0.55f && style.HairStyles.Intersect(["long", "bun", "braids"]).Any())
            hair = rng.Pick(style.HairStyles.Intersect(["long", "bun", "braids"]).ToList());
        if (man && hair is "long" or "bun" && rng.NextFloat() < 0.4f && style.HairStyles.Intersect(["crop", "shaved", "side-part"]).Any())
            hair = rng.Pick(style.HairStyles.Intersect(["crop", "shaved", "side-part"]).ToList());
        if (attire.Headwear == "hood") hair = "hood";
        f.HairStyle = hair;

        float beard = woman ? (style.BeardChance > 0.6f ? style.BeardChance * 0.35f : 0) : style.BeardChance * (man ? 1.4f : 1);
        f.Beard = rng.NextFloat() < beard;
        f.Moustache = f.Beard || (!woman && rng.NextFloat() < style.MoustacheChance * (man ? 1.4f : 1));
        f.Scar = Mark("scar"); f.Freckles = Mark("freckles"); f.Tattoo = Mark("tattoo");
        f.Earring = Mark("earring") || (woman && rng.NextFloat() < 0.3f);
        f.Cyber = Mark("cyber"); f.Eyepatch = !f.Cyber && Mark("eyepatch"); f.Seams = Mark("seams");
        f.Antenna = rng.NextFloat() < 0.7f;
        f.VisorLights = 1 + rng.Next(4);
        return f;
    }

    // Signed distance helpers ---------------------------------------------------------------

    private static float Sphere(Vector3 p, Vector3 c, float r) => (p - c).Length() - r;

    private static float Ellipsoid(Vector3 p, Vector3 c, Vector3 r)
    {
        var q = (p - c) / r;
        float k0 = q.Length(), k1 = ((p - c) / (r * r)).Length();
        return k1 < 1e-6f ? -MathF.Min(r.X, MathF.Min(r.Y, r.Z)) : k0 * (k0 - 1) / k1;
    }

    private static float Capsule(Vector3 p, Vector3 a, Vector3 b, float ra, float rb)
    {
        var pa = p - a; var ba = b - a;
        float h = Math.Clamp(Vector3.Dot(pa, ba) / Vector3.Dot(ba, ba), 0, 1);
        return (pa - ba * h).Length() - (ra + (rb - ra) * h);
    }

    private static float RoundBox(Vector3 p, Vector3 c, Vector3 b, float r)
    {
        var q = Vector3.Abs(p - c) - b;
        return Vector3.Max(q, Vector3.Zero).Length() + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, q.Z)), 0) - r;
    }

    private static float Cylinder(Vector3 p, Vector3 c, float r, float h)
    {
        var q = p - c;
        var d = new Vector2(new Vector2(q.X, q.Z).Length() - r, MathF.Abs(q.Y) - h);
        return MathF.Min(MathF.Max(d.X, d.Y), 0) + Vector2.Max(d, Vector2.Zero).Length();
    }

    private static float Torus(Vector3 p, Vector3 c, float major, float minor)
    {
        var q = p - c;
        return new Vector2(new Vector2(q.X, q.Z).Length() - major, q.Y).Length() - minor;
    }

    private static float SmoothMin(float a, float b, float k)
    {
        float h = Math.Clamp(0.5f + 0.5f * (b - a) / k, 0, 1);
        return b + (a - b) * h - k * h * (1 - h);
    }

    private static float SmoothMax(float a, float b, float k) => -SmoothMin(-a, -b, k);

    // The scene -------------------------------------------------------------------------------

    private static (float D, Mat M) Scene(Face f, Vector3 p)
    {
        float d = 1e9f;
        var mat = Mat.None;
        void Add(float dist, Mat m) { if (dist < d) { d = dist; mat = m; } }

        float w = f.HeadWidth;
        var mirror = p with { X = MathF.Abs(p.X) };

        // Shoulders and collar.
        float shoulders = RoundBox(p, new Vector3(0, -1.62f, -0.1f), new Vector3(1.3f, 0.42f, 0.45f), 0.3f);
        Add(shoulders, f.Collar is "armour" ? Mat.Metal : Mat.Cloth);
        if (f.Collar is "uniform" or "fine") Add(RoundBox(mirror, new Vector3(1.0f, -1.28f, 0), new Vector3(0.22f, 0.05f, 0.3f), 0.03f), Mat.Gold);
        if (f.Collar is "armour") Add(RoundBox(mirror, new Vector3(0.95f, -1.3f, 0), new Vector3(0.32f, 0.12f, 0.42f), 0.1f), Mat.Metal);
        if (f.Collar is "flight" or "uniform") Add(Torus(p, new Vector3(0, -1.18f, 0.02f), 0.44f, 0.09f), Mat.Trim);
        if (f.Collar is "robe" or "shawl") Add(Torus(p, new Vector3(0, -1.2f, 0), 0.62f, 0.2f), Mat.Trim);
        if (f.Badge) Add(Ellipsoid(p, new Vector3(0.62f, -1.42f, 0.42f), new Vector3(0.08f, 0.1f, 0.04f)), Mat.Gold);

        if (f.Machine)
        {
            MachineHead(f, p, Add);
            return (d, mat);
        }

        // Neck, then the head: skull and jaw blended, cheekbones, brow, sockets, nose, lips, chin.
        Add(Cylinder(p, new Vector3(0, -1.05f, -0.05f), 0.36f * w, 0.4f), Mat.Skin);
        float head = Ellipsoid(p, new Vector3(0, 0.12f, -0.05f), new Vector3(0.78f * w, 0.92f, 0.84f));
        head = SmoothMin(head, Ellipsoid(p, new Vector3(0, -0.42f, 0.05f), new Vector3(0.6f * w * f.Jaw, 0.48f, 0.66f)), 0.25f);
        head = SmoothMin(head, Ellipsoid(mirror, new Vector3(0.4f * w, -0.06f, 0.45f), new Vector3(0.2f, 0.16f * f.Cheek, 0.2f)), 0.15f);
        head = SmoothMin(head, Capsule(mirror, new Vector3(0.05f, 0.24f, 0.66f), new Vector3(0.38f * w, 0.22f, 0.6f), 0.06f + 0.05f * f.Brow, 0.05f + 0.04f * f.Brow), 0.1f);
        head = SmoothMax(head, -Sphere(mirror, new Vector3(0.28f * w, 0.1f, 0.84f), 0.17f), 0.08f);
        head = SmoothMin(head, Capsule(p, new Vector3(0, 0.14f, 0.76f), new Vector3(0, -0.2f, 0.95f + 0.08f * f.NoseLength), 0.06f * f.Nose, 0.09f * f.Nose), 0.08f);
        // Eyelids and the fold under the eyes, so the eyes sit in the face rather than on it.
        head = SmoothMin(head, Ellipsoid(mirror, new Vector3(0.28f * w, 0.2f, 0.62f), new Vector3(0.16f, 0.06f, 0.12f)), 0.04f);
        head = SmoothMin(head, Ellipsoid(mirror, new Vector3(0.28f * w, -0.03f, 0.6f), new Vector3(0.15f, 0.04f, 0.1f)), 0.05f);
        // Jaw angles: square for broad jaws, soft for narrow ones.
        head = SmoothMin(head, Sphere(mirror, new Vector3(0.48f * w * f.Jaw, -0.46f, 0.22f), 0.2f * f.Jaw), 0.18f);
        // Lines from nose to mouth.
        head = SmoothMax(head, -Capsule(mirror, new Vector3(0.14f, -0.22f, 0.86f), new Vector3(0.26f * f.Mouth, -0.46f, 0.8f), 0.012f, 0.012f), 0.03f);
        head = SmoothMin(head, Ellipsoid(p, new Vector3(0, -0.24f, 0.86f + 0.06f * f.NoseLength), new Vector3(0.13f * f.Nose, 0.07f, 0.09f)), 0.06f);
        head = SmoothMin(head, Ellipsoid(p, new Vector3(0, -0.74f, 0.66f), new Vector3(0.17f * f.Chin, 0.13f, 0.14f)), 0.12f);
        Add(head, Mat.Skin);

        float lips = MathF.Min(
            Ellipsoid(p, new Vector3(0, -0.43f, 0.79f), new Vector3(0.19f * f.Mouth, 0.045f * f.Lips, 0.08f)),
            Ellipsoid(p, new Vector3(0, -0.5f, 0.77f), new Vector3(0.17f * f.Mouth, 0.055f * f.Lips, 0.08f)));
        lips = SmoothMax(lips, -Ellipsoid(p, new Vector3(0, -0.465f, 0.86f), new Vector3(0.2f * f.Mouth, 0.012f, 0.08f)), 0.01f);
        Add(lips, Mat.Lip);

        // Ears.
        switch (f.Ears)
        {
            case "pointed": Add(Capsule(mirror, new Vector3(0.7f * w, 0.02f, -0.02f), new Vector3(1.0f * w, 0.4f, -0.12f), 0.1f, 0.02f), Mat.Skin); break;
            case "long": Add(Capsule(mirror, new Vector3(0.7f * w, 0.02f, -0.02f), new Vector3(1.25f * w, 0.55f, -0.18f), 0.1f, 0.015f), Mat.Skin); break;
            case "small": Add(Ellipsoid(mirror, new Vector3(0.73f * w, 0f, 0), new Vector3(0.07f, 0.13f, 0.1f)), Mat.Skin); break;
            case "round": Add(Ellipsoid(mirror, new Vector3(0.74f * w, 0f, 0), new Vector3(0.08f, 0.19f, 0.12f)), Mat.Skin); break;
        }
        if (f.Earring && f.Ears != "none") Add(Torus(p with { Y = p.Z, Z = p.Y }, new Vector3(-0.78f * w, 0.02f, -0.16f), 0.05f, 0.012f), Mat.Gold);

        // Eyes: white spheres in the sockets; the shading pass paints iris and pupil.
        if (!f.Eyepatch) Add(Sphere(p, new Vector3(-0.28f * w, 0.09f, 0.63f), 0.115f), Mat.EyeWhite);
        else Add(Ellipsoid(p, new Vector3(-0.28f * w, 0.09f, 0.74f), new Vector3(0.17f, 0.14f, 0.06f)), Mat.Lens);
        if (!f.Cyber) Add(Sphere(p, new Vector3(0.28f * w, 0.09f, 0.63f), 0.115f), Mat.EyeWhite);
        else
        {
            Add(Ellipsoid(p, new Vector3(0.28f * w, 0.09f, 0.72f), new Vector3(0.2f, 0.16f, 0.08f)), Mat.Metal);
            Add(Sphere(p, new Vector3(0.28f * w, 0.09f, 0.79f), 0.05f), Mat.Glow);
        }

        if (f.Tusks)
            Add(Capsule(mirror, new Vector3(0.17f * f.Mouth, -0.52f, 0.78f), new Vector3(0.21f * f.Mouth, -0.27f, 0.86f), 0.045f, 0.012f), Mat.Ivory);

        // Facial hair, roughened so it reads as hair rather than a solid.
        // Hair falls in streaks: ridges that run mostly downwards.
        float rough = 0.005f * MathF.Sin(p.X * 90 + p.Z * 30 + MathF.Sin(p.Y * 9 + p.X * 4) * 5) + 0.003f * MathF.Sin(p.X * 37 - p.Y * 21);
        if (f.Beard)
        {
            float beard = Ellipsoid(p, new Vector3(0, -0.6f, 0.38f), new Vector3(0.62f * w * f.Jaw, 0.42f, 0.5f));
            beard = SmoothMax(beard, p.Y + 0.28f, 0.05f);
            beard = SmoothMax(beard, -Ellipsoid(p, new Vector3(0, -0.47f, 0.84f), new Vector3(0.22f, 0.1f, 0.2f)), 0.03f);
            Add(beard + rough, Mat.Hair);
        }
        if (f.Moustache) Add(Capsule(mirror, new Vector3(0.02f, -0.37f, 0.86f), new Vector3(0.2f * f.Mouth, -0.44f, 0.8f), 0.045f, 0.03f) + rough, Mat.Hair);

        Hair(f, p, mirror, rough, Add);
        Headwear(f, p, mirror, Add);

        // Eyewear sits just in front of the eyes.
        switch (f.Eyewear)
        {
            case "shades": Add(RoundBox(p, new Vector3(0, 0.1f, 0.84f), new Vector3(0.48f * w, 0.09f, 0.02f), 0.03f), Mat.Lens); break;
            case "visor": Add(RoundBox(p, new Vector3(0, 0.1f, 0.86f), new Vector3(0.6f * w, 0.07f, 0.02f), 0.03f), Mat.Lens); break;
            case "glasses":
                Add(Torus(new Vector3(p.X, p.Z, p.Y) with { }, new Vector3(-0.28f * w, 0.84f, 0.09f), 0.13f, 0.012f), Mat.Gold);
                Add(Torus(new Vector3(p.X, p.Z, p.Y), new Vector3(0.28f * w, 0.84f, 0.09f), 0.13f, 0.012f), Mat.Gold);
                break;
            case "monocle": Add(Torus(new Vector3(p.X, p.Z, p.Y), new Vector3(0.28f * w, 0.84f, 0.09f), 0.14f, 0.015f), Mat.Gold); break;
        }
        return (d, mat);
    }

    private static void Hair(Face f, Vector3 p, Vector3 mirror, float rough, Action<float, Mat> add)
    {
        float w = f.HeadWidth;
        // A shell over the skull that stops at the hairline and above the ears.
        float shell = Ellipsoid(p, new Vector3(0, 0.16f, -0.08f), new Vector3(0.79f * w, 1.0f, 0.9f)) + rough;
        float Cap(float hairline) => SmoothMax(shell, -(p.Y - hairline) + MathF.Max(0, p.Z - 0.2f) * 0.6f, 0.06f);
        switch (f.HairStyle)
        {
            case "crop": add(Cap(0.42f), Mat.Hair); break;
            case "shaved": add(Cap(0.55f) + 0.02f, Mat.Hair); break;
            case "side-part":
                add(Cap(0.4f), Mat.Hair);
                add(Ellipsoid(p, new Vector3(0.2f, 0.62f, 0.55f), new Vector3(0.42f, 0.14f, 0.2f)) + rough, Mat.Hair);
                break;
            case "long":
            case "braids":
                add(Cap(0.36f), Mat.Hair);
                add(SmoothMax(RoundBox(p, new Vector3(0, -0.45f, -0.25f), new Vector3(0.78f * w, 0.75f, 0.35f), 0.15f) + rough, -(p.Z - 0.3f), 0.05f), Mat.Hair);
                if (f.HairStyle == "braids")
                    add(Capsule(mirror, new Vector3(0.72f * w, 0, 0.1f), new Vector3(0.8f * w, -1.25f, 0.2f), 0.09f, 0.06f) + rough, Mat.Hair);
                break;
            case "bun":
                add(Cap(0.4f), Mat.Hair);
                add(Sphere(p, new Vector3(0, 0.95f, -0.45f), 0.3f) + rough, Mat.Hair);
                break;
            case "mohawk": add(SmoothMax(Ellipsoid(p, new Vector3(0, 0.6f, -0.1f), new Vector3(0.14f, 0.55f, 0.85f)) + rough, -(p.Y - 0.3f), 0.05f), Mat.Hair); break;
            case "topknot":
                add(Cap(0.62f) + 0.02f, Mat.Hair);
                add(Sphere(p, new Vector3(0, 1.12f, -0.1f), 0.18f) + rough, Mat.Hair);
                break;
            case "hood":
                float hood = Ellipsoid(p, new Vector3(0, 0.12f, -0.1f), new Vector3(0.98f * w, 1.18f, 1.05f));
                hood = SmoothMax(hood, -Ellipsoid(p, new Vector3(0, 0.05f, 0.6f), new Vector3(0.72f * w, 0.92f, 0.9f)), 0.05f);
                add(SmoothMax(hood, -(p.Y + 1.0f), 0.1f), Mat.Trim);
                break;
        }
    }

    private static void Headwear(Face f, Vector3 p, Vector3 mirror, Action<float, Mat> add)
    {
        float w = f.HeadWidth;
        switch (f.Headwear)
        {
            case "hardhat":
                add(SmoothMax(Ellipsoid(p, new Vector3(0, 0.55f, -0.05f), new Vector3(0.88f * w, 0.72f, 0.92f)), -(p.Y - 0.5f), 0.03f), Mat.HiVis);
                add(SmoothMax(Ellipsoid(p, new Vector3(0, 0.52f, 0.05f), new Vector3(1.05f * w, 0.04f, 1.12f)), -(p.Z + 0.1f), 0.02f), Mat.HiVis);
                if (f.Lamp)
                {
                    add(Cylinder(p with { Y = p.Z, Z = p.Y }, new Vector3(0, 0.88f, 0.85f), 0.13f, 0.06f), Mat.Metal);
                    add(Sphere(p, new Vector3(0, 0.85f, 0.95f), 0.09f), Mat.Glow);
                }
                break;
            case "cap":
                add(Cylinder(p, new Vector3(0, 0.78f, -0.02f), 0.82f * w, 0.18f) - 0.05f, Mat.Cloth);
                add(SmoothMax(Ellipsoid(p, new Vector3(0, 0.6f, 0.55f), new Vector3(0.7f * w, 0.03f, 0.55f)), -(p.Z - 0.4f), 0.02f), Mat.Lens);
                add(Ellipsoid(p, new Vector3(0, 0.8f, 0.8f), new Vector3(0.12f, 0.12f, 0.05f)), Mat.Gold);
                break;
            case "beanie":
                add(SmoothMax(Ellipsoid(p, new Vector3(0, 0.45f, -0.08f), new Vector3(0.84f * w, 0.8f, 0.92f)) + 0.01f * MathF.Sin(p.X * 60), -(p.Y - 0.42f), 0.03f), Mat.Trim);
                add(SmoothMax(Torus(p, new Vector3(0, 0.46f, -0.08f), 0.78f * w, 0.09f), -(p.Y - 0.38f), 0.02f), Mat.Trim);
                break;
            case "helmet":
                float helmet = Ellipsoid(p, new Vector3(0, 0.22f, -0.08f), new Vector3(0.95f * w, 1.12f, 1.02f));
                helmet = SmoothMax(helmet, -RoundBox(p, new Vector3(0, -0.2f, 0.8f), new Vector3(0.6f * w, 0.55f, 0.6f), 0.1f), 0.06f);
                add(SmoothMax(helmet, -(p.Y + 0.55f), 0.05f), Mat.Metal);
                break;
            case "headset":
                add(Torus(p with { Y = p.Z, Z = p.Y }, new Vector3(0, -0.02f, 0.05f), 0.86f * w, 0.035f) + MathF.Max(0, -p.Y + 0.05f), Mat.Metal);
                add(Ellipsoid(mirror, new Vector3(0.8f * w, 0.02f, 0), new Vector3(0.1f, 0.2f, 0.18f)), Mat.Lens);
                add(Capsule(p, new Vector3(-0.8f * w, -0.1f, 0.12f), new Vector3(-0.3f, -0.5f, 0.75f), 0.025f, 0.025f), Mat.Metal);
                break;
            case "goggles":
                add(Torus(p, new Vector3(0, 0.55f, -0.05f), 0.8f * w, 0.05f), Mat.Cloth);
                add(Cylinder(new Vector3(mirror.X, mirror.Z, mirror.Y), new Vector3(0.26f, 0.72f, 0.55f), 0.15f, 0.08f), Mat.Metal);
                add(Cylinder(new Vector3(mirror.X, mirror.Z, mirror.Y), new Vector3(0.26f, 0.8f, 0.55f), 0.11f, 0.01f), Mat.Lens);
                break;
            case "bandana":
                add(SmoothMax(Ellipsoid(p, new Vector3(0, 0.18f, -0.06f), new Vector3(0.8f * w, 1.0f, 0.88f)), MathF.Abs(p.Y - 0.5f) - 0.1f, 0.02f), Mat.Trim);
                break;
        }
    }

    private static void MachineHead(Face f, Vector3 p, Action<float, Mat> add)
    {
        float w = f.HeadWidth;
        add(Cylinder(p, new Vector3(0, -1.0f, -0.05f), 0.3f, 0.45f), Mat.Metal);
        float head = RoundBox(p, new Vector3(0, 0.02f, 0), new Vector3(0.58f * w, 0.78f, 0.6f), 0.18f);
        float visor = RoundBox(p, new Vector3(0, 0.12f, 0.75f), new Vector3(0.52f * w, 0.11f, 0.2f), 0.03f);
        add(SmoothMax(head, -visor, 0.02f), Mat.Chassis);
        add(RoundBox(p, new Vector3(0, 0.12f, 0.6f), new Vector3(0.5f * w, 0.09f, 0.06f), 0.02f), Mat.Lens);
        for (int i = 0; i < f.VisorLights; i++)
        {
            float x = -0.12f * (f.VisorLights - 1) + i * 0.24f;
            add(RoundBox(p, new Vector3(x * w, 0.12f, 0.66f), new Vector3(0.07f, 0.03f, 0.02f), 0.01f), Mat.Glow);
        }
        for (int i = 0; i < 4; i++) add(RoundBox(p, new Vector3(-0.18f + i * 0.12f, -0.42f, 0.78f), new Vector3(0.025f, 0.12f, 0.02f), 0.01f), Mat.Lens);
        if (f.Antenna)
        {
            add(Capsule(p, new Vector3(0.3f, 0.8f, -0.1f), new Vector3(0.38f, 1.25f, -0.15f), 0.025f, 0.02f), Mat.Metal);
            add(Sphere(p, new Vector3(0.38f, 1.27f, -0.15f), 0.06f), Mat.Glow);
        }
    }

    // Rendering --------------------------------------------------------------------------------

    private static readonly int[,] Bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };

    public static Vector4[] Render(Face f)
    {
        var pixels = new Vector4[Width * Height];
        float cos = MathF.Cos(f.Yaw), sin = MathF.Sin(f.Yaw);
        Vector3 ToHead(Vector3 w) => new(w.X * cos - w.Z * sin, w.Y, w.X * sin + w.Z * cos);

        var key = Vector3.Normalize(new Vector3(-0.65f, 0.6f, 0.62f));
        var fill = Vector3.Normalize(new Vector3(0.8f, -0.2f, 0.5f));
        var rim = Vector3.Normalize(new Vector3(0.7f, 0.5f, -0.6f));

        for (int py = 0; py < Height; py++)
        for (int px = 0; px < Width; px++)
        {
            // A tight close-up: the face fills the frame, the crown and collar just cut off.
            float x = ((px + 0.5f) / Width - 0.5f) * 1.95f;
            float y = 1.12f - (py + 0.5f) / Height * 2.28f;
            var origin = new Vector3(x, y, 3f);
            var dir = new Vector3(0, 0, -1);

            float t = 0;
            Mat mat = Mat.None;
            Vector3 hit = default;
            for (int step = 0; step < 90 && t < 6; step++)
            {
                hit = origin + dir * t;
                var (d, m) = Scene(f, ToHead(hit));
                if (d < 0.003f) { mat = m; break; }
                t += d * 0.85f;
            }

            int index = py * Width + px;
            float dither = (Bayer[py % 4, px % 4] + 0.5f) / 16f - 0.5f;
            if (mat == Mat.None)
            {
                // Near-black backdrop with a faint glow behind the head.
                float glow = MathF.Max(0, 1 - new Vector2(x, y - 0.2f).Length() / 1.6f) * 0.08f;
                pixels[index] = Quantise(new Vector3(0.04f, 0.035f, 0.05f) + new Vector3(0.6f, 0.4f, 0.3f) * glow, 0);
                continue;
            }

            // Normal from the distance field, in head space then back to world for lighting.
            var hp = ToHead(hit);
            const float e = 0.004f;
            var n = Vector3.Normalize(new Vector3(
                Scene(f, hp + new Vector3(e, 0, 0)).D - Scene(f, hp - new Vector3(e, 0, 0)).D,
                Scene(f, hp + new Vector3(0, e, 0)).D - Scene(f, hp - new Vector3(0, e, 0)).D,
                Scene(f, hp + new Vector3(0, 0, e)).D - Scene(f, hp - new Vector3(0, 0, e)).D));
            n = new Vector3(n.X * cos + n.Z * sin, n.Y, -n.X * sin + n.Z * cos);

            var (baseColour, shine, glowing) = Material(f, mat, hp);
            // Ambient occlusion: how buried the point is. Darkens sockets, under the nose, creases.
            float occlusion = 0;
            var nHead = new Vector3(n.X * cos - n.Z * sin, n.Y, n.X * sin + n.Z * cos);
            for (int k = 1; k <= 4; k++)
            {
                float h = 0.035f * k;
                occlusion += (h - Scene(f, hp + nHead * h).D) / k;
            }
            float ao = Math.Clamp(1 - occlusion * 3.2f, 0.25f, 1);
            float diffuse = MathF.Max(0, Vector3.Dot(n, key));
            float light = (0.04f + diffuse * 1.38f + MathF.Max(0, Vector3.Dot(n, fill)) * 0.14f) * ao + MathF.Pow(MathF.Max(0, Vector3.Dot(n, rim)), 2) * 0.3f;
            float spec = shine > 0 ? MathF.Pow(MathF.Max(0, Vector3.Dot(Vector3.Normalize(key + Vector3.UnitZ), n)), shine) : 0;
            if (glowing) light = 1.35f;

            // A short ramp: dark warm shadows, the base colour, then a warm highlight. Stepped with dither.
            // Shadows go deep and warm, highlights go golden: the look of hand-shaded portraits.
            float level = MathF.Round((light + dither * 0.13f) * 9f) / 9f;
            bool skin = mat is Mat.Skin or Mat.Lip;
            var shadow = baseColour * (skin ? new Vector3(0.22f, 0.1f, 0.06f) : new Vector3(0.14f, 0.1f, 0.1f));
            var highlight = Vector3.Lerp(baseColour, skin ? new Vector3(1f, 0.84f, 0.5f) : new Vector3(1f, 0.92f, 0.75f), skin ? 0.75f : 0.6f);
            Vector3 colour = level < 1
                ? Vector3.Lerp(shadow, baseColour * 1.05f, MathF.Pow(level, 0.85f))
                : Vector3.Lerp(baseColour * 1.05f, highlight, MathF.Min(1, (level - 1) * 2.2f));
            if (spec > 0.25f + dither * 0.3f) colour = Vector3.Lerp(colour, new Vector3(1f, 0.97f, 0.9f), MathF.Min(1, spec) * (mat is Mat.Metal or Mat.Lens or Mat.Gold or Mat.EyeWhite ? 0.85f : 0.35f));
            pixels[index] = Quantise(colour, dither);
        }
        return pixels;
    }

    /// <summary>Base colour, shininess (0 for matt) and whether it glows, with face details painted on.</summary>
    private static (Vector3 Colour, float Shine, bool Glow) Material(Face f, Mat mat, Vector3 p)
    {
        switch (mat)
        {
            case Mat.Skin:
            {
                var c = f.Skin;
                if (f.Freckles && Hash(p * 90) > 0.93f && p.Z > 0.5f && p.Y is > -0.25f and < 0.08f) c *= 0.78f;
                if (f.Scar && MathF.Abs((p.X - 0.25f) - (p.Y - 0.1f) * 0.4f) < 0.025f && p.Y is > -0.25f and < 0.45f && p.X > 0) c = Vector3.Lerp(c, new Vector3(0.75f, 0.45f, 0.42f), 0.6f);
                if (f.Tattoo && p.X < -0.3f && MathF.Abs(MathF.Sin(p.Y * 30)) > 0.85f && p.Y is > -0.4f and < 0.0f) c = Vector3.Lerp(c, f.Cloth * 0.4f, 0.6f);
                if (f.Seams && (MathF.Abs(p.Y - 0.45f) < 0.012f || MathF.Abs(p.X + p.Y * 0.2f + 0.45f) < 0.012f)) c *= 0.55f;
                if (f.Grime && Hash(p * 60) > 0.86f && p.Y < 0.2f) c *= 0.72f;
                return (c, 9, false);
            }
            case Mat.Lip: return (Vector3.Lerp(f.Skin, new Vector3(0.62f, 0.25f, 0.25f), 0.35f), 12, false);
            case Mat.Hair: return (f.Hair, 6, false);
            case Mat.Cloth:
                if (f.Collar == "overalls" && MathF.Abs(MathF.Abs(p.X) - 0.42f) < 0.07f) return (new Vector3(1f, 0.6f, 0.1f), 0, false);
                if (f.Collar == "apron" && MathF.Abs(p.X) < 0.5f) return (new Vector3(0.88f, 0.86f, 0.8f), 0, false);
                return (f.Cloth, 0, false);
            case Mat.Trim: return (f.Trim, 0, false);
            case Mat.EyeWhite:
            {
                if (f.GlowEyes) return (f.Eye, 0, true);
                // Iris and pupil where the eyeball faces the viewer.
                var centre = new Vector3(p.X > 0 ? 0.28f * f.HeadWidth : -0.28f * f.HeadWidth, 0.09f, 0.63f);
                var look = Vector3.Normalize(p - centre);
                if (look.Z > 0.95f) return (new Vector3(0.04f, 0.03f, 0.03f), 30, false);
                if (look.Z > 0.82f) return (f.Eye, 30, false);
                return (new Vector3(0.88f, 0.84f, 0.78f), 30, false);
            }
            case Mat.Metal: return (new Vector3(0.62f, 0.64f, 0.7f), 28, false);
            case Mat.Lens: return (new Vector3(0.08f, 0.08f, 0.1f), 40, false);
            case Mat.Ivory: return (new Vector3(0.92f, 0.88f, 0.76f), 14, false);
            case Mat.Gold: return (new Vector3(0.95f, 0.75f, 0.3f), 30, false);
            case Mat.Glow: return (f.Machine ? f.Light : new Vector3(1f, 0.95f, 0.75f), 0, true);
            case Mat.HiVis: return (new Vector3(1f, 0.72f, 0.12f), 20, false);
            case Mat.Chassis: return (f.Chassis, 24, false);
            default: return (Vector3.One, 0, false);
        }
    }

    private static float Hash(Vector3 p)
    {
        float h = MathF.Sin(MathF.Floor(p.X) * 127.1f + MathF.Floor(p.Y) * 311.7f + MathF.Floor(p.Z) * 74.7f) * 43758.5453f;
        return h - MathF.Floor(h);
    }

    /// <summary>Snaps to 15-bit colour, nudged by the dither so gradients break into patterns.</summary>
    private static Vector4 Quantise(Vector3 c, float dither)
    {
        float Snap(float v) => MathF.Round(Math.Clamp(v + dither / 31f, 0, 1) * 31f) / 31f;
        return new Vector4(Snap(c.X), Snap(c.Y), Snap(c.Z), 1);
    }

    private static Vector3 Rgb(string hex)
    {
        var v = PortraitBuilder.Colour(hex);
        return new Vector3(v.X, v.Y, v.Z);
    }
}
