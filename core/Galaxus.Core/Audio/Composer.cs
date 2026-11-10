namespace Galaxus.Core.Audio;

/// <summary>A musical style from game/data/music: the rules the composer follows for one culture.</summary>
public sealed class MusicStyleDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Beats per minute, [min, max].</summary>
    public int[] Tempo { get; set; } = [100, 120];
    /// <summary>Scale name, see Composer.Scales.</summary>
    public string Scale { get; set; } = "aeolian";
    /// <summary>MIDI note of the key's root, e.g. 45 for A2.</summary>
    public int Root { get; set; } = 45;
    /// <summary>Chord progressions as scale degrees (1 to 7), four chords of two bars each.</summary>
    public List<int[]> Progressions { get; set; } = [[1, 6, 4, 5]];
    public VoiceDef Lead { get; set; } = new() { Wave = "square25", Octave = 2 };
    public VoiceDef Harmony { get; set; } = new() { Wave = "square50", Octave = 1, Volume = 0.5f };
    public VoiceDef Bass { get; set; } = new() { Wave = "triangle", Octave = 0, Pattern = "root" };
    public VoiceDef Arp { get; set; } = new() { Wave = "square12", Octave = 1, Pattern = "up" };
    public VoiceDef Pad { get; set; } = new() { Wave = "triangle", Octave = 1, Volume = 0.4f };
    public DrumsDef Drums { get; set; } = new();
    /// <summary>0 to 1: how busy the melody is.</summary>
    public float Density { get; set; } = 0.6f;
    /// <summary>0 to 0.3: how much off-beat eighths are delayed.</summary>
    public float Swing { get; set; }
}

public sealed class VoiceDef
{
    /// <summary>square12, square25, square50, triangle, saw, sine, noise, or "off".</summary>
    public string Wave { get; set; } = "square25";
    /// <summary>Octaves above the style's root.</summary>
    public int Octave { get; set; }
    public float Volume { get; set; } = 1f;
    /// <summary>Bass: root, pulse, walk, offbeat, drone. Arp: up, down, updown, random, off. Harmony: thirds, sixths, off.</summary>
    public string Pattern { get; set; } = "";
    /// <summary>Vibrato depth in percent of pitch.</summary>
    public float Vibrato { get; set; }
}

public sealed class DrumsDef
{
    /// <summary>four, two, half, off.</summary>
    public string Kick { get; set; } = "two";
    /// <summary>backbeat, march, sparse, off.</summary>
    public string Snare { get; set; } = "backbeat";
    /// <summary>eighth, sixteenth, quarter, off.</summary>
    public string Hats { get; set; } = "eighth";
    /// <summary>An anvil-like metal hit every other bar.</summary>
    public bool Metal { get; set; }
    public float Volume { get; set; } = 1f;
}

