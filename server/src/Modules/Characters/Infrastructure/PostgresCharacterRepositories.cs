using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Characters.Application;
using JianghuGo.Modules.Characters.Domain;
using Npgsql;

namespace JianghuGo.Modules.Characters.Infrastructure;

internal static class StatsJson
{
    private sealed record Item(string Id, int Value);

    public static string Write(IReadOnlyList<KeyValuePair<string, int>> stats) =>
        JsonSerializer.Serialize(stats.Select(s => new Item(s.Key, s.Value)), Idempotency.Json);

    public static IReadOnlyList<KeyValuePair<string, int>> Read(string json) =>
        [.. JsonSerializer.Deserialize<List<Item>>(json, Idempotency.Json)!.Select(i => new KeyValuePair<string, int>(i.Id, i.Value))];
}

internal sealed class PostgresCharacterRepository : ICharacterRepository
{
    private const string DraftColumns =
        "id, account_id, version, status, rules_version, last_seed, background_trait, bloodline_trait, constitution_trait, stats::text, reroll_count, created_at, updated_at";

    private const string CharacterColumns =
        "id, account_id, name, normalized_name, path, rules_version, background_trait, bloodline_trait, constitution_trait, stats::text, source_draft_id, created_at";

    private static CharacterDraft MapDraft(NpgsqlDataReader r) => new(
        new DraftId(r.GetGuid(0)), new AccountId(r.GetGuid(1)), r.GetInt32(2),
        r.GetString(3) == "active" ? DraftStatus.Active : DraftStatus.Finalized,
        r.GetString(4), (ulong)r.GetDecimal(5),
        new RolledTraits(r.GetString(6), r.GetString(7), r.GetString(8)),
        StatsJson.Read(r.GetString(9)), r.GetInt32(10), r.GetFieldValue<DateTimeOffset>(11), r.GetFieldValue<DateTimeOffset>(12));

    private static Character MapCharacter(NpgsqlDataReader r) => new(
        new CharacterId(r.GetGuid(0)), new AccountId(r.GetGuid(1)), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
        new RolledTraits(r.GetString(6), r.GetString(7), r.GetString(8)),
        StatsJson.Read(r.GetString(9)), new DraftId(r.GetGuid(10)), r.GetFieldValue<DateTimeOffset>(11));

    private static string Status(DraftStatus s) => s == DraftStatus.Active ? "active" : "finalized";

    public Task<CharacterDraft?> FindActiveDraftAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync(
            $"SELECT {DraftColumns} FROM characters.drafts WHERE account_id = $1 AND status = 'active'", MapDraft, ct, accountId.Value);

