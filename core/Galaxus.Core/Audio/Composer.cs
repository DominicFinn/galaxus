namespace Galaxus.Core.Audio;

/// <summary>
/// A lo-fi music style from game/data/music: the rules the composer follows for one race or culture.
/// Every style shares the same eight channels; the rules set the key, tempo, instruments, groove
/// and how much space, tape and vinyl there is.
/// </summary>
public sealed class MusicStyleDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Beats per minute, [min, max]. Lo-fi sits around 60 to 90.</summary>
    public int[] Tempo { get; set; } = [70, 80];
    /// <summary>Scale name, see Composer.Scales.</summary>
    public string Scale { get; set; } = "dorian";
    /// <summary>MIDI note of the key's root in the bass register, e.g. 41 for F2.</summary>
    public int Root { get; set; } = 41;
    /// <summary>Chord progressions as scale degrees, one chord a bar, four or eight chords each.</summary>
    public List<int[]> Progressions { get; set; } = [[2, 5, 1, 6]];
    /// <summary>0 to 1: how often a chord gets a ninth on top of its seventh.</summary>
    public float Ninths { get; set; } = 0.5f;
    /// <summary>0 to 0.35: how late the off-beat eighths land.</summary>
    public float Swing { get; set; } = 0.15f;
    public PartDef Keys { get; set; } = new() { Instrument = "rhodes", Pattern = "comp" };
    public PartDef Bass { get; set; } = new() { Instrument = "sub", Pattern = "lazy" };
    public PartDef Lead { get; set; } = new() { Instrument = "flute", Density = 0.35f };
    public PartDef Pad { get; set; } = new() { Instrument = "warm", Volume = 0.7f };
    public PartDef Plucks { get; set; } = new() { Instrument = "kalimba", Pattern = "ostinato", Volume = 0.6f };
    public DrumsDef Drums { get; set; } = new();
    public SpaceDef Space { get; set; } = new();
}

public sealed class PartDef
{
    /// <summary>Which instrument plays the part; see the sets in <see cref="Lofi"/>. "off" leaves it out.</summary>
    public string Instrument { get; set; } = "off";
    /// <summary>Keys: comp, sustain, rolled. Bass: lazy, walk, sub, bounce. Plucks: ostinato, arp, sparse.</summary>
    public string Pattern { get; set; } = "";
    public float Volume { get; set; } = 1f;
    /// <summary>0 to 1: how busy the part is.</summary>
    public float Density { get; set; } = 0.5f;
    /// <summary>0 to 1: how bright the instrument sounds.</summary>
    public float Tone { get; set; } = 0.5f;
}

public sealed class DrumsDef
{
    /// <summary>boom-bap, brushes, dusty, glitch or off.</summary>
    public string Kit { get; set; } = "boom-bap";
    public float Volume { get; set; } = 1f;
    /// <summary>0 to 1: extra kicks, ghost notes and fills.</summary>
    public float Busy { get; set; } = 0.4f;
}

public sealed class SpaceDef
{
    /// <summary>0 to 1: the high shimmer that gives the sense of space.</summary>
    public float Shimmer { get; set; } = 0.5f;
    /// <summary>0 to 1: how big and how loud the reverb is.</summary>
    public float Reverb { get; set; } = 0.6f;
    /// <summary>0 to 1: vinyl crackle.</summary>
    public float Vinyl { get; set; } = 0.4f;
    /// <summary>0 to 1: tape hiss.</summary>
    public float Hiss { get; set; } = 0.3f;
    /// <summary>0 to 1: tape wobble.</summary>
    public float Wow { get; set; } = 0.4f;
    /// <summary>0 to 1: how much the top end is rolled off.</summary>
    public float Warmth { get; set; } = 0.6f;
}

/// <summary>Where you are, which sets how much is going on in the music.</summary>
public enum Mood { Docked, Flight, Combat }

