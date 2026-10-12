using JianghuGo.Domain;

namespace JianghuGo.Modules.Inventory.Domain;

public sealed record LootLine(string ItemId, long Quantity);

/// <summary>Pure inventory rules: bag capacity, equipment bonuses, loot rolls. No I/O.</summary>
public static class InventoryRules
{
    /// <summary>Stat bonus of one equipped item: base × (100 + level × bonus%) / 100, rounded down.</summary>
    public static int ScaledStat(ItemsConfig c, int baseValue, int upgradeLevel) =>
        baseValue * (100 + upgradeLevel * c.UpgradeBonusPercentPerLevel) / 100;

    public static IReadOnlyDictionary<string, int> Bonuses(ItemsConfig c, IEnumerable<ItemInstance> equipped)
    {
        var sum = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var i in equipped)
        {
            if (c.Find(i.ItemId)?.Equipment is not { } eq) continue;
            foreach (var (stat, v) in eq.Stats)
            {
                sum[stat] = sum.GetValueOrDefault(stat) + ScaledStat(c, v, i.UpgradeLevel);
            }
        }

        return sum;
    }

    public static int Capacity(ItemsConfig c, IEnumerable<ItemInstance> equipped) =>
        c.Bag.BaseCapacity + equipped.Sum(i => c.Find(i.ItemId)?.Equipment?.BagBonus ?? 0);

    /// <summary>Bag slots in use: one per non-empty stack plus one per unique item in the bag (equipped and listed items do not count).</summary>
    public static int SlotsUsed(int stackCount, IEnumerable<ItemInstance> instances) =>
        stackCount + instances.Count(i => i.State == InstanceStates.Bag);

    public static string? SlotOf(ItemsConfig c, string itemId) => c.Find(itemId)?.Equipment?.Slot;

    /// <summary>Deterministic loot for one rewarded victory. The seed is derived server-side and never exposed.</summary>
    public static (long Stones, IReadOnlyList<LootLine> Items) RollLoot(LootTable table, ulong seed)
    {
        var rng = new DeterministicRandom(seed);
        var items = new List<LootLine>();
        foreach (var d in table.Drops)
        {
            if (rng.NextInt(0, 1000) < d.ChancePermille)
            {
                items.Add(new LootLine(d.Item, rng.NextInt(d.Min, d.Max + 1)));
            }
        }

        return (table.Stones, items);
    }

    /// <summary>A stable 64-bit seed from two GUIDs (event id + entity id).</summary>
    public static ulong SeedFrom(Guid a, Guid b)
    {
        Span<byte> x = stackalloc byte[16];
        Span<byte> y = stackalloc byte[16];
        a.TryWriteBytes(x);
        b.TryWriteBytes(y);
        return SeedMix.Mix(0x6A69616E67687531UL,
            BitConverter.ToInt64(x[..8]), BitConverter.ToInt64(x[8..]), BitConverter.ToInt64(y[..8]), BitConverter.ToInt64(y[8..]));
    }
}
