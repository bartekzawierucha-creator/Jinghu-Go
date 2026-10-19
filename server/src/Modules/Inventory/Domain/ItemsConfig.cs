namespace JianghuGo.Modules.Inventory.Domain;

public sealed record ItemsConfig(
    string RulesVersion,
    CurrencyRules Currency,
    BagRules Bag,
    int UpgradeBonusPercentPerLevel,
    IReadOnlyList<ItemDefinition> Items,
    IReadOnlyDictionary<string, LootTable> LootTables,
    IReadOnlyList<AffixDefinition>? Affixes = null,
    IReadOnlyList<ItemSet>? Sets = null,
    IReadOnlyDictionary<string, IReadOnlyList<LootDrop>>? Dismantle = null,
    MerchantRules? Merchant = null,
    IReadOnlyList<SpecialAffixDefinition>? SpecialAffixes = null)
{
    public IReadOnlyList<AffixDefinition> AffixList => Affixes ?? [];

    public IReadOnlyList<ItemSet> SetList => Sets ?? [];

    public ItemSet? SetOf(string itemId) => SetList.FirstOrDefault(s => s.Items.Contains(itemId));

    public ItemDefinition? Find(string? id) => id is null ? null : Items.FirstOrDefault(i => i.Id == id);
    public ItemDefinition Item(string id) => Find(id) ?? throw new KeyNotFoundException($"Unknown item '{id}'.");
}

public sealed record CurrencyRules(string Id, string NamePl, string NameEn, long InitialGrant);

public sealed record BagRules(int BaseCapacity, int MaxStack);

public sealed record ItemDefinition(
    string Id,
    string NamePl,
    string NameEn,
    string Type,
    string Rarity,
    bool Tradable,
    ConsumableSpec? Consumable,
    EquipmentSpec? Equipment)
{
    public bool Stackable => Type is not (ItemTypes.Equipment or ItemTypes.Scroll);
}

/// <param name="Talisman">A talisman (2.2): instead of Qi it grants stats and/or combat perks for some hours (one active at a time).</param>
public sealed record ConsumableSpec(long Qi, TalismanSpec? Talisman = null);

public sealed record TalismanSpec(int Hours, IReadOnlyDictionary<string, int>? Stats = null, IReadOnlyDictionary<string, int>? Perks = null);

/// <summary>The talisman burning for a character.</summary>
public sealed record ActiveTalisman(JianghuGo.Domain.CharacterId CharacterId, string ItemId, DateTimeOffset Until);

public sealed record EquipmentSpec(string Slot, IReadOnlyDictionary<string, int> Stats, int BagBonus = 0);

public sealed record LootTable(long Stones, IReadOnlyList<LootDrop> Drops);

/// <summary>A random property of a piece of equipment: +[min, max] × rarity tier of one stat (fixed forever by the item's id).</summary>
public sealed record AffixDefinition(string Id, string NamePl, string Stat, int Min, int Max);

public sealed record SetBonus(int Pieces, IReadOnlyDictionary<string, int> Stats);

/// <summary>Equipment set: wearing several pieces together grants extra stats.</summary>
public sealed record ItemSet(string Id, string NamePl, IReadOnlyList<string> Items, IReadOnlyList<SetBonus> Bonuses);

public sealed record LootDrop(string Item, int ChancePermille, int Min, int Max);

public static class ItemTypes
{
    public const string Material = "material";
    public const string Consumable = "consumable";
    public const string Equipment = "equipment";
    public const string Scroll = "scroll"; // unique, carries a reference (e.g. the Dao technique it teaches); not equippable
    public static readonly IReadOnlyList<string> All = [Material, Consumable, Equipment, Scroll];
}

public static class Rarities
{
    public static readonly IReadOnlyList<string> All = ["mortal", "spirit", "earth", "heavenly", "divine"];
}

public static class EquipmentSlots
{
    public static readonly IReadOnlyList<string> All = ["weapon", "robe", "talisman", "ring", "treasure"];
}

public static class ItemsConfigValidator
{
    private static readonly string[] Stats = ["strength", "spirit", "agility", "constitution", "insight", "willpower", "focus", "elementalAffinity", "fate"];

    public static IReadOnlyList<string> Validate(ItemsConfig? c)
    {
        if (c is null) return ["Config is missing."];
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(c.RulesVersion)) e.Add("rulesVersion is required.");
        if (c.Currency is null || c.Currency.InitialGrant < 0) e.Add("currency invalid.");
        if (c.Bag is null || c.Bag.BaseCapacity is < 10 or > 1000 || c.Bag.MaxStack is < 1 or > 1_000_000) e.Add("bag rules invalid.");
        if (c.UpgradeBonusPercentPerLevel is < 0 or > 100) e.Add("upgradeBonusPercentPerLevel must be 0-100.");
        if (c.Items is null || c.Items.Count == 0) return [.. e, "items are required."];
        foreach (var d in c.Items.GroupBy(i => i.Id).Where(g => g.Count() > 1)) e.Add($"duplicate item '{d.Key}'.");
        foreach (var i in c.Items)
        {
            if (string.IsNullOrWhiteSpace(i.Id) || !i.Id.All(ch => ch is >= 'a' and <= 'z' or '_')) e.Add($"item id '{i.Id}' must be [a-z_].");
            if (!ItemTypes.All.Contains(i.Type)) e.Add($"item {i.Id}: unknown type '{i.Type}'.");
            if (!Rarities.All.Contains(i.Rarity)) e.Add($"item {i.Id}: unknown rarity '{i.Rarity}'.");
            if ((i.Type == ItemTypes.Equipment) != (i.Equipment is not null)) e.Add($"item {i.Id}: equipment spec iff type equipment.");
            if ((i.Type == ItemTypes.Consumable) != (i.Consumable is not null)) e.Add($"item {i.Id}: consumable spec iff type consumable.");
            if (i.Consumable is { Qi: < 1, Talisman: null }) e.Add($"item {i.Id}: consumable must grant Qi.");
            if (i.Consumable?.Talisman is { } tal && (tal.Hours is < 1 or > 72 || ((tal.Stats?.Count ?? 0) + (tal.Perks?.Count ?? 0)) == 0
                    || tal.Stats?.Any(kv => kv.Value is < 1 or > 50) == true || tal.Perks?.Any(kv => kv.Value is < 1 or > 300) == true))
                e.Add($"item {i.Id}: talisman needs 1-72 hours and positive stats/perks.");
            if (i.Equipment is { } eq)
            {
                if (!EquipmentSlots.All.Contains(eq.Slot)) e.Add($"item {i.Id}: unknown slot '{eq.Slot}'.");
                if (eq.Stats is null || eq.Stats.Any(s => !Stats.Contains(s.Key) || s.Value is < 0 or > 50)) e.Add($"item {i.Id}: stats must be known ids with 0-50.");
                if (eq.BagBonus is < 0 or > 200) e.Add($"item {i.Id}: bagBonus 0-200.");
            }
        }

