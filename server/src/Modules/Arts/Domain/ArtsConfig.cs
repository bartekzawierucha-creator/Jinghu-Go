using System.Text.RegularExpressions;
using JianghuGo.Modules.Combat.Contracts;

namespace JianghuGo.Modules.Arts.Domain;

/// <summary>Rules arts-1: heavenly flames, venoms and the toxin body, coatings, Gu, body tempering, sword intent and path branches.</summary>
public sealed record ArtsConfig(
    string RulesVersion,
    IReadOnlyList<FlameDefinition> Flames,
    FlameRules FlameRules,
    IReadOnlyList<VenomDefinition> Venoms,
    ToxinRules Toxin,
    IReadOnlyList<CoatingDefinition> Coatings,
    GuRules Gu,
    TemperingRules Tempering,
    SwordIntentRules SwordIntent,
    BranchRules Branches,
    string WorldTimeZone = "Europe/Warsaw")
{
    public FlameDefinition? Flame(string? id) => id is null ? null : Flames.FirstOrDefault(f => f.Id == id);

    public VenomDefinition? Venom(string? item) => item is null ? null : Venoms.FirstOrDefault(v => v.Item == item);

    public CoatingDefinition? Coating(string? id) => id is null ? null : Coatings.FirstOrDefault(c => c.Id == id);

    public GuType? GuType(string? id) => id is null ? null : Gu.Types.FirstOrDefault(t => t.Id == id);

    public BathDefinition? Bath(string? id) => id is null ? null : Tempering.Baths.FirstOrDefault(b => b.Id == id);

    public IReadOnlyList<BranchDefinition> BranchesOf(string path) => Branches.Paths.GetValueOrDefault(path) ?? [];

    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(WorldTimeZone);
}

/// <param name="Rank">1 = the strongest (First Dawn) … 18 = the weakest (Soul Candle).</param>
/// <param name="Spirit">The NPC guarding the flame (fought to subdue it).</param>
public sealed record FlameDefinition(
    string Id,
    int Rank,
    string NamePl,
    string SourcePl,
    string Spirit,
    IReadOnlyList<FlameSource> Discover,
    int AbsorbHours,
    int MinRealm,
    IReadOnlyDictionary<string, int> Stats,
    PerkBonus Perks,
    int AlchemyPermille,
    int ForgePermille);

/// <summary>How a flame can be found: npc (kill, chance ‰), trace (ember traces: any / night / rare), moon, noon, tower (floor), boss, realm (reached).</summary>
public sealed record FlameSource(string Kind, string? Ref = null, int ChancePermille = 0);

public static class FlameSourceKinds
{
    public const string Npc = "npc";
    public const string Trace = "trace";
    public const string Moon = "moon";
    public const string Noon = "noon";
    public const string Tower = "tower";
    public const string Boss = "boss";
    public const string Realm = "realm";
    public static readonly string[] All = [Npc, Trace, Moon, Noon, Tower, Boss, Realm];
}

public sealed record CodexTier(int Count, string TitlePl);

/// <param name="SlotRealms">Realm needed for each flame slot (slot 1 at realm 0, …).</param>
/// <param name="OtherPathPowerPermille">Non-Flame-path cultivators get this share of a flame's power.</param>
public sealed record FlameRules(
    string FeedItem,
    int XpPerFeed,
    int XpPerLevel,
    int MaxLevel,
    int LevelPermille,
    IReadOnlyList<int> SlotRealms,
    int OtherPathPowerPermille,
    int AbsorbBasePermille,
    int AbsorbRealmPermille,
    int BurnHours,
    int FusionBonusPermille,
    string TraceItem,
    int TracesPerUse,
    IReadOnlyList<CodexTier> Codex);

public sealed record VenomDefinition(string Item, string Rarity, int Xp, string Element);

public sealed record ToxinPerk(int Level, string DescriptionPl, PerkBonus Perks);

/// <param name="Levels">Toxin XP needed for each toxin body level (cumulative thresholds).</param>
public sealed record ToxinRules(
    IReadOnlyList<int> Levels,
    IReadOnlyList<ToxinPerk> Perks,
    int FailPermille,
    string AntidoteItem,
    int PoisonedHours,
    int PoisonedStatMalus,
    IReadOnlyList<CodexTier> Codex);

