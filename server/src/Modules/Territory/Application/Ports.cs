using JianghuGo.Application;
using JianghuGo.Modules.Territory.Domain;

namespace JianghuGo.Modules.Territory.Application;

public interface ITerritoryRepository
{
    Task<Domain.Territory?> FindAsync(IUnitOfWork uow, string key, CancellationToken ct);

    /// <summary>Insert when <paramref name="expectedVersion"/> is 0 (first claim), otherwise update only at that version.</summary>
    Task<bool> TrySaveAsync(IUnitOfWork uow, Domain.Territory territory, int expectedVersion, CancellationToken ct);
    Task<IReadOnlyList<Domain.Territory>> ListOwnedAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct);

    Task AddWarAsync(IUnitOfWork uow, War war, CancellationToken ct);
    Task<War?> FindWarAsync(IUnitOfWork uow, Guid id, CancellationToken ct);
    Task<War?> FindOpenWarAsync(IUnitOfWork uow, string territoryKey, CancellationToken ct);
    Task<IReadOnlyList<War>> ListOpenWarsAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct);
    Task<IReadOnlyList<War>> ListWarsAsync(IUnitOfWork uow, string territoryKey, int limit, CancellationToken ct);
    Task<bool> TryUpdateWarAsync(IUnitOfWork uow, War war, int expectedVersion, CancellationToken ct);

    /// <summary>False if this battle was already counted for this war (exactly-once scoring).</summary>
    Task<bool> TryRecordWarBattleAsync(IUnitOfWork uow, Guid warId, Guid battleId, Guid winnerSectId, DateTimeOffset at, CancellationToken ct);

    Task AddOwnershipChangeAsync(IUnitOfWork uow, OwnershipChange change, CancellationToken ct);
    Task<IReadOnlyList<OwnershipChange>> ListOwnershipChangesAsync(IUnitOfWork uow, string territoryKey, int limit, CancellationToken ct);
}