/// <summary>
/// Writes and renders lo-fi songs. Eight channels: keys, bass, lead, pad, drums, plucks, space and
/// texture (tape and vinyl). A song is 44 bars: intro, A, B, A, a break, B, outro, and it loops
/// cleanly back to the intro. The same style, seed and mood always give the same song, and the three
/// moods of a style and seed share their key, tempo and chords, so they can crossfade.
/// </summary>
public static class Composer
{
    public static readonly Dictionary<string, int[]> Scales = new()
    {
        ["ionian"] = [0, 2, 4, 5, 7, 9, 11],
        ["dorian"] = [0, 2, 3, 5, 7, 9, 10],
        ["phrygian"] = [0, 1, 3, 5, 7, 8, 10],
        ["lydian"] = [0, 2, 4, 6, 7, 9, 11],
        ["mixolydian"] = [0, 2, 4, 5, 7, 9, 10],
        ["aeolian"] = [0, 2, 3, 5, 7, 8, 10],
        ["locrian"] = [0, 1, 3, 5, 6, 8, 10],
        ["harmonic-minor"] = [0, 2, 3, 5, 7, 8, 11],
        ["pentatonic-minor"] = [0, 3, 5, 7, 10],
        ["pentatonic-major"] = [0, 2, 4, 7, 9],
        ["whole-tone"] = [0, 2, 4, 6, 8, 10],
    };

    public static readonly HashSet<string> KeysPatterns = ["comp", "sustain", "rolled"];
    public static readonly HashSet<string> BassPatterns = ["lazy", "walk", "sub", "bounce"];
    public static readonly HashSet<string> PluckPatterns = ["ostinato", "arp", "sparse"];

    public const int Keys = 0, Bass = 1, Lead = 2, Pad = 3, Drums = 4, Plucks = 5, Space = 6;

    /// <summary>A note. Drums use <paramref name="Piece"/> (see Lofi.Kick and friends) instead of a pitch.</summary>
    public sealed record Note(int Channel, float Beat, float Beats, float Midi, float Velocity, int Piece = 0);

    public sealed record Section(string Name, int StartBar, int Bars, int Progression);

    public sealed record Song(MusicStyleDef Style, Mood Mood, ulong Seed, int Tempo, List<Section> Sections, List<Note> Notes)
    {
        public int Bars => Sections.Sum(s => s.Bars);
        public float SecondsPerBeat => 60f / Tempo;
        public float Seconds => Bars * 4 * SecondsPerBeat;
    }

    private static readonly (string Name, int Bars, int Progression)[] Form =
        [("intro", 4, 0), ("a", 8, 0), ("b", 8, 1), ("a2", 8, 0), ("break", 4, 1), ("b2", 8, 1), ("outro", 4, 0)];

