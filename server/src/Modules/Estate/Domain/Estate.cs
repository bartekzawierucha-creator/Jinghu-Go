using System.Text.RegularExpressions;
using JianghuGo.Domain;

namespace JianghuGo.Modules.Estate.Domain;

public sealed record EstateConfig(string RulesVersion, GardenRules Garden, DiscipleRules Disciples, IReadOnlyList<ExpeditionDefinition> Expeditions)
{
    public CropDefinition? Crop(string? seed) => seed is null ? null : Garden.Crops.FirstOrDefault(c => c.Seed == seed);

    public ExpeditionDefinition? Expedition(string? id) => id is null ? null : Expeditions.FirstOrDefault(e => e.Id == id);
}

public sealed record Drop(string Item, int ChancePermille, int Min, int Max);

public sealed record CropDefinition(string Seed, string NamePl, int Hours, int MinRealm, IReadOnlyList<Drop> Yields);

public sealed record GardenRules(int PlotsBase, int RealmsPerPlot, int MaxPlots, IReadOnlyList<CropDefinition> Crops);

public sealed record TalentDefinition(string Id, string NamePl, string DescriptionPl);

/// <param name="RecruitStones">Price of the n-th disciple (by number already recruited).</param>
public sealed record DiscipleRules(int BaseSlots, int RealmsPerSlot, int MaxSlots, IReadOnlyList<long> RecruitStones, IReadOnlyList<string> Names,
    IReadOnlyList<TalentDefinition> Talents, int LevelXp, int MaxLevel, int TalentBonusPermille, int LevelChancePermille);

/// <param name="Stones">[min, max] spirit stones brought back.</param>
public sealed record ExpeditionDefinition(string Id, string NamePl, string DescriptionPl, int Hours, int MinRealm, int MinLevel, IReadOnlyList<long> Stones, string Talent,
    IReadOnlyList<Drop> Drops, int Xp);

public sealed record Plot(int Slot, string Seed, DateTimeOffset PlantedAt, DateTimeOffset ReadyAt);

public sealed record Disciple(Guid Id, string Name, string Talent, int Level, int Xp, string? Expedition, DateTimeOffset? Until, DateTimeOffset RecruitedAt);

public sealed record EstateProfile(CharacterId CharacterId, IReadOnlyList<Plot> Plots, IReadOnlyList<Disciple> Disciples, int Recruited, int Version)
{
    public static EstateProfile New(CharacterId id) => new(id, [], [], 0, 0);
}

public static class EstateRules
{
    public static int Plots(EstateConfig c, int realm) => Math.Min(c.Garden.MaxPlots, c.Garden.PlotsBase + (realm / c.Garden.RealmsPerPlot));

    public static int DiscipleSlots(EstateConfig c, int realm) => Math.Min(c.Disciples.MaxSlots, c.Disciples.BaseSlots + (realm / c.Disciples.RealmsPerSlot));

    public static long? RecruitPrice(EstateConfig c, int recruited) =>
        c.Disciples.RecruitStones.Count == 0 ? null : c.Disciples.RecruitStones[Math.Min(recruited, c.Disciples.RecruitStones.Count - 1)];

    public static IReadOnlyList<(string Item, int Quantity)> Roll(IReadOnlyList<Drop> drops, DeterministicRandom rnd, int chanceBonusPermille, int quantityBonusPermille)
    {
        var list = new List<(string, int)>();
        foreach (var d in drops)
        {
            if (rnd.NextInt(0, 1000) >= Math.Min(1000, d.ChancePermille + (d.ChancePermille * chanceBonusPermille / 1000))) continue;
            var q = rnd.NextInt(d.Min, d.Max + 1);
            q += q * quantityBonusPermille / 1000;
            if (q > 0) list.Add((d.Item, q));
        }

        return list;
    }

    public static Disciple AddXp(EstateConfig c, Disciple d, int xp)
    {
        var level = d.Level;
        var total = d.Xp + xp;
        while (level < c.Disciples.MaxLevel && total >= c.Disciples.LevelXp * level)
        {
            total -= c.Disciples.LevelXp * level;
            level++;
        }

        return d with { Level = level, Xp = level >= c.Disciples.MaxLevel ? 0 : total };
    }
}

