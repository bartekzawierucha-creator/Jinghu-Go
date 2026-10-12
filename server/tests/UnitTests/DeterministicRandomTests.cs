using JianghuGo.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

public class DeterministicRandomTests
{
    [Fact]
    public void Matches_reference_xoshiro256starstar_vectors()
    {
        // Reference values computed by an independent Python implementation (SplitMix64 seeding).
        // If this fails, every stored seed in production would replay differently: bump rules versions.
        var r0 = new DeterministicRandom(0);
        Assert.SequenceEqual([11091344671253066420UL, 13793997310169335082UL, 1900383378846508768UL],
            [r0.NextUInt64(), r0.NextUInt64(), r0.NextUInt64()]);
        var r42 = new DeterministicRandom(42);
        Assert.SequenceEqual([1546998764402558742UL, 6990951692964543102UL, 12544586762248559009UL],
            [r42.NextUInt64(), r42.NextUInt64(), r42.NextUInt64()]);
    }

    [Fact]
    public void NextInt_stays_in_bounds_and_covers_range()
    {
        var rng = new DeterministicRandom(7);
        var seen = new int[7];
        for (var i = 0; i < 70_000; i++)
        {
            var v = rng.NextInt(-3, 4);
            Assert.InRange(v, -3, 3);
            seen[v + 3]++;
        }

        foreach (var count in seen)
        {
            Assert.InRange(count, 9_000, 11_000, "Bucket count far from uniform 10 000");
        }
    }

    [Fact]
    public void NextInt_rejects_empty_range()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeterministicRandom(1).NextInt(5, 5));
    }
}
