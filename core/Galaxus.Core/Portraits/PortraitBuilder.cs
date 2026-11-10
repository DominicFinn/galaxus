using System.Globalization;
using System.Numerics;

namespace Galaxus.Core.Portraits;

/// <summary>
/// The pool of facial features for one race, from game/data/portraits. A person's face is built
/// by picking from these with their seed, so every face is different and always the same for
/// the same person.
/// </summary>
public sealed class PortraitStyleDef
{
    public string Race { get; set; } = "";
    /// <summary>Machine heads (visor, chassis, antenna) instead of a face.</summary>
    public bool Machine { get; set; }
    public List<string> Skin { get; set; } = [];
    public List<string> Hair { get; set; } = [];
    public List<string> Eyes { get; set; } = [];
    public List<string> Clothing { get; set; } = [];
    /// <summary>round, pointed, long, small or none.</summary>
    public string Ears { get; set; } = "round";
    /// <summary>Half the head's width, [min, max], in portrait units (the portrait is 100 by 120).</summary>
    public float[] HeadWidth { get; set; } = [16, 20];
    /// <summary>Jaw width as a fraction of head width, [min, max]. Lower is a narrower, pointier face.</summary>
    public float[] Jaw { get; set; } = [0.7f, 0.9f];
    public List<string> HairStyles { get; set; } = ["crop"];
    public float BeardChance { get; set; }
    public float MoustacheChance { get; set; }
    public bool Tusks { get; set; }
    public bool GlowEyes { get; set; }
    /// <summary>Chance of each mark: scar, freckles, tattoo, earring, cyber, eyepatch, seams.</summary>
    public Dictionary<string, float> Marks { get; set; } = [];
}

/// <summary>What someone wears for their work: drawn over the face so you can tell a miner from a customs officer.</summary>
public sealed class Attire
{
    /// <summary>none, hardhat, cap, beanie, helmet, headset, hood, goggles, bandana.</summary>
    public string Headwear { get; set; } = "none";
    /// <summary>none, glasses, shades, monocle, visor.</summary>
    public string Eyewear { get; set; } = "none";
    /// <summary>plain, uniform, armour, robe, overalls, fine, shawl, flight, apron.</summary>
    public string Collar { get; set; } = "plain";
    /// <summary>Any of: lamp, badge, grime, medal, earpiece.</summary>
    public List<string> Extras { get; set; } = [];

    public static readonly HashSet<string> Headwears = ["none", "hardhat", "cap", "beanie", "helmet", "headset", "hood", "goggles", "bandana"];
    public static readonly HashSet<string> Eyewears = ["none", "glasses", "shades", "monocle", "visor"];
    public static readonly HashSet<string> Collars = ["plain", "uniform", "armour", "robe", "overalls", "fine", "shawl", "flight", "apron"];
    public static readonly HashSet<string> ExtraNames = ["lamp", "badge", "grime", "medal", "earpiece"];
}

/// <summary>A flat-coloured convex polygon in portrait space (x 0 to 100, y 0 to 120, y down).</summary>
public sealed record PortraitShape(Vector2[] Points, Vector4 Colour);

/// <summary>
/// Builds a portrait as a stack of flat-coloured convex shapes, back to front. The game draws
/// them as polygons and the gallery tool rasterises them, so both look the same.
/// </summary>
public static class PortraitBuilder
{
    public const float Width = 100, Height = 120;

    public static readonly HashSet<string> HairStyleNames = ["bald", "crop", "side-part", "long", "bun", "mohawk", "braids", "hood", "topknot", "shaved"];
    public static readonly HashSet<string> EarTypes = ["round", "pointed", "long", "small", "none"];
    public static readonly HashSet<string> MarkNames = ["scar", "freckles", "tattoo", "earring", "cyber", "eyepatch", "seams"];

    /// <param name="clothing">Optional #rrggbb for the collar, such as the person's faction colour.</param>
    /// <param name="gender">woman, man or neither. Shapes the jaw, brows, lips, beard and hair.</param>
    public static List<PortraitShape> Build(PortraitStyleDef style, ulong seed, string? clothing = null, Attire? attire = null, string gender = "neither")
    {
        attire ??= new Attire();
        var rng = new Rng(seed);
        var shapes = new List<PortraitShape>();
        var cloth = Colour(clothing ?? Pick(rng, style.Clothing, "#445566"));
        var background = Mix(cloth, new Vector4(0.06f, 0.08f, 0.1f, 1), 0.78f);

        // Backdrop: a dark wash of the clothing colour, lighter at the top.
        shapes.Add(Rect(0, 0, Width, Height, background));
        shapes.Add(Rect(0, 0, Width, 40, Mix(background, Vector4.One, 0.05f)));

        if (style.Machine) BuildMachine(shapes, rng, style, cloth, attire);
        else BuildFace(shapes, rng, style, cloth, attire, gender);
        return shapes;
    }