public sealed record CoatingDefinition(string Id, string NamePl, IReadOnlyList<string> Venoms, int Battles, PerkBonus Perks);

public sealed record GuType(string Id, string NamePl, string DescriptionPl, PerkBonus PerLevel);

public sealed record GuRules(
    IReadOnlyList<GuType> Types,
    int SlotsBase,
    int RealmsPerSlot,
    int MaxSlots,
    int HoursPerLevel,
    int MaxLevel,
    int MaxRank,
    int MutationPermille,
    string HerbItem,
    int HatchHerbs = 5,
    int FeedHerbs = 3);

public sealed record ItemQuantity(string Item, int Quantity);

public sealed record BathDefinition(string Id, string NamePl, IReadOnlyList<ItemQuantity> Items, int Hours, int Xp);

public sealed record TemperStage(int From, string NamePl, int HpPermillePerLevel, PerkBonus Perks);

public sealed record TemperingRules(
    int MaxLevel,
    int StepsPerXp,
    double XpPerDamage,
    int TribulationMultiplier,
    IReadOnlyList<BathDefinition> Baths,
    int XpBase,
    double XpGrowth,
    int ChanceStart,
    int ChanceDecay,
    int ChanceMin,
    int PillEvery,
    string PillItem,
    IReadOnlyList<TemperStage> Stages);

/// <param name="Levels">Sword XP thresholds for swords 1-9 ("⚔N").</param>
public sealed record SwordIntentRules(
    string EchoItem,
    int XpPerEcho,
    int XpPerSwordsman,
    IReadOnlyList<string> Swordsmen,
    IReadOnlyList<int> Levels,
    int DamagePermillePerLevel,
    int FirstStrikePermillePerLevel,
    PerkBonus FullArrayPerks);

public sealed record BranchDefinition(string Id, string NamePl, string DescriptionPl, PerkBonus PerTier);

/// <param name="TierRealms">Realm at which each branch tier opens (I, II, III).</param>
public sealed record BranchRules(IReadOnlyList<int> TierRealms, IReadOnlyDictionary<string, IReadOnlyList<BranchDefinition>> Paths, long RespecStones = 1000);

public static class ArtsConfigValidator
{
    public static readonly string[] Stats = ["strength", "spirit", "agility", "constitution", "insight", "willpower", "focus", "elementalAffinity", "fate"];
    public static readonly string[] Paths = ["qi", "body", "flame", "poison"];
    private static readonly Regex Id = new("^[a-z0-9_]{1,40}$", RegexOptions.CultureInvariant);

    public static List<string> Validate(ArtsConfig? c)
    {
        if (c is null) return ["config is empty."];
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(c.RulesVersion)) e.Add("rulesVersion is required.");
        if (c.Flames is null || c.FlameRules is null || c.Venoms is null || c.Toxin is null || c.Coatings is null || c.Gu is null || c.Tempering is null
            || c.SwordIntent is null || c.Branches is null)
            return [.. e, "flames, flameRules, venoms, toxin, coatings, gu, tempering, swordIntent and branches are required."];
        try
        {
            _ = c.TimeZone;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            e.Add($"worldTimeZone '{c.WorldTimeZone}' is unknown.");
        }

