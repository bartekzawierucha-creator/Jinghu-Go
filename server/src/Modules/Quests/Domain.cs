using JianghuGo.Domain;

namespace JianghuGo.Modules.Quests.Domain;

public sealed record QuestDefinition(string Id, string NamePl, string DescriptionPl, string Metric, long Target, long Stones, string? TitlePl = null);

public sealed record CheckinConfig(long BaseStones, long PerDayStones, int MaxBonusDays);

public sealed record QuestsConfig(
    string RulesVersion,
    string WorldTimeZone,
    int DailyCount,
    IReadOnlyList<QuestDefinition> Daily,
    IReadOnlyList<QuestDefinition> Achievements,
    CheckinConfig Checkin)
{
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

    public static readonly string[] All = [BattlesWon, PvpWon, BossesWon, MeditationMinutes, Crafts, Upgrades, ItemsGathered, WorldQi, Breakthroughs, PillsUsed, MarketSales, Checkins];
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
        }

        if (c.Checkin.BaseStones < 0 || c.Checkin.PerDayStones < 0 || c.Checkin.MaxBonusDays < 1) e.Add("checkin values invalid.");
        return e;
    }
}