    /// <summary>Composes a song. Moods change the arrangement, not the key, tempo or chords.</summary>
    public static Song Compose(MusicStyleDef style, ulong seed, Mood mood = Mood.Flight)
    {
        // The shared material (tempo, chords, motifs) comes from the style and seed alone.
        var rng = new Rng(seed ^ Rng.Hash("lofi:" + style.Id));
        int[] scale = Scales[style.Scale];
        int tempo = style.Tempo[0] + rng.Next(Math.Max(1, style.Tempo[1] - style.Tempo[0] + 1));
        var progA = rng.Pick(style.Progressions);
        var progB = style.Progressions.Count > 1 ? style.Progressions.First(p => p != progA) : Variation(progA);
        int[][] progressions = [progA, progB];
        var ninths = Enumerable.Range(0, 16).Select(_ => rng.NextFloat() < style.Ninths).ToArray();
        var keysRhythms = Enumerable.Range(0, 4).Select(_ => KeysRhythm(rng)).ToArray();
        var leadMotifs = new[] { Motif(rng, style.Lead.Density), Motif(rng, style.Lead.Density) };
        var ostinatos = new[] { Ostinato(rng, style.Plucks.Density), Ostinato(rng, style.Plucks.Density) };
        // Details that may differ between moods (humanising, fills) come from their own stream.
        var feel = new Rng(seed ^ Rng.Hash($"lofi-feel:{style.Id}:{mood}"));

        var notes = new List<Note>();
        var sections = new List<Section>();
        int bar = 0;
        int[]? lastVoicing = null;
        int leadDegree = 17;
        foreach (var (name, bars, progIndex) in Form)
        {
            sections.Add(new Section(name, bar, bars, progIndex));
            var prog = progressions[progIndex];
            var parts = Arrangement(name, mood, style);
            for (int b = 0; b < bars; b++, bar++)
            {
                float beat0 = bar * 4;
                int chordIndex = b % prog.Length;
                int degree = prog[chordIndex];
                int nextDegree = prog[(b + 1) % prog.Length];
                bool ninth = ninths[(progIndex * 8 + chordIndex) % ninths.Length];
                var tones = ChordTones(scale, style.Root, degree, ninth);
                bool lastBar = b == bars - 1;

                if (parts.Keys)
                {
                    var voicing = Voice(tones, lastVoicing, style.Root + 22);
                    lastVoicing = voicing;
                    AddKeys(notes, style, voicing, beat0, keysRhythms[(bar / 2) % keysRhythms.Length], feel, mood);
                }
                if (parts.Bass) AddBass(notes, style, scale, tones, degree, nextDegree, beat0, mood, feel, parts.BassSustain);
                if (parts.Pad) AddPad(notes, tones, beat0);
                if (parts.Plucks) AddPlucks(notes, style, tones, beat0, ostinatos[progIndex], feel);
                if (parts.Lead && b % 2 == 0)
                    leadDegree = AddLead(notes, style, scale, prog, b, beat0, leadMotifs[progIndex], feel, leadDegree, mood);
                if (parts.Drums != DrumLevel.None) AddDrums(notes, style, beat0, b, lastBar, parts.Drums, mood, feel);
                if (parts.Shimmer && b % 4 == 0)
                {
                    float hold = Math.Min(4, bars - b) * 4 - 0.5f;
                    int pc = ((tones[0] - style.Root) % 12 + 12) % 12;
                    notes.Add(new Note(Space, beat0, hold, style.Root + 48 + pc, style.Space.Shimmer));
                    notes.Add(new Note(Space, beat0 + 2, hold - 2, style.Root + 55 + pc, style.Space.Shimmer * 0.6f));
                }
            }
        }
        return new Song(style, mood, seed, tempo, sections, notes);
    }

    private enum DrumLevel { None, Light, Full }

    private sealed record Parts(bool Keys, bool Bass, bool BassSustain, bool Lead, bool Pad, bool Plucks, bool Shimmer, DrumLevel Drums);

    /// <summary>Which channels play in each section, by mood. Docked is sparse; combat keeps the drums going.</summary>
    private static Parts Arrangement(string section, Mood mood, MusicStyleDef style)
    {
        bool calm = mood == Mood.Docked, tense = mood == Mood.Combat;
        DrumLevel full = calm ? DrumLevel.Light : DrumLevel.Full;
        return section switch
        {
            "intro" => new(true, tense, !tense, false, true, false, true, tense ? DrumLevel.Light : DrumLevel.None),
            "a" => new(true, true, false, false, false, style.Plucks.Density > 0.6f, false, full),
            "b" => new(true, true, false, !calm, true, true, false, full),
            "a2" => new(true, true, false, true, false, false, false, full),
            "break" => new(true, true, true, false, true, true, true, tense ? DrumLevel.Light : DrumLevel.None),
            "b2" => new(true, true, false, true, true, true, !tense, full),
            _ => new(true, true, true, false, true, false, true, tense ? DrumLevel.Light : DrumLevel.None),
        };
    }

    // Harmony ------------------------------------------------------------------------------

    private static int Pitch(int[] scale, int root, int degree)
    {
        int index = degree - 1;
        int octaves = (int)Math.Floor(index / (double)scale.Length);
        int step = ((index % scale.Length) + scale.Length) % scale.Length;
        return root + 12 * octaves + scale[step];
    }

