namespace Galaxus.Core.Audio;

/// <summary>
/// Sound effects, made from scratch, to sit with the lo-fi music: soft attacks, rounded tones,
/// nothing shrill, and each kind of sound at its own level (a menu tick is quiet, an explosion
/// isn't). Ship sounds vary with the race of the ship's builder, so an orc engine growls, an elf
/// engine hums and a dwarf engine chugs. All mono at Synth.SampleRate.
/// </summary>
public static class SoundBank
{
    private const int Rate = Synth.SampleRate;

    /// <summary>Peak levels by role, so effects sit in a mix rather than all shouting at once.</summary>
    public const float UiPeak = 0.3f, WeaponPeak = 0.42f, HitPeak = 0.38f, BigPeak = 0.6f, LoopPeak = 0.4f;

    private static float[] Mono(float seconds) => new float[(int)(seconds * Rate)];

    /// <summary>A tone into a buffer. Frequency and envelope take the position through the sound, 0 to 1.</summary>
    private static void Tone(float[] buffer, float start, float seconds, Func<float, float> hz, Wave wave, Func<float, float> envelope, float volume, uint seed = 7)
    {
        // Double precision: over a four-second loop, float rounding would leave the end a fraction
        // of a cycle out, and the loop would click where it joins.
        double phase = 0;
        uint noise = seed;
        int first = (int)(start * Rate);
        for (int i = 0; i < seconds * Rate && first + i < buffer.Length; i++)
        {
            float t = i / (float)Rate;
            phase += hz(t / seconds) / (double)Rate;
            phase -= Math.Floor(phase);
            buffer[first + i] += Synth.Sample(wave, (float)phase, ref noise) * envelope(t / seconds) * volume;
        }
    }

    private static float Decay(float x, float speed) => MathF.Exp(-x * speed);

    /// <summary>A quick fade-in then a decay, so nothing starts with a click.</summary>
    private static Func<float, float> Pluck(float seconds, float speed, float attack = 0.004f) =>
        x => MathF.Min(1, x * seconds / attack) * MathF.Exp(-x * speed);

    /// <summary>Rises and falls like a breath.</summary>
    private static float Swell(float x) => MathF.Sin(MathF.Min(x, 1) * MathF.PI);

    private static float[] Finish(float[] mono, float peak)
    {
        float max = mono.Max(MathF.Abs);
        if (max < 1e-6f) return mono;
        for (int i = 0; i < mono.Length; i++) mono[i] = MathF.Tanh(mono[i] / max * 1.1f) / MathF.Tanh(1.1f) * peak;
        return mono;
    }

    // Weapons ---------------------------------------------------------------------------------

