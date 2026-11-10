namespace Galaxus.Core.Audio;

/// <summary>
/// The instruments and effects the lo-fi composer plays with. Everything is synthesised: soft
/// electric pianos, warm basses, breathy leads, plucked strings, pads, dusty drums, a big shared
/// reverb, tape wobble and vinyl crackle. Instruments write mono samples at <see cref="Rate"/>.
/// </summary>
public static class Lofi
{
    /// <summary>Music sample rate. Lo-fi rolls off the top end anyway, and a lower rate keeps the songs small.</summary>
    public const int Rate = 24000;

    public static readonly HashSet<string> KeysInstruments = ["rhodes", "felt-piano", "glass", "organ", "off"];
    public static readonly HashSet<string> BassInstruments = ["sub", "round", "upright", "off"];
    public static readonly HashSet<string> LeadInstruments = ["flute", "sine", "mellow", "bell", "off"];
    public static readonly HashSet<string> PadInstruments = ["warm", "glass", "choir", "dark", "off"];
    public static readonly HashSet<string> PluckInstruments = ["kalimba", "koto", "harp", "bell", "off"];
    public static readonly HashSet<string> DrumKits = ["boom-bap", "brushes", "dusty", "glitch", "off"];

    private const float Tau = MathF.Tau;

    public static float MidiToHz(float midi) => 440f * MathF.Pow(2f, (midi - 69f) / 12f);

    private static float Wrap(float phase) => phase >= Tau ? phase - Tau * MathF.Floor(phase / Tau) : phase;

    /// <summary>Release tail after a note is let go, so it never clicks off.</summary>
    private static float Release(float t, float hold, float release) =>
        t <= hold ? 1 : MathF.Max(0, MathF.Exp(-(t - hold) / release) - 0.01f * (t - hold) / release);

    private static int Samples(float seconds) => (int)(seconds * Rate);

    // Keys ---------------------------------------------------------------------------------

    /// <summary>A keys note. <paramref name="tone"/> 0 to 1 is how bright it is.</summary>
    public static void Keys(string instrument, float[] buf, int start, float hz, float hold, float vel, float tone)
    {
        switch (instrument)
        {
            case "felt-piano": FeltPiano(buf, start, hz, hold, vel, tone); break;
            case "glass": Fm(buf, start, hz, hold, vel, ratio: 2f, index: 0.8f + tone, indexDecay: 0.8f, decay: 3.5f, attack: 0.006f, release: 0.4f); break;
            case "organ": Organ(buf, start, hz, hold, vel); break;
            case "off": break;
            default: Rhodes(buf, start, hz, hold, vel, tone); break;
        }
    }

    /// <summary>An electric piano: a soft FM tone with a short metallic tine at the start.</summary>
    private static void Rhodes(float[] buf, int start, float hz, float hold, float vel, float tone)
    {
        float decay = 1.4f + 2.6f * MathF.Exp(-hz / 260f);
        int length = Math.Min(Samples(hold + 0.5f), Samples(decay * 3));
        float w = Tau * hz / Rate, tineW = Tau * hz * 7.03f / Rate;
        float phase = 0, tine = 0;
        float bright = 0.4f + tone * 0.9f;
        for (int i = 0; i < length && start + i < buf.Length; i++)
        {
            float t = i / (float)Rate;
            float env = (1 - MathF.Exp(-t / 0.002f)) * MathF.Exp(-t / decay) * Release(t, hold, 0.12f);
            float index = (0.35f + 1.3f * vel * bright) * MathF.Exp(-t / 0.4f) + 0.1f;
            float s = MathF.Sin(phase + index * MathF.Sin(phase));
            float bell = 0.1f * bright * vel * MathF.Sin(tine) * MathF.Exp(-t / 0.045f);
            if (start + i >= 0) buf[start + i] += (s * 0.8f + bell) * env * vel;
            phase = Wrap(phase + w);
            tine = Wrap(tine + tineW);
        }
    }

