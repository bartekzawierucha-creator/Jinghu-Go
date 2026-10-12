namespace JianghuGo.Modules.Inventory.Domain;

public sealed record ItemsConfig(
    string RulesVersion,
    CurrencyRules Currency,
    BagRules Bag,
    int UpgradeBonusPercentPerLevel,
    IReadOnlyList<ItemDefinition> Items,
    IReadOnlyDictionary<string, LootTable> LootTables)
{
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

public sealed record ConsumableSpec(long Qi);

public sealed record EquipmentSpec(string Slot, IReadOnlyDictionary<string, int> Stats, int BagBonus = 0);

public sealed record LootTable(long Stones, IReadOnlyList<LootDrop> Drops);

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
            if (i.Consumable is { Qi: < 1 }) e.Add($"item {i.Id}: consumable must grant Qi.");
            if (i.Equipment is { } eq)
            {
                if (!EquipmentSlots.All.Contains(eq.Slot)) e.Add($"item {i.Id}: unknown slot '{eq.Slot}'.");
                if (eq.Stats is null || eq.Stats.Any(s => !Stats.Contains(s.Key) || s.Value is < 0 or > 50)) e.Add($"item {i.Id}: stats must be known ids with 0-50.");
                if (eq.BagBonus is < 0 or > 200) e.Add($"item {i.Id}: bagBonus 0-200.");
            }
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
