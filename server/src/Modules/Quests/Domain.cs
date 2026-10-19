using JianghuGo.Domain;

namespace JianghuGo.Modules.Quests.Domain;

/// <param name="Bonus">Permanent stat bonus of the title (counts in every battle once the achievement is claimed).</param>
/// <param name="Secret">Hidden achievement: until completed the board shows only <paramref name="RiddlePl"/>.</param>
/// <param name="Rarity">Colour of the title plate: common / rare / epic / legendary / mythic (derived from the reward when absent).</param>
public sealed record QuestDefinition(string Id, string NamePl, string DescriptionPl, string Metric, long Target, long Stones, string? TitlePl = null,
    IReadOnlyDictionary<string, int>? Bonus = null, bool Secret = false, string? RiddlePl = null, string? Rarity = null)
{
    public string TitleRarity => Rarity ?? (Secret ? "legendary" : Stones >= 1000 ? "legendary" : Stones >= 400 ? "epic" : Stones >= 150 ? "rare" : "common");
}

/// <summary>A yearly event achievement: counted only between <paramref name="From"/> and <paramref name="To"/> (MM-dd, world time); the title stays forever.</summary>
public sealed record SeasonalQuest(string Id, string NamePl, string DescriptionPl, string Metric, long Target, long Stones, string TitlePl, string Rarity, string From, string To);

/// <summary>One chapter of the story: chapters open one after another; each goal is a lifetime counter.</summary>
public sealed record StoryChapter(string Id, int Number, string TitlePl, string TextPl, string GoalPl, string Metric, long Target, long Stones, string? Item = null, int ItemQuantity = 0);

/// <summary>Title for having collected a weekly reward in an arena league.</summary>
public sealed record LeagueTitle(string League, string TitlePl, string Rarity);

public sealed record CheckinConfig(long BaseStones, long PerDayStones, int MaxBonusDays);

public sealed record QuestsConfig(
    string RulesVersion,
    string WorldTimeZone,
    int DailyCount,
    IReadOnlyList<QuestDefinition> Daily,
    IReadOnlyList<QuestDefinition> Achievements,
    CheckinConfig Checkin,
    IReadOnlyList<SeasonalQuest>? Seasonal = null,
    IReadOnlyList<StoryChapter>? Story = null,
    IReadOnlyList<LeagueTitle>? ArenaTitles = null)
{
    public IReadOnlyList<SeasonalQuest> SeasonalList => Seasonal ?? [];

    public IReadOnlyList<StoryChapter> StoryList => Story ?? [];

    public IReadOnlyList<LeagueTitle> ArenaTitleList => ArenaTitles ?? [];

    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(WorldTimeZone);

    public QuestDefinition? Find(string id) => Daily.FirstOrDefault(q => q.Id == id) ?? Achievements.FirstOrDefault(q => q.Id == id);
}

/// <summary>What the game counts. Every metric is fed by a published integration event.</summary>
public static class Metrics
{
    public const string BattlesWon = "battles_won";
    public const string PvpWon = "pvp_won";
    public const string BossesWon = "bosses_won";
    public const string MeditationMinutes = "meditation_minutes";
    public const string Crafts = "crafts";
    public const string Upgrades = "upgrades";
    public const string ItemsGathered = "items_gathered";
    public const string WorldQi = "world_qi";
    public const string Breakthroughs = "breakthroughs";
    public const string PillsUsed = "pills_used";
    public const string MarketSales = "market_sales";
    public const string Checkins = "checkins";
    public const string ArenaWon = "arena_won";
    public const string TowerWon = "tower_won";
    public const string Tribulations = "tribulations";
    public const string SpiritsWon = "spirits_won";
    public const string BattlesLost = "battles_lost";
    public const string NightWins = "night_wins";
    public const string FullMoonWins = "full_moon_wins";
    public const string Steps = "steps";
    public const string WorldBossFights = "world_boss_fights";
    public const string Expeditions = "expeditions";
    public const string Harvests = "harvests";
    // max-type: the best value reached (not summed)
    public const string Realm = "realm";
    public const string FlamesOwned = "flames_owned";
    public const string ToxinLevel = "toxin_level";
    public const string Temper = "temper";
    public const string Swords = "swords";
    public const string CheckinStreak = "checkin_streak";

    public static readonly string[] Max = [Realm, FlamesOwned, ToxinLevel, Temper, Swords, CheckinStreak];