    /// <summary>A soft upright piano with felt over the hammers: few harmonics, quick to fade.</summary>
    private static void FeltPiano(float[] buf, int start, float hz, float hold, float vel, float tone)
    {
        float decay = 1.2f + 2f * MathF.Exp(-hz / 300f);
        int length = Math.Min(Samples(hold + 0.6f), Samples(decay * 3));
        const int Partials = 6;
        Span<float> phases = stackalloc float[Partials];
        Span<float> steps = stackalloc float[Partials];
        Span<float> amps = stackalloc float[Partials];
        Span<float> decays = stackalloc float[Partials];
        for (int n = 0; n < Partials; n++)
        {
            int k = n + 1;
            steps[n] = Tau * hz * k * MathF.Sqrt(1 + 0.0004f * k * k) / Rate;
            amps[n] = MathF.Pow(k, -(1.6f - tone * 0.5f));
            decays[n] = decay / (1 + 0.7f * n);
        }
        uint noise = (uint)(hz * 977) | 1;
        float hammer = 0;
        for (int i = 0; i < length && start + i < buf.Length; i++)
        {
            float t = i / (float)Rate;
            float s = 0;
            for (int n = 0; n < Partials; n++)
            {
                s += MathF.Sin(phases[n]) * amps[n] * MathF.Exp(-t / decays[n]);
                phases[n] = Wrap(phases[n] + steps[n]);
            }
            hammer += 0.2f * (Noise(ref noise) - hammer);
            s += hammer * 0.25f * MathF.Exp(-t / 0.004f);
            float env = (1 - MathF.Exp(-t / 0.003f)) * Release(t, hold, 0.18f);
            if (start + i >= 0) buf[start + i] += s * env * vel * 0.6f;
        }
    }

    private static void Organ(float[] buf, int start, float hz, float hold, float vel)
    {
        int length = Samples(hold + 0.25f);
        float w = Tau * hz / Rate, phase = 0, lfo = 0;
        for (int i = 0; i < length && start + i < buf.Length; i++)
        {
            float t = i / (float)Rate;
            float s = MathF.Sin(phase) + 0.5f * MathF.Sin(2 * phase) + 0.25f * MathF.Sin(3 * phase) + 0.12f * MathF.Sin(4 * phase);
            float swirl = 1 + 0.1f * MathF.Sin(lfo);
            float env = (1 - MathF.Exp(-t / 0.025f)) * Release(t, hold, 0.08f);
            if (start + i >= 0) buf[start + i] += s * 0.4f * env * vel * swirl;
            phase = Wrap(phase + w);
            lfo = Wrap(lfo + Tau * 5.5f / Rate);
        }
    }

    // Bass ---------------------------------------------------------------------------------

    public static void Bass(string instrument, float[] buf, int start, float hz, float hold, float vel)
    {
        switch (instrument)
        {
            case "round": RoundBass(buf, start, hz, hold, vel); break;
            case "upright": Pluck(buf, start, hz, hold, vel, brightness: 0.25f, sustain: 0.996f, gain: 1.4f); break;
            case "off": break;
            default: SubBass(buf, start, hz, hold, vel); break;
        }
    }

    /// <summary>A round sine bass with a little second harmonic, so it carries on small speakers.</summary>
    private static void SubBass(float[] buf, int start, float hz, float hold, float vel)
    {
        int length = Samples(hold + 0.15f);
        float w = Tau * hz / Rate, phase = 0;
        for (int i = 0; i < length && start + i < buf.Length; i++)
        {
            float t = i / (float)Rate;
            float s = MathF.Sin(phase) + 0.28f * MathF.Sin(2 * phase) + 0.08f * MathF.Sin(3 * phase);
            float env = (1 - MathF.Exp(-t / 0.012f)) * (0.75f + 0.25f * MathF.Exp(-t / 0.4f)) * Release(t, hold, 0.06f);
            if (start + i >= 0) buf[start + i] += MathF.Tanh(s * 1.3f) * env * vel * 0.8f;
            phase = Wrap(phase + w);
        }
    }