        // flames
        if (c.Flames.Count == 0) e.Add("at least one flame is required.");
        foreach (var d in c.Flames.GroupBy(f => f.Id).Where(g => g.Count() > 1)) e.Add($"flame '{d.Key}' duplicated.");
        foreach (var d in c.Flames.GroupBy(f => f.Rank).Where(g => g.Count() > 1)) e.Add($"flame rank {d.Key} duplicated.");
        foreach (var f in c.Flames)
        {
            if (!Id.IsMatch(f.Id ?? "")) e.Add($"flame id '{f.Id}' invalid.");
            if (f.Rank is < 1 or > 99) e.Add($"flame {f.Id}: rank 1-99.");
            if (string.IsNullOrWhiteSpace(f.NamePl) || string.IsNullOrWhiteSpace(f.SourcePl) || string.IsNullOrWhiteSpace(f.Spirit)) e.Add($"flame {f.Id}: name, source and spirit are required.");
            if (f.Discover is null || f.Discover.Count == 0) e.Add($"flame {f.Id}: at least one discovery source.");
            foreach (var s in f.Discover ?? [])
            {
                if (!FlameSourceKinds.All.Contains(s.Kind)) e.Add($"flame {f.Id}: unknown source '{s.Kind}'.");
                if (s.Kind == FlameSourceKinds.Npc && (s.Ref is null || s.ChancePermille is < 1 or > 1000)) e.Add($"flame {f.Id}: npc source needs ref and chance 1-1000.");
                if (s.Kind is FlameSourceKinds.Tower or FlameSourceKinds.Realm && !int.TryParse(s.Ref, out _)) e.Add($"flame {f.Id}: {s.Kind} source needs a number.");
                if (s.Kind == FlameSourceKinds.Trace && s.Ref is not ("any" or "night" or "rare")) e.Add($"flame {f.Id}: trace ref must be any/night/rare.");
            }

            if (f.AbsorbHours is < 1 or > 168 || f.MinRealm is < 0 or > 65) e.Add($"flame {f.Id}: absorbHours 1-168, minRealm 0-65.");
            if (f.Stats is null || f.Stats.Any(s => !Stats.Contains(s.Key) || s.Value is < 0 or > 50)) e.Add($"flame {f.Id}: stats must be known ids 0-50.");
            if (f.Perks is null) e.Add($"flame {f.Id}: perks required.");
            if (f.AlchemyPermille is < 0 or > 500 || f.ForgePermille is < 0 or > 500) e.Add($"flame {f.Id}: crafting bonuses 0-500.");
        }

        var r = c.FlameRules;
        if (r.XpPerFeed < 1 || r.XpPerLevel < 1 || r.MaxLevel is < 1 or > 50 || r.LevelPermille is < 0 or > 1000) e.Add("flameRules: feed/level values invalid.");
        if (r.SlotRealms is null || r.SlotRealms.Count is < 1 or > 6 || r.SlotRealms[0] != 0 || r.SlotRealms.Zip(r.SlotRealms.Skip(1)).Any(p => p.Second < p.First))
            e.Add("flameRules.slotRealms must start at 0 and not decrease (1-6 slots).");
        if (r.OtherPathPowerPermille is < 0 or > 1000 || r.AbsorbBasePermille is < 1 or > 1000 || r.AbsorbRealmPermille is < 0 or > 100) e.Add("flameRules: power/absorb values invalid.");
        if (r.BurnHours is < 0 or > 168 || r.FusionBonusPermille is < 0 or > 1000 || r.TracesPerUse < 1) e.Add("flameRules: burn/fusion/trace values invalid.");
        if (string.IsNullOrWhiteSpace(r.FeedItem) || string.IsNullOrWhiteSpace(r.TraceItem)) e.Add("flameRules: feedItem and traceItem are required.");
        ValidateCodex(e, "flameRules.codex", r.Codex);

        // venoms, toxin body, coatings
        foreach (var d in c.Venoms.GroupBy(v => v.Item).Where(g => g.Count() > 1)) e.Add($"venom '{d.Key}' duplicated.");
        foreach (var v in c.Venoms)
            if (!Id.IsMatch(v.Item ?? "") || v.Xp is < 1 or > 1000) e.Add($"venom '{v.Item}': id and xp 1-1000.");
        var t = c.Toxin;
        if (t.Levels is null || t.Levels.Count == 0 || t.Levels.Zip(t.Levels.Skip(1)).Any(p => p.Second <= p.First) || t.Levels[0] < 1) e.Add("toxin.levels must be increasing and positive.");
        foreach (var p in t.Perks ?? [])
            if (p.Level < 1 || p.Level > (t.Levels?.Count ?? 0) || p.Perks is null || string.IsNullOrWhiteSpace(p.DescriptionPl)) e.Add($"toxin perk at level {p.Level} invalid.");
        if (t.FailPermille is < 0 or > 1000 || t.PoisonedHours is < 0 or > 72 || t.PoisonedStatMalus is < 0 or > 20 || string.IsNullOrWhiteSpace(t.AntidoteItem)) e.Add("toxin: fail/poison values invalid.");
        ValidateCodex(e, "toxin.codex", t.Codex);
        foreach (var co in c.Coatings)
        {
            if (!Id.IsMatch(co.Id ?? "") || string.IsNullOrWhiteSpace(co.NamePl)) e.Add($"coating '{co.Id}' invalid.");
            if (co.Venoms is null || co.Venoms.Count == 0 || co.Venoms.Any(v => c.Venom(v) is null)) e.Add($"coating {co.Id}: venoms must be known venoms.");
            if (co.Battles is < 1 or > 50 || co.Perks is null) e.Add($"coating {co.Id}: battles 1-50 and perks required.");
        }