    public static readonly string[] All = [BattlesWon, PvpWon, BossesWon, MeditationMinutes, Crafts, Upgrades, ItemsGathered, WorldQi, Breakthroughs, PillsUsed, MarketSales, Checkins,
        ArenaWon, TowerWon, Tribulations, SpiritsWon, BattlesLost, NightWins, FullMoonWins, Steps, WorldBossFights, Expeditions, Harvests, .. Max];
}

public sealed record CheckinState(CharacterId CharacterId, DateOnly LastDay, int Streak, int BestStreak, DateTimeOffset UpdatedAt);

public static class QuestRules
{
    public const string Life = "life";

    public static DateOnly WorldDate(QuestsConfig c, DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, c.TimeZone).DateTime);

    public static string Period(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The character's daily quests for a day: a deterministic pick (same for every call that day).</summary>
    public static IReadOnlyList<QuestDefinition> DailyFor(QuestsConfig c, Guid characterId, DateOnly day)
    {
        var seed = Fnv(characterId.ToByteArray()) ^ ((ulong)day.DayNumber * 0x9E3779B97F4A7C15UL);
        var rnd = new DeterministicRandom(seed);
        var pool = c.Daily.ToList();
        var picked = new List<QuestDefinition>();
        while (picked.Count < c.DailyCount && pool.Count > 0)
        {
            var i = rnd.NextInt(0, pool.Count);
            // never two quests on the same metric in one day
            if (picked.All(p => p.Metric != pool[i].Metric)) picked.Add(pool[i]);
            pool.RemoveAt(i);
        }

        return picked;
    }

    /// <summary>Streak after checking in on <paramref name="today"/> (consecutive world days; a gap restarts at 1).</summary>
    public static (int Streak, int Best) NextStreak(CheckinState? s, DateOnly today)
    {
        if (s is null) return (1, 1);
        var streak = s.LastDay == today.AddDays(-1) ? s.Streak + 1 : s.LastDay == today ? s.Streak : 1;
        return (streak, Math.Max(streak, s.BestStreak));
    }

    /// <summary>World-time night: midnight to 4 a.m.</summary>
    public static bool IsNight(QuestsConfig c, DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, c.TimeZone).Hour < 4;

    public static bool IsFullMoon(DateTimeOffset at)
    {
        var knownNew = new DateTimeOffset(2000, 1, 6, 18, 14, 0, TimeSpan.Zero);
        const double synodic = 29.530588853;
        var age = (at - knownNew).TotalDays % synodic;
        if (age < 0) age += synodic;
        return Math.Abs(age - (synodic / 2)) <= 1.0;
    }

    /// <summary>Whether a seasonal quest is running on <paramref name="day"/> (ranges may wrap the new year).</summary>
    public static bool SeasonActive(SeasonalQuest q, DateOnly day)
    {
        var md = day.ToString("MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return string.CompareOrdinal(q.From, q.To) <= 0
            ? string.CompareOrdinal(md, q.From) >= 0 && string.CompareOrdinal(md, q.To) <= 0
            : string.CompareOrdinal(md, q.From) >= 0 || string.CompareOrdinal(md, q.To) <= 0;
    }

    public static string SeasonPeriod(DateOnly day) => "S" + day.Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);

    public static long CheckinStones(QuestsConfig c, int streak) => c.Checkin.BaseStones + (c.Checkin.PerDayStones * (Math.Min(streak, c.Checkin.MaxBonusDays) - 1));

    private static ulong Fnv(byte[] bytes)
    {
        var h = 14695981039346656037UL;
        foreach (var b in bytes) h = (h ^ b) * 1099511628211UL;
        return h;
    }
}

public static class QuestsConfigValidator
{
    public static readonly string[] Rarities = ["common", "rare", "epic", "legendary", "mythic"];
    public static readonly string[] Stats = ["strength", "spirit", "agility", "constitution", "insight", "willpower", "focus", "elementalAffinity", "fate"];