    private static void BuildFace(List<PortraitShape> s, Rng rng, PortraitStyleDef style, Vector4 cloth, Attire attire, string gender)
    {
        bool woman = gender == "woman", man = gender == "man";
        var skin = Colour(Pick(rng, style.Skin, "#c89a7a"));
        var hair = Colour(Pick(rng, style.Hair, "#3a2a20"));
        var eye = Colour(Pick(rng, style.Eyes, "#4a6a8a"));
        var shade = Mix(skin, new Vector4(0.1f, 0.08f, 0.12f, 1), 0.22f);
        float w = rng.Range(style.HeadWidth[0], style.HeadWidth[1]) * (woman ? 0.95f : 1f);
        // Women's jaws sit towards the narrow end of the race's range, men's towards the broad end.
        float jawT = woman ? rng.Range(0f, 0.6f) : man ? rng.Range(0.4f, 1f) : rng.NextFloat();
        float jaw = style.Jaw[0] + (style.Jaw[1] - style.Jaw[0]) * jawT;
        float chin = w * jaw * rng.Range(0.35f, 0.55f);
        const float cx = 50, eyeY = 58;
        string hairStyle = Pick(rng, style.HairStyles, "crop");
        // Lean hair choices by gender without ruling anything out.
        if (woman && hairStyle is "bald" or "shaved" or "crop" && rng.NextFloat() < 0.55f && style.HairStyles.Intersect(["long", "bun", "braids"]).Any())
            hairStyle = Pick(rng, [.. style.HairStyles.Intersect(["long", "bun", "braids"])], hairStyle);
        if (man && hairStyle is "long" or "bun" && rng.NextFloat() < 0.4f && style.HairStyles.Intersect(["crop", "shaved", "side-part"]).Any())
            hairStyle = Pick(rng, [.. style.HairStyles.Intersect(["crop", "shaved", "side-part"])], hairStyle);
        if (attire.Headwear == "hood") hairStyle = "hood";
        // Hard hats, helmets and caps sit on short hair; long hair still shows below them.
        if (attire.Headwear is "hardhat" or "helmet" or "cap" or "beanie" && hairStyle is "bun" or "mohawk" or "topknot") hairStyle = "crop";
        // Dwarf women keep a chance of a beard; other women don't grow them.
        float beardChance = woman ? (style.BeardChance > 0.6f ? style.BeardChance * 0.35f : 0) : style.BeardChance * (man ? 1.4f : 1f);
        bool beard = rng.NextFloat() < beardChance;
        bool moustache = beard || (!woman && rng.NextFloat() < style.MoustacheChance * (man ? 1.4f : 1f));
        bool Mark(string name) => rng.NextFloat() < style.Marks.GetValueOrDefault(name);
        bool scar = Mark("scar"), freckles = Mark("freckles"), tattoo = Mark("tattoo"), earring = Mark("earring") || (woman && rng.NextFloat() < 0.3f);
        bool cyber = Mark("cyber"), eyepatch = !cyber && Mark("eyepatch"), seams = Mark("seams");

        // Hair that hangs behind the head goes first.
        if (hairStyle is "long" or "braids")
            s.Add(Poly(hair, (cx - w - 4, 40), (cx + w + 4, 40), (cx + w + 6, 100), (cx - w - 6, 100)));
        if (hairStyle == "hood")
            s.Add(Poly(Mix(cloth, new Vector4(0, 0, 0, 1), 0.45f), (cx - w - 9, 50), (cx - w + 2, 22), (cx, 16), (cx + w - 2, 22), (cx + w + 9, 50), (cx + w + 12, 100), (cx - w - 12, 100)));

        // Shoulders and collar.
        s.Add(Poly(cloth, (6, 120), (94, 120), (82, 97), (18, 97)));
        s.Add(Poly(Mix(cloth, Vector4.One, 0.18f), (38, 97), (62, 97), (50, 112)));
        DrawCollar(s, attire, cloth);
        s.Add(Poly(shade, (cx - w * 0.5f, 70), (cx + w * 0.5f, 70), (cx + w * 0.55f, 99), (cx - w * 0.55f, 99)));
        s.Add(Poly(skin, (42, 97), (58, 97), (50, 106)));

        // Ears sit behind the head.
        foreach (int side in new[] { -1, 1 })
        {
            float ex = cx + side * w;
            switch (style.Ears)
            {
                case "pointed": s.Add(Poly(shade, (ex, 52), (ex + side * 12, 40), (ex + side * 2, 64))); break;
                case "long": s.Add(Poly(shade, (ex, 50), (ex + side * 20, 30), (ex + side * 3, 64))); break;
                case "small": s.Add(Ellipse(ex, 58, 3, 4.5f, shade)); break;
                case "round": s.Add(Ellipse(ex, 58, 4, 6.5f, shade)); break;
            }
            if (earring && side == 1 && style.Ears != "none") s.Add(Ellipse(ex + 2, 66, 1.6f, 1.6f, new Vector4(0.95f, 0.78f, 0.3f, 1)));
        }

        // Head: an upper ellipse into cheekbones, jaw and chin. Then the shadow side, for the flat-shaded look.
        var head = new List<Vector2>();
        for (int i = 0; i <= 10; i++)
        {
            float a = MathF.PI + i * MathF.PI / 10;
            head.Add(new Vector2(cx + MathF.Cos(a) * w, 55 + MathF.Sin(a) * 26));
        }
        head.Add(new Vector2(cx + w * 0.98f, 63));
        head.Add(new Vector2(cx + w * jaw, 74));
        head.Add(new Vector2(cx + chin, 82));
        head.Add(new Vector2(cx - chin, 82));
        head.Add(new Vector2(cx - w * jaw, 74));
        head.Add(new Vector2(cx - w * 0.98f, 63));
        s.Add(new PortraitShape([.. head], skin));
        s.Add(new PortraitShape(ClipRight(head, cx + 2), Mix(skin, shade, 0.45f)));

        if (freckles)
            for (int i = 0; i < 9; i++)
                s.Add(Ellipse(cx + rng.Range(-w * 0.7f, w * 0.7f), rng.Range(61, 67), 0.6f, 0.6f, Mix(skin, shade, 0.9f)));
        if (tattoo)
        {
            var ink = Mix(cloth, new Vector4(0.1f, 0.1f, 0.2f, 1), 0.3f);
            for (int k = 0; k < 2; k++) s.Add(Line(cx - w * 0.75f + k * 3, 61, cx - w * 0.55f + k * 3, 70, 1.1f, ink));
        }

        // Eyes, brows, nose and mouth.
        float eyeGap = w * rng.Range(0.38f, 0.48f), eyeH = rng.Range(1.8f, 3.4f);
        float browTilt = rng.Range(-2.2f, 2.2f), browThick = woman ? rng.Range(0.8f, 1.4f) : man ? rng.Range(1.6f, 2.6f) : rng.Range(1, 2.4f);
        foreach (int side in new[] { -1, 1 })
        {
            float ex = cx + side * eyeGap;
            bool patched = eyepatch && side == 1, plated = cyber && side == 1;
            if (patched) s.Add(Ellipse(ex, eyeY, 5, 4, new Vector4(0.08f, 0.07f, 0.07f, 1)));
            else if (plated)
            {
                s.Add(Poly(new Vector4(0.55f, 0.58f, 0.62f, 1), (ex - 6, eyeY - 5), (ex + 6, eyeY - 6), (ex + 7, eyeY + 5), (ex - 5, eyeY + 6)));
                s.Add(Ellipse(ex, eyeY, 1.8f, 1.8f, new Vector4(1f, 0.25f, 0.2f, 1)));
            }
            else if (style.GlowEyes)
            {
                s.Add(Ellipse(ex, eyeY, 4.2f, eyeH, Mix(eye, new Vector4(0, 0, 0, 1), 0.5f)));
                s.Add(Ellipse(ex, eyeY, 3f, eyeH * 0.7f, eye));
            }
            else
            {
                s.Add(Ellipse(ex, eyeY, 4.2f, eyeH, new Vector4(0.93f, 0.92f, 0.88f, 1)));
                s.Add(Ellipse(ex, eyeY, 2f, MathF.Min(2f, eyeH), eye));
                s.Add(Ellipse(ex, eyeY, 0.9f, 0.9f, new Vector4(0.05f, 0.05f, 0.06f, 1)));
            }
            if (!patched)
                s.Add(Line(ex - 4.5f, eyeY - 5 - side * browTilt * 0.5f, ex + 4.5f, eyeY - 5 + side * browTilt * 0.5f, browThick, Mix(hair, shade, 0.3f)));
        }
        if (eyepatch) s.Add(Line(cx - w, 50, cx + w, 62, 0.8f, new Vector4(0.08f, 0.07f, 0.07f, 1)));

        if (!eyepatch) DrawEyewear(s, attire.Eyewear, cx, eyeY, eyeGap);

        float noseW = rng.Range(2.2f, style.Tusks ? 5.5f : 4.2f), noseL = rng.Range(7, 11);
        s.Add(Poly(Mix(skin, shade, 0.7f), (cx + 0.5f, eyeY + 1), (cx + noseW, eyeY + noseL), (cx - noseW * 0.6f, eyeY + noseL)));

        float mouthY = rng.Range(72, 75), mouthW = rng.Range(4.5f, 7.5f);
        var lips = Mix(skin, new Vector4(0.5f, 0.15f, 0.15f, 1), 0.35f);
        if (woman || rng.NextFloat() < 0.4f)
        {
            float full = woman ? rng.Range(2.4f, 3.4f) : 2;
            if (woman) lips = Mix(lips, new Vector4(0.6f, 0.2f, 0.25f, 1), rng.Range(0, 0.4f));
            s.Add(Poly(lips, (cx - mouthW, mouthY), (cx + mouthW, mouthY), (cx + mouthW * 0.6f, mouthY + full), (cx - mouthW * 0.6f, mouthY + full)));
        }
        else s.Add(Line(cx - mouthW, mouthY + rng.Range(-0.8f, 0.8f), cx + mouthW, mouthY, 1.3f, Mix(lips, shade, 0.4f)));
        if (style.Tusks)
            foreach (int side in new[] { -1, 1 })
                s.Add(Poly(new Vector4(0.93f, 0.9f, 0.8f, 1), (cx + side * (mouthW - 1.5f), mouthY + 1), (cx + side * (mouthW + 0.5f), mouthY + 1), (cx + side * (mouthW - 0.2f), mouthY - 5)));

        if (scar) s.Add(Line(cx + eyeGap - 3, eyeY - 9, cx + eyeGap + 4, eyeY + 9, 1.2f, Mix(skin, new Vector4(0.6f, 0.3f, 0.3f, 1), 0.5f)));
        if (seams)
        {
            var seam = Mix(skin, new Vector4(0.2f, 0.25f, 0.35f, 1), 0.5f);
            s.Add(Line(cx - w * 0.95f, 66, cx - chin, 80, 0.7f, seam));
            s.Add(Line(cx - w * 0.6f, 40, cx + w * 0.6f, 40, 0.7f, seam));
        }

        // Facial hair over the jaw.
        if (beard)
        {
            float length = style.BeardChance > 0.6f ? rng.Range(96, 112) : rng.Range(84, 94);
            s.Add(Poly(hair, (cx - w * 0.98f, 64), (cx + w * 0.98f, 64), (cx + w * jaw + 1, 78), (cx + chin + 2, length - 4), (cx, length), (cx - chin - 2, length - 4), (cx - w * jaw - 1, 78)));
            s.Add(Poly(lips, (cx - mouthW * 0.8f, mouthY), (cx + mouthW * 0.8f, mouthY), (cx + mouthW * 0.5f, mouthY + 1.6f), (cx - mouthW * 0.5f, mouthY + 1.6f)));
        }
        if (moustache) s.Add(Poly(hair, (cx - mouthW - 1, mouthY - 0.5f), (cx - 1, mouthY - 3.5f), (cx + 1, mouthY - 3.5f), (cx + mouthW + 1, mouthY - 0.5f)));

        // Hair on top.
        var cap = Mix(hair, Vector4.One, 0.06f);
        void Cap(float top, float forehead)
        {
            var points = new List<Vector2>();
            for (int i = 0; i <= 10; i++)
            {
                float a = MathF.PI + i * MathF.PI / 10;
                points.Add(new Vector2(cx + MathF.Cos(a) * (w + 1.5f), 55 + MathF.Sin(a) * (55 - top)));
            }
            // Cut the dome off at the hairline, which keeps it convex.
            s.Add(new PortraitShape(ClipAbove(points, forehead + 6), cap));
        }
        switch (hairStyle)
        {
            case "crop": Cap(27, 40); break;
            case "shaved": Cap(29, 36); break;
            case "side-part":
                Cap(26, 40);
                s.Add(Poly(cap, (cx - w, 40), (cx + w * 0.3f, 34), (cx - w * 0.2f, 50)));
                break;
            case "long": Cap(26, 42); break;
            case "braids":
                Cap(27, 41);
                foreach (int side in new[] { -1, 1 }) s.Add(Line(cx + side * (w + 3), 60, cx + side * (w + 4), 112, 3.5f, cap));
                break;
            case "bun":
                Cap(27, 40);
                s.Add(Ellipse(cx, 24, 7, 6, cap));
                break;
            case "mohawk": s.Add(Poly(cap, (cx - 3.5f, 26), (cx + 3.5f, 26), (cx + 3, 45), (cx - 3, 45))); break;
            case "topknot":
                Cap(28.5f, 36);
                s.Add(Ellipse(cx, 23, 4, 5, cap));
                break;
            case "hood":
                s.Add(Poly(Mix(cloth, new Vector4(0, 0, 0, 1), 0.35f), (cx - w + 2, 30), (cx, 26), (cx + w - 2, 30), (cx + w - 1, 41), (cx - w + 1, 41)));
                break;
        }

        if (attire.Extras.Contains("grime"))
            for (int i = 0; i < 5; i++)
                s.Add(Ellipse(cx + rng.Range(-w * 0.8f, w * 0.8f), rng.Range(60, 78), rng.Range(1.5f, 3.5f), rng.Range(1, 2.2f), new Vector4(0.15f, 0.12f, 0.1f, 0.35f)));
        DrawHeadwear(s, attire, cloth, cx, w, rng);
    }