    private static void RoundBass(float[] buf, int start, float hz, float hold, float vel)
    {
        int length = Samples(hold + 0.2f);
        float w = Tau * hz / Rate, phase = 0;
        for (int i = 0; i < length && start + i < buf.Length; i++)
        {
            float t = i / (float)Rate;
            float bright = MathF.Exp(-t / 0.15f);
            float s = MathF.Sin(phase) + (0.15f + 0.3f * bright) * MathF.Sin(2 * phase) + 0.15f * bright * MathF.Sin(3 * phase);
            float env = (1 - MathF.Exp(-t / 0.004f)) * MathF.Exp(-t / 1.4f) * Release(t, hold, 0.07f);
            if (start + i >= 0) buf[start + i] += s * env * vel * 0.75f;
            phase = Wrap(phase + w);
        }
    }

    // Leads --------------------------------------------------------------------------------

    public static void Lead(string instrument, float[] buf, int start, float hz, float hold, float vel)
    {
        switch (instrument)
        {
            case "bell": Fm(buf, start, hz, hold, vel, ratio: 3.5f, index: 1.6f, indexDecay: 0.5f, decay: 1.8f, attack: 0.003f, release: 0.5f); break;
            case "off": break;
            default: Breath(buf, start, hz, hold, vel, instrument); break;
        }
    }

    /// <summary>Flute, sine and mellow leads: soft attacks, breath, and vibrato that arrives late.</summary>
    private static void Breath(float[] buf, int start, float hz, float hold, float vel, string kind)
    {
        int length = Samples(hold + 0.3f);
        float w = Tau * hz / Rate, phase = 0, vib = 0;
        uint noise = (uint)(hz * 131) | 1;
        float breath = 0;
        float attack = kind == "flute" ? 0.07f : 0.035f;
        for (int i = 0; i < length && start + i < buf.Length; i++)
        {
            float t = i / (float)Rate;
            float s = kind switch
            {
                "flute" => MathF.Sin(phase) + 0.18f * MathF.Sin(2 * phase) + 0.05f * MathF.Sin(3 * phase),
                "mellow" => MathF.Sin(phase) + 0.22f * MathF.Sin(3 * phase) + 0.08f * MathF.Sin(5 * phase),
                _ => MathF.Sin(phase) + 0.04f * MathF.Sin(3 * phase),
            };
            if (kind == "flute")
            {
                breath += 0.15f * (Noise(ref noise) - breath);
                s += breath * 0.35f * (0.4f + 0.6f * MathF.Exp(-t / 0.08f));
            }
            float env = (1 - MathF.Exp(-t / attack)) * Release(t, hold, 0.14f);
            if (start + i >= 0) buf[start + i] += s * env * vel * 0.5f;
            float depth = 0.0045f * MathF.Min(1, MathF.Max(0, (t - 0.25f) / 0.4f));
            phase = Wrap(phase + w * (1 + depth * MathF.Sin(vib)));
            vib = Wrap(vib + Tau * 5f / Rate);
        }
    }

    // Plucks -------------------------------------------------------------------------------

    public static void Plucked(string instrument, float[] buf, int start, float hz, float hold, float vel)
    {
        switch (instrument)
        {
            case "koto": Pluck(buf, start, hz, hold, vel, brightness: 0.75f, sustain: 0.994f, gain: 0.9f); break;
            case "harp": Pluck(buf, start, hz, hold, vel, brightness: 0.45f, sustain: 0.997f, gain: 0.9f); break;
            case "bell": Fm(buf, start, hz, hold, vel * 0.8f, ratio: 3.5f, index: 1.2f, indexDecay: 0.3f, decay: 1.4f, attack: 0.002f, release: 0.6f); break;
            case "off": break;
            default: Kalimba(buf, start, hz, vel); break;
        }
    }

    private static void Kalimba(float[] buf, int start, float hz, float vel)
    {
        int length = Samples(1.6f);
        float w = Tau * hz / Rate, phase = 0, over = 0;
        for (int i = 0; i < length && start + i < buf.Length; i++)
        {
            float t = i / (float)Rate;
            float s = MathF.Sin(phase) * MathF.Exp(-t / 0.7f) + 0.3f * MathF.Sin(over) * MathF.Exp(-t / 0.06f);
            float env = 1 - MathF.Exp(-t / 0.0015f);
            if (start + i >= 0) buf[start + i] += s * env * vel * 0.7f;
            phase = Wrap(phase + w);
            over = Wrap(over + w * 5.93f);
        }
    }

