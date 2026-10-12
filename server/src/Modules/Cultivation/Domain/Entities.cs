using JianghuGo.Domain;

namespace JianghuGo.Modules.Cultivation.Domain;

/// <summary>Cultivation progress of one character. <see cref="Version"/> is the optimistic-concurrency token.</summary>
public sealed record CultivationState(
    CharacterId CharacterId,
    AccountId AccountId,
    string Path,
    int RealmIndex,
    int Stage,
    long Qi,
    int Version,
    DateTimeOffset? DeviationUntil,
    DateTimeOffset? MeditationStartedAt,
    DateOnly DailyDate,
    long DailyMeditationQi,
    int DailySteps,
    DateTimeOffset? LastStepsReportAt,
    DateTimeOffset UpdatedAt)
{
    public static CultivationState Initial(CharacterId id, AccountId account, string path, DateOnly today, DateTimeOffset now) =>
        new(id, account, path, 0, 1, 0, 0, null, null, today, 0, 0, null, now);

    public bool InDeviation(DateTimeOffset now) => DeviationUntil is { } d && d > now;

    /// <summary>Daily counters belong to one world-calendar day; a new day starts from zero.</summary>
    public CultivationState ForDay(DateOnly today) =>
        today == DailyDate ? this : this with { DailyDate = today, DailyMeditationQi = 0, DailySteps = 0 };
}

/// <summary>Immutable record of a breakthrough attempt: enough to audit and replay the roll.</summary>
public sealed record BreakthroughAttempt(
    Guid Id,
    CharacterId CharacterId,
    AccountId AccountId,
    int FromRealm,
    int FromStage,
    int ToRealm,
    int ToStage,
    bool Success,
    int ChancePpm,
    int Roll,
    ulong Seed,
    long QiSpent,
    string RulesVersion,
    DateTimeOffset OccurredAt);