    private static readonly Vector4 HiVis = new(1f, 0.62f, 0.1f, 1), Gold = new(0.95f, 0.78f, 0.3f, 1), Steel = new(0.6f, 0.63f, 0.68f, 1);

    private static void DrawCollar(List<PortraitShape> s, Attire attire, Vector4 cloth)
    {
        var dark = Mix(cloth, new Vector4(0, 0, 0, 1), 0.35f);
        switch (attire.Collar)
        {
            case "uniform":
                s.Add(Poly(dark, (6, 120), (94, 120), (82, 97), (18, 97)));
                s.Add(Poly(Mix(cloth, Vector4.One, 0.1f), (38, 95), (62, 95), (60, 103), (40, 103)));
                s.Add(Poly(Gold, (19, 97), (31, 97), (30, 100), (18, 100)));
                s.Add(Poly(Gold, (69, 97), (81, 97), (82, 100), (70, 100)));
                break;
            case "armour":
                s.Add(Poly(Steel, (6, 120), (20, 99), (36, 99), (34, 120)));
                s.Add(Poly(Steel, (94, 120), (80, 99), (64, 99), (66, 120)));
                s.Add(Poly(Mix(Steel, new Vector4(0, 0, 0, 1), 0.3f), (36, 104), (64, 104), (60, 120), (40, 120)));
                break;
            case "robe":
                s.Add(Poly(Mix(cloth, Vector4.One, 0.25f), (4, 120), (18, 96), (40, 98), (50, 120)));
                s.Add(Poly(Mix(cloth, Vector4.One, 0.15f), (96, 120), (82, 96), (60, 98), (50, 120)));
                break;
            case "overalls":
                s.Add(Poly(Mix(cloth, new Vector4(0.2f, 0.3f, 0.5f, 1), 0.6f), (26, 106), (74, 106), (78, 120), (22, 120)));
                s.Add(Poly(HiVis, (30, 97), (35, 97), (35, 120), (30, 120)));
                s.Add(Poly(HiVis, (65, 97), (70, 97), (70, 120), (65, 120)));
                s.Add(Poly(new Vector4(0.85f, 0.88f, 0.6f, 1), (22, 112), (78, 112), (78, 114), (22, 114)));
                break;
            case "fine":
                s.Add(Poly(Gold, (36, 96), (64, 96), (62, 99), (38, 99)));
                s.Add(Ellipse(50, 108, 2.4f, 2.4f, Gold));
                break;
            case "shawl":
                s.Add(Poly(Mix(cloth, new Vector4(0.6f, 0.45f, 0.3f, 1), 0.55f), (8, 120), (16, 98), (50, 104), (84, 98), (92, 120)));
                break;
            case "flight":
                s.Add(Poly(Mix(cloth, new Vector4(0.4f, 0.42f, 0.38f, 1), 0.5f), (34, 93), (66, 93), (68, 101), (32, 101)));
                s.Add(Poly(HiVis, (60, 104), (68, 104), (68, 108), (60, 108)));
                break;
            case "apron":
                s.Add(Poly(new Vector4(0.85f, 0.83f, 0.78f, 1), (32, 104), (68, 104), (72, 120), (28, 120)));
                break;
        }
        if (attire.Extras.Contains("badge")) s.Add(Poly(Gold, (68, 104), (73, 103), (74, 109), (71, 111), (67, 109)));
        if (attire.Extras.Contains("medal"))
        {
            s.Add(Poly(new Vector4(0.7f, 0.15f, 0.15f, 1), (30, 103), (34, 103), (34, 108), (30, 108)));
            s.Add(Ellipse(32, 110, 2, 2, Gold));
        }
    }

