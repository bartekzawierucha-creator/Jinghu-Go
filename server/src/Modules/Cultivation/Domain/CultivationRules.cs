using JianghuGo.Domain;

namespace JianghuGo.Modules.Cultivation.Domain;

/// <summary>Pure, deterministic cultivation formulas (rules cultivation-3). Every number comes from <see cref="CultivationConfig"/>.</summary>
public static class CultivationRules
{
    /// <summary>Each realm's tribulation is a little fiercer than the last (on top of being raised to the realm by Combat's scaling).</summary>
    public const int TribulationPowerPerRealmPermille = 3;

    public const int ChanceScale = 1_000_000; // chances are stored and rolled in parts-per-million
    public const int StagesPerRealm = 12;

    /// <summary>0-based position of (realm, stage) on the whole ladder (66 × 12 = 792 positions).</summary>
    public static int GlobalIndex(CultivationConfig c, int realm, int stage) => (realm * StagesPerRealm) + (stage - 1);

    public static bool IsMajor(CultivationConfig c, int realm, int stage) => stage == c.Realms[realm].Stages;

    public static bool IsPeak(CultivationConfig c, int realm, int stage) => realm == c.Realms.Count - 1 && IsMajor(c, realm, stage);

    /// <summary>The bottleneck stage (Dog) needs the era's breakthrough pill to be left.</summary>
    public static bool IsBottleneck(CultivationConfig c, int stage) => stage == c.Breakthrough.BottleneckStage;

    public static (int Realm, int Stage) Next(CultivationConfig c, int realm, int stage) =>
        IsMajor(c, realm, stage) ? (realm + 1, 1) : (realm, stage + 1);

    public static string StageName(CultivationConfig c, int stage) => c.StageNames[Math.Clamp(stage, 1, 12) - 1];

    private static decimal Pow(decimal x, int n)
    {
        var r = 1m;
        for (var i = 0; i < n; i++) r *= x;
        return r;
    }

