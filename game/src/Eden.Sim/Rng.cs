namespace Eden.Sim;

/// <summary>
/// xoshiro256** seeded PRNG. The only randomness source allowed inside the simulation.
/// Value type on purpose: copy it to fork a stream, hash it for determinism checks.
/// </summary>
public struct Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed)
    {
        // SplitMix64 to expand the seed into four non-zero words.
        ulong x = seed;
        _s0 = SplitMix(ref x);
        _s1 = SplitMix(ref x);
        _s2 = SplitMix(ref x);
        _s3 = SplitMix(ref x);
    }

    /// <summary>Raw generator state, for save files only.</summary>
    public readonly (ulong, ulong, ulong, ulong) State => (_s0, _s1, _s2, _s3);

    public static Rng FromState((ulong s0, ulong s1, ulong s2, ulong s3) state)
    {
        var r = default(Rng);
        (r._s0, r._s1, r._s2, r._s3) = state;
        return r;
    }

    private static ulong SplitMix(ref ulong x)
    {
        ulong z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextUInt64()
    {
        ulong result = RotL(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotL(_s3, 45);
        return result;
    }

    private static ulong RotL(ulong x, int k) => (x << k) | (x >> (64 - k));

    /// <summary>Uniform float in [0, 1).</summary>
    public float NextFloat() => (NextUInt64() >> 40) * (1.0f / 16777216.0f);

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Uniform int in [0, maxExclusive).</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(NextDouble() * maxExclusive);
    }

    public int NextInt(int minInclusive, int maxExclusive) => minInclusive + NextInt(maxExclusive - minInclusive);

    public bool Chance(float probability) => NextFloat() < probability;

    /// <summary>Approximately normal (0, 1) via the sum of 12 uniforms; cheap and good enough for genes.</summary>
    public float NextGaussian()
    {
        float sum = 0;
        for (int i = 0; i < 12; i++) sum += NextFloat();
        return sum - 6f;
    }

    /// <summary>Hash of the generator state; feeds <see cref="World.StateHash"/>.</summary>
    public readonly ulong StateHash() => Hash.Combine(_s0, _s1, _s2, _s3);
}

/// <summary>FNV-1a style 64-bit mixing used for deterministic state hashes.</summary>
public static class Hash
{
    public const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong Mix(ulong hash, ulong value)
    {
        for (int i = 0; i < 8; i++)
        {
            hash ^= value & 0xFF;
            hash *= Prime;
            value >>= 8;
        }
        return hash;
    }

    public static ulong Mix(ulong hash, float value) => Mix(hash, BitConverter.SingleToUInt32Bits(value));

    public static ulong Combine(params ReadOnlySpan<ulong> values)
    {
        ulong h = Offset;
        foreach (var v in values) h = Mix(h, v);
        return h;
    }
}
