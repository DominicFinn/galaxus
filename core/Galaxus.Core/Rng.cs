namespace Galaxus.Core;

/// <summary>
/// Small deterministic random generator (SplitMix64). The galaxy, NPCs and missions are
/// regenerated from seeds, so results must be identical on every platform and .NET version.
/// </summary>
public sealed class Rng
{
    private ulong _state;

    public Rng(ulong seed) => _state = seed;

    public Rng(string seed) : this(Hash(seed)) { }

    public ulong NextULong()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    public float NextFloat() => (float)NextDouble();

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    /// <summary>Uniform int in [0, maxExclusive).</summary>
    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(NextULong() % (ulong)maxExclusive);
    }

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

    /// <summary>A child generator whose sequence depends only on this seed and the key.</summary>
    public Rng Fork(string key) => new(NextULong() ^ Hash(key));

    /// <summary>Stable FNV-1a hash. string.GetHashCode is randomised per process, so it can't be used for seeds.</summary>
    public static ulong Hash(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