    public static List<string> Validate(QuestsConfig? c)
    {
        var e = new List<string>();
        if (c is null) return ["config is empty."];
        try
        {
            _ = c.TimeZone;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            e.Add($"worldTimeZone '{c.WorldTimeZone}' is not a known time zone.");
        }

        if (c.DailyCount < 1 || c.DailyCount > 6) e.Add("dailyCount must be 1-6.");
        if (c.Daily is null || c.Achievements is null || c.Checkin is null) return [.. e, "daily, achievements and checkin are required."];
        if (c.Daily.Select(q => q.Metric).Distinct().Count() < c.DailyCount) e.Add("daily pool needs at least dailyCount distinct metrics.");
        var ids = new HashSet<string>();
        foreach (var q in c.Daily.Concat(c.Achievements))
        {
            if (string.IsNullOrWhiteSpace(q.Id) || !System.Text.RegularExpressions.Regex.IsMatch(q.Id, "^[a-z0-9_]{1,60}$")) e.Add($"quest id '{q.Id}' invalid.");
            if (!ids.Add(q.Id)) e.Add($"quest id '{q.Id}' duplicated.");
            if (!Metrics.All.Contains(q.Metric)) e.Add($"quest {q.Id}: unknown metric '{q.Metric}'.");
            if (q.Target < 1 || q.Stones < 0 || q.Stones > 100_000) e.Add($"quest {q.Id}: target must be >= 1 and stones 0-100000.");
            if (string.IsNullOrWhiteSpace(q.NamePl) || string.IsNullOrWhiteSpace(q.DescriptionPl)) e.Add($"quest {q.Id}: name and description are required.");
            if (q.Bonus is { } b && (q.TitlePl is null || b.Count == 0 || b.Any(kv => !Stats.Contains(kv.Key) || kv.Value is < 1 or > 20)))
                e.Add($"quest {q.Id}: a bonus needs a title and 1-20 points of known stats.");
        }

        foreach (var q in c.Achievements.Where(q => q.Secret))
            if (string.IsNullOrWhiteSpace(q.RiddlePl)) e.Add($"secret quest {q.Id}: a riddle is required.");
        foreach (var q in c.Daily.Concat(c.Achievements))
            if (q.Rarity is not null && !Rarities.Contains(q.Rarity)) e.Add($"quest {q.Id}: unknown rarity '{q.Rarity}'.");
        foreach (var s in c.SeasonalList)
        {
            if (!ids.Add(s.Id) || !System.Text.RegularExpressions.Regex.IsMatch(s.Id, "^[a-z0-9_]{1,60}$")) e.Add($"seasonal '{s.Id}' invalid or duplicated.");
            if (!Metrics.All.Contains(s.Metric) || Metrics.Max.Contains(s.Metric) || s.Target < 1 || s.Stones < 0) e.Add($"seasonal {s.Id}: metric/target invalid.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(s.From ?? "", "^[01][0-9]-[0-3][0-9]$") || !System.Text.RegularExpressions.Regex.IsMatch(s.To ?? "", "^[01][0-9]-[0-3][0-9]$"))
                e.Add($"seasonal {s.Id}: from/to must be MM-dd.");
            if (string.IsNullOrWhiteSpace(s.TitlePl) || !Rarities.Contains(s.Rarity)) e.Add($"seasonal {s.Id}: title and rarity required.");
        }

        var number = 0;
        foreach (var ch in c.StoryList)
        {
            if (ch.Number != ++number) e.Add($"story chapter {ch.Id}: numbers must be 1, 2, 3...");
            if (!ids.Add(ch.Id) || !System.Text.RegularExpressions.Regex.IsMatch(ch.Id, "^[a-z0-9_]{1,60}$")) e.Add($"story '{ch.Id}' invalid or duplicated.");
            if (!Metrics.All.Contains(ch.Metric) || ch.Target < 1 || ch.Stones < 0 || (ch.Item is null) != (ch.ItemQuantity == 0) || ch.ItemQuantity < 0) e.Add($"story {ch.Id}: goal/reward invalid.");
            if (string.IsNullOrWhiteSpace(ch.TitlePl) || string.IsNullOrWhiteSpace(ch.TextPl) || string.IsNullOrWhiteSpace(ch.GoalPl)) e.Add($"story {ch.Id}: texts required.");
        }

        foreach (var t in c.ArenaTitleList)
            if (string.IsNullOrWhiteSpace(t.League) || string.IsNullOrWhiteSpace(t.TitlePl) || !Rarities.Contains(t.Rarity)) e.Add($"arena title '{t.League}' invalid.");

        if (c.Checkin.BaseStones < 0 || c.Checkin.PerDayStones < 0 || c.Checkin.MaxBonusDays < 1) e.Add("checkin values invalid.");
        return e;
    }
}
