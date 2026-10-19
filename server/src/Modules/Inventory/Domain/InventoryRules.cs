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
        var list = equipped.ToList();
        foreach (var i in list)
        {
            if (c.Find(i.ItemId)?.Equipment is not { } eq) continue;
            foreach (var (stat, v) in eq.Stats)
            {
                sum[stat] = sum.GetValueOrDefault(stat) + ScaledStat(c, v, i.UpgradeLevel);
            }

            foreach (var a in Affixes(c, i)) sum[a.Stat] = sum.GetValueOrDefault(a.Stat) + a.Value;
        }

        foreach (var (_, bonus) in ActiveSets(c, list))
            foreach (var (stat, v) in bonus) sum[stat] = sum.GetValueOrDefault(stat) + v;
        return sum;
    }

    public static readonly IReadOnlyDictionary<string, int> AffixCount = new Dictionary<string, int> { ["mortal"] = 0, ["spirit"] = 1, ["earth"] = 1, ["heavenly"] = 2, ["divine"] = 3 };

    public static int RarityTier(string rarity) => rarity switch { "spirit" => 2, "earth" => 3, "heavenly" => 4, "divine" => 5, _ => 1 };

    public sealed record RolledAffix(string Id, string NamePl, string Stat, int Value);

    /// <summary>The item's random properties: a pure function of its (server-generated) id and rarity — never stored, never rerolled.</summary>
    public static IReadOnlyList<RolledAffix> Affixes(ItemsConfig c, ItemInstance i)
    {
        if (c.AffixList.Count == 0 || c.Find(i.ItemId) is not { Equipment: not null } d) return [];
        var n = Math.Min(AffixCount.GetValueOrDefault(d.Rarity), c.AffixList.Count);
        if (n == 0) return [];
        var rng = new DeterministicRandom(SeedFrom(i.Id, new Guid("6a69616e-6768-752d-6166-666978657321")));
        var pool = c.AffixList.ToList();
        var tier = RarityTier(d.Rarity);
        var list = new List<RolledAffix>();
        for (var k = 0; k < n; k++)
        {
            var a = pool[rng.NextInt(0, pool.Count)];
            pool.Remove(a);
            list.Add(new RolledAffix(a.Id, a.NamePl, a.Stat, rng.NextInt(a.Min, a.Max + 1) * tier / 2 + 1));
        }

        return list;
    }

    /// <summary>Sets with enough pieces worn: (set id, summed stats of every reached bonus).</summary>
    public static IReadOnlyList<(ItemSet Set, IReadOnlyDictionary<string, int> Stats)> ActiveSets(ItemsConfig c, IEnumerable<ItemInstance> equipped)
    {
        var ids = equipped.Select(i => i.ItemId).ToHashSet();
        var list = new List<(ItemSet, IReadOnlyDictionary<string, int>)>();
        foreach (var set in c.SetList)
        {
            var worn = set.Items.Count(ids.Contains);
            var stats = new Dictionary<string, int>();
            foreach (var b in set.Bonuses.Where(b => worn >= b.Pieces))
                foreach (var (stat, v) in b.Stats) stats[stat] = stats.GetValueOrDefault(stat) + v;
            if (stats.Count > 0) list.Add((set, stats));
        }

        return list;
    }

    /// <summary>Materials from dismantling a piece: by rarity, plus refining stones for half its upgrade levels.</summary>
    public static IReadOnlyList<LootLine> Dismantle(ItemsConfig c, ItemInstance i)
    {
        var d = c.Find(i.ItemId);
        var rng = new DeterministicRandom(SeedFrom(i.Id, i.Id));
        var lines = new List<LootLine>();
        foreach (var drop in (d is null ? null : c.Dismantle?.GetValueOrDefault(d.Rarity)) ?? [])
            if (rng.NextInt(0, 1000) < drop.ChancePermille) lines.Add(new LootLine(drop.Item, rng.NextInt(drop.Min, drop.Max + 1)));
        if (i.UpgradeLevel >= 2 && c.Find("refining_stone") is not null) lines.Add(new LootLine("refining_stone", i.UpgradeLevel / 2));
        return [.. lines.GroupBy(l => l.ItemId).Select(g => new LootLine(g.Key, g.Sum(l => l.Quantity)))];
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
