using JianghuGo.Domain;

namespace JianghuGo.Modules.Cultivation.Domain;

/// <summary>Pure, deterministic cultivation formulas. Every number comes from <see cref="CultivationConfig"/>.</summary>
public static class CultivationRules
{
    public const int ChanceScale = 1_000_000; // chances are stored and rolled in parts-per-million

    /// <summary>0-based position of (realm, stage) on the whole ladder.</summary>
    public static int GlobalIndex(CultivationConfig c, int realm, int stage) =>
        c.Realms.Take(realm).Sum(r => r.Stages) + (stage - 1);

    public static bool IsMajor(CultivationConfig c, int realm, int stage) => stage == c.Realms[realm].Stages;

    public static bool IsPeak(CultivationConfig c, int realm, int stage) => realm == c.Realms.Count - 1 && IsMajor(c, realm, stage);

    public static (int Realm, int Stage) Next(CultivationConfig c, int realm, int stage) =>
        IsMajor(c, realm, stage) ? (realm + 1, 1) : (realm, stage + 1);

    /// <summary>Qi needed to attempt the breakthrough out of (realm, stage). base × growth^index (× major multiplier).</summary>
    public static long QiRequired(CultivationConfig c, int realm, int stage)
    {
        var value = c.QiRequired.Base;
        for (var i = 0; i < GlobalIndex(c, realm, stage); i++)
        {
            value *= c.QiRequired.Growth;
        }

        if (IsMajor(c, realm, stage))
        {
            value *= c.QiRequired.MajorMultiplier;
        }

        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    public static int SuccessChancePpm(CultivationConfig c, int realm, int stage, IReadOnlyDictionary<string, int> stats)
    {
        var b = c.Breakthrough;
        var chance = IsMajor(c, realm, stage)
            ? b.MajorBaseChance
            : b.MinorBaseChance - (b.MinorDecayPerStage * GlobalIndex(c, realm, stage));
        chance += (stats.GetValueOrDefault("insight") * b.InsightWeight)
                  + (stats.GetValueOrDefault("willpower") * b.WillpowerWeight)
                  + (stats.GetValueOrDefault("fate") * b.FateWeight);
        chance = Math.Clamp(chance, b.MinChance, b.MaxChance);
        return (int)Math.Round(chance * ChanceScale, MidpointRounding.AwayFromZero);
    }

    /// <summary>Roll in [0, 1 000 000). Success iff roll &lt; chance. Reproducible from the stored seed.</summary>
    public static int Roll(ulong seed) => new DeterministicRandom(seed).NextInt(0, ChanceScale);

    public static long FailureQiLoss(CultivationConfig c, long required) =>
        (long)Math.Floor(required * c.Breakthrough.FailureQiLossFraction);

    public static bool IsPoisonNight(CultivationConfig c, DateTimeOffset at)
    {
        var hour = TimeZoneInfo.ConvertTime(at, c.TimeZone).Hour;
        var p = c.Paths.Poison;
        return p.NightStartHour > p.NightEndHour
            ? hour >= p.NightStartHour || hour < p.NightEndHour   // window wraps midnight (21-05)
            : hour >= p.NightStartHour && hour < p.NightEndHour;
    }

    public static DateOnly WorldDate(CultivationConfig c, DateTimeOffset at) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, c.TimeZone).DateTime);

    /// <summary>
    /// Qi from a meditation session, evaluated minute by minute (so night and Qi-deviation boundaries inside a
    /// session are honoured). Only whole minutes count; sessions are capped at maxMinutes.
    /// </summary>
    public static (long Qi, int Minutes) MeditationQi(CultivationConfig c, string path, int spirit, DateTimeOffset start, DateTimeOffset end, DateTimeOffset? deviationUntil)
    {
        var m = c.Meditation;
        var minutes = (int)Math.Min(m.MaxMinutes, Math.Floor((end - start).TotalMinutes));
        if (minutes < m.MinMinutes)
        {
            return (0, Math.Max(0, minutes));
        }

        decimal total = 0;
        for (var i = 0; i < minutes; i++)
        {
            total += MeditationRate(c, path, spirit, start.AddMinutes(i), deviationUntil);
        }

        return ((long)Math.Floor(total), minutes);
    }

    /// <summary>Qi per minute of meditation at a given moment (path, spirit, poison night, Qi deviation).</summary>
    public static decimal MeditationRate(CultivationConfig c, string path, int spirit, DateTimeOffset at, DateTimeOffset? deviationUntil)
    {
        var m = c.Meditation;
        var rate = m.BaseQiPerMinute * (1 + (spirit * m.SpiritBonusPerPoint)) * c.Paths.MeditationMultiplier(path);
        if (path == "poison" && IsPoisonNight(c, at)) rate *= c.Paths.Poison.NightMultiplier;
        if (deviationUntil is { } d && at < d) rate *= m.DeviationMultiplier;
        return rate;
    }

    /// <summary>
    /// Body path: accepts at most maxStepsPerMinute × elapsed minutes since the last report (a pedometer cannot
    /// report more than a human can walk) and the daily cap. Returns accepted steps and whether input was clipped.
    /// </summary>
    public static (int Accepted, bool Clipped) PlausibleSteps(CultivationConfig c, int reported, TimeSpan sinceLastReport, int stepsAlreadyToday)
    {
        var body = c.Paths.Body;
        var byTime = (long)Math.Floor(Math.Max(0, sinceLastReport.TotalMinutes)) * body.MaxStepsPerMinute;
        var byDay = Math.Max(0, body.DailyStepCap - stepsAlreadyToday);
        var accepted = (int)Math.Min(reported, Math.Min(byTime, byDay));
        return (accepted, accepted < reported);
    }

    /// <summary>Qi from steps without losing remainders across reports in the same day.</summary>
    public static long StepsQi(CultivationConfig c, int stepsBefore, int stepsAfter) =>
        (stepsAfter / c.Paths.Body.StepsPerQi) - (stepsBefore / c.Paths.Body.StepsPerQi);
}
