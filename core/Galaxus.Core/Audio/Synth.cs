namespace Galaxus.Core.Audio;

public enum Wave { Square12, Square25, Square50, Triangle, Saw, Sine, Noise }

/// <summary>
/// A tiny chip-style synthesiser. Everything renders into float buffers at SampleRate; the game
/// turns those into audio streams. No samples or recordings: every sound is made here.
/// </summary>
public static class Synth
{
    public const int SampleRate = 22050;

    public static Wave ParseWave(string name) => name switch
    {
        "square12" => Wave.Square12,
        "square25" => Wave.Square25,
        "square50" or "square" => Wave.Square50,
        "triangle" => Wave.Triangle,
        "saw" => Wave.Saw,
        "sine" => Wave.Sine,
        "noise" => Wave.Noise,
        _ => throw new ArgumentException($"Unknown wave '{name}'."),
    };

    public static readonly HashSet<string> WaveNames = ["square12", "square25", "square50", "square", "triangle", "saw", "sine", "noise"];

    public static float MidiToHz(float midi) => 440f * MathF.Pow(2f, (midi - 69f) / 12f);

    /// <summary>One sample of a waveform at phase 0 to 1.</summary>
    public static float Sample(Wave wave, float phase, ref uint noise)
    {
        phase -= MathF.Floor(phase);
        switch (wave)
        {
            case Wave.Square12: return phase < 0.125f ? 1 : -1;
            case Wave.Square25: return phase < 0.25f ? 1 : -1;
            case Wave.Square50: return phase < 0.5f ? 1 : -1;
            case Wave.Triangle: return phase < 0.5f ? phase * 4 - 1 : 3 - phase * 4;
            case Wave.Saw: return phase * 2 - 1;
            case Wave.Sine: return MathF.Sin(phase * MathF.Tau);
            default:
                noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
                return (noise & 0xffff) / 32768f - 1f;
        }
    }

    /// <summary>Attack, decay, sustain level and release, in seconds.</summary>
    public readonly record struct Envelope(float Attack, float Decay, float Sustain, float Release)
    {
        /// <summary>Level at time t into a note that is held for <paramref name="held"/> seconds.</summary>
        public float Level(float t, float held)
        {
            if (t < 0) return 0;
            float level = t < Attack ? t / MathF.Max(Attack, 1e-4f)
                : t < Attack + Decay ? 1 - (1 - Sustain) * (t - Attack) / MathF.Max(Decay, 1e-4f)
                : Sustain;
            if (t > held)
            {
                float r = (t - held) / MathF.Max(Release, 1e-4f);
                if (r >= 1) return 0;
                float atRelease = held < Attack ? held / MathF.Max(Attack, 1e-4f) : held < Attack + Decay ? 1 - (1 - Sustain) * (held - Attack) / MathF.Max(Decay, 1e-4f) : Sustain;
                level = atRelease * (1 - r);
            }
            return level;
        }

        public float Length(float held) => held + Release;
    }

    /// <summary>Adds a note into a stereo interleaved buffer.</summary>
    public static void AddNote(float[] stereo, float start, float held, float hz, Wave wave, Envelope env, float volume, float pan,
        float vibrato = 0, float slideTo = 0, uint seed = 1)
    {
        int first = (int)(start * SampleRate);
        int count = (int)(env.Length(held) * SampleRate);
        float left = volume * MathF.Min(1, 1 - pan), right = volume * MathF.Min(1, 1 + pan);
        float phase = 0;
        uint noise = seed | 1;
        int frames = stereo.Length / 2;
        for (int i = 0; i < count && first + i < frames; i++)
        {
            float t = i / (float)SampleRate;
            float f = hz;
            if (slideTo > 0) f = hz + (slideTo - hz) * MathF.Min(1, t / MathF.Max(env.Length(held), 0.01f));
            if (vibrato > 0 && t > 0.12f) f *= 1 + vibrato * 0.01f * MathF.Sin(t * MathF.Tau * 5.5f);
            phase += f / SampleRate;
            float s = Sample(wave, phase, ref noise) * env.Level(t, held);
            int index = (first + i) * 2;
            if (index < 0) continue;
            stereo[index] += s * left;
            stereo[index + 1] += s * right;
        }
    }

    /// <summary>Gentle saturation, then scales the loudest sample to <paramref name="peak"/>.</summary>
    public static void Master(float[] buffer, float peak = 0.85f, float drive = 1.2f)
    {
        float max = 1e-6f;
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = MathF.Tanh(buffer[i] * drive);
            max = MathF.Max(max, MathF.Abs(buffer[i]));
        }
        float gain = peak / max;
        for (int i = 0; i < buffer.Length; i++) buffer[i] *= gain;
    }

    /// <summary>One-pole low-pass filter in place. Cutoff in Hz, optionally changing over the buffer.</summary>
    public static void LowPass(float[] mono, float cutoffStart, float cutoffEnd = -1)
    {
        if (cutoffEnd < 0) cutoffEnd = cutoffStart;
        float y = 0;
        for (int i = 0; i < mono.Length; i++)
        {
            float cutoff = cutoffStart + (cutoffEnd - cutoffStart) * i / mono.Length;
            float a = 1 - MathF.Exp(-MathF.Tau * cutoff / SampleRate);
            y += a * (mono[i] - y);
            mono[i] = y;
        }
    }

    /// <summary>Converts float samples to 16-bit little-endian PCM bytes.</summary>
    public static byte[] ToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)(Math.Clamp(samples[i], -1f, 1f) * 32767);
            bytes[i * 2] = (byte)(s & 0xff);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }
        return bytes;
    }
}
