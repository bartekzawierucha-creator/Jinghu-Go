using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Arts.Application;
using JianghuGo.Modules.Arts.Domain;

namespace JianghuGo.Modules.Arts.Infrastructure;

internal sealed class InMemoryArtsRepository : IArtsRepository
{
    private const string Profiles = "arts.profiles";
    private const string Handled = "arts.handled_events";

    public Task<ArtsProfile?> FindAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<CharacterId, ArtsProfile>(Profiles).GetValueOrDefault(characterId));

    public Task<bool> TrySaveAsync(IUnitOfWork uow, ArtsProfile p, int expectedVersion, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<CharacterId, ArtsProfile>(Profiles);
        if ((t.GetValueOrDefault(p.CharacterId)?.Version ?? 0) != expectedVersion || p.Version != expectedVersion + 1) return Task.FromResult(false);
        u.SetTable(Profiles, t.SetItem(p.CharacterId, p));
        return Task.FromResult(true);
    }

    public Task<bool> TryMarkHandledAsync(IUnitOfWork uow, Guid guardId, CharacterId characterId, DateTimeOffset at, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<Guid, DateTimeOffset>(Handled);
        if (t.ContainsKey(guardId)) return Task.FromResult(false);
        u.SetTable(Handled, t.Add(guardId, at));
        return Task.FromResult(true);
    }
}

internal sealed class PostgresArtsRepository : IArtsRepository
{
    public Task<ArtsProfile?> FindAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync("SELECT data::text FROM arts.profiles WHERE character_id = $1",
            r => JsonSerializer.Deserialize<ArtsProfile>(r.GetString(0), Idempotency.Json), ct, characterId.Value);

    public async Task<bool> TrySaveAsync(IUnitOfWork uow, ArtsProfile p, int expectedVersion, CancellationToken ct)
    {
        if (p.Version != expectedVersion + 1) return false;
        var json = JsonSerializer.Serialize(p, Idempotency.Json);
        return expectedVersion == 0
            ? await PostgresUnitOfWork.From(uow).ExecuteAsync(
                "INSERT INTO arts.profiles (character_id, data, version, updated_at) VALUES ($1, $2::jsonb, $3, now()) ON CONFLICT (character_id) DO NOTHING",
                ct, p.CharacterId.Value, json, p.Version) == 1
            : await PostgresUnitOfWork.From(uow).ExecuteAsync(
                "UPDATE arts.profiles SET data = $2::jsonb, version = $3, updated_at = now() WHERE character_id = $1 AND version = $4",
                ct, p.CharacterId.Value, json, p.Version, expectedVersion) == 1;
    }

    public async Task<bool> TryMarkHandledAsync(IUnitOfWork uow, Guid guardId, CharacterId characterId, DateTimeOffset at, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO arts.handled_events (id, character_id, handled_at) VALUES ($1, $2, $3) ON CONFLICT DO NOTHING", ct, guardId, characterId.Value, at) == 1;
}
