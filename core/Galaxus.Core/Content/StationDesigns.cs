using System.Numerics;
using Galaxus.Core.Galaxy;

namespace Galaxus.Core.Content;

/// <summary>
/// Station shapes, built from the same shape vocabulary as ships. Each design is a generator that
/// takes the station's size and a seed, so neighbouring stations of the same faction differ a
/// little and citadels are grander than outposts. Every design docks on its -Z face, where a lit
/// bay opens outwards. The owner's power or race picks the design; faction types can name one
/// (or supply their own model, as before).
/// </summary>
public static class StationDesigns
{
    public static readonly string[] All = ["ring", "claw", "sphere", "pyramid", "asteroid", "spindle", "lattice", "crown", "broken"];

    public static string Describe(string design) => design switch
    {
        "claw" => "Claw spire: a core tower with a habitat ring round its middle and curved docking pylons reaching up and down. Frontier trading posts.",
        "sphere" => "Battle sphere: an armoured globe banded with trenches, a great dish set into one hemisphere. Military strongholds and capitals.",
        "pyramid" => "Pyramid: a stepped pyramid with a glowing cap and docking slots along its base. Old, imperial and a little eerie.",
        "asteroid" => "Asteroid base: a rock hollowed out and lit, with gantries and a cut bay. Miners, dwarves, pirates.",
        "spindle" => "Spindle: a long habitat cylinder with end caps and lit bands. Human colonies and corporations.",
        "lattice" => "Lattice: an open framework of struts and nodes. Free machines and synths.",
        "crown" => "Crown: tall spires round a central dome. The light elf Houses.",
        "broken" => "Broken ring: a ruined ring, patched and incomplete. The rim and the dark elves.",
        _ => "Ring: a hub with a docking bay, spokes and a habitat ring. The classic, and the Concord's own.",
    };

    /// <summary>The design for a system's station, from who owns it and how big it is.</summary>
    public static string Pick(ContentDatabase db, StarSystem system, Faction? owner, StationSize size)
    {
        if (owner != null && db.FactionTypes.TryGetValue(owner.TypeId, out var type) && type.StationDesign is { } named && All.Contains(named)) return named;
        var rng = new Rng(system.Seed ^ 0x57A7);
        if (owner == null) return rng.NextFloat() < 0.6f ? "asteroid" : "broken";
        if (owner.Criminal) return "asteroid";
        bool citadel = size == StationSize.Citadel;
        string? byPower = owner.PowerId switch
        {
            "meridian-concord" => "ring",
            "hesper-directorate" => citadel ? "sphere" : "spindle",
            "tolland-compact" => "claw",
            "lantern-synod" => "pyramid",
            "eleven-houses" => "crown",
            "hold-league" => "asteroid",
            "grav-khanates" => citadel || size == StationSize.Hub ? "sphere" : "asteroid",
            "the-lattice" => "lattice",
            "ruin-courts" => "broken",
            _ => null,
        };
        if (byPower != null) return byPower;
        return owner.RaceId switch
        {
            "dwarf" => "asteroid",
            "orc" => rng.NextFloat() < 0.5f ? "asteroid" : "claw",
            "light-elf" => "crown",
            "dark-elf" => "broken",
            "free-machine" or "synth" => "lattice",
            _ => rng.Pick(new[] { "ring", "spindle", "claw", "ring" }),
        };
    }

    /// <summary>A design's shapes for a size and seed, and the radius to collide with.</summary>
    public static (List<ShapeDef> Model, float Radius) Build(string design, StationSize size, ulong seed)
    {
        var b = new Builder(new Rng(seed ^ Rng.Hash(design)), size);
        switch (design)
        {
            case "claw": b.Claw(); break;
            case "sphere": b.Sphere(); break;
            case "pyramid": b.Pyramid(); break;
            case "asteroid": b.Asteroid(); break;
            case "spindle": b.Spindle(); break;
            case "lattice": b.Lattice(); break;
            case "crown": b.Crown(); break;
            case "broken": b.Ring(broken: true); break;
            default: b.Ring(broken: false); break;
        }
        // The game scales stations by the population they serve, so the model stays at one size here.
        var model = b.Shapes;
        return (model, Extent(model) * 0.85f);
    }