    private static void DrawEyewear(List<PortraitShape> s, string eyewear, float cx, float eyeY, float gap)
    {
        var frame = new Vector4(0.15f, 0.13f, 0.12f, 1);
        switch (eyewear)
        {
            case "glasses":
                foreach (int side in new[] { -1, 1 }) s.Add(Ellipse(cx + side * gap, eyeY, 5, 4, new Vector4(0.75f, 0.85f, 0.95f, 0.28f)));
                s.Add(Line(cx - gap + 5, eyeY - 1, cx + gap - 5, eyeY - 1, 0.8f, frame));
                break;
            case "shades":
                foreach (int side in new[] { -1, 1 }) s.Add(Poly(new Vector4(0.06f, 0.06f, 0.08f, 0.95f), (cx + side * gap - 5, eyeY - 3), (cx + side * gap + 5, eyeY - 3), (cx + side * gap + 4, eyeY + 3), (cx + side * gap - 4, eyeY + 3)));
                s.Add(Line(cx - gap + 5, eyeY - 2, cx + gap - 5, eyeY - 2, 0.8f, frame));
                break;
            case "monocle":
                s.Add(Ellipse(cx + gap, eyeY, 4.6f, 4.6f, new Vector4(0.85f, 0.9f, 0.95f, 0.3f)));
                s.Add(Line(cx + gap + 3, eyeY + 4, cx + gap + 8, eyeY + 22, 0.5f, Gold));
                break;
            case "visor":
                s.Add(Poly(new Vector4(0.3f, 0.9f, 1f, 0.45f), (cx - gap - 7, eyeY - 4), (cx + gap + 7, eyeY - 4), (cx + gap + 6, eyeY + 3), (cx - gap - 6, eyeY + 3)));
                break;
        }
    }

