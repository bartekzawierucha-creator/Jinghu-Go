using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Combat.Domain;

namespace JianghuGo.Modules.Combat.Application;

public interface ICombatRepository
{
    Task<Loadout?> FindLoadoutAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    /// <summary>Insert when <paramref name="expectedVersion"/> is 0, otherwise update only at that version.</summary>
    Task<bool> TrySaveLoadoutAsync(IUnitOfWork uow, Loadout loadout, int expectedVersion, CancellationToken ct);

    Task<IReadOnlyList<TechniqueMastery>> ListMasteryAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
    Task SaveMasteryAsync(IUnitOfWork uow, TechniqueMastery mastery, CancellationToken ct);

    Task AddBattleAsync(IUnitOfWork uow, Battle battle, CancellationToken ct);
    Task<Battle?> FindBattleAsync(IUnitOfWork uow, Guid id, CancellationToken ct);
    Task<bool> TryUpdateBattleAsync(IUnitOfWork uow, Battle battle, int expectedVersion, CancellationToken ct);

    /// <summary>Battles of a character that are pending acceptance or active (as side A or B).</summary>
    Task<IReadOnlyList<Battle>> ListOpenBattlesAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    Task<int> CountRewardedPveWinsSinceAsync(IUnitOfWork uow, CharacterId characterId, DateTimeOffset since, CancellationToken ct);

    Task<IReadOnlyList<SubmittedIntent>> ListIntentsAsync(IUnitOfWork uow, Guid battleId, int turn, CancellationToken ct);

    /// <summary>Throws <see cref="DuplicateKeyException"/> if the side already submitted for this turn.</summary>
    Task AddIntentAsync(IUnitOfWork uow, SubmittedIntent intent, CancellationToken ct);

    /// <summary>Throws <see cref="DuplicateKeyException"/> if the turn was already resolved.</summary>
    Task AddTurnAsync(IUnitOfWork uow, TurnRecord turn, CancellationToken ct);
    Task<IReadOnlyList<TurnRecord>> ListTurnsAsync(IUnitOfWork uow, Guid battleId, CancellationToken ct);
}
