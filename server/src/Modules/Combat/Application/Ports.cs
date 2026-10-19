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

    /// <summary>Battles of this kind and context the character started (as side A) at or after <paramref name="since"/>.</summary>
    Task<int> CountBattlesSinceAsync(IUnitOfWork uow, CharacterId characterId, string kind, string context, DateTimeOffset since, CancellationToken ct);

    // ---- Tower of Trials
    Task<TowerProgress?> FindTowerAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    /// <summary>Insert when <paramref name="expectedVersion"/> is 0, otherwise update only at that version.</summary>
    Task<bool> TrySaveTowerAsync(IUnitOfWork uow, TowerProgress progress, int expectedVersion, CancellationToken ct);

    /// <summary>Highest floor first; ties: who got there first.</summary>
    Task<IReadOnlyList<TowerProgress>> ListTowerTopAsync(IUnitOfWork uow, int limit, CancellationToken ct);
    Task<int> CountTowerAheadAsync(IUnitOfWork uow, TowerProgress mine, CancellationToken ct);

    // ---- arena
    Task<ArenaEntry?> FindArenaAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    /// <summary>Insert when <paramref name="expectedVersion"/> is 0, otherwise update only at that version.</summary>
    Task<bool> TrySaveArenaAsync(IUnitOfWork uow, ArenaEntry entry, int expectedVersion, CancellationToken ct);

    /// <summary>Entries with rating in [min, max], best first (the caller removes itself).</summary>
    Task<IReadOnlyList<ArenaEntry>> ListArenaAsync(IUnitOfWork uow, int minRating, int maxRating, int limit, CancellationToken ct);
    Task<int> CountArenaAheadAsync(IUnitOfWork uow, ArenaEntry mine, CancellationToken ct);

    Task<bool> ArenaWeekClaimedAsync(IUnitOfWork uow, CharacterId characterId, string week, CancellationToken ct);

    /// <summary>Battles of a kind the character started (side A) at or after <paramref name="since"/>.</summary>
    Task<int> CountBattlesOfKindSinceAsync(IUnitOfWork uow, CharacterId characterId, string kind, DateTimeOffset since, CancellationToken ct);

    /// <summary>False if the weekly reward of this week was already claimed.</summary>
    Task<bool> TryClaimArenaWeekAsync(IUnitOfWork uow, CharacterId characterId, string week, DateTimeOffset at, CancellationToken ct);

    // ---- world boss
    Task<WorldBossState?> FindBossAsync(IUnitOfWork uow, int cycle, CancellationToken ct);

    /// <summary>Insert when <paramref name="expectedVersion"/> is 0, otherwise update only at that version.</summary>
    Task<bool> TrySaveBossAsync(IUnitOfWork uow, WorldBossState boss, int expectedVersion, CancellationToken ct);
    Task<WorldBossDamage?> FindBossDamageAsync(IUnitOfWork uow, int cycle, CharacterId characterId, CancellationToken ct);

    /// <summary>Insert when <paramref name="expectedVersion"/> is 0, otherwise update only at that version.</summary>
    Task<bool> TrySaveBossDamageAsync(IUnitOfWork uow, WorldBossDamage damage, int expectedVersion, CancellationToken ct);

    /// <summary>Most damage first; ties by character id.</summary>
    Task<IReadOnlyList<WorldBossDamage>> ListBossTopAsync(IUnitOfWork uow, int cycle, int limit, CancellationToken ct);
    Task<int> CountBossAheadAsync(IUnitOfWork uow, WorldBossDamage mine, CancellationToken ct);

    Task<IReadOnlyList<SubmittedIntent>> ListIntentsAsync(IUnitOfWork uow, Guid battleId, int turn, CancellationToken ct);

    /// <summary>Throws <see cref="DuplicateKeyException"/> if the side already submitted for this turn.</summary>
    Task AddIntentAsync(IUnitOfWork uow, SubmittedIntent intent, CancellationToken ct);

    /// <summary>Throws <see cref="DuplicateKeyException"/> if the turn was already resolved.</summary>
    Task AddTurnAsync(IUnitOfWork uow, TurnRecord turn, CancellationToken ct);
    Task<IReadOnlyList<TurnRecord>> ListTurnsAsync(IUnitOfWork uow, Guid battleId, CancellationToken ct);

    // ---- idle hunts
    Task<IdleHunt?> FindHuntAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    /// <summary>Insert when <paramref name="expectedVersion"/> is 0, otherwise update only at that version.</summary>
    Task<bool> TrySaveHuntAsync(IUnitOfWork uow, IdleHunt hunt, int expectedVersion, CancellationToken ct);
    Task DeleteHuntAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    // ---- spirit pets
    Task<SpiritPet?> FindPetAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
    Task SavePetAsync(IUnitOfWork uow, SpiritPet pet, CancellationToken ct);
    Task DeletePetAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    // ---- Dao Lab
    Task AddCustomTechniqueAsync(IUnitOfWork uow, CustomTechnique technique, CancellationToken ct);
    Task<CustomTechnique?> FindCustomTechniqueAsync(IUnitOfWork uow, string id, CancellationToken ct);
    Task<int> CountDesignsAsync(IUnitOfWork uow, CharacterId author, CancellationToken ct);

    /// <summary>False if the character already knows the technique.</summary>
    Task<bool> TryAddKnownAsync(IUnitOfWork uow, KnownTechnique known, CancellationToken ct);

    /// <summary>Dao techniques the character knows, with their definitions.</summary>
    Task<IReadOnlyList<(KnownTechnique Known, CustomTechnique Technique)>> ListKnownCustomAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
}