    /// <summary>How far the model reaches from its centre, generously.</summary>
    public static float Extent(IEnumerable<ShapeDef> model) => model.Select(s =>
        new Vector3(s.Position[0], s.Position[1], s.Position[2]).Length()
        + MathF.Sqrt(s.Length * s.Length / 4 + MathF.Pow(MathF.Max(MathF.Max(s.Back[0], s.Back[1]), MathF.Max(s.Front[0], s.Front[1])) / 2, 2))).DefaultIfEmpty(100).Max();

    private sealed class Builder(Rng rng, StationSize size)
    {
        public List<ShapeDef> Shapes { get; } = [];
        private bool Big => size is StationSize.Hub or StationSize.Citadel;
        private bool Citadel => size == StationSize.Citadel;

        private void Add(string shape, Vector3 at, float w, float h, float length, string colour, float frontW = -1, float frontH = -1,
            float roll = 0, float pitch = 0, float yaw = 0, int sides = 0) =>
            Shapes.Add(new ShapeDef
            {
                Shape = shape, Position = [at.X, at.Y, at.Z], Back = [w, h], Front = [frontW < 0 ? w : frontW, frontH < 0 ? h : frontH],
                Length = length, Roll = roll, Pitch = pitch, Yaw = yaw, Sides = sides, Color = colour,
            });

        private void Box(Vector3 at, float w, float h, float length, string colour, float roll = 0) => Add("taperedBox", at, w, h, length, colour, roll: roll);

        /// <summary>A box or tube from one point to another.</summary>
        private void Beam(Vector3 from, Vector3 to, float w, float h, string colour, string shape = "taperedBox", float frontScale = 1, int sides = 0)
        {
            var d = to - from;
            float length = d.Length();
            if (length < 0.01f) return;
            d /= length;
            // A shape's length runs along -Z (back to front): turn -Z onto the direction.
            float pitch = MathF.Asin(Math.Clamp(d.Y, -1, 1)) * 180 / MathF.PI;
            float yaw = MathF.Atan2(-d.X, -d.Z) * 180 / MathF.PI;
            Add(shape, (from + to) / 2, w, h, length, colour, w * frontScale, h * frontScale, pitch: pitch, yaw: yaw, sides: sides);
        }

        /// <summary>The docking bay on the -Z face: a frame and a lit opening.</summary>
        private void Bay(float z, float w, float h, float depth = 10)
        {
            Box(new Vector3(0, 0, z - depth / 2), w + 12, h + 12, depth, "trim");
            Box(new Vector3(0, 0, z - depth - 0.5f), w, h, 1, "glow");
        }

        private void Lights(Vector3 centre, float radius, float y, int count, float size = 3)
        {
            for (int i = 0; i < count; i++)
            {
                float a = MathF.Tau * i / count;
                Box(centre + new Vector3(MathF.Cos(a) * radius, y, MathF.Sin(a) * radius), size, size, size, "glow");
            }
        }

        /// <summary>A ring in the XY plane (facing the dock), or round the Y axis when flat.</summary>
        private void RingSegments(float radius, Vector3 centre, int segments, float thick, bool flat, Func<int, string> colour, Func<int, bool>? skip = null)
        {
            for (int i = 0; i < segments; i++)
            {
                if (skip?.Invoke(i) == true) continue;
                float a0 = MathF.Tau * i / segments, a1 = MathF.Tau * (i + 1) / segments;
                Vector3 P(float a) => flat ? centre + new Vector3(MathF.Cos(a) * radius, 0, MathF.Sin(a) * radius) : centre + new Vector3(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0);
                Beam(P(a0), P(a1), thick, thick, colour(i));
            }
        }

