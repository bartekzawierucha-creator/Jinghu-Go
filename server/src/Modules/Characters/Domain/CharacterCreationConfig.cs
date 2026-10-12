namespace JianghuGo.Modules.Characters.Domain;

// Data model of content/game-config/character-creation.json. All formulas' inputs live here,
// never as literals in code. Validated at startup by CharacterConfigValidator.

public sealed record CharacterCreationConfig(
    string RulesVersion,
    int InitialDestinyTokens,
    int MaxCharactersPerAccount,
    int MaxRerollsPerDraft,
    RerollRules Reroll,
    NameRules Name,
    IReadOnlyDictionary<string, int> RarityWeights,
    IReadOnlyList<string> Paths,
    IReadOnlyList<StatDefinition> Stats,
    IReadOnlyList<TraitDefinition> Traits)
{
    public int RerollCost(int lockedTraits) => Reroll.BaseCost + (Reroll.CostPerLockedTrait * lockedTraits);

    public TraitDefinition Trait(string id) =>
        Traits.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException($"Unknown trait '{id}'.");
}

public sealed record RerollRules(int BaseCost, int CostPerLockedTrait, int MaxLockedTraits);

public sealed record NameRules(int MinLength, int MaxLength);

public sealed record StatDefinition(string Id, int Base, int Variance, int Min, int Max);

public sealed record TraitDefinition(
    string Id,
    string Category,
    string Rarity,
    string NamePl,
    string NameEn,
    IReadOnlyDictionary<string, int> Modifiers);

public static class TraitCategories
{
    public const string Background = "background";
    public const string Bloodline = "bloodline";
    public const string Constitution = "constitution";

    /// <summary>Fixed order: part of the roll algorithm, changing it requires a new rules version.</summary>
    public static readonly IReadOnlyList<string> All = [Background, Bloodline, Constitution];
}

public static class Rarities
{
    public static readonly IReadOnlyList<string> All = ["common", "rare", "epic", "legendary", "mythic"];
}

public static class StatIds
{
    public static readonly IReadOnlyList<string> All =
        ["strength", "spirit", "agility", "constitution", "insight", "willpower", "focus", "elementalAffinity", "fate"];
}

public static class CultivationPaths
{
    public static readonly IReadOnlyList<string> All = ["qi", "body", "flame", "poison"];
}