    private static void DrawHeadwear(List<PortraitShape> s, Attire attire, Vector4 cloth, float cx, float w, Rng rng)
    {
        Vector4 Dome(float top, float baseY, float extra, Vector4 colour)
        {
            var points = new List<Vector2>();
            for (int i = 0; i <= 10; i++)
            {
                float a = MathF.PI + i * MathF.PI / 10;
                points.Add(new Vector2(cx + MathF.Cos(a) * (w + extra), baseY + MathF.Sin(a) * (baseY - top)));
            }
            s.Add(new PortraitShape([.. points], colour));
            return colour;
        }
        switch (attire.Headwear)
        {
            case "hardhat":
            {
                var hat = rng.NextFloat() < 0.6f ? new Vector4(0.95f, 0.78f, 0.15f, 1) : HiVis;
                Dome(22, 41, 2.5f, hat);
                s.Add(Poly(Mix(hat, new Vector4(0, 0, 0, 1), 0.2f), (cx - w - 6, 40), (cx + w + 6, 40), (cx + w + 6, 43), (cx - w - 6, 43)));
                s.Add(Poly(Mix(hat, Vector4.One, 0.3f), (cx - 1.5f, 23), (cx + 1.5f, 23), (cx + 1.5f, 40), (cx - 1.5f, 40)));
                if (attire.Extras.Contains("lamp"))
                {
                    s.Add(Ellipse(cx, 34, 4, 3.4f, new Vector4(0.25f, 0.25f, 0.27f, 1)));
                    s.Add(Ellipse(cx, 34, 2.6f, 2.2f, new Vector4(1f, 0.97f, 0.8f, 1)));
                }
                break;
            }
            case "cap":
            {
                var capColour = Mix(cloth, new Vector4(0, 0, 0, 1), 0.3f);
                Dome(25, 40, 1.5f, capColour);
                s.Add(Poly(new Vector4(0.08f, 0.08f, 0.1f, 1), (cx - w + 2, 40), (cx + w - 2, 40), (cx + w - 4, 45), (cx - w + 4, 45)));
                s.Add(Poly(Gold, (cx - 2.5f, 31), (cx + 2.5f, 31), (cx + 2.5f, 36), (cx - 2.5f, 36)));
                break;
            }
            case "beanie":
            {
                var knit = Mix(cloth, new Vector4(rng.NextFloat(), rng.NextFloat(), rng.NextFloat(), 1), 0.5f);
                Dome(24, 42, 1.5f, knit);
                s.Add(Poly(Mix(knit, new Vector4(0, 0, 0, 1), 0.2f), (cx - w - 1.5f, 38), (cx + w + 1.5f, 38), (cx + w + 1.5f, 44), (cx - w - 1.5f, 44)));
                break;
            }
            case "helmet":
            {
                var shell = Mix(cloth, Steel, 0.6f);
                Dome(22, 52, 4, shell);
                foreach (int side in new[] { -1, 1 })
                    s.Add(Poly(Mix(shell, new Vector4(0, 0, 0, 1), 0.25f), (cx + side * (w + 4), 46), (cx + side * (w - 1), 46), (cx + side * (w - 1), 66), (cx + side * (w + 4), 64)));
                s.Add(Poly(new Vector4(0.3f, 0.5f, 0.6f, 0.6f), (cx - w, 34), (cx + w, 34), (cx + w - 1, 40), (cx - w + 1, 40)));
                break;
            }
            case "headset":
                s.Add(Line(cx - w - 2, 56, cx, 26, 1.6f, new Vector4(0.2f, 0.2f, 0.22f, 1)));
                s.Add(Line(cx, 26, cx + w + 2, 56, 1.6f, new Vector4(0.2f, 0.2f, 0.22f, 1)));
                s.Add(Ellipse(cx + w + 1, 58, 3.5f, 5, new Vector4(0.15f, 0.15f, 0.17f, 1)));
                s.Add(Line(cx + w, 62, cx + 7, 74, 1, new Vector4(0.15f, 0.15f, 0.17f, 1)));
                break;
            case "goggles":
                s.Add(Poly(new Vector4(0.25f, 0.2f, 0.15f, 1), (cx - w - 1, 39), (cx + w + 1, 39), (cx + w + 1, 43), (cx - w - 1, 43)));
                foreach (int side in new[] { -1, 1 })
                {
                    s.Add(Ellipse(cx + side * 6.5f, 41, 5, 4, new Vector4(0.35f, 0.32f, 0.28f, 1)));
                    s.Add(Ellipse(cx + side * 6.5f, 41, 3.5f, 2.8f, new Vector4(0.5f, 0.8f, 0.85f, 1)));
                }
                break;
            case "bandana":
                s.Add(Poly(Mix(cloth, new Vector4(0.7f, 0.2f, 0.15f, 1), 0.6f), (cx - w - 1, 36), (cx + w + 1, 36), (cx + w + 1, 42), (cx - w - 1, 42)));
                break;
        }
        if (attire.Extras.Contains("earpiece")) s.Add(Ellipse(cx - w + 0.5f, 59, 1.8f, 2.4f, new Vector4(0.15f, 0.15f, 0.17f, 1)));
    }

