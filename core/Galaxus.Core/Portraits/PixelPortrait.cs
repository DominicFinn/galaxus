using System.Numerics;

namespace Galaxus.Core.Portraits;

/// <summary>
/// Turns a built portrait into 16-bit style pixel art: the shapes are drawn onto a small grid
/// with no smoothing, outlined in dark pixels, given dithered backdrops and eye highlights, and
/// snapped to the SNES's 15-bit colour range. The game shows the result scaled up with sharp pixels.
/// </summary>
public static class PixelPortrait
{
    public const int Width = 64, Height = 76;

    /// <summary>Returns RGBA pixels, row by row from the top, each channel 0 to 1.</summary>
    public static Vector4[] Render(List<PortraitShape> shapes)
    {
        var pixels = new Vector4[Width * Height];
        var layer = new int[Width * Height];
        // Zoom in a little about the face so heads fill the frame, as in classic portraits.
        float scale = Width / PortraitBuilder.Width * 1.12f;
        var pivot = new Vector2(50, 66);

        // 1. Draw each shape, sampling at pixel centres, and remember which shape owns each pixel.
        for (int s = 0; s < shapes.Count; s++)
        {
            var shape = shapes[s];
            var points = shape.Points.Select(p => (p - pivot) * scale + new Vector2(Width / 2f, Height * 0.56f)).ToArray();
            float minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
            for (int y = Math.Max(0, (int)minY); y <= Math.Min(Height - 1, (int)maxY); y++)
            for (int x = Math.Max(0, (int)minX); x <= Math.Min(Width - 1, (int)maxX); x++)
            {
                if (!Inside(points, new Vector2(x + 0.5f, y + 0.5f))) continue;
                int i = y * Width + x;
                // Faint shapes (glasses, grime) tint what's under them rather than replacing it.
                pixels[i] = shape.Colour.W >= 0.99f ? shape.Colour : Vector4.Lerp(pixels[i], shape.Colour with { W = 1 }, shape.Colour.W);
                if (shape.Colour.W >= 0.5f) layer[i] = s;
            }
        }

        // 2. Backdrop: the first two shapes. Dither between their colours down the frame.
        var top = shapes.Count > 1 ? shapes[1].Colour : shapes[0].Colour;
        var bottom = shapes[0].Colour;
        int[,] bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int i = y * Width + x;
            if (layer[i] > 1) continue;
            // Two flat tones with a dithered band between them.
            float t = Math.Clamp((y - Height * 0.35f) / (Height * 0.2f), 0, 1) * 16f;
            pixels[i] = bayer[y % 4, x % 4] < t ? Darken(bottom, 0.85f) : Lighten(top, 1.12f);
        }

        // 3. Outlines: a dark edge where a figure meets the backdrop, and a softer one where very
        //    different colours meet inside the figure (hair on skin, collar on neck).
        var outlined = (Vector4[])pixels.Clone();
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int i = y * Width + x;
            if (layer[i] <= 1) continue;
            bool edge = false, seam = false;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                int n = ny * Width + nx;
                if (layer[n] <= 1) edge = true;
                else if (layer[n] != layer[i] && n > i && Contrast(pixels[i], pixels[n]) > 0.28f) seam = true;
            }
            if (edge) outlined[i] = Darken(pixels[i], 0.32f);
            else if (seam) outlined[i] = Darken(pixels[i], 0.7f);
        }
        pixels = outlined;

        // 4. Catch-lights: the brightest small blobs in the eye band get a white pixel at their top left.
        for (int y = 33; y < 42; y++)
        for (int x = 18; x < 46; x++)
        {
            int i = y * Width + x;
            var c = pixels[i];
            bool eyeWhite = c.X > 0.8f && c.Y > 0.8f && c.Z > 0.75f;
            int below = (y + 1) * Width + x + 1;
            if (eyeWhite && Luma(pixels[below]) < 0.45f) pixels[below - Width - 1] = new Vector4(1, 1, 1, 1);
        }

        // 5. Snap to 15-bit colour, five bits a channel, like the 16-bit consoles.
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            pixels[i] = new Vector4(Snap(c.X), Snap(c.Y), Snap(c.Z), 1);
        }
        return pixels;
    }

    private static float Snap(float v) => MathF.Round(Math.Clamp(v, 0, 1) * 31f) / 31f;

    private static float Luma(Vector4 c) => c.X * 0.3f + c.Y * 0.59f + c.Z * 0.11f;

    private static float Contrast(Vector4 a, Vector4 b) => MathF.Abs(Luma(a) - Luma(b)) + Vector3.Distance(new(a.X, a.Y, a.Z), new(b.X, b.Y, b.Z)) * 0.3f;

    private static Vector4 Darken(Vector4 c, float f) => new(c.X * f, c.Y * f, c.Z * f * 1.05f, 1);

    private static Vector4 Lighten(Vector4 c, float f) => new(MathF.Min(1, c.X * f), MathF.Min(1, c.Y * f), MathF.Min(1, c.Z * f), 1);

    /// <summary>Point in convex polygon, either winding.</summary>
    private static bool Inside(Vector2[] polygon, Vector2 p)
    {
        int sign = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            float cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            if (MathF.Abs(cross) < 1e-6f) continue;
            int s = cross > 0 ? 1 : -1;
            if (sign == 0) sign = s;
            else if (s != sign) return false;
        }
        return sign != 0;
    }
}