    /// <summary>Qi needed to leave (realm, stage).</summary>
    public static long QiRequired(CultivationConfig c, int realm, int stage)
    {
        var q = c.QiRequired;
        var value = q.Base * Pow(q.RealmGrowth, realm) * Pow(q.StageGrowth, stage - 1);
        if (IsMajor(c, realm, stage)) value *= q.MajorMultiplier;
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    /// <summary>Chance of a minor (in-realm) breakthrough; heart demons lower it, a fine foundation raises it.</summary>
    public static int SuccessChancePpm(CultivationConfig c, int realm, int stage, IReadOnlyDictionary<string, int> stats, CultivationProfile? profile = null)
    {
        var b = c.Breakthrough;
        var chance = b.MinorBaseChance - (b.MinorDecayPerStage * (stage - 1) * 10) - (0.003m * realm);
        chance += (stats.GetValueOrDefault("insight") * b.InsightWeight)
                  + (stats.GetValueOrDefault("willpower") * b.WillpowerWeight)
                  + (stats.GetValueOrDefault("fate") * b.FateWeight);
        if (profile is not null)
        {
            var demons = DemonPenaltyPermille(c, realm, profile);
            chance -= demons / 1000m;
            chance += QualityPermille(c, profile) / 2000m;
        }

        chance = Math.Clamp(chance, b.MinChance, b.MaxChance);
        return (int)Math.Round(chance * ChanceScale, MidpointRounding.AwayFromZero);
    }

    public static int DemonPenaltyPermille(CultivationConfig c, int realm, CultivationProfile p)
    {
        var h = c.HeartDemons;
        if (realm >= h.ImmuneRealm) return 0;
        return Math.Max(0, p.Instability - h.ChancePenaltyFrom) * h.ChancePenaltyPermillePerPoint;
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
            ? hour >= p.NightStartHour || hour < p.NightEndHour
            : hour >= p.NightStartHour && hour < p.NightEndHour;
    }

    public static DateOnly WorldDate(CultivationConfig c, DateTimeOffset at) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, c.TimeZone).DateTime);

    /// <summary>Full moon (±1 day) by the mean synodic month from a known new moon.</summary>
    public static bool IsFullMoon(DateTimeOffset at)
    {
        var knownNew = new DateTimeOffset(2000, 1, 6, 18, 14, 0, TimeSpan.Zero);
        const double synodic = 29.530588853;
        var age = (at - knownNew).TotalDays % synodic;
        if (age < 0) age += synodic;
        return Math.Abs(age - (synodic / 2)) <= 1.0;
    }

    // ------------------------------------------------------------------ flow

    /// <summary>Vessel capacity: the flow stops once Qi reaches it (rewards may still overfill).</summary>
    public static long VesselCapacity(CultivationConfig c, int realm, int stage, int vesselLevel) =>
        (long)Math.Round(QiRequired(c, realm, stage) * (c.Flow.VesselBase + (c.Flow.VesselPerLevel * vesselLevel)), MidpointRounding.AwayFromZero);

    public static int MasteryLevel(CultivationConfig c, CultivationProfile p) =>
        (int)Math.Min(10, p.MinutesWith(p.MethodId) / c.MethodMasteryMinutesPerLevel);

    public static int QualityPermille(CultivationConfig c, CultivationProfile p) =>
        p.Qualities.Values.Sum(q => q == QualityGrades.Heavenly ? c.Quality.HeavenlyBonusPermille : q == QualityGrades.Perfect ? c.Quality.PerfectBonusPermille : 0);

    public static int CrystalPermille(CultivationConfig c, CultivationProfile p, int realm) =>
        (int)Math.Min((long)(realm + 1) * c.Compression.CapPermillePerRealm, p.Crystals / 100 * c.Compression.BonusPermillePer100Crystals);

    /// <summary>Qi per hour at a moment in a given mode: realm, spirit, path, method and its mastery, qualities, karma, crystals, poison night, deviation.</summary>
    public static decimal FlowPerHour(CultivationConfig c, string path, int realm, int spirit, CultivationProfile p, string mode, DateTimeOffset at, DateTimeOffset? deviationUntil)
    {
        var f = c.Flow;
        var rate = f.BaseQiPerHour * Pow(f.RealmGrowth, realm) * (1 + (spirit * f.SpiritBonusPerPoint)) * c.Paths.MeditationMultiplier(path);
        var method = c.Method(p.MethodId);
        var permille = 1000 + (method?.FlowPermille ?? 0) + (MasteryLevel(c, p) * c.MethodMasteryPermillePerLevel) + QualityPermille(c, p)
                       + (p.Karma * c.Karma.FlowPermillePerKarma) + CrystalPermille(c, p, realm);
        if (mode != CultivationModes.Idle) permille += method?.MeditationPermille ?? 0;
        rate *= Math.Max(100, permille) / 1000m;
        rate *= mode switch
        {
            CultivationModes.Meditation => f.MeditationMultiplier,
            CultivationModes.Retreat => f.RetreatMultiplier,
            _ => 1m,
        };
        var night = path == "poison" && IsPoisonNight(c, at);
        if (night) rate *= c.Paths.Poison.NightMultiplier;
        if (method is { NightPermille: > 0 } && IsPoisonNight(c, at)) rate *= 1 + (method.NightPermille / 1000m);
        if (deviationUntil is { } d && at < d) rate *= f.DeviationMultiplier;
        if (FlowRules.IsWeekend(at)) rate *= f.WeekendPermille / 1000m;
        return rate;
    }

    /// <summary>Qi per minute while meditating right now (for the client's live gauge).</summary>
    public static decimal MeditationRate(CultivationConfig c, string path, int realm, int spirit, CultivationProfile p, DateTimeOffset at, DateTimeOffset? deviationUntil) =>
        FlowPerHour(c, path, realm, spirit, p, CultivationModes.Meditation, at, deviationUntil) / 60m;

    public sealed record FlowResult(long Qi, long MeditationMinutes, long NightMinutes, int Enlightenments, long EnlightenmentQi, bool VesselFull, bool RetreatFinished,
        string ModeAfter, DateTimeOffset SettledAt);

    /// <summary>
    /// Settles the Qi flow from the last settlement up to <paramref name="now"/>, in segments split at whole hours, the
    /// end of a retreat and the end of a deviation (rates are constant inside a segment). At most the offline cap is
    /// credited; the flow stops when the vessel is full. Each whole meditated/retreat hour may bring enlightenment —
    /// rolled from the character id and the hour, so a settlement is reproducible and cannot be re-rolled.
    /// </summary>
    public static FlowResult SettleFlow(CultivationConfig c, CharacterId id, string path, int realm, int stage, int spirit, long qi, CultivationProfile p,
        DateTimeOffset now, DateTimeOffset? deviationUntil)
    {
        var start = p.FlowSettledAt;
        if (now <= start) return new FlowResult(0, 0, 0, 0, 0, false, false, p.Mode, start);
        var capHours = c.Flow.OfflineCap(realm);
        var from = now - start > TimeSpan.FromHours(capHours) ? now - TimeSpan.FromHours(capHours) : start;
        var capacity = VesselCapacity(c, realm, stage, p.VesselLevel);
        var mode = p.Mode;
        decimal gained = 0;
        long medMinutes = 0, nightMinutes = 0;
        int enlight = 0;
        long enlightQi = 0;
        var retreatFinished = false;
        var t = from;
        while (t < now)
        {
            var next = new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Offset).AddHours(1);
            if (next > now) next = now;
            if (mode == CultivationModes.Retreat && p.RetreatUntil is { } ru && ru > t && ru < next) next = ru;
            if (deviationUntil is { } du && du > t && du < next) next = du;
            var minutes = (decimal)(next - t).TotalMinutes;
            var rate = FlowPerHour(c, path, realm, spirit, p, mode, t, deviationUntil);
            gained += rate * minutes / 60m;
            if (mode != CultivationModes.Idle)
            {
                medMinutes += (long)minutes;
                if (IsPoisonNight(c, t)) nightMinutes += (long)minutes;
                // enlightenment: one roll per whole hour of meditation or retreat
                if (next.Minute == 0 && next.Second == 0 && (next - t).TotalMinutes >= 59.99)
                {
                    var chance = c.Enlightenment.ChancePpmPerHour * (IsFullMoon(t) ? c.Enlightenment.FullMoonMultiplier : 1);
                    if (new DeterministicRandom(HourSeed(id, next)).NextInt(0, ChanceScale) < chance)
                    {
                        enlight++;
                        enlightQi += (long)(QiRequired(c, realm, stage) * c.Enlightenment.QiFractionOfStage);
                    }
                }
            }

            t = next;
            if (mode == CultivationModes.Retreat && p.RetreatUntil is { } end && t >= end)
            {
                mode = CultivationModes.Idle;
                retreatFinished = true;
            }

            if (qi + (long)gained >= capacity) break;
        }

        // a full vessel stops the flow, not the clock: a retreat that ran out meanwhile still ends
        if (mode == CultivationModes.Retreat && p.RetreatUntil is { } over && over <= now)
        {
            mode = CultivationModes.Idle;
            retreatFinished = true;
        }

        var room = Math.Max(0, capacity - qi);
        var credited = Math.Min(room, (long)Math.Floor(gained));
        return new FlowResult(credited, medMinutes, nightMinutes, enlight, enlightQi, qi + (long)gained >= capacity, retreatFinished, mode, now);
    }

    private static ulong HourSeed(CharacterId id, DateTimeOffset hour)
    {
        var bytes = id.Value.ToByteArray();
        var a = BitConverter.ToUInt64(bytes, 0);
        var b = BitConverter.ToUInt64(bytes, 8);
        return a ^ (b * 0x9E3779B97F4A7C15UL) ^ (ulong)(hour.ToUnixTimeSeconds() / 3600);
    }

    // ------------------------------------------------------------------ qualities

    /// <summary>
    /// Preparation score 0-100 for a foundation/core/soul: opened meridians, time held at Perfection, a calm heart,
    /// method mastery, and an auspicious moment (full moon or dawn).
    /// </summary>
    public static int QualityScore(CultivationConfig c, CultivationProfile p, int meridiansOpen, int meridiansEarned, DateTimeOffset now)
    {
        var mer = meridiansEarned <= 0 ? 1.0 : Math.Min(1.0, meridiansOpen / (double)meridiansEarned);
        var held = p.PeakSince is { } since ? Math.Min(1.0, (now - since).TotalHours / c.Quality.PeakHoursForFullScore) : 0;
        var calm = 1.0 - (p.Instability / 100.0);
        var mastery = MasteryLevel(c, p) / 10.0;
        var local = TimeZoneInfo.ConvertTime(now, c.TimeZone);
        var auspicious = IsFullMoon(now) || local.Hour is >= 5 and < 7 ? 1.0 : 0.0;
        return (int)Math.Round((30 * mer) + (25 * held) + (20 * calm) + (15 * mastery) + (10 * auspicious));
    }

    public static string Grade(CultivationConfig c, int score) =>
        score >= c.Quality.HeavenlyScore ? QualityGrades.Heavenly : score >= c.Quality.PerfectScore ? QualityGrades.Perfect : QualityGrades.Common;

    /// <summary>Colour of the golden core from how the character lived: battle, alchemy/gathering, meditation, night, or balance.</summary>
    public static string CoreColor(ProfileCounters k)
    {
        var scores = new (string Color, double Value)[]
        {
            ("crimson", k.BattlesWon * 3.0),
            ("jade", (k.Crafts * 4.0) + (k.Gathered * 0.5)),
            ("silver", k.MeditationMinutes / 10.0),
            ("violet", k.NightMinutes / 6.0),
        };
        var total = scores.Sum(s => s.Value);
        if (total <= 0) return "golden";
        var top = scores.OrderByDescending(s => s.Value).First();
        return top.Value / total < 0.4 ? "golden" : top.Color;
    }

    /// <summary>Combat stat bonus of the core colour and qualities (added to every stat or one stat).</summary>
    public static IReadOnlyDictionary<string, int> CoreBonuses(CultivationProfile p)
    {
        var d = new Dictionary<string, int>();
        void Add(string s, int v) => d[s] = d.GetValueOrDefault(s) + v;
        switch (p.CoreColor)
        {
            case "crimson": Add("strength", 6); Add("agility", 3); break;
            case "jade": Add("constitution", 5); Add("insight", 4); break;
            case "silver": Add("spirit", 6); Add("focus", 3); break;
            case "violet": Add("agility", 4); Add("elementalAffinity", 5); break;
            case "golden": foreach (var s in new[] { "strength", "spirit", "agility", "constitution" }) Add(s, 2); break;
        }

        foreach (var q in p.Qualities.Values)
        {
            var v = q == QualityGrades.Heavenly ? 4 : q == QualityGrades.Perfect ? 2 : 0;
            if (v == 0) continue;
            foreach (var s in new[] { "strength", "spirit", "agility", "constitution", "willpower" }) Add(s, v);
        }

        return d;
    }

    public static string CoreColorPl(string? color) => color switch
    {
        "crimson" => "Szkarłatny",
        "jade" => "Jadeitowy",
        "silver" => "Srebrny",
        "violet" => "Fioletowy",
        "golden" => "Złoty",
        _ => "—",
    };

    // ------------------------------------------------------------------ body path steps

    /// <summary>Body path: accepts at most maxStepsPerMinute × elapsed minutes since the last report and the daily cap.</summary>
    public static (int Accepted, bool Clipped) PlausibleSteps(CultivationConfig c, int reported, TimeSpan sinceLastReport, int stepsAlreadyToday)
    {
        var body = c.Paths.Body;
        var byTime = (long)Math.Floor(Math.Max(0, sinceLastReport.TotalMinutes)) * body.MaxStepsPerMinute;
        var byDay = Math.Max(0, body.DailyStepCap - stepsAlreadyToday);
        var accepted = (int)Math.Min(reported, Math.Min(byTime, byDay));
        return (accepted, accepted < reported);
    }

    /// <summary>Qi from steps without losing remainders across reports in the same day; grows with the realm.</summary>
    public static long StepsQi(CultivationConfig c, int realm, int stepsBefore, int stepsAfter, int stepsPermille = 0)
    {
        var raw = (stepsAfter / c.Paths.Body.StepsPerQi) - (stepsBefore / c.Paths.Body.StepsPerQi);
        return (long)(raw * Pow(c.Flow.RealmGrowth, realm) * (1000 + stepsPermille) / 1000m);
    }
}
