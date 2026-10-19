namespace JianghuGo.Modules.Cultivation.Domain;

// Data model of content/game-config/cultivation.json (rules cultivation-3: 66 realms × 12 stages, continuous Qi flow).
// Fractions are decimal so every computation is exact and identical on every platform.

public sealed record CultivationConfig(
    string RulesVersion,
    string WorldTimeZone,
    IReadOnlyList<string> StageNames,
    IReadOnlyList<EraDefinition> Eras,
    IReadOnlyList<RealmDefinition> Realms,
    QiRequiredRules QiRequired,
    BreakthroughRules Breakthrough,
    FlowRules Flow,
    CompressionRules Compression,
    HeartDemonRules HeartDemons,
    EnlightenmentRules Enlightenment,
    QualityRules Quality,
    KarmaRules Karma,
    AutoBreakthroughRules AutoBreakthrough,
    IReadOnlyList<CultivationMethod> Methods,
    int MethodMasteryMinutesPerLevel,
    int MethodMasteryPermillePerLevel,
    PathRules Paths)
{
    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(WorldTimeZone);

    public EraDefinition EraOf(int realm) => Eras.First(e => e.Era == Realms[realm].Era);

    public CultivationMethod? Method(string? id) => id is null ? null : Methods.FirstOrDefault(m => m.Id == id);

    /// <summary>Realm index by id (-1 when unknown).</summary>
    public int RealmIndex(string id)
    {
        for (var i = 0; i < Realms.Count; i++) if (Realms[i].Id == id) return i;
        return -1;
    }
}

public sealed record EraDefinition(int Era, string NamePl, string BottleneckItem);

public sealed record RealmDefinition(string Id, string NamePl, string NameEn, string Hanzi, int Era, int Stages, string UnlockPl, string TribulationPl, string TitlePl);

/// <summary>Qi to leave (realm, stage): base × realmGrowth^realm × stageGrowth^(stage−1), × majorMultiplier on the last stage.</summary>
public sealed record QiRequiredRules(decimal Base, decimal StageGrowth, decimal RealmGrowth, decimal MajorMultiplier);

public sealed record BreakthroughRules(
    decimal MinorBaseChance,
    decimal MinorDecayPerStage,
    decimal InsightWeight,
    decimal WillpowerWeight,
    decimal FateWeight,
    decimal MinChance,
    decimal MaxChance,
    decimal FailureQiLossFraction,
    int DeviationMinutes,
    int BottleneckStage);

public sealed record OfflineCapBonus(int Realm, int Hours);

/// <summary>Continuous Qi flow: always on (×1), meditation (×5), closed-door retreat (×8). Stops when the vessel is full.</summary>
public sealed record FlowRules(
    decimal BaseQiPerHour,
    decimal RealmGrowth,
    decimal SpiritBonusPerPoint,
    decimal MeditationMultiplier,
    decimal RetreatMultiplier,
    IReadOnlyList<int> RetreatHours,
    int RetreatMinRealm,
    int LongRetreatMinRealm,
    int OfflineCapHours,
    IReadOnlyList<OfflineCapBonus> OfflineCapBonuses,
    decimal DeviationMultiplier,
    decimal VesselBase,
    decimal VesselPerLevel,
    int VesselMaxLevel,
    long VesselCrystalCost,
    int DailyAcceleratorHours,
    int TimeGodRealm,
    int TimeGodHours,
    int WeekendPermille = 1000)
{
    /// <summary>Weekend event: Saturday and Sunday (UTC) the flow runs faster.</summary>
    public static bool IsWeekend(DateTimeOffset at) => at.UtcDateTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public int OfflineCap(int realm) => OfflineCapBonuses.Where(b => realm >= b.Realm).Select(b => b.Hours).DefaultIfEmpty(OfflineCapHours).Max();
}

public sealed record CompressionRules(int Ratio, int MinRealm, int BonusPermillePer100Crystals, int CapPermillePerRealm);

public sealed record HeartDemonRules(
    int PerBreakthrough,
    int PerPillUse,
    int FastBreakthroughMinutes,
    int FastBreakthroughExtra,
    int CalmPerMeditationHour,
    int ChancePenaltyFrom,
    int ChancePenaltyPermillePerPoint,
    int ImmuneRealm);

public sealed record EnlightenmentRules(int ChancePpmPerHour, int FullMoonMultiplier, decimal QiFractionOfStage);

/// <summary>Quality of foundation / core / soul (realms 3, 4, 5): common, perfect or heavenly by preparation score 0-100.</summary>
public sealed record QualityRules(int PerfectScore, int HeavenlyScore, int PerfectBonusPermille, int HeavenlyBonusPermille, int PeakHoursForFullScore);

public sealed record KarmaRules(int MinRealm, int PerRealm, int FlowPermillePerKarma);

