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
        // Shapes that sit flush on each other share faces, which flicker as the renderer can't
        // tell which is in front. Each shape grows a hair more than the one before, so details
        // listed later (glow strips, panels, cockpits) always sit just proud of what's under them.
        float step = MathF.Max(0.004f, hull.Radius * 0.0004f);
        for (int i = 0; i < hull.Model.Count; i++)
        {
            var shape = hull.Model[i];
            float grow = step * (i + 1);
            AddShape(triangles, shape, mirrored: false, grow);
            if (shape.Mirror) AddShape(triangles, shape, mirrored: true, grow);
        }
        return triangles;
    }

    private static void AddShape(List<ShipTriangle> triangles, ShapeDef shape, bool mirrored, float grow)
    {
        switch (shape.Shape)
        {
            case "cylinder": AddCylinder(triangles, shape, mirrored, grow); break;
            case "sphere": AddSphere(triangles, shape, mirrored, grow, dome: false); break;
            case "dome": AddSphere(triangles, shape, mirrored, grow, dome: true); break;
            default: AddTaperedBox(triangles, shape, mirrored, grow); break;
        }
    }

    /// <summary>Places a point given relative to the shape's centre: roll, then pitch, then yaw, then out to the centre.</summary>
    private static Vector3 Place(ShapeDef shape, Vector3 local, bool mirrored)
    {
        float sign = mirrored ? -1 : 1;
        float roll = shape.Roll * MathF.PI / 180f * sign;
        var p = new Vector3(local.X * MathF.Cos(roll) - local.Y * MathF.Sin(roll), local.X * MathF.Sin(roll) + local.Y * MathF.Cos(roll), local.Z);
        if (shape.Pitch != 0) p = Vector3.Transform(p, Quaternion.CreateFromAxisAngle(Vector3.UnitX, shape.Pitch * MathF.PI / 180f));
        if (shape.Yaw != 0) p = Vector3.Transform(p, Quaternion.CreateFromAxisAngle(Vector3.UnitY, shape.Yaw * MathF.PI / 180f * sign));
        return new Vector3(shape.Position[0] * sign, shape.Position[1], shape.Position[2]) + p;
    }

    /// <summary>A round prism along Z, tapering from the back face's size to the front's: a tube, a tower or a cone.</summary>
    private static void AddCylinder(List<ShipTriangle> triangles, ShapeDef shape, bool mirrored, float grow)
    {
        int sides = shape.Sides > 2 ? shape.Sides : 12;
        float half = shape.Length / 2f + grow;
        var back = new Vector3[sides];
        var front = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = MathF.Tau * i / sides;
            back[i] = Place(shape, new Vector3(MathF.Cos(a) * (shape.Back[0] / 2 + grow), MathF.Sin(a) * (shape.Back[1] / 2 + grow), half), mirrored);
            front[i] = Place(shape, new Vector3(MathF.Cos(a) * (shape.Front[0] / 2 + grow) + shape.Skew[0], MathF.Sin(a) * (shape.Front[1] / 2 + grow) + shape.Skew[1], -half), mirrored);
        }
        var inside = Place(shape, Vector3.Zero, mirrored);
        var backCentre = Place(shape, new Vector3(0, 0, half), mirrored);
        var frontCentre = Place(shape, new Vector3(shape.Skew[0], shape.Skew[1], -half), mirrored);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            Triangle(triangles, back[i], back[j], front[j], shape.Color, inside);
            Triangle(triangles, back[i], front[j], front[i], shape.Color, inside);
            Triangle(triangles, backCentre, back[j], back[i], shape.Color, inside);
            Triangle(triangles, frontCentre, front[i], front[j], shape.Color, inside);
        }
    }

    /// <summary>An ellipsoid, Back[0] wide, Back[1] high and Length long, or its upper half for a dome.</summary>
    private static void AddSphere(List<ShipTriangle> triangles, ShapeDef shape, bool mirrored, float grow, bool dome)
    {
        int sides = shape.Sides > 3 ? shape.Sides : 16;
        int bands = Math.Max(3, sides / 2);
        float rx = shape.Back[0] / 2 + grow, ry = shape.Back[1] / 2 + grow, rz = shape.Length / 2 + grow;
        var inside = Place(shape, Vector3.Zero, mirrored);
        Vector3 At(int band, int i)
        {
            float lat = MathF.PI / 2 - MathF.PI * band / bands;
            if (dome) lat = MathF.PI / 2 - MathF.PI / 2 * band / bands;
            float lon = MathF.Tau * i / sides;
            return Place(shape, new Vector3(MathF.Cos(lat) * MathF.Cos(lon) * rx, MathF.Sin(lat) * ry, MathF.Cos(lat) * MathF.Sin(lon) * rz), mirrored);
        }
        for (int b = 0; b < bands; b++)
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Triangle(triangles, At(b, i), At(b, j), At(b + 1, j), shape.Color, inside);
                Triangle(triangles, At(b, i), At(b + 1, j), At(b + 1, i), shape.Color, inside);
            }
        if (dome)
        {
            // Close the base so the dome has a floor.
            var floor = Place(shape, Vector3.Zero, mirrored);
            var above = Place(shape, new Vector3(0, ry * 0.5f, 0), mirrored);
            for (int i = 0; i < sides; i++)
                Triangle(triangles, floor, At(bands, (i + 1) % sides), At(bands, i), shape.Color, above);
        }
    }

    private static void AddTaperedBox(List<ShipTriangle> triangles, ShapeDef shape, bool mirrored, float grow = 0)
    {
        float sign = mirrored ? -1 : 1;
        var centre = new Vector3(shape.Position[0] * sign, shape.Position[1], shape.Position[2]);
        var skew = new Vector2(shape.Skew[0] * sign, shape.Skew[1]);
        float roll = shape.Roll * MathF.PI / 180f * sign;
        float cos = MathF.Cos(roll), sin = MathF.Sin(roll);
        float half = shape.Length / 2f + grow;

        // Back face sits towards +Z (the rear), front face towards -Z (the nose).
        Vector3 Corner(float[] face, float sx, float sy, Vector2 offset, float z)
        {
            float x = sx * (face[0] / 2f + grow), y = sy * (face[1] / 2f + grow);
            var p = new Vector2(x * cos - y * sin, x * sin + y * cos) + offset;
            var local = new Vector3(p.X, p.Y, z);
            // Pitch and yaw turn the whole box about its centre, after the roll.
            if (shape.Pitch != 0) local = Vector3.Transform(local, Quaternion.CreateFromAxisAngle(Vector3.UnitX, shape.Pitch * MathF.PI / 180f));
            if (shape.Yaw != 0) local = Vector3.Transform(local, Quaternion.CreateFromAxisAngle(Vector3.UnitY, shape.Yaw * MathF.PI / 180f * sign));
            return centre + local;
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