    /// <summary>
    /// A plucked string (Karplus-Strong): a burst of filtered noise circulating in a tuned delay.
    /// Brightness 0 to 1 sets how sharp the pluck is; sustain how slowly it dies.
    /// </summary>
    private static void Pluck(float[] buf, int start, float hz, float hold, float vel, float brightness, float sustain, float gain)
    {
        float delay = Rate / hz - 0.5f;
        int size = (int)delay + 3;
        var line = new float[size];
        uint noise = (uint)(hz * 7919) | 1;
        float smooth = 0;
        for (int i = 0; i < size; i++)
        {
            smooth += (0.15f + 0.85f * brightness) * (Noise(ref noise) - smooth);
            line[i] = smooth;
        }
        int length = Samples(MathF.Min(hold + 0.4f, 3f));
        int write = 0;
        float last = 0;
        for (int i = 0; i < length && start + i < buf.Length; i++)
        {
            float read = write - delay;
            while (read < 0) read += size;
            int a = (int)read, b = (a + 1) % size;
            float frac = read - a;
            float x = line[a] + (line[b] - line[a]) * frac;
            float y = sustain * 0.5f * (x + last);
            last = x;
            line[write] = y;
            write = (write + 1) % size;
            float t = i / (float)Rate;
            if (start + i >= 0) buf[start + i] += x * gain * vel * Release(t, hold + 0.3f, 0.1f);
        }
    }

    /// <summary>Two-operator FM: bells, glassy keys.</summary>
    private static void Fm(float[] buf, int start, float hz, float hold, float vel, float ratio, float index, float indexDecay, float decay, float attack, float release)
    {
        int length = Math.Min(Samples(hold + release * 3), Samples(decay * 3));
        float w = Tau * hz / Rate, phase = 0, mod = 0;
        for (int i = 0; i < length && start + i < buf.Length; i++)
        {
            float t = i / (float)Rate;
            float idx = index * MathF.Exp(-t / indexDecay) * vel + 0.1f;
            float s = MathF.Sin(phase + idx * MathF.Sin(mod));
            float env = (1 - MathF.Exp(-t / attack)) * MathF.Exp(-t / decay) * Release(t, hold, release);
            if (start + i >= 0) buf[start + i] += s * env * vel * 0.6f;
            phase = Wrap(phase + w);
            mod = Wrap(mod + w * ratio);
        }
    }

    // Pads and space -----------------------------------------------------------------------

    /// <summary>A pad note: slow to swell, slow to fade.</summary>
    public static void Pad(string instrument, float[] buf, int start, float hz, float hold, float vel)
    {
        if (instrument == "off") return;
        int length = Samples(hold + 2.2f);
        float attack = instrument == "glass" ? 1.6f : 1.1f;
        float cutoff = instrument switch { "dark" => 420f, "warm" => 900f, _ => 1400f };
        float[] detune = [1f, 1.0042f, 0.9958f];
        Span<float> phases = stackalloc float[3];
        float lp1 = 0, lp2 = 0, lfo = 0;
        var formants = instrument == "choir" ? (new Svf(650, 0.25f), new Svf(1080, 0.3f)) : (default(Svf), default(Svf));
        float a = 1 - MathF.Exp(-Tau * cutoff / Rate);
        for (int i = 0; i < length && start + i < buf.Length; i++)
        {
            float t = i / (float)Rate;
            float s = 0;
            for (int v = 0; v < 3; v++)
            {
                float p = phases[v] / Tau;
                float dt = hz * detune[v] / Rate;
                s += instrument == "glass"
                    ? MathF.Sin(phases[v]) + 0.3f * MathF.Sin(2 * phases[v]) + 0.1f * MathF.Sin(3 * phases[v])
                    : 2 * p - 1 - PolyBlep(p, dt);
                phases[v] = Wrap(phases[v] + Tau * dt);
            }
            s /= 3;
            if (instrument == "choir")
            {
                s = formants.Item1.BandPass(s) * 1.5f + formants.Item2.BandPass(s);
            }
            else if (instrument != "glass")
            {
                float sweep = 1 + 0.35f * MathF.Sin(lfo);
                float aa = MathF.Min(1, a * sweep);
                lp1 += aa * (s - lp1);
                lp2 += aa * (lp1 - lp2);
                s = lp2 * 1.6f;
            }
            float env = (1 - MathF.Exp(-t / attack)) * Release(t, hold, 1.4f);
            if (start + i >= 0) buf[start + i] += s * env * vel * 0.5f;
            lfo = Wrap(lfo + Tau * 0.13f / Rate);
        }
    }