        public void Ring(bool broken)
        {
            Box(Vector3.Zero, 60, 60, 90, "hull");
            Box(new Vector3(0, 0, 60), 20, 20, 30, "trim");
            Bay(-45, 24, 10);
            int segments = 12;
            var gaps = broken ? new HashSet<int> { rng.Next(segments), rng.Next(segments), (rng.Next(segments) + 6) % segments } : [];
            RingSegments(150, Vector3.Zero, segments, 26, flat: false, i => i % 3 == 0 ? "accent" : broken && i % 4 == 1 ? "trim" : "hull", i => gaps.Contains(i));
            for (int i = 0; i < segments; i += 3)
            {
                float a = MathF.Tau * i / segments;
                if (!gaps.Contains(i)) Beam(Vector3.Zero, new Vector3(MathF.Cos(a) * 140, MathF.Sin(a) * 140, 0), 8, 8, "trim");
            }
            if (broken)
            {
                // Patches and loose plating where the ring failed.
                foreach (int g in gaps)
                {
                    float a = MathF.Tau * (g + 0.5f) / segments;
                    Box(new Vector3(MathF.Cos(a) * (150 + rng.Range(-20, 20)), MathF.Sin(a) * 150, rng.Range(-30, 30)), rng.Range(8, 20), rng.Range(8, 20), rng.Range(10, 30), "trim", rng.Range(0, 90));
                }
                return;
            }
            Lights(Vector3.Zero, 164, 0, 12, 2);
            if (Big)
            {
                Box(new Vector3(0, 0, 110), 30, 30, 160, "hull");
                RingSegments(110, new Vector3(0, 0, 120), 10, 22, false, i => i % 2 == 0 ? "accent" : "hull");
                RingSegments(110, new Vector3(0, 0, -110), 10, 22, false, i => i % 2 == 0 ? "accent" : "hull");
            }
            if (Citadel)
            {
                RingSegments(230, Vector3.Zero, 18, 20, false, i => i % 3 == 0 ? "accent" : "hull");
                foreach (var (x, y) in new[] { (0f, 1f), (0f, -1f), (1f, 0f), (-1f, 0f) })
                {
                    Box(new Vector3(x * 70, y * 70, 0), 22, 22, 260, "accent");
                    Box(new Vector3(x * 70, y * 70, -131), 10, 10, 2, "glow");
                }
            }
        }

        public void Claw()
        {
            // The core: a tall tower along Y, tapering at both ends.
            Beam(new Vector3(0, -110, 0), new Vector3(0, 110, 0), 70, 70, "hull", "cylinder", sides: 12);
            Beam(new Vector3(0, 110, 0), new Vector3(0, 150, 0), 70, 70, "trim", "cylinder", 0.3f, 12);
            Beam(new Vector3(0, -110, 0), new Vector3(0, -150, 0), 70, 70, "trim", "cylinder", 0.3f, 12);
            Bay(-36, 26, 14);
            // The habitat ring round its middle, flat.
            RingSegments(130, Vector3.Zero, 16, 24, flat: true, i => i % 4 == 0 ? "accent" : "hull");
            for (int i = 0; i < 4; i++)
            {
                float a = MathF.Tau * (i + 0.5f) / 4;
                Beam(Vector3.Zero, new Vector3(MathF.Cos(a) * 120, 0, MathF.Sin(a) * 120), 10, 6, "trim");
            }
            // Claws: curved pylons reaching up and down from the ring.
            int claws = Big ? 4 : 3;
            for (int i = 0; i < claws; i++)
            {
                float a = MathF.Tau * i / claws + 0.3f;
                var dir = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
                foreach (float up in new[] { 1f, -1f })
                {
                    var p0 = dir * 140;
                    var p1 = dir * 175 + new Vector3(0, 60 * up, 0);
                    var p2 = dir * 165 + new Vector3(0, 130 * up, 0);
                    var p3 = dir * 130 + new Vector3(0, 175 * up, 0);
                    Beam(p0, p1, 12, 12, "hull");
                    Beam(p1, p2, 10, 10, "hull");
                    Beam(p2, p3, 8, 8, "accent");
                    Box(p3, 4, 4, 4, "glow");
                }
            }
            Lights(Vector3.Zero, 144, 0, 16, 2);
        }