public sealed record AutoBreakthroughRules(int MinRealm, int MaxPerSettle);

/// <summary>A cultivation method (manual): one is active at a time; mastery grows with cultivated minutes.</summary>
public sealed record CultivationMethod(
    string Id,
    string NamePl,
    string DescriptionPl,
    int FlowPermille,
    int MeditationPermille,
    int CombatPermille,
    int StepsPermille,
    int DemonPermille,
    int NightPermille,
    int WinQi,
    string? Path,
    int MinRealm,
    string? Item,
    int GatherPermille = 0,
    int TribulationPermille = 0);

public sealed record PathRules(QiPathRules Qi, BodyPathRules Body, FlamePathRules Flame, PoisonPathRules Poison)
{
    /// <summary>Path factor applied to all Qi flow.</summary>
    public decimal MeditationMultiplier(string path) => path switch
    {
        "qi" => Qi.MeditationMultiplier,
        "body" => Body.MeditationMultiplier,
        "flame" => Flame.MeditationMultiplier,
        "poison" => Poison.MeditationMultiplier,
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, "Unknown cultivation path."),
    };
}

public sealed record QiPathRules(decimal MeditationMultiplier);

public sealed record BodyPathRules(decimal MeditationMultiplier, int StepsPerQi, int MaxStepsPerMinute, int DailyStepCap);

public sealed record FlamePathRules(decimal MeditationMultiplier);

public sealed record PoisonPathRules(decimal MeditationMultiplier, decimal NightMultiplier, int NightStartHour, int NightEndHour);

public static class QualityGrades
{
    public const string Common = "common";
    public const string Perfect = "perfect";
    public const string Heavenly = "heavenly";
}

public static class CultivationModes
{
    public const string Idle = "idle";
    public const string Meditation = "meditation";
    public const string Retreat = "retreat";
}

public static class CultivationConfigValidator
{
    public static IReadOnlyList<string> Validate(CultivationConfig? c)
    {
        if (c is null) return ["Config is missing."];
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(c.RulesVersion)) e.Add("rulesVersion is required.");
        try
        {
            _ = c.TimeZone;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            e.Add($"worldTimeZone '{c.WorldTimeZone}' is not a known time zone.");
        }

        if (c.StageNames is null || c.StageNames.Count != 12) e.Add("stageNames: exactly 12 names.");
        if (c.Eras is null || c.Eras.Count == 0) e.Add("eras are required.");
        if (c.Realms is null || c.Realms.Count == 0) e.Add("realms are required.");
        else
        {
            if (c.Realms.Select(r => r.Id).Distinct().Count() != c.Realms.Count) e.Add("realm ids must be unique.");
            if (c.Realms.Any(r => r.Stages != 12)) e.Add("every realm has 12 stages.");
            if (c.Eras is not null && c.Realms.Any(r => c.Eras.All(x => x.Era != r.Era))) e.Add("every realm belongs to a known era.");
            if (c.Realms.Any(r => string.IsNullOrWhiteSpace(r.NamePl) || string.IsNullOrWhiteSpace(r.TitlePl))) e.Add("realms need Polish names and titles.");
        }

        if (c.QiRequired is null || c.QiRequired.Base < 1 || c.QiRequired.StageGrowth < 1 || c.QiRequired.RealmGrowth < 1 || c.QiRequired.MajorMultiplier < 1)
            e.Add("qiRequired: base and growth factors must be >= 1.");
        if (c.Realms is not null && c.QiRequired is not null
            && c.QiRequired.Base * Pow(c.QiRequired.RealmGrowth, c.Realms.Count - 1) * Pow(c.QiRequired.StageGrowth, 11) * c.QiRequired.MajorMultiplier > 9e17m)
            e.Add("qiRequired: the last stage must stay below 9e17 Qi.");

        var b = c.Breakthrough;
        if (b is null) e.Add("breakthrough is required.");
        else
        {
            if (!(0 < b.MinChance && b.MinChance <= b.MaxChance && b.MaxChance < 1)) e.Add("breakthrough: need 0 < minChance <= maxChance < 1.");
            if (b.MinorBaseChance is <= 0 or > 1) e.Add("breakthrough base chance must be in (0, 1].");
            if (b.MinorDecayPerStage < 0 || b.InsightWeight < 0 || b.WillpowerWeight < 0 || b.FateWeight < 0) e.Add("breakthrough weights must be >= 0.");
            if (b.FailureQiLossFraction is < 0 or > 1) e.Add("breakthrough.failureQiLossFraction must be in [0, 1].");
            if (b.DeviationMinutes is < 0 or > 24 * 60) e.Add("breakthrough.deviationMinutes must be 0-1440.");
            if (b.BottleneckStage is < 1 or > 12) e.Add("breakthrough.bottleneckStage must be 1-12.");
        }