    /// <summary>A high, slow shimmer for the sense of space: two sines a few cents apart, swelling in and out.</summary>
    public static void Shimmer(float[] buf, int start, float hz, float hold, float vel)
    {
        int length = Samples(hold + 3.5f);
        float w1 = Tau * hz * 1.0017f / Rate, w2 = Tau * hz * 0.9983f / Rate, p1 = 0, p2 = 0;
        for (int i = 0; i < length && start + i < buf.Length; i++)
        {
            float t = i / (float)Rate;
            float env = (1 - MathF.Exp(-t / 2.5f)) * Release(t, hold, 2.2f);
            if (start + i >= 0) buf[start + i] += (MathF.Sin(p1) + MathF.Sin(p2)) * 0.3f * env * vel;
            p1 = Wrap(p1 + w1);
            p2 = Wrap(p2 + w2);
        }
    }

    // Drums --------------------------------------------------------------------------------

    public const int Kick = 1, Snare = 2, Rim = 3, Hat = 4, OpenHat = 5, Brush = 6;

    /// <summary>One drum hit. Dusty kits are lower and heavier; glitch kits tighter and clickier.</summary>
    public static void Drum(string kit, int piece, float[] buf, int start, float vel, uint seed)
    {
        bool heavy = kit == "dusty", tight = kit == "glitch";
        uint noise = seed | 1;
        switch (piece)
        {
            case Kick:
            {
                float phase = 0;
                int length = Samples(heavy ? 0.6f : 0.45f);
                for (int i = 0; i < length && start + i < buf.Length; i++)
                {
                    float t = i / (float)Rate;
                    float f = (heavy ? 42 : 48) + (heavy ? 70 : 80) * MathF.Exp(-t / 0.035f);
                    float s = MathF.Sin(phase) * MathF.Exp(-t / (heavy ? 0.32f : tight ? 0.16f : 0.22f));
                    s += Noise(ref noise) * 0.15f * MathF.Exp(-t / 0.002f);
                    if (start + i >= 0) buf[start + i] += MathF.Tanh(s * 1.4f) * vel * 0.7f;
                    phase = Wrap(phase + Tau * f / Rate);
                }
                break;
            }
            case Snare:
            {
                var band = new Svf(heavy ? 1300 : 1800, 0.7f);
                float body = 0;
                int length = Samples(heavy ? 0.35f : 0.25f);
                for (int i = 0; i < length && start + i < buf.Length; i++)
                {
                    float t = i / (float)Rate;
                    float s = band.BandPass(Noise(ref noise)) * 3f * MathF.Exp(-t / (heavy ? 0.17f : tight ? 0.07f : 0.12f));
                    s += MathF.Sin(body) * 0.5f * MathF.Exp(-t / 0.05f);
                    if (start + i >= 0) buf[start + i] += s * vel * 0.7f;
                    body = Wrap(body + Tau * (heavy ? 165 : 190) / Rate);
                }
                break;
            }
            case Rim:
            {
                float p = 0;
                int length = Samples(0.06f);
                for (int i = 0; i < length && start + i < buf.Length; i++)
                {
                    float t = i / (float)Rate;
                    if (start + i >= 0) buf[start + i] += (MathF.Sin(p) * 0.6f + Noise(ref noise) * 0.2f) * MathF.Exp(-t / 0.01f) * vel * 0.6f;
                    p = Wrap(p + Tau * 1650 / Rate);
                }
                break;
            }
            case Hat:
            case OpenHat:
            {
                // Band-limited noise: high-passed for the sizzle, low-passed so it stays soft.
                float last = 0, hp = 0, lp = 0;
                float decay = piece == OpenHat ? 0.22f : tight ? 0.018f : 0.035f;
                int length = Samples(decay * 5);
                for (int i = 0; i < length && start + i < buf.Length; i++)
                {
                    float t = i / (float)Rate;
                    float x = Noise(ref noise);
                    hp = 0.6f * (hp + x - last);
                    last = x;
                    lp += 0.55f * (hp - lp);
                    if (start + i >= 0) buf[start + i] += lp * 0.45f * MathF.Exp(-t / decay) * vel;
                }
                break;
            }
            case Brush:
            {
                float lp = 0;
                int length = Samples(0.45f);
                for (int i = 0; i < length && start + i < buf.Length; i++)
                {
                    float t = i / (float)Rate;
                    lp += 0.45f * (Noise(ref noise) - lp);
                    float env = (1 - MathF.Exp(-t / 0.03f)) * MathF.Exp(-t / 0.16f);
                    if (start + i >= 0) buf[start + i] += lp * 0.5f * env * vel;
                }
                break;
            }
        }
    }