    /// <summary>Free machines: a chassis with a visor of lights, panel seams and an antenna.</summary>
    private static void BuildMachine(List<PortraitShape> s, Rng rng, PortraitStyleDef style, Vector4 cloth, Attire attire)
    {
        var body = Colour(Pick(rng, style.Skin, "#5a5f6c"));
        var light = Colour(Pick(rng, style.Eyes, "#c8ff5a"));
        var dark = Mix(body, new Vector4(0, 0, 0, 1), 0.5f);
        const float cx = 50;
        float w = rng.Range(style.HeadWidth[0], style.HeadWidth[1]);
        float top = rng.Range(24, 34), bottom = rng.Range(78, 86);
        bool round = rng.NextFloat() < 0.4f;

        s.Add(Poly(Mix(body, cloth, 0.35f), (4, 120), (96, 120), (84, 96), (16, 96)));
        DrawCollar(s, attire, Mix(body, cloth, 0.35f));
        s.Add(Poly(dark, (cx - 9, bottom - 4), (cx + 9, bottom - 4), (cx + 11, 99), (cx - 11, 99)));
        if (rng.NextFloat() < 0.7f)
        {
            float ax = cx + rng.Range(-w * 0.6f, w * 0.6f);
            s.Add(Line(ax, top + 2, ax + rng.Range(-3, 3), top - 14, 1.2f, dark));
            s.Add(Ellipse(ax, top - 14, 1.8f, 1.8f, light));
        }

        var head = round
            ? Enumerable.Range(0, 16).Select(i => new Vector2(cx + MathF.Cos(i * MathF.Tau / 16) * w, (top + bottom) / 2 + MathF.Sin(i * MathF.Tau / 16) * (bottom - top) / 2)).ToList()
            : [new(cx - w + 4, top), new(cx + w - 4, top), new(cx + w, top + 4), new(cx + w, bottom - 6), new(cx + w - 6, bottom), new(cx - w + 6, bottom), new(cx - w, bottom - 6), new(cx - w, top + 4)];
        s.Add(new PortraitShape([.. head], body));
        s.Add(new PortraitShape(ClipRight(head, cx + 3), Mix(body, dark, 0.35f)));

        float visorY = rng.Range(top + 14, top + 24);
        s.Add(Poly(new Vector4(0.04f, 0.05f, 0.06f, 1), (cx - w + 3, visorY - 4), (cx + w - 3, visorY - 4), (cx + w - 5, visorY + 4), (cx - w + 5, visorY + 4)));
        int lights = 1 + rng.Next(4);
        for (int i = 0; i < lights; i++)
        {
            float lx = cx - (lights - 1) * 5 + i * 10;
            s.Add(Poly(light, (lx - 3, visorY - 1.5f), (lx + 3, visorY - 1.5f), (lx + 3, visorY + 1.5f), (lx - 3, visorY + 1.5f)));
        }
        for (int i = 0; i < 4; i++) s.Add(Line(cx - 6 + i * 4, bottom - 12, cx - 6 + i * 4, bottom - 6, 1.4f, dark));
        s.Add(Line(cx - w + 2, top + 8, cx + w - 2, top + 8, 0.7f, dark));
        if (rng.NextFloat() < 0.5f) s.Add(Line(cx + w * 0.4f, top, cx + w * 0.4f, visorY - 4, 0.7f, dark));
    }

