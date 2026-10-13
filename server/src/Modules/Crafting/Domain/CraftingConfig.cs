using JianghuGo.Domain;

namespace JianghuGo.Modules.Crafting.Domain;

public sealed record CraftingConfig(
    string RulesVersion,
    IReadOnlyList<Recipe> Recipes,
    int AlchemyInsightBonusPermille,
    UpgradeRules Upgrade,
    ProfessionRules? Professions = null)
{
    public ProfessionRules ProfessionRules => Professions ?? new ProfessionRules(10, 15, 3, 1, 15, 10, 20);

    public Recipe? Find(string? id) => id is null ? null : Recipes.FirstOrDefault(r => r.Id == id);
}

public sealed record Recipe(string Id, string Kind, IReadOnlyList<ItemAmount> Inputs, long Stones, ItemAmount Output, int SuccessPermille, int MinLevel = 1);

/// <summary>
/// Crafting professions: Alchemy (pills, elixirs) and Smithing (forging and upgrading). Level n needs
/// xpPerLevel × n(n−1)/2 XP in total. Each level above 1 adds success chance and lowers the stone cost.
/// </summary>
public sealed record ProfessionRules(int MaxLevel, int XpPerLevel, int XpPerSuccess, int XpPerFailure, int SuccessPermillePerLevel,
    int UpgradeSuccessPermillePerLevel, int StoneDiscountPermillePerLevel)
{
    public int Level(long xp)
    {
        var level = 1;
        while (level < MaxLevel && xp >= XpFor(level + 1)) level++;
        return level;
    }

    /// <summary>Total XP needed to reach <paramref name="level"/>.</summary>
    public long XpFor(int level) => (long)XpPerLevel * level * (level - 1) / 2;

    public int SuccessBonus(int level) => (level - 1) * SuccessPermillePerLevel;
    public int UpgradeBonus(int level) => (level - 1) * UpgradeSuccessPermillePerLevel;
    public long Stones(long stones, int level) => stones * (1000 - Math.Min(500, (level - 1) * StoneDiscountPermillePerLevel)) / 1000;
}

public static class Professions
{
    public const string Alchemy = "alchemy";
    public const string Smithing = "smithing";

    public static string Of(Recipe r) => r.Kind == RecipeKinds.Alchemy ? Alchemy : Smithing;
}

public sealed record ItemAmount(string Item, int Quantity);

public sealed record UpgradeRules(int MaxLevel, long StonesPerLevel, string Material, IReadOnlyList<int> SuccessPermilleByTargetLevel);

public static class RecipeKinds
{
    public const string Forge = "forge";
    public const string Alchemy = "alchemy";
}

/// <summary>Pure crafting rules. Rolls come from server seeds that are stored with every attempt (auditable, replayable).</summary>
public static class CraftingRules
{
    public const int MaxTimesPerRequest = 10;

    /// <summary>Alchemy benefits from Insight; forging is deterministic by design. Capped at 100 %.</summary>
    public static int ChancePermille(CraftingConfig c, Recipe r, int insight) =>
        r.Kind == RecipeKinds.Alchemy ? Math.Min(1000, r.SuccessPermille + Math.Max(0, insight) * c.AlchemyInsightBonusPermille) : r.SuccessPermille;

    public static IReadOnlyList<int> Rolls(ulong seed, int count)
    {
        var rng = new DeterministicRandom(seed);
        return [.. Enumerable.Range(0, count).Select(_ => rng.NextInt(0, 1000))];
    }

    public static int UpgradeChancePermille(CraftingConfig c, int targetLevel) => c.Upgrade.SuccessPermilleByTargetLevel[targetLevel - 1];

    public static long UpgradeStones(CraftingConfig c, int targetLevel) => c.Upgrade.StonesPerLevel * targetLevel;
}

public static class CraftingConfigValidator
{
    /// <param name="itemExists">Item id → stackable? (null when the item does not exist).</param>
    public static IReadOnlyList<string> Validate(CraftingConfig? c, Func<string, bool?> itemExists)
    {
        if (c is null) return ["Config is missing."];
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(c.RulesVersion)) e.Add("rulesVersion is required.");
        if (c.AlchemyInsightBonusPermille is < 0 or > 100) e.Add("alchemyInsightBonusPermille must be 0-100.");
        if (c.Recipes is null || c.Recipes.Count == 0) return [.. e, "recipes are required."];
        foreach (var d in c.Recipes.GroupBy(r => r.Id).Where(g => g.Count() > 1)) e.Add($"duplicate recipe '{d.Key}'.");
        foreach (var r in c.Recipes)
        {
            if (r.Kind is not (RecipeKinds.Forge or RecipeKinds.Alchemy)) e.Add($"recipe {r.Id}: unknown kind '{r.Kind}'.");
            if (r.Inputs is null || r.Inputs.Count == 0 || r.Inputs.Any(i => i.Quantity is < 1 or > 100 || itemExists(i.Item) is not true))
                e.Add($"recipe {r.Id}: inputs must be existing stackable items with quantity 1-100.");
            else if (r.Inputs.Select(i => i.Item).Distinct().Count() != r.Inputs.Count || r.Inputs.Any(i => i.Item == r.Output?.Item))
                e.Add($"recipe {r.Id}: inputs must be distinct and differ from the output.");
            if (r.Stones < 0) e.Add($"recipe {r.Id}: stones must be >= 0.");
            if (r.Output is null || itemExists(r.Output.Item) is null || r.Output.Quantity is < 1 or > 100) e.Add($"recipe {r.Id}: unknown output.");
            else if (itemExists(r.Output.Item) is false && r.Output.Quantity != 1) e.Add($"recipe {r.Id}: unique items are crafted one at a time.");
            if (r.SuccessPermille is < 1 or > 1000) e.Add($"recipe {r.Id}: successPermille must be 1-1000.");
            if (r.MinLevel < 1 || r.MinLevel > c.ProfessionRules.MaxLevel) e.Add($"recipe {r.Id}: minLevel must be 1-{c.ProfessionRules.MaxLevel}.");
            if (r.Kind == RecipeKinds.Forge && r.SuccessPermille != 1000) e.Add($"recipe {r.Id}: forging never fails (only alchemy does).");
        }

        var p = c.ProfessionRules;
        if (p.MaxLevel is < 1 or > 50 || p.XpPerLevel < 1 || p.XpPerSuccess < 0 || p.XpPerFailure < 0 || p.SuccessPermillePerLevel is < 0 or > 100
            || p.UpgradeSuccessPermillePerLevel is < 0 or > 100 || p.StoneDiscountPermillePerLevel is < 0 or > 100)
            e.Add("professions rules invalid.");
        var u = c.Upgrade;
        if (u is null || u.MaxLevel is < 1 or > 100 || u.StonesPerLevel < 0 || itemExists(u.Material) is not true
            || u.SuccessPermilleByTargetLevel is null || u.SuccessPermilleByTargetLevel.Count != u.MaxLevel
            || u.SuccessPermilleByTargetLevel.Any(p => p is < 1 or > 1000))
            e.Add("upgrade rules invalid (one success chance per level, existing stackable material).");
        else if (u.SuccessPermilleByTargetLevel.Zip(u.SuccessPermilleByTargetLevel.Skip(1)).Any(p => p.Second > p.First))
            e.Add("upgrade success chances must not increase with level.");
        return e;
    }
}
