using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Cultivation.Domain;

namespace JianghuGo.Modules.Cultivation.Application;

public interface ICultivationRepository
{
    Task<CultivationState?> FindAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    /// <summary>Inserts when <paramref name="expectedVersion"/> is 0, otherwise updates only if the stored version matches.</summary>
    Task<bool> TrySaveAsync(IUnitOfWork uow, CultivationState state, int expectedVersion, CancellationToken ct);

    Task AddBreakthroughAsync(IUnitOfWork uow, BreakthroughAttempt attempt, CancellationToken ct);
    Task<IReadOnlyList<BreakthroughAttempt>> ListBreakthroughsAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
}

/// <summary>Integration events published by Cultivation (consumed via the outbox).</summary>
public static class CultivationEvents
{
    public const string BreakthroughSucceeded = "cultivation.breakthrough_succeeded";
    public const string BreakthroughFailed = "cultivation.breakthrough_failed";

    public sealed record BreakthroughV1(Guid AccountId, Guid CharacterId, Guid AttemptId, string FromRealm, int FromStage, string ToRealm, int ToStage, bool Success);
}
