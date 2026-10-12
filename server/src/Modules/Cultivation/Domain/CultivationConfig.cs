namespace JianghuGo.Modules.Cultivation.Domain;

// Data model of content/game-config/cultivation.json. Fractions are decimal so every computation is
// exact and identical on every platform (no floating-point drift between server builds).

public sealed record CultivationConfig(
    string RulesVersion,
    string WorldTimeZone,
    IReadOnlyList<RealmDefinition> Realms,
    QiRequiredRules QiRequired,
    BreakthroughRules Breakthrough,
    MeditationRules Meditation,
    PathRules Paths)
{
    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(WorldTimeZone);
}

public sealed record RealmDefinition(string Id, string NamePl, string NameEn, int Stages);

public sealed record QiRequiredRules(decimal Base, decimal Growth, decimal MajorMultiplier);

public sealed record BreakthroughRules(
    decimal MinorBaseChance,
    decimal MinorDecayPerStage,
    decimal MajorBaseChance,
    decimal InsightWeight,
    decimal WillpowerWeight,
    decimal FateWeight,
    decimal MinChance,
    decimal MaxChance,
    decimal FailureQiLossFraction,
    int DeviationMinutes);

public sealed record MeditationRules(
    decimal BaseQiPerMinute,
    decimal SpiritBonusPerPoint,
    int MinMinutes,
    int MaxMinutes,
    long DailyQiCap,
    decimal DeviationMultiplier);

public sealed record PathRules(QiPathRules Qi, BodyPathRules Body, FlamePathRules Flame, PoisonPathRules Poison)
{
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

/// <summary>The 12 fixed Flame sites need geolocation (Stage 5); until then Flame cultivates by meditation only.</summary>
public sealed record FlamePathRules(decimal MeditationMultiplier);

public sealed record PoisonPathRules(decimal MeditationMultiplier, decimal NightMultiplier, int NightStartHour, int NightEndHour);

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

        if (c.Realms is null || c.Realms.Count == 0) e.Add("realms are required.");
        else
        {
            if (c.Realms.Select(r => r.Id).Distinct().Count() != c.Realms.Count) e.Add("realm ids must be unique.");
            if (c.Realms.Any(r => r.Stages is < 1 or > 20)) e.Add("realm stages must be 1-20.");
        }

        if (c.QiRequired is null || c.QiRequired.Base < 1 || c.QiRequired.Growth < 1 || c.QiRequired.MajorMultiplier < 1)
            e.Add("qiRequired: base, growth and majorMultiplier must be >= 1.");

        var b = c.Breakthrough;
        if (b is null) e.Add("breakthrough is required.");
        else
        {
            if (!(0 < b.MinChance && b.MinChance <= b.MaxChance && b.MaxChance < 1)) e.Add("breakthrough: need 0 < minChance <= maxChance < 1 (a guaranteed breakthrough removes all risk).");
            if (b.MinorBaseChance is <= 0 or > 1 || b.MajorBaseChance is <= 0 or > 1) e.Add("breakthrough base chances must be in (0, 1].");
            if (b.MinorDecayPerStage < 0 || b.InsightWeight < 0 || b.WillpowerWeight < 0 || b.FateWeight < 0) e.Add("breakthrough weights must be >= 0.");
            if (b.FailureQiLossFraction is < 0 or > 1) e.Add("breakthrough.failureQiLossFraction must be in [0, 1].");
            if (b.DeviationMinutes is < 0 or > 24 * 60) e.Add("breakthrough.deviationMinutes must be 0-1440.");
        }

        var m = c.Meditation;
        if (m is null) e.Add("meditation is required.");
        else
        {
            if (m.BaseQiPerMinute <= 0 || m.SpiritBonusPerPoint < 0) e.Add("meditation rates must be positive.");
            if (m.MinMinutes < 1 || m.MaxMinutes < m.MinMinutes || m.MaxMinutes > 24 * 60) e.Add("meditation minutes invalid.");
            if (m.DailyQiCap < 1) e.Add("meditation.dailyQiCap must be >= 1.");
            if (m.DeviationMultiplier is < 0 or > 1) e.Add("meditation.deviationMultiplier must be in [0, 1].");
        }

        var p = c.Paths;
        if (p?.Qi is null || p.Body is null || p.Flame is null || p.Poison is null) e.Add("paths qi, body, flame, poison are all required.");
        else
        {
            if (new[] { p.Qi.MeditationMultiplier, p.Body.MeditationMultiplier, p.Flame.MeditationMultiplier, p.Poison.MeditationMultiplier }.Any(x => x is <= 0 or > 5))
                e.Add("path meditation multipliers must be in (0, 5].");
            if (p.Body.StepsPerQi < 1 || p.Body.MaxStepsPerMinute is < 1 or > 400 || p.Body.DailyStepCap < 1) e.Add("body path step rules invalid.");
            if (p.Poison.NightMultiplier < 1 || p.Poison.NightStartHour is < 0 or > 23 || p.Poison.NightEndHour is < 0 or > 23 || p.Poison.NightStartHour == p.Poison.NightEndHour)
                e.Add("poison path night rules invalid.");
        }

        return e;
    }
}