    /// <summary>
    /// A weapon shot: a soft, quick "pew" rather than a buzz. Faster, lighter bolts are higher; the
    /// builder's race sets the voice. The game varies the pitch a little on every shot.
    /// </summary>
    public static float[] Laser(string race, float projectileSpeed, float damage)
    {
        float pitch = Math.Clamp(projectileSpeed / 900f, 0.5f, 1.6f) * MathF.Pow(10f / MathF.Max(damage, 2f), 0.25f);
        var b = Mono(0.2f);
        switch (race)
        {
            case "light-elf": // a bright, glassy chime
                Tone(b, 0, 0.2f, x => 1300 * pitch * (1 - 0.3f * x), Wave.Sine, Pluck(0.2f, 9), 1);
                Tone(b, 0, 0.2f, x => 1950 * pitch * (1 - 0.3f * x), Wave.Sine, Pluck(0.2f, 14), 0.25f);
                break;
            case "dark-elf": // a low, rising hum
                Tone(b, 0, 0.2f, x => 520 * pitch * (1 + 0.35f * x), Wave.Triangle, Pluck(0.2f, 7), 1);
                Tone(b, 0, 0.2f, x => 545 * pitch * (1 + 0.35f * x), Wave.Sine, Pluck(0.2f, 7), 0.7f);
                break;
            case "orc": // a heavy thump with grit
                Tone(b, 0, 0.2f, x => 330 * pitch * (1 - 0.55f * x), Wave.Triangle, Pluck(0.2f, 7), 1);
                Tone(b, 0, 0.1f, _ => 0, Wave.Noise, Pluck(0.1f, 9), 0.35f, 31);
                Synth.LowPass(b, 2200);
                break;
            case "dwarf": // a solid, mechanical knock
                Tone(b, 0, 0.18f, x => 420 * pitch * (1 - 0.6f * x), Wave.Triangle, Pluck(0.18f, 9), 1);
                Tone(b, 0, 0.04f, _ => 0, Wave.Noise, Pluck(0.04f, 6), 0.4f, 13);
                Synth.LowPass(b, 2600);
                break;
            case "free-machine":
            case "synth": // a clean, clipped blip
                Tone(b, 0, 0.16f, x => 900 * pitch * (x < 0.25f ? 1 : 0.75f), Wave.Sine, Pluck(0.16f, 9), 1);
                Tone(b, 0, 0.16f, x => 1800 * pitch * (x < 0.25f ? 1 : 0.75f), Wave.Sine, Pluck(0.16f, 16), 0.2f);
                break;
            default: // human: the classic "pew", softened
                Tone(b, 0, 0.18f, x => 980 * pitch * (1 - 0.55f * x), Wave.Triangle, Pluck(0.18f, 9), 1);
                Tone(b, 0, 0.05f, _ => 0, Wave.Noise, Pluck(0.05f, 7), 0.15f, 3);
                Synth.LowPass(b, 3200);
                break;
        }
        return Finish(b, WeaponPeak);
    }

    /// <summary>
    /// A shot from any weapon kind, each with its own voice. Lasers keep the builder's race voice
    /// (see <see cref="Laser"/>); the heavier kinds sound like what they are.
    /// </summary>
    public static float[] Shot(string kind, string race, float projectileSpeed, float damage)
    {
        float weight = Math.Clamp(MathF.Log10(MathF.Max(damage, 2)) / 2.5f, 0.3f, 1.2f); // heavier hits sound lower
        switch (kind)
        {
            case "pulse": // two punchy blips
            {
                var b = Mono(0.18f);
                for (int k = 0; k < 2; k++)
                    Tone(b, k * 0.06f, 0.09f, x => (760 - 200 * weight) * (1 - 0.4f * x), Wave.Triangle, Pluck(0.09f, 10), 1 - k * 0.25f);
                Synth.LowPass(b, 3000);
                return Finish(b, WeaponPeak);
            }
            case "beam": // a short, humming zap
            {
                var b = Mono(0.3f);
                Tone(b, 0, 0.3f, x => (620 - 150 * weight) * (1 + 0.03f * MathF.Sin(x * 60)), Wave.Sine, x => MathF.Min(1, x * 40) * Decay(x, 4), 1);
                Tone(b, 0, 0.3f, x => (1240 - 300 * weight) * (1 + 0.03f * MathF.Sin(x * 60)), Wave.Sine, x => MathF.Min(1, x * 40) * Decay(x, 6), 0.3f);
                return Finish(b, WeaponPeak * 0.9f);
            }
            case "plasma": // a wobbly, low bloop
            {
                var b = Mono(0.3f);
                Tone(b, 0, 0.3f, x => (300 - 80 * weight) * (1 + 0.25f * MathF.Sin(x * 40)) * (1 - 0.4f * x), Wave.Triangle, Pluck(0.3f, 6), 1);
                Tone(b, 0, 0.12f, _ => 0, Wave.Noise, Pluck(0.12f, 8), 0.2f, 61);
                Synth.LowPass(b, 1800);
                return Finish(b, WeaponPeak);
            }
            case "rail": // a sharp crack that rings
            {
                var b = Mono(0.45f);
                Tone(b, 0, 0.04f, _ => 0, Wave.Noise, Pluck(0.04f, 5, 0.001f), 0.7f, 71);
                Synth.LowPass(b, 4500);
                Tone(b, 0, 0.45f, _ => 1400 - 300 * weight, Wave.Sine, Pluck(0.45f, 7, 0.001f), 0.35f);
                Tone(b, 0, 0.25f, x => 180 * (1 - 0.3f * x), Wave.Sine, Pluck(0.25f, 8), 0.6f);
                return Finish(b, WeaponPeak);
            }
            case "scatter": // a soft, gritty puff
            {
                var b = Mono(0.22f);
                Tone(b, 0, 0.22f, _ => 0, Wave.Noise, Pluck(0.22f, 9), 1, 83);
                Synth.LowPass(b, 2200, 900);
                Tone(b, 0, 0.1f, x => 220 * (1 - 0.4f * x), Wave.Triangle, Pluck(0.1f, 9), 0.5f);
                return Finish(b, WeaponPeak * 0.9f);
            }
            case "flak": // a deep launch thump
            {
                var b = Mono(0.3f);
                Tone(b, 0, 0.3f, x => 110 * (1 - 0.5f * x), Wave.Sine, Pluck(0.3f, 8), 1);
                Tone(b, 0, 0.06f, _ => 0, Wave.Noise, Pluck(0.06f, 7), 0.3f, 91);
                Synth.LowPass(b, 1400);
                return Finish(b, WeaponPeak * 0.85f);
            }
            case "missile": // a rising whoosh as it lights
            case "torpedo": // the same, heavier and lower
            {
                bool heavy = kind == "torpedo";
                float seconds = heavy ? 1f : 0.7f;
                var b = Mono(seconds);
                Tone(b, 0, seconds, _ => 0, Wave.Noise, x => MathF.Min(1, x * 12) * Decay(x, 3), 1, heavy ? 97u : 93u);
                Synth.LowPass(b, heavy ? 500 : 900, heavy ? 1300 : 2600);
                Tone(b, 0, seconds * 0.6f, x => (heavy ? 70 : 110) * (1 + 0.6f * x), Wave.Sine, Pluck(seconds * 0.6f, 5, 0.02f), 0.5f);
                return Finish(b, WeaponPeak);
            }
            default:
                return Laser(race, projectileSpeed, damage);
        }
    }