    // Shape helpers ---------------------------------------------------------------------------

    private static string Pick(Rng rng, List<string> options, string fallback) => options.Count > 0 ? rng.Pick(options) : fallback;

    public static Vector4 Colour(string hex) => new(
        int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber) / 255f,
        int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber) / 255f,
        int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber) / 255f, 1);

    private static Vector4 Mix(Vector4 a, Vector4 b, float t) => Vector4.Lerp(a, b, t) with { W = 1 };

    private static PortraitShape Poly(Vector4 colour, params (float X, float Y)[] points) =>
        new([.. points.Select(p => new Vector2(p.X, p.Y))], colour);

    private static PortraitShape Rect(float x, float y, float w, float h, Vector4 colour) =>
        Poly(colour, (x, y), (x + w, y), (x + w, y + h), (x, y + h));

    private static PortraitShape Ellipse(float x, float y, float rx, float ry, Vector4 colour) =>
        new([.. Enumerable.Range(0, 14).Select(i => new Vector2(x + MathF.Cos(i * MathF.Tau / 14) * rx, y + MathF.Sin(i * MathF.Tau / 14) * ry))], colour);

    private static PortraitShape Line(float x1, float y1, float x2, float y2, float thickness, Vector4 colour)
    {
        var d = Vector2.Normalize(new Vector2(x2 - x1, y2 - y1));
        var n = new Vector2(-d.Y, d.X) * thickness / 2;
        var a = new Vector2(x1, y1);
        var b = new Vector2(x2, y2);
        return new([a + n, b + n, b - n, a - n], colour);
    }

    /// <summary>The part of a convex polygon above y (smaller y), for hair that stops at the hairline.</summary>
    private static Vector2[] ClipAbove(List<Vector2> polygon, float y)
    {
        var result = new List<Vector2>();
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            bool aIn = a.Y <= y, bIn = b.Y <= y;
            if (aIn) result.Add(a);
            if (aIn != bIn) result.Add(Vector2.Lerp(a, b, (y - a.Y) / (b.Y - a.Y)));
        }
        return [.. result];
    }

    /// <summary>The part of a convex polygon right of x, for the shaded side of a face.</summary>
    private static Vector2[] ClipRight(List<Vector2> polygon, float x)
    {
        var result = new List<Vector2>();
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            bool aIn = a.X >= x, bIn = b.X >= x;
            if (aIn) result.Add(a);
            if (aIn != bIn)
            {
                float t = (x - a.X) / (b.X - a.X);
                result.Add(Vector2.Lerp(a, b, t));
            }
        }
        return [.. result];
    }
}