        var f = c.Flow;
        if (f is null) e.Add("flow is required.");
        else
        {
            if (f.BaseQiPerHour <= 0 || f.RealmGrowth < 1 || f.SpiritBonusPerPoint < 0) e.Add("flow rates must be positive.");
            if (f.MeditationMultiplier < 1 || f.RetreatMultiplier < f.MeditationMultiplier) e.Add("flow: 1 <= meditation <= retreat multiplier.");
            if (f.RetreatHours is null || f.RetreatHours.Count == 0 || f.RetreatHours.Any(h => h is < 1 or > 72)) e.Add("flow.retreatHours: 1-72.");
            if (f.OfflineCapHours is < 1 or > 168) e.Add("flow.offlineCapHours: 1-168.");
            if (f.DeviationMultiplier is < 0 or > 1) e.Add("flow.deviationMultiplier must be in [0, 1].");
            if (f.VesselBase < 1 || f.VesselPerLevel < 0 || f.VesselMaxLevel < 0 || f.VesselCrystalCost < 1) e.Add("flow vessel rules invalid (vessel must hold a breakthrough).");
        }

        if (c.Compression is null || c.Compression.Ratio < 2 || c.Compression.CapPermillePerRealm < 0) e.Add("compression invalid.");
        if (c.HeartDemons is null || c.HeartDemons.ChancePenaltyFrom is < 0 or > 100) e.Add("heartDemons invalid.");
        if (c.Enlightenment is null || c.Enlightenment.ChancePpmPerHour is < 0 or > 1_000_000) e.Add("enlightenment invalid.");
        if (c.Quality is null || !(0 < c.Quality.PerfectScore && c.Quality.PerfectScore < c.Quality.HeavenlyScore && c.Quality.HeavenlyScore <= 100)) e.Add("quality thresholds invalid.");
        if (c.Karma is null || c.Karma.MinRealm < 1) e.Add("karma invalid.");
        if (c.AutoBreakthrough is null || c.AutoBreakthrough.MaxPerSettle < 1) e.Add("autoBreakthrough invalid.");
        if (c.Methods is null || c.Methods.Count == 0) e.Add("methods are required.");
        else
        {
            if (c.Methods.Select(m => m.Id).Distinct().Count() != c.Methods.Count) e.Add("method ids must be unique.");
            if (c.Methods.Any(m => m.FlowPermille is < -900 or > 5000 || m.MeditationPermille is < -900 or > 5000 || m.CombatPermille is < -500 or > 1000))
                e.Add("method values out of range.");
        }

        if (c.MethodMasteryMinutesPerLevel < 1 || c.MethodMasteryPermillePerLevel < 0) e.Add("method mastery rules invalid.");

        var p = c.Paths;
        if (p?.Qi is null || p.Body is null || p.Flame is null || p.Poison is null) e.Add("paths qi, body, flame, poison are all required.");
        else
        {
            if (new[] { p.Qi.MeditationMultiplier, p.Body.MeditationMultiplier, p.Flame.MeditationMultiplier, p.Poison.MeditationMultiplier }.Any(x => x is <= 0 or > 5))
                e.Add("path multipliers must be in (0, 5].");
            if (p.Body.StepsPerQi < 1 || p.Body.MaxStepsPerMinute is < 1 or > 400 || p.Body.DailyStepCap < 1) e.Add("body path step rules invalid.");
            if (p.Poison.NightMultiplier < 1 || p.Poison.NightStartHour is < 0 or > 23 || p.Poison.NightEndHour is < 0 or > 23 || p.Poison.NightStartHour == p.Poison.NightEndHour)
                e.Add("poison path night rules invalid.");
        }

        return e;
    }

    private static decimal Pow(decimal x, int n)
    {
        var r = 1m;
        for (var i = 0; i < n; i++) r *= x;
        return r;
    }
}

/// <summary>The realm ladder and the methods on the Road of 66 Realms (1.9).</summary>
public sealed class CultivationRoad(CultivationConfig c) : JianghuGo.Modules.Characters.Contracts.IRoadRealms, JianghuGo.Modules.Characters.Contracts.IRoadContributor
{
    public IReadOnlyList<JianghuGo.Modules.Characters.Contracts.RoadRealm> Realms { get; } =
        [.. c.Realms.Select((r, i) => new JianghuGo.Modules.Characters.Contracts.RoadRealm(i, r.NamePl, r.Hanzi, r.Era, c.Eras.FirstOrDefault(e => e.Era == r.Era)?.NamePl ?? string.Empty,
            r.TribulationPl, r.UnlockPl, r.Id))];

    public IEnumerable<(int Realm, string TextPl)> Unlocks(string path) =>
        c.Methods.Where(m => m.MinRealm > 0 && (m.Path is null || m.Path == path)).Select(m => (m.MinRealm, "Metoda: " + m.NamePl));
}
