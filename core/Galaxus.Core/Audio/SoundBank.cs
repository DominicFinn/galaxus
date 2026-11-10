namespace Galaxus.Core.Audio;

/// <summary>
/// Sound effects, made from scratch. Ship sounds vary with the race of the ship's builder, so an
/// orc engine growls, an elf engine whines and a dwarf engine chugs. All mono at Synth.SampleRate.
/// </summary>
public static class SoundBank
{
    private const int Rate = Synth.SampleRate;

    private static float[] Mono(float seconds) => new float[(int)(seconds * Rate)];

    private static void Tone(float[] buffer, float start, float seconds, Func<float, float> hz, Wave wave, Func<float, float> envelope, float volume, uint seed = 7)
    {
        float phase = 0;
        uint noise = seed;
        int first = (int)(start * Rate);
        for (int i = 0; i < seconds * Rate && first + i < buffer.Length; i++)
        {
            float t = i / (float)Rate;
            phase += hz(t / seconds) / Rate;
            buffer[first + i] += Synth.Sample(wave, phase, ref noise) * envelope(t / seconds) * volume;
        }
    }

    private static float Decay(float x, float speed) => MathF.Exp(-x * speed);

    private static float[] Finish(float[] mono, float peak = 0.8f)
    {
        float max = mono.Max(MathF.Abs);
        if (max < 1e-6f) return mono;
        for (int i = 0; i < mono.Length; i++) mono[i] *= peak / max;
        return mono;
    }

    /// <summary>A weapon shot. Faster, lighter bolts are higher; the builder's race sets the voice.</summary>
    public static float[] Laser(string race, float projectileSpeed, float damage)
    {
        float pitch = Math.Clamp(projectileSpeed / 900f, 0.5f, 1.6f) * MathF.Pow(10f / MathF.Max(damage, 2f), 0.25f);
        var b = Mono(0.24f);
        switch (race)
        {
            case "light-elf":
                Tone(b, 0, 0.24f, x => 1900 * pitch * (1 - 0.35f * x), Wave.Sine, x => Decay(x, 5), 1);
                Tone(b, 0, 0.24f, x => 2850 * pitch * (1 - 0.35f * x), Wave.Triangle, x => Decay(x, 8), 0.4f);
                break;
            case "dark-elf":
                Tone(b, 0, 0.24f, x => 700 * pitch * (1 + 0.4f * x), Wave.Triangle, x => Decay(x, 4), 1);
                Tone(b, 0, 0.24f, x => 737 * pitch * (1 + 0.4f * x), Wave.Triangle, x => Decay(x, 4), 0.8f);
                break;
            case "orc":
                Tone(b, 0, 0.24f, x => 420 * pitch * (1 - 0.6f * x), Wave.Saw, x => Decay(x, 4), 1);
                Tone(b, 0, 0.12f, _ => 0, Wave.Noise, x => Decay(x, 6), 0.6f, 31);
                break;
            case "dwarf":
                Tone(b, 0, 0.2f, x => 520 * pitch * (1 - 0.7f * x), Wave.Square50, x => Decay(x, 5), 1);
                Tone(b, 0, 0.05f, _ => 0, Wave.Noise, x => 1 - x, 0.7f, 13);
                break;
            case "free-machine":
            case "synth":
                Tone(b, 0, 0.24f, x => 1300 * pitch * (x < 0.3f ? 1 : 0.6f), Wave.Square12, x => Decay(x, 5), 1);
                Tone(b, 0, 0.24f, x => 40, Wave.Square50, x => Decay(x, 5), 0.3f);
                break;
            default:
                Tone(b, 0, 0.22f, x => 1200 * pitch * (1 - 0.6f * x), Wave.Square25, x => Decay(x, 5), 1);
                Tone(b, 0, 0.06f, _ => 0, Wave.Noise, x => 1 - x, 0.3f, 3);
                break;
        }
        return Finish(b, 0.6f);
    }

