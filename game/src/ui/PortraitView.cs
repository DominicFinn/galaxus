using Godot;
using Galaxus.Core.Portraits;

namespace Galaxus.Ui;

/// <summary>Shows a generated face as 16-bit style pixel art, scaled up with sharp pixels.</summary>
public partial class PortraitView : Control
{
    private ImageTexture? _texture;

    public PortraitView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void Show(List<PortraitShape> shapes)
    {
        if (shapes.Count == 0)
        {
            _texture = null;
            QueueRedraw();
            return;
        }
        var pixels = PixelPortrait.Render(shapes);
        var bytes = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            bytes[i * 4] = (byte)(pixels[i].X * 255);
            bytes[i * 4 + 1] = (byte)(pixels[i].Y * 255);
            bytes[i * 4 + 2] = (byte)(pixels[i].Z * 255);
            bytes[i * 4 + 3] = 255;
        }
        var image = Image.CreateFromData(PixelPortrait.Width, PixelPortrait.Height, false, Image.Format.Rgba8, bytes);
        _texture = ImageTexture.CreateFromImage(image);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_texture == null) return;
        float scale = Mathf.Min(Size.X / PixelPortrait.Width, Size.Y / PixelPortrait.Height);
        var size = new Vector2(PixelPortrait.Width, PixelPortrait.Height) * scale;
        var rect = new Rect2((Size - size) / 2, size);
        DrawTextureRect(_texture, rect, false);
        DrawRect(rect, Ui.Dim, false, 1);
    }
}