    /// <summary>A flak shell bursting: a muffled pop and a puff of grit.</summary>
    public static float[] FlakBurst()
    {
        var b = Mono(0.4f);
        Tone(b, 0, 0.4f, _ => 0, Wave.Noise, Pluck(0.4f, 7, 0.002f), 1, 101);
        Synth.LowPass(b, 1600, 300);
        Tone(b, 0, 0.2f, x => 90 * (1 - 0.4f * x), Wave.Sine, Pluck(0.2f, 9), 0.6f);
        return Finish(b, HitPeak);
    }

    /// <summary>
    /// A four-second engine loop for the game to pitch up and down with throttle. Frequencies are
    /// whole multiples of 0.25 Hz so the loop joins without a click, and nothing in it buzzes.
    /// </summary>
    public static float[] Engine(string race)
    {
        const float Seconds = 4f;
        var b = Mono(Seconds);
        (float Hz, Wave Wave, float Volume)[] voices = race switch
        {
            "light-elf" => [(220, Wave.Sine, 0.5f), (330, Wave.Sine, 0.25f), (440.25f, Wave.Sine, 0.06f)],
            "dark-elf" => [(55, Wave.Triangle, 0.6f), (82.5f, Wave.Sine, 0.35f), (110.25f, Wave.Sine, 0.12f)],
            "orc" => [(45, Wave.Saw, 0.6f), (45.75f, Wave.Saw, 0.45f), (90, Wave.Triangle, 0.2f)],
            "dwarf" => [(50, Wave.Triangle, 0.6f), (75, Wave.Triangle, 0.35f)],
            "free-machine" or "synth" => [(100, Wave.Triangle, 0.35f), (150.25f, Wave.Sine, 0.2f), (300, Wave.Sine, 0.06f)],
            _ => [(60, Wave.Triangle, 0.45f), (90, Wave.Sine, 0.35f), (120.25f, Wave.Sine, 0.15f)],
        };
        foreach (var (hz, wave, volume) in voices) Tone(b, 0, Seconds, _ => hz, wave, _ => 1, volume);
        if (race == "orc")
        {
            // Filter two copies and keep the second, so the filter is already running at the start
            // and the loop's start matches its end.
            var twice = b.Concat(b).ToArray();
            Synth.LowPass(twice, 500);
            Array.Copy(twice, b.Length, b, 0, b.Length);
        }

        // Rumble: low-passed noise, crossfaded over the loop point so it joins cleanly.
        var rumble = Mono(Seconds + 0.4f);
        Tone(rumble, 0, Seconds + 0.4f, _ => 0, Wave.Noise, _ => 1, race == "orc" ? 0.5f : 0.3f, 99);
        Synth.LowPass(rumble, race == "light-elf" ? 900 : 260);
        int fade = (int)(0.4f * Rate);
        for (int i = 0; i < b.Length; i++)
        {
            float s = rumble[i];
            if (i < fade) s = s * (i / (float)fade) + rumble[b.Length + i] * (1 - i / (float)fade);
            b[i] += s;
        }

        // Dwarf engines chug: a soft pulse four times a second.
        if (race == "dwarf")
            for (int i = 0; i < b.Length; i++) b[i] *= 0.7f + 0.3f * MathF.Max(0, MathF.Sin(i / (float)Rate * MathF.Tau * 4));
        return Finish(b, LoopPeak);
    }

