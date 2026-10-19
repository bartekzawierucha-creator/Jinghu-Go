using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Inventory.Application;
using JianghuGo.Modules.Inventory.Domain;
using Npgsql;

namespace JianghuGo.Modules.Inventory.Infrastructure;

/// <summary>Balances and stack quantities change ONLY through ledger inserts; triggers re-validate every line (V0011).</summary>
internal sealed class PostgresInventoryRepository : IInventoryRepository
{
    private static PostgresUnitOfWork Pg(IUnitOfWork uow) => PostgresUnitOfWork.From(uow);

    private static ItemInstance MapInstance(NpgsqlDataReader r) => new(
        r.GetGuid(0), new CharacterId(r.GetGuid(1)), new AccountId(r.GetGuid(2)), r.GetString(3), r.GetInt32(4), r.GetString(5), r.GetInt32(6), r.GetFieldValue<DateTimeOffset>(7), r.IsDBNull(8) ? null : r.GetString(8));

    private const string InstanceColumns = "id, character_id, account_id, item_id, upgrade_level, state, version, created_at, bound_ref";

    public async Task<long> QuantityAsync(IUnitOfWork uow, CharacterId characterId, string itemId, CancellationToken ct) =>
        (await Pg(uow).QueryAsync("SELECT quantity FROM inventory.stacks WHERE character_id = $1 AND item_id = $2", r => r.GetInt64(0), ct, characterId.Value, itemId))
        .DefaultIfEmpty(0).Single();

    public async Task<IReadOnlyList<(string ItemId, long Quantity)>> ListStacksAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        await Pg(uow).QueryAsync("SELECT item_id, quantity FROM inventory.stacks WHERE character_id = $1 AND quantity > 0 ORDER BY item_id",
            r => (r.GetString(0), r.GetInt64(1)), ct, characterId.Value);

    public async Task<Result<StackEntry>> PostStackAsync(IUnitOfWork uow, CharacterId characterId, string itemId, long delta, long maxStack, string reason, string referenceId, DateTimeOffset at, CancellationToken ct)
    {
        var current = (await Pg(uow).QueryAsync("SELECT quantity FROM inventory.stacks WHERE character_id = $1 AND item_id = $2 FOR UPDATE",
            r => r.GetInt64(0), ct, characterId.Value, itemId)).DefaultIfEmpty(0).Single();
        var after = checked(current + delta);
        if (after < 0) return Error.InsufficientFunds("insufficient_items", $"Not enough {itemId} (have {current}, need {-delta}).");
        if (after > maxStack) return Error.LimitReached("stack_full", $"A stack holds at most {maxStack}.");
        var entry = new StackEntry(Uuid7.New(), characterId, itemId, delta, after, reason, referenceId, at);
        await Pg(uow).ExecuteAsync(
            "INSERT INTO inventory.stack_entries (id, character_id, item_id, delta, quantity_after, reason, reference_id, created_at) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)",
            ct, entry.Id, characterId.Value, itemId, delta, after, reason, referenceId, at);
        return entry;
    }

    public async Task<bool> EntryExistsAsync(IUnitOfWork uow, CharacterId characterId, string reason, string referenceId, CancellationToken ct) =>
        (await Pg(uow).QueryAsync(
            "SELECT 1 WHERE EXISTS (SELECT 1 FROM inventory.stack_entries WHERE character_id = $1 AND reason = $2 AND reference_id = $3) " +
            "OR EXISTS (SELECT 1 FROM inventory.wallet_entries WHERE character_id = $1 AND reason = $2 AND reference_id = $3)",
            _ => 1, ct, characterId.Value, reason, referenceId)).Count > 0;

    public async Task<long?> BalanceAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        var rows = await Pg(uow).QueryAsync("SELECT balance FROM inventory.wallets WHERE character_id = $1", r => r.GetInt64(0), ct, characterId.Value);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<Result<WalletEntry>> PostWalletAsync(IUnitOfWork uow, CharacterId characterId, long delta, string reason, string referenceId, DateTimeOffset at, CancellationToken ct)
    {
        var current = (await Pg(uow).QueryAsync("SELECT balance FROM inventory.wallets WHERE character_id = $1 FOR UPDATE", r => r.GetInt64(0), ct, characterId.Value))
            .DefaultIfEmpty(0).Single();
        var after = checked(current + delta);
        if (after < 0) return Error.InsufficientFunds("insufficient_funds", $"Not enough Spirit Stones (have {current}, need {-delta}).");
        var entry = new WalletEntry(Uuid7.New(), characterId, delta, after, reason, referenceId, at);
        await Pg(uow).ExecuteAsync(
            "INSERT INTO inventory.wallet_entries (id, character_id, delta, balance_after, reason, reference_id, created_at) VALUES ($1, $2, $3, $4, $5, $6, $7)",
            ct, entry.Id, characterId.Value, delta, after, reason, referenceId, at);
        return entry;
    }

