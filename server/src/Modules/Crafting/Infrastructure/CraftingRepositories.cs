using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Crafting.Application;

namespace JianghuGo.Modules.Crafting.Infrastructure;

internal sealed class InMemoryCraftingRepository : ICraftingRepository
{
    private const string Log = "crafting.log";

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
