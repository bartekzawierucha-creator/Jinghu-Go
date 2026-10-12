using JianghuGo.Domain;
using JianghuGo.Modules.Cultivation.Domain;

namespace JianghuGo.IntegrationTests;

/// <summary>Deterministic seeds so tests can force a breakthrough to succeed or fail.</summary>
public sealed class QueuedSeedSource : IRandomSeedSource
{
    private readonly Queue<ulong> _queue = new();
    private ulong _fallback = 1;

    public void Enqueue(ulong seed)
    {
        lock (_queue) _queue.Enqueue(seed);
    }

    public ulong NextSeed()
    {
        lock (_queue) return _queue.Count > 0 ? _queue.Dequeue() : _fallback++;
    }

    /// <summary>Finds a seed whose roll is below (success) or at/above (failure) the given chance.</summary>
    public static ulong Find(bool success, double chance)
    {
        var ppm = (int)Math.Round(chance * CultivationRules.ChanceScale);
        for (ulong s = 1; ; s++)
        {
            if ((CultivationRules.Roll(s) < ppm) == success) return s;
        }
    }
}
