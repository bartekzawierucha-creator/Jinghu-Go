namespace JianghuGo.Domain;

/// <summary>
/// xoshiro256** seeded through SplitMix64. Unlike <see cref="System.Random"/>, the output sequence is
/// fixed by this implementation and will not change between .NET versions, so a stored seed plus a
/// rules version reproduces any server-side roll exactly (audit / replay requirement).
/// Not a CSPRNG: seeds must come from a cryptographic source (see <see cref="IRandomSeedSource"/>).
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _s0, _s1, _s2, _s3;

    public DeterministicRandom(ulong seed)
    {
        var sm = seed;
        _s0 = SplitMix64(ref sm);
        _s1 = SplitMix64(ref sm);
        _s2 = SplitMix64(ref sm);
        _s3 = SplitMix64(ref sm);
    }

    private static ulong SplitMix64(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        var z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextUInt64()
    {
        var result = Rotl(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>Uniform integer in [minInclusive, maxExclusive) without modulo bias (Lemire-style rejection).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
        }

        var range = (ulong)((long)maxExclusive - minInclusive);
        var limit = ulong.MaxValue - (ulong.MaxValue % range);
        ulong r;
        do
        {
            r = NextUInt64();
        }
        while (r >= limit);

        return (int)((long)minInclusive + (long)(r % range));
    }
}

/// <summary>Source of unpredictable seeds. Production implementation uses a CSPRNG; tests may pin it.</summary>
public interface IRandomSeedSource
{
    ulong NextSeed();
}
