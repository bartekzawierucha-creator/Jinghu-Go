using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Domain;

namespace JianghuGo.Modules.Characters.Application;

public interface ICharacterRepository
{
    Task<CharacterDraft?> FindActiveDraftAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct);
    Task<CharacterDraft?> FindDraftAsync(IUnitOfWork uow, DraftId id, CancellationToken ct);
    Task AddDraftAsync(IUnitOfWork uow, CharacterDraft draft, CancellationToken ct);

    /// <summary>Optimistic concurrency: succeeds only if the stored version equals <paramref name="expectedVersion"/>.</summary>
    Task<bool> TryUpdateDraftAsync(IUnitOfWork uow, CharacterDraft draft, int expectedVersion, CancellationToken ct);

    Task<Character?> FindCharacterAsync(IUnitOfWork uow, CharacterId id, CancellationToken ct);
    Task<IReadOnlyList<Character>> ListCharactersAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct);
    Task<bool> NameExistsAsync(IUnitOfWork uow, string normalizedName, CancellationToken ct);
    Task<CharacterId?> FindIdByNameAsync(IUnitOfWork uow, string normalizedName, CancellationToken ct);

    /// <summary>Throws <see cref="DuplicateKeyException"/> on name collision.</summary>
    Task AddCharacterAsync(IUnitOfWork uow, Character character, CancellationToken ct);
}

public interface IDestinyTokenLedger
{
    Task<int?> GetBalanceAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct);
    Task<IReadOnlyList<DestinyTokenEntry>> ListEntriesAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct);

    /// <summary>Appends an entry and updates the balance atomically. Rejects results below zero.</summary>
    Task<Result<DestinyTokenEntry>> PostAsync(IUnitOfWork uow, AccountId accountId, int delta, string reason, string referenceId, DateTimeOffset now, CancellationToken ct);
}