public static class EstateConfigValidator
{
    private static readonly Regex Id = new("^[a-z0-9_]{1,40}$", RegexOptions.CultureInvariant);

    public static List<string> Validate(EstateConfig? c)
    {
        if (c is null) return ["config is empty."];
        var e = new List<string>();
        if (c.Garden is null || c.Disciples is null || c.Expeditions is null) return ["garden, disciples and expeditions are required."];
        if (c.Garden.PlotsBase is < 1 or > 12 || c.Garden.MaxPlots < c.Garden.PlotsBase || c.Garden.RealmsPerPlot < 1) e.Add("garden plots invalid.");
        foreach (var crop in c.Garden.Crops ?? [])
        {
            if (!Id.IsMatch(crop.Seed ?? "") || crop.Hours is < 1 or > 72 || crop.MinRealm is < 0 or > 65 || string.IsNullOrWhiteSpace(crop.NamePl)) e.Add($"crop '{crop.Seed}' invalid.");
            ValidateDrops(e, "crop " + crop.Seed, crop.Yields);
        }

        var d = c.Disciples;
        if (d.BaseSlots is < 1 or > 10 || d.MaxSlots < d.BaseSlots || d.RealmsPerSlot < 1) e.Add("disciple slots invalid.");
        if (d.RecruitStones is null || d.RecruitStones.Count == 0 || d.RecruitStones.Any(s => s < 0)) e.Add("recruitStones invalid.");
        if (d.Names is null || d.Names.Count == 0 || d.Talents is null || d.Talents.Count == 0 || d.Talents.Any(t => !Id.IsMatch(t.Id ?? ""))) e.Add("disciple names/talents invalid.");
        if (d.LevelXp < 1 || d.MaxLevel is < 1 or > 100 || d.TalentBonusPermille is < 0 or > 2000 || d.LevelChancePermille is < 0 or > 100) e.Add("disciple growth invalid.");
        foreach (var dup in c.Expeditions.GroupBy(x => x.Id).Where(g => g.Count() > 1)) e.Add($"expedition '{dup.Key}' duplicated.");
        foreach (var x in c.Expeditions)
        {
            if (!Id.IsMatch(x.Id ?? "") || string.IsNullOrWhiteSpace(x.NamePl) || x.Hours is < 1 or > 72 || x.MinRealm is < 0 or > 65 || x.MinLevel < 1 || x.Xp < 0)
                e.Add($"expedition '{x.Id}' invalid.");
            if (x.Stones is null || x.Stones.Count != 2 || x.Stones[0] < 0 || x.Stones[1] < x.Stones[0]) e.Add($"expedition {x.Id}: stones must be [min, max].");
            if (d.Talents is not null && d.Talents.All(t => t.Id != x.Talent)) e.Add($"expedition {x.Id}: unknown talent '{x.Talent}'.");
            ValidateDrops(e, "expedition " + x.Id, x.Drops);
        }

        return e;
    }

    private static void ValidateDrops(List<string> e, string where, IReadOnlyList<Drop>? drops)
    {
        if (drops is null || drops.Count == 0) e.Add($"{where}: drops required.");
        else if (drops.Any(x => !Id.IsMatch(x.Item ?? "") || x.ChancePermille is < 1 or > 1000 || x.Min < 1 || x.Max < x.Min || x.Max > 100)) e.Add($"{where}: a drop is invalid.");
    }
}

/// <summary>Expeditions and crops on the Road of 66 Realms (1.9).</summary>
public sealed class EstateRoad(EstateConfig c) : JianghuGo.Modules.Characters.Contracts.IRoadContributor
{
    public IEnumerable<(int Realm, string TextPl)> Unlocks(string path)
    {
        foreach (var x in c.Expeditions.Where(x => x.MinRealm > 0)) yield return (x.MinRealm, "Wyprawa: " + x.NamePl);
        foreach (var crop in c.Garden.Crops.Where(x => x.MinRealm > 0)) yield return (crop.MinRealm, "Uprawa: " + crop.NamePl);
    }
}
