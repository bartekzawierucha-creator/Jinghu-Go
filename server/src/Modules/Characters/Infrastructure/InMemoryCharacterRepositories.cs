using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Modules.Characters.Application;
using JianghuGo.Modules.Characters.Domain;

namespace JianghuGo.Modules.Characters.Infrastructure;

internal sealed class InMemoryCharacterRepository : ICharacterRepository
{
    private const string Drafts = "characters.drafts";
    private const string Characters = "characters.characters";
    private const string Names = "characters.names"; // mirrors UNIQUE(normalized_name)

    public Task<CharacterDraft?> FindActiveDraftAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<DraftId, CharacterDraft>(Drafts).Values
            .FirstOrDefault(d => d.AccountId == accountId && d.Status == DraftStatus.Active));

    public Task<CharacterDraft?> FindDraftAsync(IUnitOfWork uow, DraftId id, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<DraftId, CharacterDraft>(Drafts).GetValueOrDefault(id));

    public Task AddDraftAsync(IUnitOfWork uow, CharacterDraft draft, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var table = u.Table<DraftId, CharacterDraft>(Drafts);
        if (draft.Status == DraftStatus.Active && table.Values.Any(d => d.AccountId == draft.AccountId && d.Status == DraftStatus.Active))
        {
            throw new DuplicateKeyException("Account already has an active draft."); // mirrors partial unique index
        }

        u.SetTable(Drafts, table.Add(draft.Id, draft));
        return Task.CompletedTask;
    }

    public Task<bool> TryUpdateDraftAsync(IUnitOfWork uow, CharacterDraft draft, int expectedVersion, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var table = u.Table<DraftId, CharacterDraft>(Drafts);
        if (!table.TryGetValue(draft.Id, out var current) || current.Version != expectedVersion || draft.Version != expectedVersion + 1)
        {
            return Task.FromResult(false);
        }

        u.SetTable(Drafts, table.SetItem(draft.Id, draft));
        return Task.FromResult(true);
    }

    public Task<Character?> FindCharacterAsync(IUnitOfWork uow, CharacterId id, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<CharacterId, Character>(Characters).GetValueOrDefault(id));

    public Task<IReadOnlyList<Character>> ListCharactersAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct)
    {
        IReadOnlyList<Character> list = [.. InMemoryUnitOfWork.From(uow).Table<CharacterId, Character>(Characters).Values
            .Where(c => c.AccountId == accountId).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id.Value)];
        return Task.FromResult(list);
    }

    public Task<bool> NameExistsAsync(IUnitOfWork uow, string normalizedName, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<string, CharacterId>(Names).ContainsKey(normalizedName));

    public Task AddCharacterAsync(IUnitOfWork uow, Character character, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var names = u.Table<string, CharacterId>(Names);
        if (names.ContainsKey(character.NormalizedName))
        {
            throw new DuplicateKeyException("Character name taken.");
        }

        u.SetTable(Names, names.Add(character.NormalizedName, character.Id));
        u.SetTable(Characters, u.Table<CharacterId, Character>(Characters).Add(character.Id, character));
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryDestinyTokenLedger : IDestinyTokenLedger
{
    private const string Entries = "characters.destiny_token_entries";
    private const string Balances = "characters.destiny_token_balances";

    public Task<int?> GetBalanceAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<AccountId, int>(Balances).TryGetValue(accountId, out var b) ? b : (int?)null);

    public Task<IReadOnlyList<DestinyTokenEntry>> ListEntriesAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct)
    {
        IReadOnlyList<DestinyTokenEntry> list = [.. InMemoryUnitOfWork.From(uow).Table<Guid, DestinyTokenEntry>(Entries).Values
            .Where(e => e.AccountId == accountId).OrderBy(e => e.Id)];
        return Task.FromResult(list);
    }

    public Task<Result<DestinyTokenEntry>> PostAsync(IUnitOfWork uow, AccountId accountId, int delta, string reason, string referenceId, DateTimeOffset now, CancellationToken ct)
    {
        if (delta == 0)
        {
            return Task.FromResult<Result<DestinyTokenEntry>>(Error.Validation("ledger_zero_delta", "Ledger entries must change the balance."));
        }

        var u = InMemoryUnitOfWork.From(uow);
        var balances = u.Table<AccountId, int>(Balances);
        var current = balances.GetValueOrDefault(accountId);
        var after = checked(current + delta);
        if (after < 0)
        {
            return Task.FromResult<Result<DestinyTokenEntry>>(Error.InsufficientFunds("insufficient_destiny_tokens", $"Not enough Destiny Tokens (have {current}, need {-delta})."));
        }

        var entry = new DestinyTokenEntry(Guid.CreateVersion7(), accountId, delta, after, reason, referenceId, now);
        var entries = u.Table<Guid, DestinyTokenEntry>(Entries);
        if (entries.Values.Any(e => e.AccountId == accountId && e.Reason == reason && e.ReferenceId == referenceId))
        {
            throw new DuplicateKeyException("Ledger entry for this (account, reason, reference) already exists."); // mirrors UNIQUE in SQL
        }

        u.SetTable(Entries, entries.Add(entry.Id, entry));
        u.SetTable(Balances, balances.SetItem(accountId, after));
        return Task.FromResult<Result<DestinyTokenEntry>>(entry);
    }
}
