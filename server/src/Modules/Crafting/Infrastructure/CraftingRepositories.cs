using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Crafting.Application;

namespace JianghuGo.Modules.Crafting.Infrastructure;

internal sealed class InMemoryCraftingRepository : ICraftingRepository
{
    private const string Log = "crafting.log";
    private const string Queue = "crafting.queue";

    public Task AddQueueAsync(IUnitOfWork uow, QueueEntry entry, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<Guid, QueueEntry>(Queue);
        if (t.Values.Any(e => e.CharacterId == entry.CharacterId && e.Kind == entry.Kind)) throw new DuplicateKeyException("crafting.queue (character_id, kind)");
        u.SetTable(Queue, t.Add(entry.Id, entry));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QueueEntry>> ListQueueAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        IReadOnlyList<QueueEntry> list = [.. InMemoryUnitOfWork.From(uow).Table<Guid, QueueEntry>(Queue).Values.Where(e => e.CharacterId == characterId).OrderBy(e => e.ReadyAt)];
        return Task.FromResult(list);
    }

    public Task<bool> RemoveQueueAsync(IUnitOfWork uow, Guid id, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<Guid, QueueEntry>(Queue);
        if (!t.ContainsKey(id)) return Task.FromResult(false);
        u.SetTable(Queue, t.Remove(id));
        return Task.FromResult(true);
    }

    public Task AddAsync(IUnitOfWork uow, CraftLogEntry entry, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        u.SetTable(Log, u.Table<Guid, CraftLogEntry>(Log).Add(entry.Id, entry));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CraftLogEntry>> ListAsync(IUnitOfWork uow, CharacterId characterId, Guid? before, int limit, CancellationToken ct)
    {
        IReadOnlyList<CraftLogEntry> list = [.. InMemoryUnitOfWork.From(uow).Table<Guid, CraftLogEntry>(Log).Values
            .Where(e => e.CharacterId == characterId && (before is null || e.Id.CompareTo(before.Value) < 0)).OrderByDescending(e => e.Id).Take(limit)];
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<(string Kind, string Subject, long Attempts, long Successes)>> TotalsAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        IReadOnlyList<(string, string, long, long)> list = [.. InMemoryUnitOfWork.From(uow).Table<Guid, CraftLogEntry>(Log).Values
            .Where(e => e.CharacterId == characterId).GroupBy(e => (e.Kind, e.Subject))
            .Select(g => (g.Key.Kind, g.Key.Subject, g.Sum(e => (long)e.Attempts), g.Sum(e => (long)e.Successes)))];
        return Task.FromResult(list);
    }
}

internal sealed class PostgresCraftingRepository : ICraftingRepository
{
    private const string QueueColumns = "id, character_id, account_id, recipe_id, kind, times, chance_permille, seed, stones, started_at, ready_at";

    public Task AddQueueAsync(IUnitOfWork uow, QueueEntry e, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            $"INSERT INTO crafting.queue ({QueueColumns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)",
            ct, e.Id, e.CharacterId.Value, e.AccountId.Value, e.RecipeId, e.Kind, e.Times, e.ChancePermille, (decimal)e.Seed, e.Stones, e.StartedAt, e.ReadyAt);

    public async Task<IReadOnlyList<QueueEntry>> ListQueueAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).QueryAsync(
            $"SELECT {QueueColumns} FROM crafting.queue WHERE character_id = $1 ORDER BY ready_at",
            r => new QueueEntry(r.GetGuid(0), new CharacterId(r.GetGuid(1)), new AccountId(r.GetGuid(2)), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetInt32(6),
                (ulong)r.GetDecimal(7), r.GetInt64(8), r.GetFieldValue<DateTimeOffset>(9), r.GetFieldValue<DateTimeOffset>(10)), ct, characterId.Value);

    public async Task<bool> RemoveQueueAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).ExecuteAsync("DELETE FROM crafting.queue WHERE id = $1", ct, id) == 1;

    public Task AddAsync(IUnitOfWork uow, CraftLogEntry e, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO crafting.log (id, character_id, account_id, kind, subject, attempts, successes, chance_permille, seed, stones_spent, from_level, to_level, rules_version, occurred_at) " +
            "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14)",
            ct, e.Id, e.CharacterId.Value, e.AccountId.Value, e.Kind, e.Subject, e.Attempts, e.Successes, e.ChancePermille, (decimal)e.Seed, e.StonesSpent,
            e.FromLevel, e.ToLevel, e.RulesVersion, e.At);

    public async Task<IReadOnlyList<CraftLogEntry>> ListAsync(IUnitOfWork uow, CharacterId characterId, Guid? before, int limit, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).QueryAsync(
            "SELECT id, account_id, kind, subject, attempts, successes, chance_permille, seed, stones_spent, from_level, to_level, rules_version, occurred_at " +
            "FROM crafting.log WHERE character_id = $1 AND ($2::uuid IS NULL OR id < $2::uuid) ORDER BY id DESC LIMIT $3",
            r => new CraftLogEntry(r.GetGuid(0), characterId, new AccountId(r.GetGuid(1)), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6),
                (ulong)r.GetDecimal(7), r.GetInt64(8), r.IsDBNull(9) ? null : r.GetInt32(9), r.IsDBNull(10) ? null : r.GetInt32(10), r.GetString(11),
                r.GetFieldValue<DateTimeOffset>(12)),
            ct, characterId.Value, before, limit);

    public async Task<IReadOnlyList<(string Kind, string Subject, long Attempts, long Successes)>> TotalsAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).QueryAsync(
            "SELECT kind, subject, sum(attempts)::bigint, sum(successes)::bigint FROM crafting.log WHERE character_id = $1 GROUP BY kind, subject",
            r => (r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3)), ct, characterId.Value);
}
