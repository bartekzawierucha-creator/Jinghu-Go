namespace JianghuGo.Modules.Characters.Domain;

public static class CharacterConfigValidator
{
    /// <summary>Returns every problem found (empty list = valid). Never throws on bad content.</summary>
    public static IReadOnlyList<string> Validate(CharacterCreationConfig? c)
    {
        var errors = new List<string>();
        if (c is null)
        {
            return ["Config is missing."];
        }

        if (string.IsNullOrWhiteSpace(c.RulesVersion)) errors.Add("rulesVersion is required.");
        if (c.InitialDestinyTokens < 0) errors.Add("initialDestinyTokens must be >= 0.");
        if (c.MaxCharactersPerAccount is < 1 or > 20) errors.Add("maxCharactersPerAccount must be 1-20.");
        if (c.MaxRerollsPerDraft < 0) errors.Add("maxRerollsPerDraft must be >= 0.");

        if (c.Reroll is null) errors.Add("reroll section is required.");
        else
        {
            if (c.Reroll.BaseCost < 1) errors.Add("reroll.baseCost must be >= 1 (free rerolls allow unlimited fishing for mythic traits).");
            if (c.Reroll.CostPerLockedTrait < 0) errors.Add("reroll.costPerLockedTrait must be >= 0.");
            if (c.Reroll.MaxLockedTraits < 0 || c.Reroll.MaxLockedTraits >= TraitCategories.All.Count)
                errors.Add($"reroll.maxLockedTraits must be 0-{TraitCategories.All.Count - 1}.");
        }

        if (c.Name is null || c.Name.MinLength < 1 || c.Name.MaxLength < c.Name.MinLength || c.Name.MaxLength > 64)
            errors.Add("name rules invalid (1 <= minLength <= maxLength <= 64).");

        if (c.Paths is null || !c.Paths.Order().SequenceEqual(CultivationPaths.All.Order()))
            errors.Add($"paths must be exactly: {string.Join(", ", CultivationPaths.All)}.");

        if (c.RarityWeights is null || !c.RarityWeights.Keys.Order().SequenceEqual(Rarities.All.Order()))
            errors.Add($"rarityWeights must define exactly: {string.Join(", ", Rarities.All)}.");
        else if (c.RarityWeights.Values.Any(w => w <= 0))
            errors.Add("rarityWeights must all be > 0.");

        if (c.Stats is null || !c.Stats.Select(s => s.Id).SequenceEqual(StatIds.All))
            errors.Add($"stats must be exactly, in order: {string.Join(", ", StatIds.All)}.");
        else
        {
            foreach (var s in c.Stats)
            {
                if (s.Variance < 0) errors.Add($"stat {s.Id}: variance must be >= 0.");
                if (s.Min > s.Max) errors.Add($"stat {s.Id}: min > max.");
                if (s.Base - s.Variance < s.Min || s.Base + s.Variance > s.Max) errors.Add($"stat {s.Id}: base +/- variance outside [min, max].");
            }
        }

        if (c.Traits is null || c.Traits.Count == 0)
        {
            errors.Add("traits are required.");
            return errors;
        }

        foreach (var dup in c.Traits.GroupBy(t => t.Id).Where(g => g.Count() > 1))
            errors.Add($"duplicate trait id '{dup.Key}'.");

        foreach (var t in c.Traits)
        {
            if (string.IsNullOrWhiteSpace(t.Id)) errors.Add("trait with empty id.");
            if (!TraitCategories.All.Contains(t.Category)) errors.Add($"trait {t.Id}: unknown category '{t.Category}'.");
            if (!Rarities.All.Contains(t.Rarity)) errors.Add($"trait {t.Id}: unknown rarity '{t.Rarity}'.");
            if (string.IsNullOrWhiteSpace(t.NamePl) || string.IsNullOrWhiteSpace(t.NameEn)) errors.Add($"trait {t.Id}: names required.");
            foreach (var (stat, delta) in t.Modifiers ?? new Dictionary<string, int>())
            {
                if (!StatIds.All.Contains(stat)) errors.Add($"trait {t.Id}: unknown stat '{stat}'.");
                if (delta is < -10 or > 10) errors.Add($"trait {t.Id}: modifier {stat}={delta} outside [-10, 10].");
            }
        }

        foreach (var cat in TraitCategories.All)
        foreach (var rarity in Rarities.All)
        {
            if (!c.Traits.Any(t => t.Category == cat && t.Rarity == rarity))
                errors.Add($"no trait for category '{cat}' with rarity '{rarity}' (roll could not resolve).");
        }

        return errors;
    }
}
