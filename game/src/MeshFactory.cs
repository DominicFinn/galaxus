using Godot;
using Galaxus.Core.Content;

namespace Galaxus;

/// <summary>
/// Builds flat-shaded low-poly meshes. Every triangle gets its own vertices and face normal,
/// which gives the faceted look without any special shader.
/// </summary>
public sealed class FlatMeshBuilder
{
    private readonly List<Vector3> _vertices = [];
    private readonly List<Vector3> _normals = [];
    private readonly List<Color> _colors = [];

    public bool IsEmpty => _vertices.Count == 0;

    /// <summary>Adds a triangle facing away from <paramref name="inside"/>, whatever order the corners come in.</summary>
    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color, Vector3 inside)
    {
        var normal = (b - a).Cross(c - a);
        if (normal.LengthSquared() < 1e-10f) return; // degenerate, e.g. a pointed nose
        normal = normal.Normalized();

        bool outward = normal.Dot((a + b + c) / 3f - inside) > 0;
        if (!outward) normal = -normal;

        // Godot treats clockwise triangles as front faces. (a, b, c) is anticlockwise seen from the
        // side the cross product points to, so swap b and c when that side is the outside.
        if (outward) (b, c) = (c, b);

        _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
        for (int i = 0; i < 3; i++) { _normals.Add(normal); _colors.Add(color); }
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Vector3 inside)
    {
        Triangle(a, b, c, color, inside);
        Triangle(a, c, d, color, inside);
    }

    public void AddTo(ArrayMesh mesh, Material material)
    {
        if (IsEmpty) return;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _colors.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }
}

public static class MeshFactory
{
    public static readonly StandardMaterial3D LitMaterial = new()
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Roughness = 1f,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
    };

    public static readonly StandardMaterial3D GlowMaterial = new()
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    public static StandardMaterial3D Unshaded(Color color, bool transparent = false) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = transparent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        CullMode = transparent ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back,
    };

    /// <summary>Builds a ship from its hull's list of shapes, coloured with the builder's palette.</summary>
    public static ArrayMesh BuildShip(HullDef hull, PaletteDef palette)
    {
        var lit = new FlatMeshBuilder();
        var glow = new FlatMeshBuilder();

        foreach (var shape in hull.Model)
        {
            bool isGlow = shape.Color == "glow";
            var color = ResolveColour(shape.Color, palette);
            AddTaperedBox(isGlow ? glow : lit, shape, color, mirrored: false);
            if (shape.Mirror) AddTaperedBox(isGlow ? glow : lit, shape, color, mirrored: true);
        }

        var mesh = new ArrayMesh();
        lit.AddTo(mesh, LitMaterial);
        glow.AddTo(mesh, GlowMaterial);
        return mesh;
    }

    public static Color ResolveColour(string name, PaletteDef palette) => Color.FromHtml(name switch
    {
        "hull" => palette.Hull,
        "accent" => palette.Accent,
        "trim" => palette.Trim,
        "glow" => palette.Glow,
        _ => name,
    });

    private static void AddTaperedBox(FlatMeshBuilder builder, ShapeDef shape, Color color, bool mirrored)
    {
        float sign = mirrored ? -1 : 1;
        var centre = new Vector3(shape.Position[0] * sign, shape.Position[1], shape.Position[2]);
        var skew = new Vector2(shape.Skew[0] * sign, shape.Skew[1]);
        float roll = Mathf.DegToRad(shape.Roll) * sign;
        float half = shape.Length / 2f;

        // Back face sits towards +Z (the rear), front face towards -Z (the nose).
        Vector3 Corner(float w, float h, float sx, float sy, Vector2 offset, float z)
        {
            var p = new Vector2(sx * w / 2f, sy * h / 2f).Rotated(roll) + offset;
            return centre + new Vector3(p.X, p.Y, z);
        }

        Vector3[] back =
        [
            Corner(shape.Back[0], shape.Back[1], -1, -1, Vector2.Zero, half),
            Corner(shape.Back[0], shape.Back[1], 1, -1, Vector2.Zero, half),
            Corner(shape.Back[0], shape.Back[1], 1, 1, Vector2.Zero, half),
            Corner(shape.Back[0], shape.Back[1], -1, 1, Vector2.Zero, half),
        ];
        Vector3[] front =
        [
            Corner(shape.Front[0], shape.Front[1], -1, -1, skew, -half),
            Corner(shape.Front[0], shape.Front[1], 1, -1, skew, -half),
            Corner(shape.Front[0], shape.Front[1], 1, 1, skew, -half),
            Corner(shape.Front[0], shape.Front[1], -1, 1, skew, -half),
        ];

        var inside = (back[0] + back[1] + back[2] + back[3] + front[0] + front[1] + front[2] + front[3]) / 8f;
        builder.Quad(back[0], back[1], back[2], back[3], color, inside);
        builder.Quad(front[0], front[1], front[2], front[3], color, inside);
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            builder.Quad(back[i], back[j], front[j], front[i], color, inside);
        }
    }

    /// <summary>A subdivided icosahedron, flat shaded, with a colour chosen per face.</summary>
    public static ArrayMesh Icosphere(float radius, int subdivisions, Func<Vector3, Color> colourAt, Material? material = null)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var vertices = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (int i = 0; i < vertices.Count; i++) vertices[i] = vertices[i].Normalized();

        var faces = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11),
            (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9),
            (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };

        for (int s = 0; s < subdivisions; s++)
        {
            var cache = new Dictionary<(int, int), int>();
            int Midpoint(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (cache.TryGetValue(key, out int index)) return index;
                vertices.Add(((vertices[a] + vertices[b]) / 2f).Normalized());
                return cache[key] = vertices.Count - 1;
            }

            var next = new List<(int, int, int)>();
            foreach (var (a, b, c) in faces)
            {
                int ab = Midpoint(a, b), bc = Midpoint(b, c), ca = Midpoint(c, a);
                next.Add((a, ab, ca)); next.Add((b, bc, ab)); next.Add((c, ca, bc)); next.Add((ab, bc, ca));
            }
            faces = next;
        }

        var builder = new FlatMeshBuilder();
        foreach (var (a, b, c) in faces)
        {
            var centre = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            builder.Triangle(vertices[a] * radius, vertices[b] * radius, vertices[c] * radius, colourAt(centre.Normalized()), Vector3.Zero);
        }

        var mesh = new ArrayMesh();
        builder.AddTo(mesh, material ?? LitMaterial);
        return mesh;
    }
}