    /// <summary>The chord on a degree as MIDI notes: root, third, fifth, seventh and maybe the ninth.</summary>
    private static int[] ChordTones(int[] scale, int root, int degree, bool ninth)
    {
        var tones = new List<int> { degree, degree + 2, degree + 4, degree + 6 };
        if (ninth) tones.Add(degree + 8);
        return tones.Select(d => Pitch(scale, root, d)).ToArray();
    }

    /// <summary>
    /// A rootless voicing of the chord (the bass has the root) placed close to the last one, so the
    /// keys move smoothly from chord to chord.
    /// </summary>
    private static int[] Voice(int[] tones, int[]? last, int centre)
    {
        var classes = tones.Skip(1).Select(t => t % 12).Distinct().ToArray();
        float target = last != null ? (float)last.Average() : centre;
        int[]? best = null;
        float bestCost = float.MaxValue;
        for (int low = centre - 8; low <= centre + 6; low++)
        {
            var voicing = classes.Select(pc => low + ((pc - low) % 12 + 12) % 12).OrderBy(n => n).ToArray();
            float cost = MathF.Abs((float)voicing.Average() - target) + MathF.Abs((float)voicing.Average() - centre) * 0.3f;
            if (cost < bestCost) { bestCost = cost; best = voicing; }
        }
        return best!;
    }

    private static int[] Variation(int[] prog) => prog.Length >= 2 ? [.. prog[1..], prog[0]] : prog;

    // Parts --------------------------------------------------------------------------------

    private static float Swing(MusicStyleDef style, float beat)
    {
        float frac = beat - MathF.Floor(beat);
        if (MathF.Abs(frac - 0.5f) < 0.01f) return style.Swing * 0.5f;
        if (MathF.Abs(frac - 0.25f) < 0.01f || MathF.Abs(frac - 0.75f) < 0.01f) return style.Swing * 0.25f;
        return 0;
    }

    private static float[] KeysRhythm(Rng rng) => rng.Next(5) switch
    {
        0 => [0, 2.5f],
        1 => [0, 1.5f, 3],
        2 => [0, 3.5f],
        3 => [0, 2],
        _ => [0],
    };

    private static void AddKeys(List<Note> notes, MusicStyleDef style, int[] voicing, float beat0, float[] rhythm, Rng feel, Mood mood)
    {
        string pattern = style.Keys.Pattern;
        float[] hits = pattern switch { "sustain" => [0], "rolled" => [0], _ => mood == Mood.Docked ? [rhythm[0]] : rhythm };
        for (int h = 0; h < hits.Length; h++)
        {
            float start = hits[h] + Swing(style, hits[h]);
            float end = h + 1 < hits.Length ? hits[h + 1] : 4;
            float hold = MathF.Max(0.4f, end - hits[h] - 0.15f);
            float vel = (h == 0 ? 0.7f : 0.55f) + feel.Range(-0.06f, 0.06f);
            for (int n = 0; n < voicing.Length; n++)
            {
                float roll = pattern == "rolled" ? n * 0.09f : n * feel.Range(0.005f, 0.02f);
                notes.Add(new Note(Keys, beat0 + start + roll + feel.Range(-0.01f, 0.01f), hold - roll, voicing[n], vel * (n == voicing.Length - 1 ? 1 : 0.85f)));
            }
        }
    }