    public async Task<IReadOnlyList<WalletEntry>> ListWalletEntriesAsync(IUnitOfWork uow, CharacterId characterId, Guid? before, int limit, CancellationToken ct) =>
        await Pg(uow).QueryAsync(
            "SELECT id, delta, balance_after, reason, reference_id, created_at FROM inventory.wallet_entries " +
            "WHERE character_id = $1 AND ($2::uuid IS NULL OR id < $2::uuid) ORDER BY id DESC LIMIT $3",
            r => new WalletEntry(r.GetGuid(0), characterId, r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetFieldValue<DateTimeOffset>(5)),
            ct, characterId.Value, before, limit);

    public Task AddInstanceAsync(IUnitOfWork uow, ItemInstance i, CancellationToken ct) =>
        Pg(uow).ExecuteAsync($"INSERT INTO inventory.items ({InstanceColumns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)",
            ct, i.Id, i.CharacterId.Value, i.AccountId.Value, i.ItemId, i.UpgradeLevel, i.State, i.Version, i.CreatedAt, i.BoundRef);

    public Task<ItemInstance?> FindInstanceAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        Pg(uow).QuerySingleOrDefaultAsync($"SELECT {InstanceColumns} FROM inventory.items WHERE id = $1", MapInstance, ct, id);

    public async Task<bool> TryUpdateInstanceAsync(IUnitOfWork uow, ItemInstance i, int expectedVersion, CancellationToken ct) =>
        i.Version == expectedVersion + 1 && await Pg(uow).ExecuteAsync(
            "UPDATE inventory.items SET character_id = $2, account_id = $3, upgrade_level = $4, state = $5, version = $6 WHERE id = $1 AND item_id = $7 AND version = $8",
            ct, i.Id, i.CharacterId.Value, i.AccountId.Value, i.UpgradeLevel, i.State, i.Version, i.ItemId, expectedVersion) == 1;

    public async Task<IReadOnlyList<ItemInstance>> ListInstancesAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        await Pg(uow).QueryAsync($"SELECT {InstanceColumns} FROM inventory.items WHERE character_id = $1 AND state <> 'destroyed' ORDER BY id", MapInstance, ct, characterId.Value);

    public Task AddItemEventAsync(IUnitOfWork uow, ItemEvent e, CancellationToken ct) =>
        Pg(uow).ExecuteAsync("INSERT INTO inventory.item_events (id, item_instance_id, character_id, kind, data, created_at) VALUES ($1, $2, $3, $4, $5::jsonb, $6)",
            ct, e.Id, e.ItemInstanceId, e.CharacterId.Value, e.Kind, e.DataJson, e.At);

    public async Task<IReadOnlyList<ItemEvent>> ListItemEventsAsync(IUnitOfWork uow, Guid instanceId, CancellationToken ct) =>
        await Pg(uow).QueryAsync("SELECT id, character_id, kind, data::text, created_at FROM inventory.item_events WHERE item_instance_id = $1 ORDER BY id",
            r => new ItemEvent(r.GetGuid(0), instanceId, new CharacterId(r.GetGuid(1)), r.GetString(2), r.GetString(3), r.GetFieldValue<DateTimeOffset>(4)), ct, instanceId);

    public async Task<bool> TryRecordGrantAsync(IUnitOfWork uow, Guid sourceId, CharacterId characterId, string kind, DateTimeOffset at, CancellationToken ct) =>
        await Pg(uow).ExecuteAsync(
            "INSERT INTO inventory.grants (source_id, character_id, kind, granted_at) VALUES ($1, $2, $3, $4) ON CONFLICT DO NOTHING",
            ct, sourceId, characterId.Value, kind, at) == 1;

    public async Task<(int Wallets, int Stacks)> CountMismatchesAsync(IUnitOfWork uow, CancellationToken ct) =>
        (await Pg(uow).QueryAsync(
            "SELECT (SELECT count(*) FROM inventory.wallet_mismatches), (SELECT count(*) FROM inventory.stack_mismatches)",
            r => ((int)r.GetInt64(0), (int)r.GetInt64(1)), ct)).Single();

    public Task<ActiveTalisman?> FindTalismanAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync("SELECT item_id, until FROM inventory.talismans WHERE character_id = $1",
            r => new ActiveTalisman(characterId, r.GetString(0), r.GetFieldValue<DateTimeOffset>(1)), ct, characterId.Value);

    public Task SetTalismanAsync(IUnitOfWork uow, ActiveTalisman t, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO inventory.talismans (character_id, item_id, until) VALUES ($1, $2, $3) ON CONFLICT (character_id) DO UPDATE SET item_id = EXCLUDED.item_id, until = EXCLUDED.until",
            ct, t.CharacterId.Value, t.ItemId, t.Until);
}