    // Hits and explosions ---------------------------------------------------------------------

    public static float[] Explosion(int seed)
    {
        var b = Mono(1.6f);
        Tone(b, 0, 1.6f, _ => 0, Wave.Noise, x => MathF.Min(1, x * 80) * Decay(x, 3.2f), 1, (uint)seed | 1);
        Synth.LowPass(b, 1500, 90);
        Tone(b, 0, 0.6f, x => 64 * (1 - 0.45f * x), Wave.Sine, Pluck(0.6f, 4, 0.006f), 1);
        // Debris crackling a moment later.
        var rng = new Rng((ulong)seed * 977);
        for (int k = 0; k < 6; k++)
            Tone(b, rng.Range(0.15f, 0.9f), 0.05f, _ => 0, Wave.Noise, Pluck(0.05f, 8), 0.12f, (uint)(seed * 31 + k) | 1);
        return Finish(b, BigPeak);
    }

    /// <summary>Energy shields taking a hit: a soft, rising "fwip".</summary>
    public static float[] ShieldHit()
    {
        var b = Mono(0.2f);
        Tone(b, 0, 0.2f, x => 420 + 500 * x, Wave.Sine, Pluck(0.2f, 7), 0.9f);
        Tone(b, 0, 0.12f, _ => 0, Wave.Noise, Pluck(0.12f, 9), 0.25f, 41);
        Synth.LowPass(b, 2400);
        return Finish(b, HitPeak);
    }

    /// <summary>The hull taking a hit: a dull, metallic thud.</summary>
    public static float[] HullHit()
    {
        var b = Mono(0.22f);
        Tone(b, 0, 0.22f, _ => 0, Wave.Noise, Pluck(0.22f, 8), 1, 77);
        Synth.LowPass(b, 1200, 300);
        Tone(b, 0, 0.14f, x => 150 * (1 - 0.35f * x), Wave.Triangle, Pluck(0.14f, 8), 0.6f);
        return Finish(b, HitPeak);
    }

    // Ship and station moments ----------------------------------------------------------------

    /// <summary>Docking clamps: a soft, heavy clunk, then air hiss.</summary>
    public static float[] Dock()
    {
        var b = Mono(1.2f);
        foreach (var (ratio, volume) in new[] { (1f, 1f), (2.76f, 0.4f), (5.4f, 0.15f) })
            Tone(b, 0, 0.6f, _ => 120 * ratio, Wave.Sine, Pluck(0.6f, 8), volume);
        var hiss = Mono(1.2f);
        Tone(hiss, 0.25f, 0.9f, _ => 0, Wave.Noise, x => Swell(x) * 0.5f, 1, 5);
        Synth.LowPass(hiss, 2500);
        for (int i = 0; i < b.Length; i++) b[i] += hiss[i] * 0.5f;
        return Finish(b, BigPeak * 0.8f);
    }