/// <summary>
/// Writes and renders chiptune loops. Eight channels: lead, harmony, bass, arpeggio, pad, kick,
/// snare and hats. Each song is 24 bars in an A, B, A shape, two bars per chord, and loops cleanly.
/// The same style and seed always give the same song.
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

    public static readonly HashSet<string> BassPatterns = ["root", "pulse", "walk", "offbeat", "drone"];
    public static readonly HashSet<string> ArpPatterns = ["up", "down", "updown", "random", "off", ""];
    public static readonly HashSet<string> HarmonyPatterns = ["thirds", "sixths", "off", ""];

    private const int Bars = 24;

    public sealed record Note(int Channel, float Beat, float Beats, float Midi, float Velocity);

    public sealed record Song(MusicStyleDef Style, int Tempo, List<Note> Notes)
    {
        public float SecondsPerBeat => 60f / Tempo;
        public float Seconds => Bars * 4 * SecondsPerBeat;
    }

    private static int Degree(int[] scale, int root, int degree, int octave)
    {
        int index = degree - 1;
        int octaves = (int)Math.Floor(index / (double)scale.Length);
        int step = ((index % scale.Length) + scale.Length) % scale.Length;
        return root + 12 * (octave + octaves) + scale[step];
    }

    /// <summary>Scale degrees of the chord on a degree: stacked thirds for 7-note scales, neighbours for the rest.</summary>
    private static int[] Chord(int[] scale, int degree) =>
        scale.Length == 7 ? [degree, degree + 2, degree + 4] : [degree, degree + 1, degree + 2];

    public static Song Compose(MusicStyleDef style, ulong seed)
    {
        var rng = new Rng(seed ^ Rng.Hash(style.Id));
        var scale = Scales[style.Scale];
        int tempo = style.Tempo[0] + rng.Next(Math.Max(1, style.Tempo[1] - style.Tempo[0] + 1));
        var notes = new List<Note>();

        var progA = rng.Pick(style.Progressions);
        var progB = style.Progressions.Count > 1 ? style.Progressions.First(p => p != progA) : progA.Reverse().ToArray();
        // A, B, A: eight bars each, two bars per chord.
        int[] chordPerBar = Enumerable.Range(0, Bars).Select(bar =>
        {
            var prog = bar / 8 == 1 ? progB : progA;
            return prog[(bar % 8) / 2 % prog.Length];
        }).ToArray();

        var motifA = Motif(rng.Fork("motif-a"), style.Density);
        var motifB = Motif(rng.Fork("motif-b"), style.Density);

        for (int bar = 0; bar < Bars; bar++)
        {
            float beat0 = bar * 4;
            int degree = chordPerBar[bar];
            var chord = Chord(scale, degree);
            int section = bar / 8;

            // Lead: the section's motif, landing on chord tones, varied in the last bars of the reprise.
            var motif = section == 1 ? motifB : motifA;
            var barMotif = motif.Where(m => (int)(m.Beat / 4) == bar % 2).ToList();
            bool vary = section == 2 && bar % 8 >= 6;
            foreach (var (beat, length, step) in barMotif)
            {
                int target = chord[(step + (vary ? 1 : 0)) % chord.Length] + (step >= 3 ? 2 : 0);
                float midi = Degree(scale, style.Root, target, style.Lead.Octave + 1);
                notes.Add(new Note(0, beat0 + beat % 4 + Swing(style, beat), length, midi, 0.85f));
                if (style.Harmony.Pattern is "thirds" or "sixths")
                {
                    int offset = style.Harmony.Pattern == "thirds" ? -2 : -5;
                    notes.Add(new Note(1, beat0 + beat % 4 + Swing(style, beat), length, Degree(scale, style.Root, target + offset, style.Lead.Octave + 1), 0.6f));
                }
            }

            AddBass(notes, style, scale, chord, beat0, rng);

            // Arpeggio through the chord in sixteenths or eighths.
            if (style.Arp.Pattern is not ("off" or ""))
            {
                var tones = chord.Concat(chord.Select(c => c + (scale.Length == 7 ? 7 : scale.Length))).ToArray();
                for (int i = 0; i < 16; i++)
                {
                    int index = style.Arp.Pattern switch
                    {
                        "down" => tones.Length - 1 - i % tones.Length,
                        "updown" => (i % (tones.Length * 2 - 2)) is var k && k < tones.Length ? k : tones.Length * 2 - 2 - k,
                        "random" => rng.Next(tones.Length),
                        _ => i % tones.Length,
                    };
                    notes.Add(new Note(3, beat0 + i * 0.25f, 0.2f, Degree(scale, style.Root, tones[index], style.Arp.Octave + 1), 0.5f));
                }
            }

            // Pad: the chord held for the bar.
            if (style.Pad.Wave != "off")
                foreach (var tone in chord)
                    notes.Add(new Note(4, beat0, 4, Degree(scale, style.Root, tone, style.Pad.Octave + 1), 0.4f));

            AddDrums(notes, style.Drums, beat0, bar);
        }
        return new Song(style, tempo, notes);
    }

    private static float Swing(MusicStyleDef style, float beat) => beat % 1 == 0.5f ? style.Swing * 0.5f : 0;

    /// <summary>A two-bar melodic rhythm: start beat, length and which chord tone (0 to 4) to aim at.</summary>
    private static List<(float Beat, float Length, int Step)> Motif(Rng rng, float density)
    {
        var motif = new List<(float, float, int)>();
        float beat = 0;
        while (beat < 8)
        {
            float length = rng.Pick(new[] { 0.5f, 0.5f, 1f, 1f, 1.5f, 2f });
            if (beat + length > 8) length = 8 - beat;
            if (rng.NextFloat() < density || beat % 4 == 0) motif.Add((beat, length * 0.9f, rng.Next(5)));
            beat += length;
        }
        return motif;
    }

    private static void AddBass(List<Note> notes, MusicStyleDef style, int[] scale, int[] chord, float beat0, Rng rng)
    {
        float Root(int tone) => Degree(scale, style.Root, tone, style.Bass.Octave);
        switch (style.Bass.Pattern)
        {
            case "pulse":
                for (int i = 0; i < 8; i++) notes.Add(new Note(2, beat0 + i * 0.5f, 0.4f, Root(chord[0]), 0.9f));
                break;
            case "walk":
                for (int i = 0; i < 4; i++) notes.Add(new Note(2, beat0 + i, 0.9f, Root(chord[i % chord.Length] + (i == 3 ? 1 : 0)), 0.9f));
                break;
            case "offbeat":
                for (int i = 0; i < 4; i++) notes.Add(new Note(2, beat0 + i + 0.5f, 0.4f, Root(chord[0]), 0.9f));
                break;
            case "drone":
                notes.Add(new Note(2, beat0, 4, Root(chord[0]), 0.8f));
                break;
            default: // root and fifth
                notes.Add(new Note(2, beat0, 1.8f, Root(chord[0]), 0.9f));
                notes.Add(new Note(2, beat0 + 2, 1.8f, Root(chord[^1]), 0.8f));
                break;
        }
    }

    private static void AddDrums(List<Note> notes, DrumsDef drums, float beat0, int bar)
    {
        float[] kicks = drums.Kick switch { "four" => [0, 1, 2, 3], "two" => [0, 2], "half" => [0], _ => [] };
        foreach (var k in kicks) notes.Add(new Note(5, beat0 + k, 0.3f, 0, drums.Volume));

        float[] snares = drums.Snare switch
        {
            "backbeat" => [1, 3],
            "march" => bar % 2 == 1 ? [1, 2.75f, 3, 3.25f, 3.5f, 3.75f] : [1, 3],
            "sparse" => bar % 2 == 1 ? [3] : [],
            _ => [],
        };
        foreach (var s in snares) notes.Add(new Note(6, beat0 + s, 0.2f, 0, drums.Volume));

        float step = drums.Hats switch { "sixteenth" => 0.25f, "eighth" => 0.5f, "quarter" => 1f, _ => 0 };
        if (step > 0) for (float h = 0; h < 4; h += step) notes.Add(new Note(7, beat0 + h, 0.05f, 0, drums.Volume * (h % 1 == 0 ? 1 : 0.6f)));

        if (drums.Metal && bar % 2 == 1) notes.Add(new Note(7, beat0 + 3.5f, -1, 0, drums.Volume)); // length -1 marks the anvil
    }

    /// <summary>Renders a song to stereo interleaved samples that loop seamlessly.</summary>
    public static float[] Render(Song song)
    {
        var style = song.Style;
        float spb = song.SecondsPerBeat;
        int frames = (int)(song.Seconds * Synth.SampleRate);
        // Render with a tail, then fold the tail back onto the start so notes ring across the loop point.
        var buffer = new float[(frames + Synth.SampleRate * 2) * 2];

        var voices = new (VoiceDef Voice, Synth.Envelope Env, float Volume, float Pan)[]
        {
            (style.Lead, new(0.01f, 0.1f, 0.7f, 0.08f), 0.32f, -0.15f),
            (style.Harmony, new(0.01f, 0.1f, 0.6f, 0.08f), 0.16f, 0.35f),
            (style.Bass, new(0.005f, 0.06f, 0.8f, 0.05f), 0.4f, 0f),
            (style.Arp, new(0.002f, 0.06f, 0.3f, 0.03f), 0.13f, 0.45f),
            (style.Pad, new(0.35f, 0.3f, 0.8f, 0.6f), 0.09f, -0.4f),
        };

        foreach (var note in song.Notes)
        {
            float start = note.Beat * spb;
            if (note.Channel < 5)
            {
                var (voice, env, volume, pan) = voices[note.Channel];
                if (voice.Wave == "off") continue;
                Synth.AddNote(buffer, start, note.Beats * spb, Synth.MidiToHz(note.Midi), Synth.ParseWave(voice.Wave), env,
                    volume * voice.Volume * note.Velocity, pan, voice.Vibrato);
            }
            else Drum(buffer, note, start);
        }

        var loop = new float[frames * 2];
        Array.Copy(buffer, loop, loop.Length);
        for (int i = 0; i < buffer.Length - loop.Length; i++) loop[i % loop.Length] += buffer[loop.Length + i];
        Synth.Master(loop);
        return loop;
    }

    private static void Drum(float[] buffer, Note note, float start)
    {
        float v = note.Velocity;
        switch (note.Channel)
        {
            case 5: // kick: a falling sine thump
                Synth.AddNote(buffer, start, 0.01f, 130, Wave.Sine, new(0.001f, 0.01f, 1f, 0.2f), 0.55f * v, 0, slideTo: 40);
                break;
            case 6: // snare: noise and a short body tone
                Synth.AddNote(buffer, start, 0.01f, 0, Wave.Noise, new(0.001f, 0.01f, 1f, 0.14f), 0.25f * v, 0.05f, seed: 17);
                Synth.AddNote(buffer, start, 0.01f, 190, Wave.Triangle, new(0.001f, 0.01f, 1f, 0.08f), 0.2f * v, 0);
                break;
            default:
                if (note.Beats < 0) // anvil
                    foreach (var (ratio, level) in new[] { (1f, 1f), (2.76f, 0.5f), (5.4f, 0.3f) })
                        Synth.AddNote(buffer, start, 0.01f, 420 * ratio, Wave.Sine, new(0.001f, 0.01f, 1f, 0.5f), 0.12f * v * level, 0.2f);
                else // hats: very short noise
                    Synth.AddNote(buffer, start, 0.005f, 0, Wave.Noise, new(0.001f, 0.005f, 1f, 0.03f), 0.08f * v, 0.4f, seed: 29);
                break;
        }
    }
}