        public void Sphere()
        {
            const float r = 150;
            Add("sphere", Vector3.Zero, r * 2, r * 2, r * 2, "hull", sides: 24);
            // Bands and the deep equatorial trench.
            foreach (float y in new[] { -110f, -60f, 60f, 110f })
            {
                float d = MathF.Sqrt(r * r - y * y) * 2 + 3;
                Beam(new Vector3(0, y - 2, 0), new Vector3(0, y + 2, 0), d, d, "trim", "cylinder", sides: 24);
            }
            Beam(new Vector3(0, -6, 0), new Vector3(0, 6, 0), r * 2 + 8, r * 2 + 8, "trim", "cylinder", sides: 24);
            Lights(Vector3.Zero, r + 4, 0, 20, 3);
            // The great dish in the upper hemisphere, facing out.
            var normal = Vector3.Normalize(new Vector3(0.45f, 0.55f, -0.7f));
            Beam(normal * (r - 10), normal * (r + 4), 110, 110, "trim", "cylinder", 0.9f, 20);
            Beam(normal * (r + 2), normal * (r + 8), 26, 26, "accent", "cylinder", 0.6f, 12);
            Box(normal * (r + 10), 6, 6, 6, "glow");
            // The bay in the equator, facing out.
            Bay(-r + 4, 34, 10, 14);
            if (Citadel)
                foreach (float y in new[] { -1f, 1f })
                    Beam(new Vector3(0, y * r * 0.95f, 0), new Vector3(0, y * (r + 70), 0), 18, 18, "accent", "cylinder", 0.2f, 8);
        }

        public void Pyramid()
        {
            // Steps rising along +Y, the cap glowing.
            float baseW = 260, height = 210;
            int steps = Big ? 4 : 3;
            for (int i = 0; i < steps; i++)
            {
                float y0 = -height / 2 + height * i / steps, y1 = -height / 2 + height * (i + 1) / steps;
                float w0 = baseW * (1 - (float)i / steps * 0.85f), w1 = baseW * (1 - (float)(i + 1) / steps * 0.85f) + 10;
                Beam(new Vector3(0, y0, 0), new Vector3(0, y1, 0), w0, w0, i % 2 == 0 ? "hull" : "accent", "taperedBox", w1 / w0);
            }
            Beam(new Vector3(0, height / 2, 0), new Vector3(0, height / 2 + 40, 0), 30, 30, "glow", "taperedBox", 0.05f);
            // Docking slots along the base edges, and the main bay.
            for (int i = -2; i <= 2; i++)
            {
                if (i == 0) continue;
                Box(new Vector3(i * 45, -height / 2 + 20, -baseW / 2 + 2), 16, 6, 4, "glow");
            }
            Box(new Vector3(0, -height / 2 + 30, -baseW / 2 + 10), 50, 40, 24, "trim");
            Box(new Vector3(0, -height / 2 + 30, -baseW / 2 - 2.5f), 34, 26, 1, "glow");
            Lights(new Vector3(0, -height / 2, 0), baseW * 0.7f, 0, 4, 6);
        }

        public void Asteroid()
        {
            // A clump of rock, lumpy from the seed.
            Add("sphere", Vector3.Zero, 230, 190, 210, "#5c544c", sides: 12);
            for (int i = 0; i < 6; i++)
            {
                var at = new Vector3(rng.Range(-90, 90), rng.Range(-70, 70), rng.Range(-40, 90));
                float s = rng.Range(70, 130);
                Add("sphere", at, s, s * rng.Range(0.7f, 1.1f), s * rng.Range(0.8f, 1.2f), i % 2 == 0 ? "#655b52" : "#4f4842", sides: 10);
            }
            // The cut bay and its lit mouth, then gantries and lights on the rock.
            Box(new Vector3(0, 0, -95), 60, 40, 30, "trim");
            Box(new Vector3(0, 0, -110.5f), 44, 26, 1, "glow");
            for (int i = 0; i < (Big ? 6 : 4); i++)
            {
                float a = MathF.Tau * i / 4 + rng.Range(0, 0.5f);
                var root = new Vector3(MathF.Cos(a) * 100, MathF.Sin(a) * 80, -60);
                var tip = root + new Vector3(MathF.Cos(a) * 60, MathF.Sin(a) * 40, -40);
                Beam(root, tip, 6, 6, "trim");
                Box(tip, 10, 10, 10, "accent");
                Box(tip + new Vector3(0, 7, 0), 3, 3, 3, "glow");
            }
            if (Citadel) Beam(new Vector3(0, 80, 0), new Vector3(0, 200, 0), 16, 16, "accent", "cylinder", 0.3f, 8);
        }

