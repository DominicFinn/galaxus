using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using Galaxus.Core.Content;

// Renders a hull from four angles to a PNG with a small software rasteriser.
// No Godot and no window, so it never takes focus.
//
//   dotnet run --project tools/HullPreview -- <data folder> <hull id> <output.png>
//
// Layout: top left front three-quarter, top right side (nose left),
// bottom left top-down (nose up), bottom right rear three-quarter.
// The green ring in each view is the collision radius.

// Optional fourth argument "single": one large front three-quarter view, for galleries.
bool single = args.Length == 4 && args[3] == "single";
if (args.Length is not (3 or 4))
{
    Console.Error.WriteLine("usage: HullPreview <data folder> <hull id> <output.png> [single]");
    return 2;
}

var db = ContentDatabase.Load(new DirectoryContentSource(args[0]), throwOnProblems: false);
string hullId = args[1];
foreach (var problem in db.Problems.Where(p => p.Contains($"'{hullId}'")))
    Console.WriteLine($"Content problem: {problem}");
if (!db.Hulls.TryGetValue(hullId, out var hull))
{
    Console.Error.WriteLine($"No hull '{hullId}'. Known hulls: {string.Join(", ", db.Hulls.Keys.Order())}");
    return 1;
}
var palette = db.Builders.TryGetValue(hull.Builder, out var builder) ? builder.Palette : new PaletteDef();

int width = single ? 480 : 1280, height = single ? 320 : 720;
var image = new Canvas(width, height, new Vector3(0.08f, 0.09f, 0.12f));
var triangles = ShipGeometry.Build(hull);
float extent = MathF.Max(ContentDatabase.ModelExtent(hull), hull.Radius);
float scale = (single ? height * 0.62f : height / 2f * 0.45f) / extent; // pixels per metre, so the model fills each cell

// Same euler order as Godot (Y, then X, then Z), angles in degrees.
// Front 3/4 tilts the nose down so it looks onto the top of the ship, like the rear view.
(int Col, int Row, float X, float Y)[] views = single ? [(0, 0, -22, 215)] : [(0, 0, -20, 215), (1, 0, 0, 90), (0, 1, 90, 0), (1, 1, 20, 35)];
var lightDir = Vector3.Normalize(new Vector3(-0.5f, -0.6f, -0.6f));
var ambient = new Vector3(0.35f, 0.4f, 0.55f) * 0.35f;

foreach (var (col, row, rx, ry) in views)
{
    var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, ry * MathF.PI / 180f)
                   * Quaternion.CreateFromAxisAngle(Vector3.UnitX, rx * MathF.PI / 180f);
    var centre = single ? new Vector2(width / 2f, height / 2f) : new Vector2(width / 4f + col * width / 2f, height / 4f + row * height / 2f);
    image.ClearDepth();

    Vector3 Project(Vector3 p)
    {
        var r = Vector3.Transform(p, rotation);
        return new Vector3(centre.X + r.X * scale, centre.Y - r.Y * scale, r.Z);
    }

    foreach (var t in triangles)
    {
        var colour = Hex(t.Color switch
        {
            "hull" => palette.Hull,
            "accent" => palette.Accent,
            "trim" => palette.Trim,
            "glow" => palette.Glow,
            _ => t.Color,
        });
        if (t.Color != "glow")
        {
            var n = Vector3.Transform(t.Normal, rotation);
            // Key light as in the game, plus a soft fill from the opposite side so shaded faces
            // stay readable when checking a model. The game itself has no fill light.
            float diffuse = MathF.Max(0, Vector3.Dot(n, -lightDir)) * 1.3f + MathF.Max(0, Vector3.Dot(n, lightDir)) * 0.45f;
            colour = colour * (ambient + new Vector3(diffuse));
        }
        image.Triangle(Project(t.A), Project(t.B), Project(t.C), Vector3.Clamp(colour, Vector3.Zero, Vector3.One));
    }
    if (!single) image.Circle(centre, hull.Radius * scale, new Vector3(0.3f, 0.6f, 0.3f));
}

image.SavePng(args[2]);
Console.WriteLine($"Saved preview of {hull.Name} ({hull.Id}, {hull.Class}, radius {hull.Radius} m) to {args[2]}");
Console.WriteLine("Views: top left front 3/4, top right side (nose left), bottom left top (nose up), bottom right rear 3/4.");
return 0;

static Vector3 Hex(string hex) => new(
    int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber) / 255f,
    int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber) / 255f,
    int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber) / 255f);

sealed class Canvas(int width, int height, Vector3 background)
{
    private readonly Vector3[] _pixels = Enumerable.Repeat(background, width * height).ToArray();
    private readonly float[] _depth = new float[width * height];

    public void ClearDepth() => Array.Fill(_depth, float.NegativeInfinity);

    /// <summary>Fills a triangle with a z-buffer. Larger z is nearer the camera.</summary>
    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 colour)
    {
        float area = Edge(a, b, c.X, c.Y);
        if (MathF.Abs(area) < 1e-6f) return;
        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        int maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        int maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));

        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            float px = x + 0.5f, py = y + 0.5f;
            float w0 = Edge(b, c, px, py) / area, w1 = Edge(c, a, px, py) / area, w2 = Edge(a, b, px, py) / area;
            if (w0 < 0 || w1 < 0 || w2 < 0) continue;
            float z = w0 * a.Z + w1 * b.Z + w2 * c.Z;
            int i = y * width + x;
            if (z <= _depth[i]) continue;
            _depth[i] = z;
            _pixels[i] = colour;
        }
    }

    private static float Edge(Vector3 a, Vector3 b, float x, float y) => (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);

    public void Circle(Vector2 centre, float radius, Vector3 colour)
    {
        int steps = Math.Max(64, (int)(radius * 8));
        for (int s = 0; s < steps; s++)
        {
            float angle = s * MathF.Tau / steps;
            int x = (int)(centre.X + MathF.Cos(angle) * radius), y = (int)(centre.Y + MathF.Sin(angle) * radius);
            if (x >= 0 && y >= 0 && x < width && y < height) _pixels[y * width + x] = colour;
        }
    }

    public void SavePng(string path)
    {
        var raw = new byte[height * (width * 3 + 1)];
        for (int y = 0; y < height; y++)
        {
            int row = y * (width * 3 + 1);
            raw[row] = 0; // no filter
            for (int x = 0; x < width; x++)
            {
                var p = _pixels[y * width + x];
                raw[row + 1 + x * 3] = (byte)(p.X * 255);
                raw[row + 2 + x * 3] = (byte)(p.Y * 255);
                raw[row + 3 + x * 3] = (byte)(p.Z * 255);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(raw);

        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // RGB
        Chunk(file, "IHDR", header);
        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        stream.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, Crc32([.. typeBytes, .. data]));
        stream.Write(crc);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }
}
