using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Estate.Application;
using JianghuGo.Modules.Estate.Domain;

namespace JianghuGo.Modules.Estate.Infrastructure;

internal sealed class InMemoryEstateRepository : IEstateRepository
{
    private const string Profiles = "estate.profiles";

    public Task<EstateProfile?> FindAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<CharacterId, EstateProfile>(Profiles).GetValueOrDefault(characterId));

    public Task<bool> TrySaveAsync(IUnitOfWork uow, EstateProfile p, int expectedVersion, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<CharacterId, EstateProfile>(Profiles);
        if ((t.GetValueOrDefault(p.CharacterId)?.Version ?? 0) != expectedVersion || p.Version != expectedVersion + 1) return Task.FromResult(false);
        u.SetTable(Profiles, t.SetItem(p.CharacterId, p));
        return Task.FromResult(true);
    }
}

internal sealed class PostgresEstateRepository : IEstateRepository
{
    public Task<EstateProfile?> FindAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync("SELECT data::text FROM estate.profiles WHERE character_id = $1",
            r => JsonSerializer.Deserialize<EstateProfile>(r.GetString(0), Idempotency.Json), ct, characterId.Value);

    public async Task<bool> TrySaveAsync(IUnitOfWork uow, EstateProfile p, int expectedVersion, CancellationToken ct)
    {
        if (p.Version != expectedVersion + 1) return false;
        var json = JsonSerializer.Serialize(p, Idempotency.Json);
        return expectedVersion == 0
            ? await PostgresUnitOfWork.From(uow).ExecuteAsync(
                "INSERT INTO estate.profiles (character_id, data, version, updated_at) VALUES ($1, $2::jsonb, $3, now()) ON CONFLICT (character_id) DO NOTHING",
                ct, p.CharacterId.Value, json, p.Version) == 1
            : await PostgresUnitOfWork.From(uow).ExecuteAsync(
                "UPDATE estate.profiles SET data = $2::jsonb, version = $3, updated_at = now() WHERE character_id = $1 AND version = $4",
                ct, p.CharacterId.Value, json, p.Version, expectedVersion) == 1;
    }
}
