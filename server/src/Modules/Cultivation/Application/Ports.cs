using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Cultivation.Domain;

namespace JianghuGo.Modules.Cultivation.Application;

public interface ICultivationRepository
{
    Task<CultivationState?> FindAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    Task<CultivationProfile?> FindProfileAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    /// <summary>Inserts when <paramref name="expectedVersion"/> is 0, otherwise updates only at that version.</summary>
    Task<bool> TrySaveProfileAsync(IUnitOfWork uow, CultivationProfile profile, int expectedVersion, CancellationToken ct);

    /// <summary>Inserts when <paramref name="expectedVersion"/> is 0, otherwise updates only if the stored version matches.</summary>
    Task<bool> TrySaveAsync(IUnitOfWork uow, CultivationState state, int expectedVersion, CancellationToken ct);

    Task AddBreakthroughAsync(IUnitOfWork uow, BreakthroughAttempt attempt, CancellationToken ct);
    Task<IReadOnlyList<BreakthroughAttempt>> ListBreakthroughsAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    /// <summary>Records a Qi grant from another module's event. False if this (source, character) was already granted.</summary>
    Task<bool> TryRecordRewardAsync(IUnitOfWork uow, Guid sourceId, CharacterId characterId, long qi, DateTimeOffset at, CancellationToken ct);

    /// <summary>Exactly-once guard for event handlers that change the profile without paying Qi (false = already applied).</summary>
    Task<bool> TryMarkHandledAsync(IUnitOfWork uow, Guid guardId, CharacterId characterId, DateTimeOffset at, CancellationToken ct);

    /// <summary>Meridian points opened per stat (only stats with points &gt; 0).</summary>
    Task<IReadOnlyDictionary<string, int>> ListMeridiansAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    Task SaveMeridianAsync(IUnitOfWork uow, CharacterId characterId, string stat, int points, CancellationToken ct);

    Task ClearMeridiansAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    /// <summary>Highest cultivators: realm, then stage, then Qi (ties: earliest to get there).</summary>
    Task<IReadOnlyList<CultivationState>> ListTopAsync(IUnitOfWork uow, int limit, CancellationToken ct);

    /// <summary>How many characters are strictly ahead of the given state.</summary>
    Task<int> CountAheadAsync(IUnitOfWork uow, CultivationState state, CancellationToken ct);
}

/// <summary>Integration events published by Cultivation (consumed via the outbox).</summary>
public static class CultivationEvents
{
    public const string BreakthroughSucceeded = "cultivation.breakthrough_succeeded";
    public const string BreakthroughFailed = "cultivation.breakthrough_failed";

    public const string MeditationFinished = "cultivation.meditation_finished";

    public sealed record MeditationFinishedV1(Guid AccountId, Guid CharacterId, int Minutes, long Qi);

    public const string RealmReached = "cultivation.realm_reached";

    /// <summary>A new great realm (after a heavenly tribulation), with the quality earned for foundation/core/soul if any.</summary>
    public sealed record RealmReachedV1(Guid AccountId, Guid CharacterId, int Realm, string RealmId, string? Quality, string? CoreColor);

    public sealed record BreakthroughV1(Guid AccountId, Guid CharacterId, Guid AttemptId, string FromRealm, int FromStage, string ToRealm, int ToStage, bool Success);
}
