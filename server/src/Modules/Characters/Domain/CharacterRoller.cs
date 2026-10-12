using JianghuGo.Domain;

namespace JianghuGo.Modules.Characters.Domain;

public sealed record RolledTraits(string Background, string Bloodline, string Constitution)
{
    public string Get(string category) => category switch
    {
        TraitCategories.Background => Background,
        TraitCategories.Bloodline => Bloodline,
        TraitCategories.Constitution => Constitution,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown trait category."),
    };

    public IEnumerable<string> All => [Background, Bloodline, Constitution];
}

public sealed record RollResult(RolledTraits Traits, IReadOnlyList<KeyValuePair<string, int>> Stats);

/// <summary>
/// Pure, deterministic: (config, seed, locked traits) always yields the same result. This is what makes
/// every roll auditable and replayable from the stored seed and rules version.
/// </summary>
public static class CharacterRoller
{
    public static RollResult Roll(CharacterCreationConfig config, ulong seed, RolledTraits? previous, IReadOnlySet<string> lockedCategories)
    {
        if (lockedCategories.Count > 0 && previous is null)
        {
            throw new ArgumentException("Cannot lock traits without a previous roll.", nameof(lockedCategories));
        }

        var rng = new DeterministicRandom(seed);
        var chosen = new Dictionary<string, string>();
        foreach (var category in TraitCategories.All)
        {
            // Always consume randomness for every category so locking one category does not shift
            // the random stream of the others (makes outcomes easier to reason about in audits).
            var rolled = RollTrait(config, category, rng);
            chosen[category] = lockedCategories.Contains(category) ? previous!.Get(category) : rolled;
        }

        var traits = new RolledTraits(chosen[TraitCategories.Background], chosen[TraitCategories.Bloodline], chosen[TraitCategories.Constitution]);
        var traitDefs = traits.All.Select(config.Trait).ToList();

        var stats = new List<KeyValuePair<string, int>>(config.Stats.Count);
        foreach (var stat in config.Stats)
        {
            var value = stat.Base + rng.NextInt(-stat.Variance, stat.Variance + 1);
            value += traitDefs.Sum(t => t.Modifiers.GetValueOrDefault(stat.Id));
            stats.Add(new(stat.Id, Math.Clamp(value, stat.Min, stat.Max)));
        }

        return new RollResult(traits, stats);
    }

    private static string RollTrait(CharacterCreationConfig config, string category, DeterministicRandom rng)
    {
        var total = Rarities.All.Sum(r => config.RarityWeights[r]);
        var pick = rng.NextInt(0, total);
        var rarity = Rarities.All[^1];
        foreach (var r in Rarities.All)
        {
            pick -= config.RarityWeights[r];
            if (pick < 0)
            {
                rarity = r;
                break;
            }
        }

        var candidates = config.Traits
            .Where(t => t.Category == category && t.Rarity == rarity)
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
        return candidates[rng.NextInt(0, candidates.Count)].Id;
    }
}