        public void Spindle()
        {
            float length = Big ? 420 : 320;
            Beam(new Vector3(0, 0, length / 2), new Vector3(0, 0, -length / 2), 110, 110, "hull", "cylinder", sides: 16);
            Beam(new Vector3(0, 0, length / 2), new Vector3(0, 0, length / 2 + 50), 110, 110, "trim", "cylinder", 0.4f, 16);
            Beam(new Vector3(0, 0, -length / 2), new Vector3(0, 0, -length / 2 - 30), 110, 110, "trim", "cylinder", 0.6f, 16);
            // Bands round the drum, and lit windows between them.
            int bands = Big ? 5 : 3;
            for (int i = 0; i < bands; i++)
            {
                float z = -length / 2 + length * (i + 0.5f) / bands;
                Beam(new Vector3(0, 0, z - 6), new Vector3(0, 0, z + 6), 118, 118, "accent", "cylinder", sides: 16);
                for (int k = 0; k < 8; k++)
                {
                    float a = MathF.Tau * k / 8;
                    Box(new Vector3(MathF.Cos(a) * 56, MathF.Sin(a) * 56, z + length / bands / 2), 3, 3, length / bands * 0.6f, "glow", a * 180 / MathF.PI);
                }
            }
            // The bay on the nose cap.
            Bay(-length / 2 - 30, 30, 14);
            if (Citadel)
                foreach (float s in new[] { 1f, -1f })
                    Beam(new Vector3(0, 0, 0), new Vector3(s * 160, 0, 0), 14, 14, "trim");
        }

        public void Lattice()
        {
            int n = Big ? 4 : 3;
            float gap = 70, off = (n - 1) * gap / 2;
            Vector3 Node(int x, int y, int z) => new(x * gap - off, y * gap - off, z * gap - off);
            for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            for (int z = 0; z < n; z++)
            {
                var p = Node(x, y, z);
                bool corner = (x == 0 || x == n - 1) && (y == 0 || y == n - 1) && (z == 0 || z == n - 1);
                Box(p, 14, 14, 14, corner ? "accent" : "hull");
                if ((x + y + z) % 3 == 0) Box(p + new Vector3(0, 9, 0), 3, 3, 3, "glow");
                if (x + 1 < n) Beam(p, Node(x + 1, y, z), 4, 4, "trim");
                if (y + 1 < n) Beam(p, Node(x, y + 1, z), 4, 4, "trim");
                if (z + 1 < n) Beam(p, Node(x, y, z + 1), 4, 4, "trim");
            }
            // A solid core holds the bay, which opens through the front face.
            Box(Vector3.Zero, 50, 50, 50, "hull");
            Beam(new Vector3(0, 0, -25), new Vector3(0, 0, -off - 10), 26, 26, "trim");
            Box(new Vector3(0, 0, -off - 10.5f), 20, 12, 1, "glow");
        }

        public void Crown()
        {
            // A base disc and a dome on it.
            Beam(new Vector3(0, -30, 0), new Vector3(0, -10, 0), 260, 260, "trim", "cylinder", sides: 20);
            Add("dome", new Vector3(0, -10, 0), 150, 120, 150, "hull", sides: 20);
            Box(new Vector3(0, 50, 0), 8, 8, 8, "glow");
            // Spires round the edge, taller towards the back.
            int spires = Big ? 10 : 7;
            for (int i = 0; i < spires; i++)
            {
                float a = MathF.Tau * i / spires;
                var root = new Vector3(MathF.Cos(a) * 110, -20, MathF.Sin(a) * 110);
                float height = 120 + 60 * (0.5f + 0.5f * MathF.Sin(a)) + rng.Range(-10, 20);
                Beam(root, root + new Vector3(0, height, 0), 18, 18, i % 2 == 0 ? "accent" : "hull", "cylinder", 0.1f, 8);
                Box(root + new Vector3(0, height + 4, 0), 4, 4, 4, "glow");
            }
            // The bay under the dome's front.
            Box(new Vector3(0, -20, -120), 50, 24, 30, "hull");
            Box(new Vector3(0, -20, -135.5f), 34, 12, 1, "glow");
            if (Citadel) Beam(new Vector3(0, 40, 0), new Vector3(0, 230, 0), 24, 24, "accent", "cylinder", 0.05f, 10);
        }
    }
}
