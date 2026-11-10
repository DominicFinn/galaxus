using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Galaxus.Core.Content;

// Exports every ship, warship and station as flat-shaded triangles for the website's three.js viewer.
//
//   dotnet run --project tools/WebExport -- <data folder> <output folder>
//
// Each model becomes <id>.bin: float32 positions (9 per triangle), then uint8 sRGB colours
// (9 per triangle). Lit triangles come first and glow triangles after, so the page can draw the
// glow unshaded. models.json lists every model with its counts and what the viewer shows about it.
// Triangles are rewound anticlockwise, which is what three.js treats as the front face.

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: WebExport <data folder> <output folder>");
    return 2;
}

var db = ContentDatabase.Load(new DirectoryContentSource(args[0]), throwOnProblems: false);
string outDir = args[1];
Directory.CreateDirectory(outDir);

var manifest = new List<object>();

void Export(string id, string name, string category, string builder, string cls, string description, float radius, HullDef hull, PaletteDef palette, object? stats)
{
    var triangles = ShipGeometry.Build(hull);
    var lit = triangles.Where(t => t.Color != "glow").ToList();
    var glow = triangles.Where(t => t.Color == "glow").ToList();
    var ordered = lit.Concat(glow).ToList();

    using var stream = File.Create(Path.Combine(outDir, id + ".bin"));
    using var writer = new BinaryWriter(stream);
    foreach (var t in ordered)
        foreach (var v in new[] { t.A, t.C, t.B })
        {
            writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z);
        }
    foreach (var t in ordered)
    {
        var (r, g, b) = Rgb(Resolve(t.Color, palette));
        for (int i = 0; i < 3; i++) { writer.Write(r); writer.Write(g); writer.Write(b); }
    }

    float extent = MathF.Max(ContentDatabase.ModelExtent(hull), 0.1f);
    manifest.Add(new { id, name, category, builder, cls, description, radius, extent, lit = lit.Count, glow = glow.Count, glowColour = palette.Glow, stats });
}

string BuilderName(string id) => db.Builders.TryGetValue(id, out var b) ? b.Name : id;

foreach (var hull in db.Hulls.Values.OrderBy(h => BuilderName(h.Builder)).ThenBy(h => h.Radius))
{
    var palette = db.Builders.TryGetValue(hull.Builder, out var b) ? b.Palette : new PaletteDef();
    bool warship = hull.IsCapital || hull.Id.StartsWith("fm-unbound");
    string category = hull.Class == "drone" ? "drone" : warship ? "warship" : "ship";
    var stats = new { speed = hull.BaseSpeed, hull = hull.HullPoints, cargo = hull.CargoTonnes, mass = hull.Mass, price = hull.ForSale ? hull.Price : 0 };
    Export(hull.Id, hull.Name, category, BuilderName(hull.Builder), hull.Class, hull.Description, hull.Radius, hull, palette, stats);
}

// Stations use the same shapes as the game's MeshFactory.BuildStation.
foreach (var (id, name, citadel, accent, note) in new[]
{
    ("station", "Station", false, "#4a78d0", "The standard station: a hub with a docking bay and a habitat ring."),
    ("hub", "Hub", true, "#e0a020", "Where billions live: habitat rings stacked along a long spine."),
    ("citadel", "Citadel", true, "#9ad8c8", "A great power's capital: a great outer ring and four towers."),
})
{
    var shapes = StationShapes(hub: citadel, citadel: id == "citadel");
    var palette = new PaletteDef { Hull = "#8d939c", Accent = accent, Trim = "#3c4048", Glow = "#ffe6a0" };
    var hull = new HullDef { Id = id, Model = shapes };
    Export(id, name, "station", "", "station", note, ContentDatabase.ModelExtent(hull), hull, palette, null);
}

File.WriteAllText(Path.Combine(outDir, "models.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
Console.WriteLine($"Exported {manifest.Count} models to {outDir}");
return 0;

static string Resolve(string name, PaletteDef p) => name switch
{
    "hull" => p.Hull, "accent" => p.Accent, "trim" => p.Trim, "glow" => p.Glow, _ => name,
};

static (byte, byte, byte) Rgb(string hex)
{
    hex = hex.TrimStart('#');
    if (hex.Length < 6) return (255, 0, 255);
    byte P(int i) => byte.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber);
    return (P(0), P(2), P(4));
}

static List<ShapeDef> StationShapes(bool hub, bool citadel)
{
    var shapes = new List<ShapeDef>
    {
        new() { Position = [0, 0, 0], Back = [60, 60], Front = [60, 60], Length = 90, Color = "hull" },
        new() { Position = [0, 0, -50], Back = [36, 36], Front = [36, 36], Length = 10, Color = "trim" },
        new() { Position = [0, 0, -55.5f], Back = [24, 10], Front = [24, 10], Length = 1, Color = "glow" },
        new() { Position = [0, 0, 60], Back = [20, 20], Front = [44, 44], Length = 30, Color = "trim" },
    };
    void Ring(float ringRadius, float z, int segments, bool spokes)
    {
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 360f / segments;
            float rad = angle * MathF.PI / 180f;
            float chord = 2 * ringRadius * MathF.Sin(MathF.PI / segments) + 4;
            shapes.Add(new ShapeDef
            {
                Position = [MathF.Cos(rad) * ringRadius, MathF.Sin(rad) * ringRadius, z],
                Back = [26, chord], Front = [26, chord], Length = 30, Roll = angle, Color = i % 3 == 0 ? "accent" : "hull",
            });
            if (spokes && i % 3 == 0)
                shapes.Add(new ShapeDef
                {
                    Position = [MathF.Cos(rad) * ringRadius * 0.55f, MathF.Sin(rad) * ringRadius * 0.55f, z],
                    Back = [ringRadius * 0.85f, 8], Front = [ringRadius * 0.85f, 8], Length = 8, Roll = angle, Color = "trim",
                });
            shapes.Add(new ShapeDef
            {
                Position = [MathF.Cos(rad) * (ringRadius + 13.5f), MathF.Sin(rad) * (ringRadius + 13.5f), z - 12],
                Back = [1.5f, 3], Front = [1.5f, 3], Length = 1, Roll = angle, Color = "glow",
            });
        }
    }
    Ring(150f, 0, 12, true);
    if (hub)
    {
        shapes.Add(new ShapeDef { Position = [0, 0, 110], Back = [30, 30], Front = [30, 30], Length = 160, Color = "hull" });
        Ring(110f, 120, 10, true);
        Ring(110f, -110, 10, true);
        shapes.Add(new ShapeDef { Position = [0, 0, -100], Back = [30, 30], Front = [30, 30], Length = 120, Color = "hull" });
    }
    if (citadel)
    {
        Ring(230f, 0, 18, true);
        foreach (var (x, y) in new[] { (0f, 1f), (0f, -1f), (1f, 0f), (-1f, 0f) })
        {
            shapes.Add(new ShapeDef { Position = [x * 70, y * 70, 0], Back = [22, 22], Front = [10, 10], Length = 260, Color = "accent" });
            shapes.Add(new ShapeDef { Position = [x * 70, y * 70, -131], Back = [10, 10], Front = [4, 4], Length = 2, Color = "glow" });
        }
    }
    return shapes;
}