        foreach (var a in c.AffixList)
            if (string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.NamePl) || !Stats.Contains(a.Stat) || a.Min < 1 || a.Max < a.Min || a.Max > 10) e.Add($"affix '{a.Id}' invalid.");
        foreach (var set in c.SetList)
        {
            if (string.IsNullOrWhiteSpace(set.Id) || string.IsNullOrWhiteSpace(set.NamePl) || set.Items is null || set.Items.Count < 2) e.Add($"set '{set.Id}' invalid.");
            else if (set.Items.Any(i => c.Find(i)?.Equipment is null)) e.Add($"set {set.Id}: every piece must be equipment.");
            if (set.Bonuses is null || set.Bonuses.Count == 0 || set.Bonuses.Any(b => b.Pieces < 2 || b.Pieces > (set.Items?.Count ?? 0) || b.Stats is null || b.Stats.Any(kv => !Stats.Contains(kv.Key) || kv.Value is < 1 or > 50)))
                e.Add($"set {set.Id}: bonuses invalid.");
        }

        foreach (var dup in c.SetList.SelectMany(s => s.Items).GroupBy(i => i).Where(g => g.Count() > 1)) e.Add($"item {dup.Key} belongs to two sets.");
        if (c.Merchant is { } m)
        {
            if (m.SellPermille is < 1 or > 900 || m.MaxPerTrade is < 1 or > 999 || m.BasePrices is null || m.Stock is null) e.Add("merchant: sellPermille 1-900, maxPerTrade 1-999, prices and stock required.");
            else
            {
                foreach (var i in c.Items.Where(i => i.Tradable)) if (m.BasePrice(i) <= 0) e.Add($"merchant: no base price for grade '{i.Rarity}'.");
                foreach (var o in m.Stock)
                {
                    var d = c.Items.FirstOrDefault(i => i.Id == o.Item);
                    if (d is null) e.Add($"merchant stock: unknown item '{o.Item}'.");
                    else if (o.Price <= m.SellPrice(d)) e.Add($"merchant stock: '{o.Item}' must cost more than the merchant pays for it.");
                }
            }
        }

        foreach (var (rarity, drops) in c.Dismantle ?? new Dictionary<string, IReadOnlyList<LootDrop>>())
        {
            if (!Rarities.All.Contains(rarity)) e.Add($"dismantle: unknown rarity '{rarity}'.");
            foreach (var d in drops)
                if (c.Find(d.Item) is null || d.ChancePermille is < 1 or > 1000 || d.Min < 1 || d.Max < d.Min || d.Max > 100) e.Add($"dismantle {rarity}: drop '{d.Item}' invalid.");
        }

        foreach (var (npc, table) in c.LootTables ?? new Dictionary<string, LootTable>())
        {
            if (table.Stones < 0) e.Add($"loot {npc}: stones >= 0.");
            foreach (var d in table.Drops)
            {
                if (c.Find(d.Item) is null) e.Add($"loot {npc}: unknown item '{d.Item}'.");
                if (d.ChancePermille is < 1 or > 1000 || d.Min < 1 || d.Max < d.Min || d.Max > 100) e.Add($"loot {npc}: drop '{d.Item}' invalid.");
            }
        }

        return e;
    }
}

/// <summary>
/// The travelling merchant (1.6): buys tradable items for <see cref="SellPermille"/> of their base price (by grade, +10 % per upgrade
/// level) and sells a small stock of basic goods at fixed prices. Buying back is always dearer than selling, so there is no loop.
/// </summary>
public sealed record MerchantRules(int SellPermille, IReadOnlyDictionary<string, long> BasePrices, IReadOnlyList<MerchantOffer> Stock, int MaxPerTrade = 99)
{
    public long BasePrice(ItemDefinition d) => BasePrices.GetValueOrDefault(d.Rarity);

    public long SellPrice(ItemDefinition d, int upgradeLevel = 0) => BasePrice(d) * SellPermille / 1000 * (10 + Math.Max(0, upgradeLevel)) / 10;

    public MerchantOffer? Offer(string? itemId) => Stock.FirstOrDefault(o => o.Item == itemId);
}

public sealed record MerchantOffer(string Item, long Price);

/// <summary>A combat perk a heavenly or divine item may carry (1.8): Perk is a PerkBonus field name, values in ‰ (base, before grade).</summary>
public sealed record SpecialAffixDefinition(string Id, string NamePl, string Perk, int Min, int Max);