    // Texture and effects ------------------------------------------------------------------

    /// <summary>Adds vinyl crackle and tape hiss across a stereo buffer.</summary>
    public static void Vinyl(float[] stereo, float crackle, float hiss, uint seed)
    {
        if (crackle <= 0 && hiss <= 0) return;
        var rng = new Rng(seed);
        uint noise = (uint)seed | 1;
        float rate = 4 + 18 * crackle;
        int frames = stereo.Length / 2;
        float lpL = 0, lpR = 0;
        for (int i = 0; i < frames; i++)
        {
            lpL += 0.35f * (Noise(ref noise) - lpL);
            lpR += 0.35f * (Noise(ref noise) - lpR);
            stereo[i * 2] += lpL * 0.012f * hiss;
            stereo[i * 2 + 1] += lpR * 0.012f * hiss;
        }
        if (crackle <= 0) return;
        int pops = (int)(rate * frames / Rate);
        for (int k = 0; k < pops; k++)
        {
            int at = rng.Next(frames);
            float amp = MathF.Pow(rng.NextFloat(), 3) * 0.12f * crackle * (rng.NextFloat() < 0.5f ? -1 : 1);
            float pan = rng.Range(-0.7f, 0.7f);
            int length = 6 + rng.Next(40);
            for (int i = 0; i < length && at + i < frames; i++)
            {
                float s = amp * MathF.Exp(-i / (length * 0.25f)) * (i % 2 == 0 ? 1 : -0.6f);
                stereo[(at + i) * 2] += s * (1 - pan);
                stereo[(at + i) * 2 + 1] += s * (1 + pan);
            }
        }
    }