    public Task<CharacterDraft?> FindDraftAsync(IUnitOfWork uow, DraftId id, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync($"SELECT {DraftColumns} FROM characters.drafts WHERE id = $1", MapDraft, ct, id.Value);

    public Task AddDraftAsync(IUnitOfWork uow, CharacterDraft d, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO characters.drafts (id, account_id, version, status, rules_version, last_seed, background_trait, bloodline_trait, constitution_trait, stats, reroll_count, created_at, updated_at) " +
            "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10::jsonb, $11, $12, $13)",
            ct, d.Id.Value, d.AccountId.Value, d.Version, Status(d.Status), d.RulesVersion, (decimal)d.LastSeed,
            d.Traits.Background, d.Traits.Bloodline, d.Traits.Constitution, StatsJson.Write(d.Stats), d.RerollCount, d.CreatedAt, d.UpdatedAt);

    public async Task<bool> TryUpdateDraftAsync(IUnitOfWork uow, CharacterDraft d, int expectedVersion, CancellationToken ct)
    {
        if (d.Version != expectedVersion + 1)
        {
            return false;
        }

        var rows = await PostgresUnitOfWork.From(uow).ExecuteAsync(
            "UPDATE characters.drafts SET version = $3, status = $4, last_seed = $5, background_trait = $6, bloodline_trait = $7, constitution_trait = $8, " +
            "stats = $9::jsonb, reroll_count = $10, updated_at = $11 WHERE id = $1 AND version = $2",
            ct, d.Id.Value, expectedVersion, d.Version, Status(d.Status), (decimal)d.LastSeed,
            d.Traits.Background, d.Traits.Bloodline, d.Traits.Constitution, StatsJson.Write(d.Stats), d.RerollCount, d.UpdatedAt);
        return rows == 1;
    }

    public Task<Character?> FindCharacterAsync(IUnitOfWork uow, CharacterId id, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync($"SELECT {CharacterColumns} FROM characters.characters WHERE id = $1", MapCharacter, ct, id.Value);

    public async Task<IReadOnlyList<Character>> ListCharactersAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).QueryAsync(
            $"SELECT {CharacterColumns} FROM characters.characters WHERE account_id = $1 ORDER BY created_at, id", MapCharacter, ct, accountId.Value);

    public async Task<bool> NameExistsAsync(IUnitOfWork uow, string normalizedName, CancellationToken ct) =>
        (await PostgresUnitOfWork.From(uow).QueryAsync("SELECT 1 FROM characters.characters WHERE normalized_name = $1", r => 1, ct, normalizedName)).Count > 0;

    public async Task<CharacterId?> FindIdByNameAsync(IUnitOfWork uow, string normalizedName, CancellationToken ct)
    {
        var rows = await PostgresUnitOfWork.From(uow).QueryAsync("SELECT id FROM characters.characters WHERE normalized_name = $1", r => r.GetGuid(0), ct, normalizedName);
        return rows.Count == 0 ? null : new CharacterId(rows[0]);
    }

    public Task AddCharacterAsync(IUnitOfWork uow, Character c, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            $"INSERT INTO characters.characters ({CharacterColumns.Replace("stats::text", "stats", StringComparison.Ordinal)}) " +
            "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10::jsonb, $11, $12)",
            ct, c.Id.Value, c.AccountId.Value, c.Name, c.NormalizedName, c.Path, c.RulesVersion,
            c.Traits.Background, c.Traits.Bloodline, c.Traits.Constitution, StatsJson.Write(c.Stats), c.SourceDraftId.Value, c.CreatedAt);
}

internal sealed class PostgresDestinyTokenLedger : IDestinyTokenLedger
{
    public async Task<int?> GetBalanceAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct)
    {
        var rows = await PostgresUnitOfWork.From(uow).QueryAsync(
            "SELECT balance FROM characters.destiny_token_balances WHERE account_id = $1", r => r.GetInt32(0), ct, accountId.Value);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<IReadOnlyList<DestinyTokenEntry>> ListEntriesAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).QueryAsync(
            "SELECT id, delta, balance_after, reason, reference_id, created_at FROM characters.destiny_token_entries WHERE account_id = $1 ORDER BY id",
            r => new DestinyTokenEntry(r.GetGuid(0), accountId, r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetFieldValue<DateTimeOffset>(5)),
            ct, accountId.Value);

    public async Task<Result<DestinyTokenEntry>> PostAsync(IUnitOfWork uow, AccountId accountId, int delta, string reason, string referenceId, DateTimeOffset now, CancellationToken ct)
    {
        if (delta == 0)
        {
            return Error.Validation("ledger_zero_delta", "Ledger entries must change the balance.");
        }

        var pg = PostgresUnitOfWork.From(uow);
        // Row lock: the balance cannot change between this read and the insert below.
        var balance = (await pg.QueryAsync("SELECT balance FROM characters.destiny_token_balances WHERE account_id = $1 FOR UPDATE",
            r => r.GetInt32(0), ct, accountId.Value)).DefaultIfEmpty(0).Single();
        var after = checked(balance + delta);
        if (after < 0)
        {
            return Error.InsufficientFunds("insufficient_destiny_tokens", $"Not enough Destiny Tokens (have {balance}, need {-delta}).");
        }

        var entry = new DestinyTokenEntry(Uuid7.New(), accountId, delta, after, reason, referenceId, now);
        // The insert trigger re-validates balance_after and updates the balance row.
        await pg.ExecuteAsync(
            "INSERT INTO characters.destiny_token_entries (id, account_id, delta, balance_after, reason, reference_id, created_at) VALUES ($1, $2, $3, $4, $5, $6, $7)",
            ct, entry.Id, accountId.Value, delta, after, reason, referenceId, now);
        return entry;
    }
}