    public static float[] Undock()
    {
        var b = Mono(0.9f);
        Tone(b, 0, 0.9f, _ => 0, Wave.Noise, x => MathF.Min(1, x * 20) * (1 - x) * 0.5f, 1, 9);
        Synth.LowPass(b, 2200, 500);
        Tone(b, 0, 0.5f, _ => 100, Wave.Sine, Pluck(0.5f, 6), 0.8f);
        return Finish(b, BigPeak * 0.7f);
    }

    /// <summary>Gate jump: a deep whoosh rising into a soft shimmer.</summary>
    public static float[] Jump()
    {
        var b = Mono(1.8f);
        Tone(b, 0, 1.8f, _ => 0, Wave.Noise, Swell, 0.6f, 51);
        Synth.LowPass(b, 400, 2600);
        Tone(b, 0, 1.8f, x => 70 + 500 * x * x, Wave.Sine, Swell, 0.7f);
        float[] chord = [69, 73, 76, 81];
        for (int k = 0; k < chord.Length; k++)
            Tone(b, 0.6f + k * 0.15f, 0.9f, _ => Synth.MidiToHz(chord[k]), Wave.Sine, x => Swell(x) * Decay(x, 1.5f), 0.12f);
        return Finish(b, BigPeak * 0.75f);
    }

    // Interface -------------------------------------------------------------------------------

    /// <summary>A soft tick, for menus.</summary>
    public static float[] Click()
    {
        var b = Mono(0.06f);
        Tone(b, 0, 0.06f, _ => 1100, Wave.Sine, Pluck(0.06f, 12, 0.003f), 1);
        return Finish(b, UiPeak * 0.6f);
    }

    /// <summary>Yes: two soft rising notes, like a marimba.</summary>
    public static float[] Confirm()
    {
        var b = Mono(0.35f);
        Tone(b, 0, 0.25f, _ => Synth.MidiToHz(76), Wave.Sine, Pluck(0.25f, 9), 1);
        Tone(b, 0.08f, 0.27f, _ => Synth.MidiToHz(81), Wave.Sine, Pluck(0.27f, 9), 0.9f);
        return Finish(b, UiPeak);
    }

    /// <summary>No: two soft falling notes, low and round, never a buzz.</summary>
    public static float[] Error()
    {
        var b = Mono(0.35f);
        Tone(b, 0, 0.18f, _ => Synth.MidiToHz(62), Wave.Triangle, Pluck(0.18f, 7), 1);
        Tone(b, 0.12f, 0.23f, _ => Synth.MidiToHz(57), Wave.Triangle, Pluck(0.23f, 7), 1);
        Synth.LowPass(b, 1800);
        return Finish(b, UiPeak);
    }

    /// <summary>Getting paid: a gentle rising arpeggio, like a kalimba.</summary>
    public static float[] Paid()
    {
        var b = Mono(0.7f);
        float[] notes = [72, 76, 79, 84];
        for (int i = 0; i < notes.Length; i++)
        {
            float hz = Synth.MidiToHz(notes[i]);
            Tone(b, i * 0.08f, 0.4f, _ => hz, Wave.Sine, Pluck(0.4f, 7), 0.8f);
            Tone(b, i * 0.08f, 0.08f, _ => hz * 5.9f, Wave.Sine, Pluck(0.08f, 12), 0.1f);
        }
        return Finish(b, UiPeak);
    }

    /// <summary>Power and time steps: a soft blip, higher for each step up.</summary>
    public static float[] Pip(int system)
    {
        var b = Mono(0.1f);
        Tone(b, 0, 0.1f, _ => 520 + system * 160, Wave.Sine, Pluck(0.1f, 10), 1);
        return Finish(b, UiPeak * 0.7f);
    }

    /// <summary>A target picked or a lock made: two soft pings.</summary>
    public static float[] TargetLock()
    {
        var b = Mono(0.25f);
        Tone(b, 0, 0.12f, _ => 1320, Wave.Sine, Pluck(0.12f, 10), 1);
        Tone(b, 0.09f, 0.14f, _ => 1760, Wave.Sine, Pluck(0.14f, 10), 0.8f);
        return Finish(b, UiPeak * 0.8f);
    }