    private static void AddBass(List<Note> notes, MusicStyleDef style, int[] scale, int[] tones, int degree, int nextDegree, float beat0, Mood mood, Rng feel, bool sustain)
    {
        int Low(int midi) => midi - style.Root > 9 ? midi - 12 : midi;
        int root = Low(tones[0]), fifth = Low(tones[2]);
        int next = Low(Pitch(scale, style.Root, nextDegree));
        string pattern = sustain ? "sub" : mood switch
        {
            Mood.Docked when style.Bass.Pattern is "walk" or "bounce" => "lazy",
            Mood.Combat when style.Bass.Pattern is "sub" => "lazy",
            Mood.Combat when style.Bass.Pattern is "lazy" => "bounce",
            _ => style.Bass.Pattern,
        };
        void Add(float beat, float beats, int midi, float vel) =>
            notes.Add(new Note(Bass, beat0 + beat + Swing(style, beat), beats, midi, vel + feel.Range(-0.05f, 0.05f)));
        switch (pattern)
        {
            case "sub":
                Add(0, 3.85f, root, 0.85f);
                break;
            case "walk":
                Add(0, 0.9f, root, 0.85f);
                Add(1, 0.9f, Low(tones[1]), 0.7f);
                Add(2, 0.9f, fifth, 0.75f);
                Add(3, 0.9f, next + (next > root ? -1 : 1), 0.7f);
                break;
            case "bounce":
                Add(0, 0.6f, root, 0.9f);
                Add(0.75f, 0.4f, root, 0.6f);
                Add(2, 0.6f, fifth, 0.8f);
                Add(2.5f, 0.9f, root, 0.7f);
                break;
            default: // lazy
                Add(0, 1.6f, root, 0.85f);
                Add(2.5f, 1.1f, feel.NextFloat() < 0.6f ? root : fifth, 0.7f);
                if (feel.NextFloat() < 0.35f) Add(3.5f, 0.4f, next + (next > root ? -2 : 2), 0.55f);
                break;
        }
    }

    private static void AddPad(List<Note> notes, int[] tones, float beat0)
    {
        notes.Add(new Note(Pad, beat0, 4.1f, tones[0] + 12, 0.7f));
        notes.Add(new Note(Pad, beat0, 4.1f, tones[2] + 12, 0.55f));
        notes.Add(new Note(Pad, beat0, 4.1f, tones[1] + 24, 0.5f));
        notes.Add(new Note(Pad, beat0, 4.1f, tones[^1] + 24, 0.45f));
    }

    /// <summary>Eight eighth-note slots: which chord tone to play, or -1 to rest.</summary>
    private static int[] Ostinato(Rng rng, float density)
    {
        var slots = new int[8];
        for (int i = 0; i < 8; i++) slots[i] = i == 0 || rng.NextFloat() < 0.35f + density * 0.4f ? rng.Next(5) : -1;
        return slots;
    }

    private static void AddPlucks(List<Note> notes, MusicStyleDef style, int[] tones, float beat0, int[] ostinato, Rng feel)
    {
        var upper = tones.Select(t => t + 24).Concat(tones.Take(2).Select(t => t + 36)).OrderBy(t => t).ToArray();
        switch (style.Plucks.Pattern)
        {
            case "arp":
                for (int i = 0; i < 8; i++)
                {
                    int idx = i < 4 ? i : 7 - i;
                    float beat = i * 0.5f;
                    notes.Add(new Note(Plucks, beat0 + beat + Swing(style, beat), 0.45f, upper[idx % upper.Length], 0.5f + feel.Range(-0.08f, 0.08f)));
                }
                break;
            case "sparse":
                int count = 1 + feel.Next(3);
                for (int i = 0; i < count; i++)
                {
                    float beat = feel.Next(8) * 0.5f;
                    notes.Add(new Note(Plucks, beat0 + beat + Swing(style, beat), 1, upper[feel.Next(upper.Length)] + 12, 0.45f));
                }
                break;
            default:
                for (int i = 0; i < 8; i++)
                {
                    if (ostinato[i] < 0) continue;
                    float beat = i * 0.5f;
                    notes.Add(new Note(Plucks, beat0 + beat + Swing(style, beat), 0.45f, upper[ostinato[i] % upper.Length], (i % 2 == 0 ? 0.55f : 0.42f) + feel.Range(-0.05f, 0.05f)));
                }
                break;
        }
    }

