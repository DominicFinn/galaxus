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

    /// <summary>Adds a triangle that is already wound and has its normal.</summary>
    public void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color color)
    {
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

        foreach (var t in ShipGeometry.Build(hull))
        {
            var target = t.Color == "glow" ? glow : lit;
            target.Add(t.A.ToGodot(), t.B.ToGodot(), t.C.ToGodot(), t.Normal.ToGodot(), ResolveColour(t.Color, palette));
        }

        var mesh = new ArrayMesh();
        lit.AddTo(mesh, LitMaterial);
        glow.AddTo(mesh, GlowMaterial);
        return mesh;
    }

    /// <summary>
    /// A station: a hub with a docking bay on its front face, spokes and a ring, built from the same
    /// tapered boxes as ships so it shares their look. Owner colour picks out the ring.
    /// </summary>
    public static ArrayMesh BuildStation(Color ownerColour)
    {
        var shapes = new List<ShapeDef>
        {
            new() { Position = [0, 0, 0], Back = [60, 60], Front = [60, 60], Length = 90, Color = "hull" },
            new() { Position = [0, 0, -50], Back = [36, 36], Front = [36, 36], Length = 10, Color = "trim" },
            new() { Position = [0, 0, -55.5f], Back = [24, 10], Front = [24, 10], Length = 1, Color = "glow" },
            new() { Position = [0, 0, 60], Back = [20, 20], Front = [44, 44], Length = 30, Color = "trim" },
        };
        const int segments = 12;
        const float ringRadius = 150f;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 360f / segments;
            float rad = Mathf.DegToRad(angle);
            float chord = 2 * ringRadius * Mathf.Sin(Mathf.Pi / segments) + 4;
            shapes.Add(new ShapeDef
            {
                Position = [Mathf.Cos(rad) * ringRadius, Mathf.Sin(rad) * ringRadius, 0],
                Back = [26, chord], Front = [26, chord], Length = 30, Roll = angle, Color = i % 3 == 0 ? "accent" : "hull",
            });
            if (i % 3 == 0)
                shapes.Add(new ShapeDef
                {
                    Position = [Mathf.Cos(rad) * ringRadius * 0.55f, Mathf.Sin(rad) * ringRadius * 0.55f, 0],
                    Back = [ringRadius * 0.85f, 8], Front = [ringRadius * 0.85f, 8], Length = 8, Roll = angle, Color = "trim",
                });
            shapes.Add(new ShapeDef
            {
                Position = [Mathf.Cos(rad) * (ringRadius + 13.5f), Mathf.Sin(rad) * (ringRadius + 13.5f), -12],
                Back = [1.5f, 3], Front = [1.5f, 3], Length = 1, Roll = angle, Color = "glow",
            });
        }
        var hull = new HullDef { Id = "station", Model = shapes };
        var palette = new PaletteDef { Hull = "#8d939c", Accent = "#" + ownerColour.ToHtml(false), Trim = "#3c4048", Glow = "#ffe6a0" };
        return BuildShip(hull, palette);
    }

    public static Color ResolveColour(string name, PaletteDef palette) => Color.FromHtml(name switch
    {
        "hull" => palette.Hull,
        "accent" => palette.Accent,
        "trim" => palette.Trim,
        "glow" => palette.Glow,
        _ => name,
    });

    /// <summary>A subdivided icosahedron, flat shaded, with a colour chosen per face.</summary>
    /// <param name="heightAt">Optional relief: how far to push each vertex out, as a fraction of the radius.</param>
    public static ArrayMesh Icosphere(float radius, int subdivisions, Func<Vector3, Color> colourAt, Material? material = null, Func<Vector3, float>? heightAt = null)
    {
        var mesh = new ArrayMesh();
        IcosphereTriangles(radius, subdivisions, colourAt, heightAt).AddTo(mesh, material ?? LitMaterial);
        return mesh;
    }

    /// <summary>The triangles of an icosphere, without touching the engine, so it can be built on a worker thread.</summary>
    public static FlatMeshBuilder IcosphereTriangles(float radius, int subdivisions, Func<Vector3, Color> colourAt, Func<Vector3, float>? heightAt = null)
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

        if (heightAt != null)
            for (int i = 0; i < vertices.Count; i++) vertices[i] *= 1 + heightAt(vertices[i]);
        var builder = new FlatMeshBuilder();
        foreach (var (a, b, c) in faces)
        {
            var centre = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            builder.Triangle(vertices[a] * radius, vertices[b] * radius, vertices[c] * radius, colourAt(centre.Normalized()), Vector3.Zero);
        }
        return builder;
    }
}
