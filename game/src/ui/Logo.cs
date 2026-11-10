using Godot;
using Galaxus.Core;

namespace Galaxus.Ui;

/// <summary>
/// The Galaxus logo, drawn in code: a four-armed spiral galaxy inside a gate ring, beside the
/// name in wide-spaced pixel capitals. Scale sets the size; Alpha fades it.
/// </summary>
public partial class Logo : Control
{
    private static readonly Color Amber = new(1f, 0.74f, 0.32f);
    private static readonly Color Ring = new(0.45f, 1f, 0.55f);
    private readonly List<(Vector2 Offset, float Size, Color Colour)> _stars = [];

    public float LogoScale { get; set; } = 1f;
    public float Alpha { get; set; } = 1f;
    public float Spin { get; set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        // Four arms, as in the lore: Meridian, Tolland Reach, Anvil Drift and the Shroud Arm.
        var rng = new Rng("galaxus-logo");
        for (int i = 0; i < 420; i++)
        {
            int arm = i % 4;
            float t = MathF.Pow(rng.NextFloat(), 0.8f);
            float r = 3 + t * 30;
            float angle = arm * MathF.PI / 2 + t * 4.2f + (rng.NextFloat() - 0.5f) * 0.5f;
            var colour = t < 0.25f ? new Color(1f, 0.92f, 0.7f) : rng.NextFloat() < 0.75f ? new Color(0.75f, 0.85f, 1f) : Amber;
            colour.A = 1 - t * 0.6f;
            _stars.Add((new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * r, rng.NextFloat() < 0.15f ? 2 : 1, colour));
        }
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var centre = Size / 2;
        float s = LogoScale;
        var emblem = centre + new Vector2(-118, 0) * s;

        // Gate ring: the elves' network that everything still runs on.
        DrawArc(emblem, 40 * s, 0, Mathf.Tau, 64, new Color(Ring, 0.55f * Alpha), 2 * s);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8 + Spin * 0.2f;
            var p = emblem + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 40 * s;
            DrawRect(new Rect2(p - Vector2.One * 2 * s, Vector2.One * 4 * s), new Color(Ring, 0.9f * Alpha));
        }

        // The galaxy itself, slowly turning.
        float cos = Mathf.Cos(Spin), sin = Mathf.Sin(Spin);
        foreach (var (offset, size, colour) in _stars)
        {
            // Turn in the galaxy's plane, then squash it so it reads as a disc seen at an angle.
            var rotated = new Vector2(offset.X * cos - offset.Y * sin, (offset.X * sin + offset.Y * cos) * 0.62f);
            DrawRect(new Rect2(emblem + rotated * s, Vector2.One * size * s), new Color(colour, colour.A * Alpha));
        }
        DrawRect(new Rect2(emblem - Vector2.One * 2.5f * s, Vector2.One * 5 * s), new Color(1f, 0.95f, 0.8f, Alpha));

        // The name, in wide-spaced capitals with a hard drop shadow.
        var font = ThemeDB.FallbackFont;
        int fontSize = (int)(34 * s);
        const string name = "GALAXUS";
        float x = emblem.X + 56 * s;
        float baseline = centre.Y + 12 * s;
        foreach (char c in name)
        {
            string letter = c.ToString();
            DrawString(font, new Vector2(x + 2 * s, baseline + 2 * s), letter, HorizontalAlignment.Left, -1, fontSize, new Color(0.25f, 0.1f, 0.02f, Alpha));
            DrawString(font, new Vector2(x, baseline), letter, HorizontalAlignment.Left, -1, fontSize, new Color(Amber, Alpha));
            x += font.GetStringSize(letter, HorizontalAlignment.Left, -1, fontSize).X + 6 * s;
        }
        DrawLine(new Vector2(emblem.X + 56 * s, baseline + 8 * s), new Vector2(x - 6 * s, baseline + 8 * s), new Color(Ring, 0.6f * Alpha), 1);
    }
}