    /// <summary>
    /// A two-second engine loop for the game to pitch up and down with throttle. Frequencies are
    /// whole multiples of 0.5 Hz so the loop joins without a click.
    /// </summary>
    public static float[] Engine(string race)
    {
        var b = Mono(2f);
        (float Hz, Wave Wave, float Volume)[] voices = race switch
        {
            "light-elf" => [(220, Wave.Sine, 0.5f), (330, Wave.Sine, 0.3f), (880, Wave.Triangle, 0.08f)],
            "dark-elf" => [(55, Wave.Triangle, 0.6f), (82.5f, Wave.Triangle, 0.4f), (165, Wave.Sine, 0.15f)],
            "orc" => [(45, Wave.Saw, 0.7f), (46, Wave.Saw, 0.5f), (90, Wave.Square50, 0.2f)],
            "dwarf" => [(50, Wave.Square50, 0.5f), (75, Wave.Triangle, 0.4f)],
            "free-machine" or "synth" => [(100, Wave.Square12, 0.3f), (150, Wave.Square25, 0.2f), (400, Wave.Sine, 0.1f)],
            _ => [(60, Wave.Saw, 0.4f), (90, Wave.Triangle, 0.4f), (120, Wave.Sine, 0.2f)],
        };
        foreach (var (hz, wave, volume) in voices) Tone(b, 0, 2f, _ => hz, wave, _ => 1, volume);

        // Rumble: low-passed noise, crossfaded over the loop point so it joins cleanly.
        var rumble = Mono(2.2f);
        Tone(rumble, 0, 2.2f, _ => 0, Wave.Noise, _ => 1, race == "orc" ? 0.6f : 0.35f, 99);
        Synth.LowPass(rumble, race == "light-elf" ? 1200 : 300);
        int fade = (int)(0.2f * Rate);
        for (int i = 0; i < b.Length; i++)
        {
            float s = rumble[i];
            if (i < fade) s = s * (i / (float)fade) + rumble[b.Length + i] * (1 - i / (float)fade);
            b[i] += s;
        }

        // Dwarf engines chug: a pulse four times a second.
        if (race == "dwarf")
            for (int i = 0; i < b.Length; i++) b[i] *= 0.65f + 0.35f * MathF.Max(0, MathF.Sin(i / (float)Rate * MathF.Tau * 4));
        return Finish(b, 0.5f);
    }

    public static float[] Explosion(int seed)
    {
        var b = Mono(1.4f);
        Tone(b, 0, 1.4f, _ => 0, Wave.Noise, x => Decay(x, 3.5f), 1, (uint)seed | 1);
        Synth.LowPass(b, 2400, 120);
        Tone(b, 0, 0.5f, x => 70 * (1 - 0.5f * x), Wave.Sine, x => Decay(x, 4), 0.9f);
        return Finish(b, 0.9f);
    }

    public static float[] ShieldHit()
    {
        var b = Mono(0.18f);
        Tone(b, 0, 0.18f, x => 600 + 1400 * x, Wave.Triangle, x => Decay(x, 6), 0.8f);
        Tone(b, 0, 0.18f, x => 2400, Wave.Sine, x => Decay(x, 10), 0.3f);
        return Finish(b, 0.5f);
    }

    public static float[] HullHit()
    {
        var b = Mono(0.16f);
        Tone(b, 0, 0.16f, _ => 0, Wave.Noise, x => Decay(x, 7), 1, 77);
        Synth.LowPass(b, 1800, 400);
        Tone(b, 0, 0.1f, x => 180 * (1 - 0.4f * x), Wave.Square50, x => Decay(x, 8), 0.4f);
        return Finish(b, 0.6f);
    }

    /// <summary>Docking clamps: a heavy metal clunk, then air hiss.</summary>
    public static float[] Dock()
    {
        var b = Mono(1.1f);
        foreach (var (ratio, volume) in new[] { (1f, 1f), (2.76f, 0.6f), (5.4f, 0.35f), (8.9f, 0.2f) })
            Tone(b, 0, 0.6f, _ => 140 * ratio, Wave.Sine, x => Decay(x, 7), volume);
        var hiss = Mono(1.1f);
        Tone(hiss, 0.25f, 0.85f, _ => 0, Wave.Noise, x => MathF.Sin(x * MathF.PI) * 0.5f, 1, 5);
        for (int i = 0; i < b.Length; i++) b[i] += hiss[i] * 0.4f;
        return Finish(b, 0.7f);
    }

    public static float[] Undock()
    {
        var b = Mono(0.8f);
        Tone(b, 0, 0.8f, _ => 0, Wave.Noise, x => (1 - x) * 0.5f, 1, 9);
        Synth.LowPass(b, 3000, 600);
        Tone(b, 0, 0.4f, _ => 110, Wave.Sine, x => Decay(x, 6), 0.8f);
        return Finish(b, 0.6f);
    }

    /// <summary>Gate jump: a rising sweep with shimmer.</summary>
    public static float[] Jump()
    {
        var b = Mono(1.6f);
        Tone(b, 0, 1.6f, x => 80 + 900 * x * x, Wave.Saw, x => MathF.Sin(x * MathF.PI), 0.7f);
        Tone(b, 0, 1.6f, x => 160 + 1800 * x * x, Wave.Triangle, x => MathF.Sin(x * MathF.PI), 0.5f);
        for (int k = 0; k < 6; k++) Tone(b, 0.2f + k * 0.2f, 0.25f, _ => 1500 + k * 250, Wave.Sine, x => Decay(x, 5), 0.2f);
        return Finish(b, 0.7f);
    }

    public static float[] Click()
    {
        var b = Mono(0.04f);
        Tone(b, 0, 0.04f, _ => 1300, Wave.Square25, x => 1 - x, 1);
        return Finish(b, 0.35f);
    }

