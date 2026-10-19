using JianghuGo.Domain;

namespace JianghuGo.Modules.Cultivation.Domain;

/// <summary>
/// Everything about a cultivator beyond realm/stage/Qi (rules cultivation-3): the Qi flow mode and when it was last
/// settled, the vessel, crystals, method, heart demons, the qualities of foundation/core/soul, karma, counters that
/// decide the core colour, and the last offline summary. Stored as one JSON document per character.
/// </summary>
public sealed record CultivationProfile(
    CharacterId CharacterId,
    int LadderVersion,
    string Mode,
    DateTimeOffset FlowSettledAt,
    DateTimeOffset? RetreatUntil,
    int RetreatHours,
    int VesselLevel,
    long Crystals,
    string? MethodId,
    IReadOnlyList<string> KnownMethods,
    IReadOnlyDictionary<string, long> MethodMinutes,
    int Instability,
    IReadOnlyDictionary<string, string> Qualities,
    string? CoreColor,
    int Karma,
    int Reincarnations,
    int? AutoBreakthroughPermille,
    DateOnly? AcceleratorDate,
    DateTimeOffset? PeakSince,
    DateTimeOffset? LastBreakthroughAt,
    Guid? TribulationBattleId,
    ProfileCounters Counters,
    int Enlightenments,
    SettleSummary? LastSummary,
    long LifetimeQi,
    int Version,
    DateOnly? PillDay = null,
    IReadOnlyDictionary<string, int>? PillsToday = null)
{
    /// <summary>Pills of this kind already taken today (1.8 tolerance; the count resets with the world day).</summary>
    public int PillsTaken(string itemId, DateOnly today) => PillDay == today ? PillsToday?.GetValueOrDefault(itemId) ?? 0 : 0;

    public const int CurrentLadder = 3;

    public static CultivationProfile New(CharacterId id, string path, DateTimeOffset now) =>
        new(id, CurrentLadder, CultivationModes.Idle, now, null, 0, 0, 0, StarterMethod(path), [StarterMethod(path)], new Dictionary<string, long>(), 0,
            new Dictionary<string, string>(), null, 0, 0, null, null, null, null, null, new ProfileCounters(0, 0, 0, 0, 0), 0, null, 0, 0);

    public static string StarterMethod(string path) => path switch
    {
        "body" => "iron_body_canon",
        "flame" => "phoenix_song",
        "poison" => "blood_moon_book",
        _ => "still_water_sutra",
    };

    public string? Quality(string stage) => Qualities.TryGetValue(stage, out var q) ? q : null;

    public long MinutesWith(string? method) => method is not null && MethodMinutes.TryGetValue(method, out var m) ? m : 0;
}

/// <summary>Lifetime counters that decide the colour of the golden core.</summary>
public sealed record ProfileCounters(long BattlesWon, long Crafts, long MeditationMinutes, long NightMinutes, long Gathered);

/// <summary>What happened while the player was away (shown once, then cleared by the client acknowledging it).</summary>
public sealed record SettleSummary(DateTimeOffset From, DateTimeOffset To, long Qi, int Enlightenments, int AutoBreakthroughs, bool VesselFull, bool RetreatFinished);