    /// <summary>A two-bar melodic rhythm: start beat, length, and whether to land on a chord tone.</summary>
    private static List<(float Beat, float Length, bool Strong)> Motif(Rng rng, float density)
    {
        var motif = new List<(float, float, bool)>();
        float beat = rng.NextFloat() < 0.5f ? 0.5f : 0;
        while (beat < 7)
        {
            float length = rng.Pick(new[] { 0.5f, 0.5f, 1f, 1f, 1.5f, 2f });
            if (rng.NextFloat() < 0.3f + density * 0.6f) motif.Add((beat, MathF.Min(length, 8 - beat) * 0.92f, beat % 2 == 0));
            beat += length + (rng.NextFloat() < 0.4f - density * 0.3f ? 1 : 0);
        }
        // End the phrase on a held note.
        if (motif.Count > 0)
        {
            var (b, _, s) = motif[^1];
            motif[^1] = (b, MathF.Max(1.5f, 8 - b - 0.5f), s);
        }
        return motif;
    }

    /// <summary>A two-bar phrase over the motif's rhythm: stepwise, landing on chord tones on the strong beats.</summary>
    private static int AddLead(List<Note> notes, MusicStyleDef style, int[] scale, int[] prog, int barInSection, float beat0, List<(float Beat, float Length, bool Strong)> motif, Rng feel, int degree, Mood mood)
    {
        float keep = mood == Mood.Docked ? 0.6f : 1;
        foreach (var (beat, length, strong) in motif)
        {
            if (feel.NextFloat() > keep) continue;
            int chordDegree = prog[(barInSection + (beat >= 4 ? 1 : 0)) % prog.Length];
            if (strong)
            {
                // Nearest chord tone (root, third or fifth) to where the melody is.
                int best = degree, bestDistance = int.MaxValue;
                foreach (int offset in new[] { 0, 2, 4 })
                    for (int octave = 1; octave <= 3; octave++)
                    {
                        int candidate = chordDegree + offset + octave * scale.Length;
                        int distance = Math.Abs(candidate - degree);
                        if (distance < bestDistance) { bestDistance = distance; best = candidate; }
                    }
                degree = best;
            }
            else degree += feel.Pick(new[] { -2, -1, -1, 1, 1, 2 });
            int low = 2 * scale.Length + 1, high = 3 * scale.Length + 3;
            degree = Math.Clamp(degree, low, high);
            int midi = Pitch(scale, style.Root, degree);
            notes.Add(new Note(Lead, beat0 + beat + Swing(style, beat), length, midi, 0.6f + feel.Range(-0.08f, 0.08f)));
        }
        return degree;
    }

    private static void AddDrums(List<Note> notes, MusicStyleDef style, float beat0, int bar, bool lastBar, DrumLevel level, Mood mood, Rng feel)
    {
        if (style.Drums.Kit == "off") return;
        float busy = style.Drums.Busy + (mood == Mood.Combat ? 0.3f : mood == Mood.Docked ? -0.2f : 0);
        float energy = level == DrumLevel.Light ? 0.6f : 1;
        void Hit(int piece, float beat, float vel)
        {
            // Snares sit a touch behind the beat, which is half the lo-fi feel.
            float late = piece is Lofi.Snare or Lofi.Brush ? 0.03f : 0;
            notes.Add(new Note(Drums, beat0 + beat + Swing(style, beat) + late + feel.Range(-0.008f, 0.008f), 0.1f, 0, vel * energy * style.Drums.Volume * (1 + feel.Range(-0.08f, 0.08f)), piece));
        }
        string kit = style.Drums.Kit;
        bool brushes = kit == "brushes" || (mood == Mood.Docked && level == DrumLevel.Light && kit == "boom-bap");
        bool dropBeforeNext = lastBar && feel.NextFloat() < 0.5f;

        // Kick.
        Hit(Lofi.Kick, 0, 0.9f);
        if (!dropBeforeNext)
        {
            if (kit == "dusty") { Hit(Lofi.Kick, 2, 0.8f); if (feel.NextFloat() < busy) Hit(Lofi.Kick, 0.75f, 0.55f); }
            else if (kit == "glitch") Hit(Lofi.Kick, 2.25f, 0.75f);
            else Hit(Lofi.Kick, 2.5f, 0.75f);
            if (level == DrumLevel.Full && feel.NextFloat() < busy * 0.6f) Hit(Lofi.Kick, 1.5f, 0.5f);
        }
        if (level == DrumLevel.Light && mood != Mood.Combat)
        {
            // Light drums: kick and a rim or brush on the backbeat, no hats.
            Hit(brushes ? Lofi.Brush : Lofi.Rim, 1, 0.55f);
            Hit(brushes ? Lofi.Brush : Lofi.Rim, 3, 0.55f);
            return;
        }

        // Backbeat.
        int back = brushes ? Lofi.Brush : Lofi.Snare;
        Hit(back, 1, 0.8f);
        if (!dropBeforeNext) Hit(back, 3, 0.8f);
        if (feel.NextFloat() < busy * 0.5f) Hit(Lofi.Snare, 3.75f, 0.25f);

        // Hats.
        float step = kit == "glitch" || mood == Mood.Combat ? 0.25f : brushes ? 1 : 0.5f;
        for (float h = 0; h < 4; h += step)
        {
            if (kit == "glitch" && feel.NextFloat() < 0.25f) continue;
            float vel = h % 1 == 0 ? 0.55f : h % 0.5f == 0 ? 0.38f : 0.25f;
            Hit(h == 3.5f && bar % 4 == 3 ? Lofi.OpenHat : Lofi.Hat, h, vel);
        }
        if (kit == "glitch" && bar % 4 == 3) for (int r = 0; r < 4; r++) Hit(Lofi.Hat, 3.5f + r * 0.125f, 0.2f + r * 0.05f);
    }