    public static float[] Confirm()
    {
        var b = Mono(0.18f);
        Tone(b, 0, 0.08f, _ => 880, Wave.Square25, x => 1 - x, 1);
        Tone(b, 0.08f, 0.1f, _ => 1320, Wave.Square25, x => 1 - x, 1);
        return Finish(b, 0.4f);
    }

    public static float[] Error()
    {
        var b = Mono(0.25f);
        Tone(b, 0, 0.25f, _ => 110, Wave.Square50, x => 1 - x, 1);
        Tone(b, 0, 0.25f, _ => 116, Wave.Square50, x => 1 - x, 0.8f);
        return Finish(b, 0.4f);
    }

    /// <summary>Getting paid: a quick rising arpeggio.</summary>
    public static float[] Paid()
    {
        var b = Mono(0.5f);
        float[] notes = [72, 76, 79, 84];
        for (int i = 0; i < notes.Length; i++)
            Tone(b, i * 0.07f, 0.22f, _ => Synth.MidiToHz(notes[i]), Wave.Square25, x => Decay(x, 4), 0.8f);
        return Finish(b, 0.5f);
    }

    public static float[] Pip(int system)
    {
        var b = Mono(0.07f);
        Tone(b, 0, 0.07f, _ => 660 + system * 220, Wave.Square50, x => 1 - x, 1);
        return Finish(b, 0.3f);
    }

    public static float[] TargetLock()
    {
        var b = Mono(0.16f);
        Tone(b, 0, 0.05f, _ => 1760, Wave.Square25, _ => 1, 1);
        Tone(b, 0.08f, 0.05f, _ => 1760, Wave.Square25, _ => 1, 1);
        return Finish(b, 0.3f);
    }

    /// <summary>
    /// Station ambience: an eight-second loop of hum, hiss and machinery, shaped by what the
    /// station does. Mining stations grind, industrial ones thump, high-tech ones chirp.
    /// </summary>
    public static float[] StationAmbience(string economy, ulong seed)
    {
        var rng = new Rng(seed);
        var b = Mono(8f);
        Tone(b, 0, 8f, _ => 50, Wave.Sine, _ => 1, 0.35f);
        Tone(b, 0, 8f, _ => 100, Wave.Triangle, _ => 1, 0.15f);

        var air = Mono(8.5f);
        Tone(air, 0, 8.5f, _ => 0, Wave.Noise, _ => 1, 0.12f, 21);
        Synth.LowPass(air, 700);
        int fade = (int)(0.5f * Rate);
        for (int i = 0; i < b.Length; i++)
            b[i] += i < fade ? air[i] * (i / (float)fade) + air[b.Length + i] * (1 - i / (float)fade) : air[i];

        // Metal clanks: inharmonic partials, scattered through the loop.
        int clanks = economy is "Industrial" or "Mining" or "Refinery" ? 9 : 4;
        for (int c = 0; c < clanks; c++)
        {
            float at = rng.Range(0.2f, 7.2f), baseHz = rng.Range(180, 520), volume = rng.Range(0.15f, 0.35f);
            foreach (var (ratio, v) in new[] { (1f, 1f), (2.76f, 0.5f), (5.4f, 0.25f) })
                Tone(b, at, 0.5f, _ => baseHz * ratio, Wave.Sine, x => Decay(x, 8), volume * v);
        }

        switch (economy)
        {
            case "Industrial": // a press thumping twice a second, a quarter of the time
                for (int beat = 0; beat < 16; beat++)
                    if (beat % 4 != 3) Tone(b, beat * 0.5f, 0.3f, x => 70 * (1 - 0.5f * x), Wave.Sine, x => Decay(x, 9), 0.5f);
                break;
            case "Mining": // a drill grinding in long cycles
            case "Refinery":
                Tone(b, 1, 3, x => 140 + 30 * MathF.Sin(x * 30), Wave.Saw, x => MathF.Sin(x * MathF.PI), 0.18f);
                Tone(b, 5, 2.5f, x => 120 + 25 * MathF.Sin(x * 24), Wave.Saw, x => MathF.Sin(x * MathF.PI), 0.15f);
                break;
            case "HighTech": // terminals chirping
                for (int k = 0; k < 10; k++)
                {
                    float at = rng.Range(0, 7.8f), hz = rng.Range(1500, 2600);
                    Tone(b, at, 0.06f, _ => hz, Wave.Square25, x => 1 - x, 0.12f);
                }
                break;
            case "Agricultural": // pumps and water
                Tone(b, 0, 8, x => 30 + 4 * MathF.Sin(x * MathF.Tau * 4), Wave.Sine, _ => 1, 0.25f);
                break;
        }
        return Finish(b, 0.5f);
    }
}
