using Godot;
using Galaxus.Core.Portraits;

namespace Galaxus.Ui;

/// <summary>Draws a generated face, scaled to fit, with a thin frame.</summary>
public partial class PortraitView : Control
{
    private List<PortraitShape> _shapes = [];

    public PortraitView() => MouseFilter = MouseFilterEnum.Ignore;

    public void Show(List<PortraitShape> shapes)
    {
        _shapes = shapes;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_shapes.Count == 0) return;
        float scale = Mathf.Min(Size.X / PortraitBuilder.Width, Size.Y / PortraitBuilder.Height);
        var offset = (Size - new Vector2(PortraitBuilder.Width, PortraitBuilder.Height) * scale) / 2;
        foreach (var shape in _shapes)
        {
            var points = shape.Points.Select(p => offset + new Vector2(p.X, p.Y) * scale).ToArray();
            var colour = new Color(shape.Colour.X, shape.Colour.Y, shape.Colour.Z, shape.Colour.W);
            // Shapes are convex, so a fan of triangles from the first point fills them exactly.
            for (int k = 1; k + 1 < points.Length; k++)
            {
                Vector2[] triangle = [points[0], points[k], points[k + 1]];
                if (Drawable(triangle)) DrawColoredPolygon(triangle, colour);
            }
        }
        DrawRect(new Rect2(offset, new Vector2(PortraitBuilder.Width, PortraitBuilder.Height) * scale), Ui.Dim, false, 1);
    }

    /// <summary>Skips slivers with no area (a clipped edge of a shadow, say), which Godot can't triangulate.</summary>
    private static bool Drawable(Vector2[] points)
    {
        if (points.Length < 3) return false;
        float area = 0;
        for (int i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            area += a.X * b.Y - b.X * a.Y;
        }
        return Mathf.Abs(area) > 0.05f;
    }
}