    // Rendering ----------------------------------------------------------------------------

    /// <summary>
    /// Renders a song to stereo interleaved samples at <see cref="Lofi.Rate"/> that loop seamlessly.
    /// <paramref name="solo"/> renders one channel alone, unmastered, for balancing the mix.
    /// </summary>
    public static float[] Render(Song song, int? solo = null)
    {
        var style = song.Style;
        var space = style.Space;
        float spb = song.SecondsPerBeat;
        float loopSeconds = song.Seconds;
        int frames = (int)(loopSeconds * Lofi.Rate);
        int total = frames + Lofi.Rate * 6; // a tail for reverb and releases, folded back onto the start
        var mix = new float[total * 2];
        var send = new float[total];
        var channel = new float[total];

        // Kick envelope for side-chain ducking: other parts dip a little under each kick.
        var kick = new float[total];
        foreach (var n in song.Notes.Where(n => n.Channel == Drums && n.Piece == Lofi.Kick))
        {
            int at = (int)(n.Beat * spb * Lofi.Rate);
            for (int i = Math.Max(0, -at); i < Lofi.Rate / 3 && at + i < total; i++)
                kick[at + i] = MathF.Max(kick[at + i], MathF.Exp(-i / (0.12f * Lofi.Rate)) * MathF.Min(1, n.Velocity));
        }

        // A whole number of cycles per loop, so modulation lines up when the song loops.
        float Fit(float hz) => MathF.Max(1, MathF.Round(hz * loopSeconds)) / loopSeconds;
        float warmth = space.Warmth;

        //              channel  level  send   duck   pan    pan sway (Hz)  low-pass
        var settings = new (int Ch, float Level, float Send, float Duck, float Pan, float Sway, float Cutoff)[]
        {
            (Keys,   0.40f * style.Keys.Volume,   0.30f, 0.25f, -0.05f, Fit(0.11f), 6000 - 2600 * warmth),
            (Bass,   0.28f * style.Bass.Volume,   0.04f, 0.35f,  0f,    0,          900),
            (Lead,   0.60f * style.Lead.Volume,   0.45f, 0.10f, -0.1f,  Fit(0.05f), 7000 - 2500 * warmth),
            (Pad,    0.40f * style.Pad.Volume,    0.60f, 0.45f,  0.1f,  Fit(0.04f), 9000),
            (Drums,  0.50f * style.Drums.Volume,  0.10f, 0f,     0f,    0,          9000 - 4000 * warmth),
            (Plucks, 0.55f * style.Plucks.Volume, 0.50f, 0.20f,  0.3f,  Fit(0.07f), 8000 - 3000 * warmth),
            (Space,  0.20f,                        1.00f, 0.30f,  0f,    Fit(0.03f), 9000),
        };

        foreach (var (ch, level, sendLevel, duck, pan, sway, cutoff) in settings)
        {
            var notes = song.Notes.Where(n => n.Channel == ch).ToList();
            if (notes.Count == 0 || level <= 0 || (solo != null && solo != ch)) continue;
            Array.Clear(channel);
            foreach (var n in notes)
            {
                int start = (int)(n.Beat * spb * Lofi.Rate);
                float hold = n.Beats * spb;
                float hz = Lofi.MidiToHz(n.Midi);
                switch (ch)
                {
                    case Keys: Lofi.Keys(style.Keys.Instrument, channel, start, hz, hold, n.Velocity, style.Keys.Tone); break;
                    case Bass: Lofi.Bass(style.Bass.Instrument, channel, start, hz, hold, n.Velocity); break;
                    case Lead: Lofi.Lead(style.Lead.Instrument, channel, start, hz, hold, n.Velocity); break;
                    case Pad: Lofi.Pad(style.Pad.Instrument, channel, start, hz, hold, n.Velocity); break;
                    case Drums: Lofi.Drum(style.Drums.Kit, n.Piece, channel, start, n.Velocity, (uint)(start * 2654435761u)); break;
                    case Plucks: Lofi.Plucked(style.Plucks.Instrument, channel, start, hz, hold, n.Velocity); break;
                    case Space: Lofi.Shimmer(channel, start, hz, hold, n.Velocity); break;
                }
            }
            Lofi.LowPass(channel, cutoff);
            for (int i = 0; i < total; i++)
            {
                float s = channel[i] * level * (1 - duck * kick[i]);
                float p = pan + (sway > 0 ? 0.25f * MathF.Sin(MathF.Tau * sway * i / Lofi.Rate) : 0);
                mix[i * 2] += s * MathF.Min(1, 1 - p);
                mix[i * 2 + 1] += s * MathF.Min(1, 1 + p);
                send[i] += s * sendLevel;
            }
        }

        Lofi.Reverb(send, mix, size: 0.55f + 0.4f * space.Reverb, damp: 0.5f, wet: (0.35f + 0.65f * space.Reverb) * 2.5f);
        Lofi.Wow(mix, space.Wow, Fit(0.4f), Fit(5.7f));

        // Fold the tail back onto the start so it loops without a seam.
        var loop = new float[frames * 2];
        Array.Copy(mix, loop, loop.Length);
        for (int i = loop.Length; i < mix.Length; i++) loop[i % loop.Length] += mix[i];

        if (solo != null) return loop;
        Lofi.Vinyl(loop, space.Vinyl, space.Hiss, (uint)(song.Seed ^ 0x51ab));
        Lofi.Tone(loop, lowPass: 10500 - 4500 * warmth, highPass: 32);
        Master(loop);
        return loop;
    }

    /// <summary>
    /// Brings every song to the same gentle loudness, with tape-style saturation and a soft ceiling,
    /// so music sits under the game rather than on top of it.
    /// </summary>
    public static void Master(float[] buffer, float targetRms = 0.11f, float ceiling = 0.9f)
    {
        double sum = 0;
        for (int i = 0; i < buffer.Length; i++) sum += buffer[i] * buffer[i];
        float rms = (float)Math.Sqrt(sum / Math.Max(1, buffer.Length));
        float gain = rms > 1e-6f ? targetRms / rms : 1;
        for (int i = 0; i < buffer.Length; i++) buffer[i] = ceiling * MathF.Tanh(buffer[i] * gain / ceiling);
    }

    /// <summary>A song as a 16-bit stereo WAV file, for listening outside the game.</summary>
    public static byte[] ToWav(float[] stereo)
    {
        var pcm = Synth.ToPcm16(stereo);
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(Lofi.Rate); w.Write(Lofi.Rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write(pcm.Length); w.Write(pcm);
        w.Flush();
        return stream.ToArray();
    }
}
