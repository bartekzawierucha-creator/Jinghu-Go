using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Sects.Application;
using JianghuGo.Modules.Sects.Domain;

namespace JianghuGo.Modules.Sects.Infrastructure;

internal sealed class InMemorySectDevelopmentRepository : ISectDevelopmentRepository
{
    private const string Table = "sects.development";
    private const string Handled = "sects.handled_events";

    public Task<SectDevelopment?> FindAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<Guid, SectDevelopment>(Table).GetValueOrDefault(sectId));

    public Task<bool> TrySaveAsync(IUnitOfWork uow, SectDevelopment d, int expectedVersion, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<Guid, SectDevelopment>(Table);
        if ((t.GetValueOrDefault(d.SectId)?.Version ?? 0) != expectedVersion || d.Version != expectedVersion + 1) return Task.FromResult(false);
        u.SetTable(Table, t.SetItem(d.SectId, d));
        return Task.FromResult(true);
    }

    public Task<bool> TryMarkHandledAsync(IUnitOfWork uow, Guid guardId, DateTimeOffset at, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<Guid, DateTimeOffset>(Handled);
        if (t.ContainsKey(guardId)) return Task.FromResult(false);
        u.SetTable(Handled, t.Add(guardId, at));
        return Task.FromResult(true);
    }
}

internal sealed class PostgresSectDevelopmentRepository : ISectDevelopmentRepository
{
    public Task<SectDevelopment?> FindAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync("SELECT data::text FROM sects.development WHERE sect_id = $1",
            r => JsonSerializer.Deserialize<SectDevelopment>(r.GetString(0), Idempotency.Json), ct, sectId);

    public async Task<bool> TrySaveAsync(IUnitOfWork uow, SectDevelopment d, int expectedVersion, CancellationToken ct)
    {
        if (d.Version != expectedVersion + 1) return false;
        var json = JsonSerializer.Serialize(d, Idempotency.Json);
        return expectedVersion == 0
            ? await PostgresUnitOfWork.From(uow).ExecuteAsync(
                "INSERT INTO sects.development (sect_id, data, version, updated_at) VALUES ($1, $2::jsonb, $3, now()) ON CONFLICT (sect_id) DO NOTHING",
                ct, d.SectId, json, d.Version) == 1
            : await PostgresUnitOfWork.From(uow).ExecuteAsync(
                "UPDATE sects.development SET data = $2::jsonb, version = $3, updated_at = now() WHERE sect_id = $1 AND version = $4",
                ct, d.SectId, json, d.Version, expectedVersion) == 1;
    }

    public async Task<bool> TryMarkHandledAsync(IUnitOfWork uow, Guid guardId, DateTimeOffset at, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).ExecuteAsync("INSERT INTO sects.handled_events (id, handled_at) VALUES ($1, $2) ON CONFLICT DO NOTHING", ct, guardId, at) == 1;
}