        // Gu
        var g = c.Gu;
        if (g.Types is null || g.Types.Count == 0 || g.Types.Any(x => !Id.IsMatch(x.Id ?? "") || x.PerLevel is null)) e.Add("gu.types invalid.");
        if (g.SlotsBase is < 1 or > 9 || g.RealmsPerSlot < 1 || g.MaxSlots < g.SlotsBase || g.MaxSlots > 12) e.Add("gu: slot values invalid.");
        if (g.HoursPerLevel is < 1 or > 168 || g.MaxLevel is < 1 or > 20 || g.MaxRank is < 1 or > 9 || g.MutationPermille is < 0 or > 1000) e.Add("gu: growth values invalid.");
        if (string.IsNullOrWhiteSpace(g.HerbItem) || g.HatchHerbs < 1 || g.FeedHerbs < 1) e.Add("gu: herb item and costs are required.");

        // tempering
        var tm = c.Tempering;
        if (tm.MaxLevel is < 1 or > 999 || tm.StepsPerXp < 1 || tm.XpPerDamage < 0 || tm.TribulationMultiplier < 1) e.Add("tempering: basic values invalid.");
        if (tm.XpBase < 1 || tm.XpGrowth < 1 || tm.ChanceStart is < 1 or > 1000 || tm.ChanceMin is < 1 or > 1000 || tm.ChanceDecay < 0 || tm.PillEvery < 1) e.Add("tempering: curve values invalid.");
        if (tm.Stages is null || tm.Stages.Count == 0 || tm.Stages[0].From != 1 || tm.Stages.Zip(tm.Stages.Skip(1)).Any(p => p.Second.From <= p.First.From)) e.Add("tempering.stages must start at 1 and increase.");
        foreach (var b in tm.Baths ?? [])
            if (!Id.IsMatch(b.Id ?? "") || b.Items is null || b.Items.Count == 0 || b.Items.Any(i => i.Quantity < 1) || b.Hours is < 1 or > 48 || b.Xp < 1) e.Add($"bath '{b.Id}' invalid.");

        // sword intent
        var s2 = c.SwordIntent;
        if (s2.Levels is null || s2.Levels.Count is < 1 or > 9 || s2.Levels.Zip(s2.Levels.Skip(1)).Any(p => p.Second <= p.First)) e.Add("swordIntent.levels must be 1-9 increasing thresholds.");
        if (s2.XpPerEcho < 1 || s2.XpPerSwordsman < 0 || s2.Swordsmen is null || s2.FullArrayPerks is null) e.Add("swordIntent values invalid.");

        // branches
        var br = c.Branches;
        if (br.TierRealms is null || br.TierRealms.Count is < 1 or > 5 || br.TierRealms.Zip(br.TierRealms.Skip(1)).Any(p => p.Second <= p.First)) e.Add("branches.tierRealms invalid.");
        foreach (var path in Paths)
        {
            var list = br.Paths?.GetValueOrDefault(path);
            if (list is null || list.Count == 0) e.Add($"branches: path '{path}' has no branches.");
            else if (list.Any(b => !Id.IsMatch(b.Id ?? "") || b.PerTier is null || string.IsNullOrWhiteSpace(b.NamePl))) e.Add($"branches: path '{path}' has an invalid branch.");
        }

        return e;
    }

    private static void ValidateCodex(List<string> e, string where, IReadOnlyList<CodexTier>? codex)
    {
        if (codex is null) return;
        if (codex.Any(x => x.Count < 1 || string.IsNullOrWhiteSpace(x.TitlePl)) || codex.Zip(codex.Skip(1)).Any(p => p.Second.Count <= p.First.Count)) e.Add($"{where} must have increasing counts and titles.");
    }
}