    /// <summary>Tape wobble: a slowly varying delay on a stereo buffer. Rates should fit the loop a whole number of times.</summary>
    public static void Wow(float[] stereo, float depth, float wowHz, float flutterHz)
    {
        if (depth <= 0) return;
        int size = Samples(0.03f);
        var ringL = new float[size];
        var ringR = new float[size];
        int frames = stereo.Length / 2;
        float baseDelay = 0.009f * Rate;
        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)Rate;
            ringL[i % size] = stereo[i * 2];
            ringR[i % size] = stereo[i * 2 + 1];
            float d = baseDelay + depth * (0.0028f * Rate * MathF.Sin(Tau * wowHz * t) + 0.00025f * Rate * MathF.Sin(Tau * flutterHz * t));
            float read = i - d;
            if (read < 0) { stereo[i * 2] = 0; stereo[i * 2 + 1] = 0; continue; }
            int a = (int)read;
            float frac = read - a;
            int ia = a % size, ib = (a + 1) % size;
            stereo[i * 2] = ringL[ia] + (ringL[ib] - ringL[ia]) * frac;
            stereo[i * 2 + 1] = ringR[ia] + (ringR[ib] - ringR[ia]) * frac;
        }
    }

    /// <summary>A two-pole low-pass on a mono buffer.</summary>
    public static void LowPass(float[] mono, float cutoff)
    {
        float a = 1 - MathF.Exp(-Tau * cutoff / Rate);
        float y1 = 0, y2 = 0;
        for (int i = 0; i < mono.Length; i++)
        {
            y1 += a * (mono[i] - y1);
            y2 += a * (y1 - y2);
            mono[i] = y2;
        }
    }

    /// <summary>Low-pass and high-pass on an interleaved stereo buffer.</summary>
    public static void Tone(float[] stereo, float lowPass, float highPass)
    {
        float a = 1 - MathF.Exp(-Tau * lowPass / Rate);
        float h = MathF.Exp(-Tau * highPass / Rate);
        for (int c = 0; c < 2; c++)
        {
            float y1 = 0, y2 = 0, hpY = 0, hpX = 0;
            for (int i = c; i < stereo.Length; i += 2)
            {
                y1 += a * (stereo[i] - y1);
                y2 += a * (y1 - y2);
                hpY = h * (hpY + y2 - hpX);
                hpX = y2;
                stereo[i] = hpY;
            }
        }
    }

    /// <summary>
    /// A stereo reverb (Freeverb): eight damped combs and four all-passes a side. Big and soft, for
    /// the sense of space. Adds the wet signal of <paramref name="send"/> into <paramref name="stereo"/>.
    /// </summary>
    public static void Reverb(float[] send, float[] stereo, float size, float damp, float wet)
    {
        int[] combs = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
        int[] allpasses = [556, 441, 341, 225];
        const int Spread = 23;
        float scale = Rate / 44100f;
        float feedback = 0.7f + 0.28f * size;
        float damping = 0.4f * damp;
        int frames = stereo.Length / 2;
        for (int side = 0; side < 2; side++)
        {
            var combLines = combs.Select(c => new float[(int)((c + side * Spread) * scale)]).ToArray();
            var combStore = new float[combs.Length];
            var combIndex = new int[combs.Length];
            var apLines = allpasses.Select(c => new float[(int)((c + side * Spread) * scale)]).ToArray();
            var apIndex = new int[allpasses.Length];
            for (int i = 0; i < frames; i++)
            {
                float input = send[i] * 0.03f;
                float output = 0;
                for (int c = 0; c < combLines.Length; c++)
                {
                    var line = combLines[c];
                    float y = line[combIndex[c]];
                    combStore[c] = y * (1 - damping) + combStore[c] * damping;
                    line[combIndex[c]] = input + combStore[c] * feedback;
                    if (++combIndex[c] >= line.Length) combIndex[c] = 0;
                    output += y;
                }
                for (int a = 0; a < apLines.Length; a++)
                {
                    var line = apLines[a];
                    float b = line[apIndex[a]];
                    line[apIndex[a]] = output + b * 0.5f;
                    output = b - output;
                    if (++apIndex[a] >= line.Length) apIndex[a] = 0;
                }
                stereo[i * 2 + side] += output * wet;
            }
        }
    }

    // Helpers ------------------------------------------------------------------------------

    private static float Noise(ref uint state)
    {
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        return (state & 0xffff) / 32768f - 1f;
    }

    private static float PolyBlep(float t, float dt)
    {
        if (t < dt) { t /= dt; return t + t - t * t - 1; }
        if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
        return 0;
    }

    /// <summary>A state-variable filter, used here as a band-pass.</summary>
    private struct Svf(float cutoff, float damping)
    {
        private readonly float _f = 2 * MathF.Sin(MathF.PI * cutoff / Rate);
        private readonly float _q = damping;
        private float _low, _band;

        public float BandPass(float x)
        {
            _low += _f * _band;
            float high = x - _low - _q * _band;
            _band += _f * high;
            return _band;
        }
    }
}
