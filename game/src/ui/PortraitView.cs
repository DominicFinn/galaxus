using Godot;
using Galaxus.Core.Portraits;

namespace Galaxus.Ui;

/// <summary>Shows a person's painted portrait (or flat pixel art), scaled up with sharp pixels.</summary>
public partial class PortraitView : Control
{
    private ImageTexture? _texture;

    public PortraitView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    private int _width = PixelPortrait.Width, _height = PixelPortrait.Height;

    public void Clear()
    {
        _texture = null;
        QueueRedraw();
    }

    /// <summary>The painted, sculpted portrait for a person.</summary>
    public void ShowPerson(Galaxus.Core.Content.ContentDatabase db, Galaxus.Core.Galaxy.Galaxy galaxy, Galaxus.Core.People.Person person) =>
        ShowPixels(Galaxus.Core.People.PeopleDirectory.Painted(db, galaxy, person), SculptedPortrait.Width, SculptedPortrait.Height);

    public void Show(List<PortraitShape> shapes)
    {
        if (shapes.Count == 0) { Clear(); return; }
        ShowPixels(PixelPortrait.Render(shapes), PixelPortrait.Width, PixelPortrait.Height);
    }

    private void ShowPixels(System.Numerics.Vector4[] pixels, int width, int height)
    {
        _width = width;
        _height = height;
        var bytes = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            bytes[i * 4] = (byte)(pixels[i].X * 255);
            bytes[i * 4 + 1] = (byte)(pixels[i].Y * 255);
            bytes[i * 4 + 2] = (byte)(pixels[i].Z * 255);
            bytes[i * 4 + 3] = 255;
        }
        var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, bytes);
        _texture = ImageTexture.CreateFromImage(image);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_texture == null) return;
        float scale = Mathf.Min(Size.X / _width, Size.Y / _height);
        var size = new Vector2(_width, _height) * scale;
        var rect = new Rect2((Size - size) / 2, size);
        DrawTextureRect(_texture, rect, false);
        DrawRect(rect, Ui.Dim, false, 1);
    }
}