    /// <summary>
    /// A missile coming for you: an urgent two-note "doo-dee", rounded so it warns without
    /// grating when it repeats. The game plays it faster as the missile closes.
    /// </summary>
    public static float[] MissileWarning()
    {
        var b = Mono(0.24f);
        Tone(b, 0, 0.1f, _ => 880, Wave.Triangle, Pluck(0.1f, 5, 0.006f), 1);
        Tone(b, 0.11f, 0.12f, _ => 1175, Wave.Triangle, Pluck(0.12f, 6, 0.006f), 1);
        Synth.LowPass(b, 3000);
        return Finish(b, UiPeak);
    }

    /// <summary>
    /// Station ambience: a sixteen-second loop of hum, air and machinery, shaped by what the station
    /// does. Mining stations grind, industrial ones thump, high-tech ones chirp softly.
    /// </summary>
    public static float[] StationAmbience(string economy, ulong seed)
    {
        const float Seconds = 16f;
        var rng = new Rng(seed);
        var b = Mono(Seconds);
        Tone(b, 0, Seconds, _ => 50, Wave.Sine, _ => 1, 0.3f);
        Tone(b, 0, Seconds, _ => 100, Wave.Sine, _ => 1, 0.1f);

        var air = Mono(Seconds + 0.5f);
        Tone(air, 0, Seconds + 0.5f, _ => 0, Wave.Noise, _ => 1, 0.12f, 21);
        Synth.LowPass(air, 600);
        int fade = (int)(0.5f * Rate);
        for (int i = 0; i < b.Length; i++)
            b[i] += i < fade ? air[i] * (i / (float)fade) + air[b.Length + i] * (1 - i / (float)fade) : air[i];

        // Distant metal clanks: inharmonic partials, scattered through the loop.
        int clanks = economy is "Industrial" or "Mining" or "Refinery" ? 12 : 6;
        for (int c = 0; c < clanks; c++)
        {
            float at = rng.Range(0.2f, Seconds - 0.8f), baseHz = rng.Range(160, 420), volume = rng.Range(0.08f, 0.2f);
            foreach (var (ratio, v) in new[] { (1f, 1f), (2.76f, 0.4f), (5.4f, 0.15f) })
                Tone(b, at, 0.5f, _ => baseHz * ratio, Wave.Sine, Pluck(0.5f, 8), volume * v);
        }

        switch (economy)
        {
            case "Industrial": // a press thumping, three beats in four
                for (int beat = 0; beat < Seconds * 2; beat++)
                    if (beat % 4 != 3) Tone(b, beat * 0.5f, 0.3f, x => 70 * (1 - 0.5f * x), Wave.Sine, Pluck(0.3f, 9), 0.4f);
                break;
            case "Mining": // a drill grinding in long cycles, muffled behind bulkheads
            case "Refinery":
            {
                var drill = Mono(Seconds);
                Tone(drill, 1, 3, x => 140 + 30 * MathF.Sin(x * 30), Wave.Saw, Swell, 0.16f);
                Tone(drill, 7, 2.5f, x => 120 + 25 * MathF.Sin(x * 24), Wave.Saw, Swell, 0.13f);
                Tone(drill, 11.5f, 3, x => 130 + 28 * MathF.Sin(x * 27), Wave.Saw, Swell, 0.14f);
                Synth.LowPass(drill, 700);
                for (int i = 0; i < b.Length; i++) b[i] += drill[i];
                break;
            }
            case "HighTech": // terminals chirping softly
                for (int k = 0; k < 14; k++)
                {
                    float at = rng.Range(0, Seconds - 0.2f), hz = rng.Range(1100, 1900);
                    Tone(b, at, 0.08f, _ => hz, Wave.Sine, Pluck(0.08f, 9), 0.08f);
                }
                break;
            case "Agricultural": // pumps and water
                Tone(b, 0, Seconds, x => 30 + 4 * MathF.Sin(x * MathF.Tau * 8), Wave.Sine, _ => 1, 0.22f);
                break;
        }
        return Finish(b, LoopPeak);
    }
}
