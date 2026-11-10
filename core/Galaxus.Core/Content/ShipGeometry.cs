using System.Numerics;

namespace Galaxus.Core.Content;

/// <summary>One flat-shaded triangle of a ship model. Corners are wound clockwise seen from outside, as Godot expects.</summary>
public readonly record struct ShipTriangle(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal, string Color);

/// <summary>
/// Turns a hull's list of tapered boxes into triangles. Shared by the game's mesh builder and the
/// windowless preview tool, so both always draw the same shape.
/// </summary>
public static class ShipGeometry
{
    public static List<ShipTriangle> Build(HullDef hull)
    {
        var triangles = new List<ShipTriangle>();
        foreach (var shape in hull.Model)
        {
            AddTaperedBox(triangles, shape, mirrored: false);
            if (shape.Mirror) AddTaperedBox(triangles, shape, mirrored: true);
        }
        return triangles;
    }

    private static void AddTaperedBox(List<ShipTriangle> triangles, ShapeDef shape, bool mirrored)
    {
        float sign = mirrored ? -1 : 1;
        var centre = new Vector3(shape.Position[0] * sign, shape.Position[1], shape.Position[2]);
        var skew = new Vector2(shape.Skew[0] * sign, shape.Skew[1]);
        float roll = shape.Roll * MathF.PI / 180f * sign;
        float cos = MathF.Cos(roll), sin = MathF.Sin(roll);
        float half = shape.Length / 2f;

        // Back face sits towards +Z (the rear), front face towards -Z (the nose).
        Vector3 Corner(float[] face, float sx, float sy, Vector2 offset, float z)
        {
            float x = sx * face[0] / 2f, y = sy * face[1] / 2f;
            var p = new Vector2(x * cos - y * sin, x * sin + y * cos) + offset;
            return centre + new Vector3(p.X, p.Y, z);
        }

        (float, float)[] corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)];
        var back = corners.Select(c => Corner(shape.Back, c.Item1, c.Item2, Vector2.Zero, half)).ToArray();
        var front = corners.Select(c => Corner(shape.Front, c.Item1, c.Item2, skew, -half)).ToArray();
        var inside = (back.Aggregate(Vector3.Zero, (a, b) => a + b) + front.Aggregate(Vector3.Zero, (a, b) => a + b)) / 8f;

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Triangle(triangles, a, b, c, shape.Color, inside);
            Triangle(triangles, a, c, d, shape.Color, inside);
        }

        Quad(back[0], back[1], back[2], back[3]);
        Quad(front[0], front[1], front[2], front[3]);
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            Quad(back[i], back[j], front[j], front[i]);
        }
    }

    private static void Triangle(List<ShipTriangle> triangles, Vector3 a, Vector3 b, Vector3 c, string color, Vector3 inside)
    {
        var normal = Vector3.Cross(b - a, c - a);
        if (normal.LengthSquared() < 1e-10f) return; // degenerate, e.g. a pointed nose
        normal = Vector3.Normalize(normal);

        bool outward = Vector3.Dot(normal, (a + b + c) / 3f - inside) > 0;
        if (!outward) normal = -normal;
        // Clockwise from outside: (a, b, c) is anticlockwise seen from where the cross product points.
        if (outward) (b, c) = (c, b);

        triangles.Add(new ShipTriangle(a, b, c, normal, color));
    }
}
