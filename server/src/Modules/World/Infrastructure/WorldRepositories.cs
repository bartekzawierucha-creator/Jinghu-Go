using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.World.Application;

namespace JianghuGo.Modules.World.Infrastructure;

internal sealed record Claim(string NodeKey, CharacterId CharacterId, AccountId AccountId, string Kind, DateTimeOffset ClaimedAt);

internal sealed class InMemoryWorldRepository : IWorldRepository
{
    private const string Claims = "world.node_claims";
    private const string Attunements = "world.flame_attunements";

    public Task<bool> TryClaimAsync(IUnitOfWork uow, string nodeKey, CharacterId characterId, AccountId accountId, string kind, DateTimeOffset at, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<(string, CharacterId), Claim>(Claims);
        if (t.ContainsKey((nodeKey, characterId))) return Task.FromResult(false);
        u.SetTable(Claims, t.Add((nodeKey, characterId), new Claim(nodeKey, characterId, accountId, kind, at)));
        return Task.FromResult(true);
    }

    public Task<int> CountClaimsSinceAsync(IUnitOfWork uow, CharacterId characterId, DateTimeOffset since, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<(string, CharacterId), Claim>(Claims).Values.Count(c => c.CharacterId == characterId && c.ClaimedAt >= since));

    public Task<IReadOnlySet<string>> ClaimedAmongAsync(IUnitOfWork uow, CharacterId characterId, IReadOnlyCollection<string> nodeKeys, CancellationToken ct)
    {
        var t = InMemoryUnitOfWork.From(uow).Table<(string, CharacterId), Claim>(Claims);
        IReadOnlySet<string> set = nodeKeys.Where(k => t.ContainsKey((k, characterId))).ToHashSet();
        return Task.FromResult(set);
    }

    public Task<FlameAttunement?> FindAttunementAsync(IUnitOfWork uow, CharacterId characterId, string siteId, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<(CharacterId, string), FlameAttunement>(Attunements).GetValueOrDefault((characterId, siteId)));

    public Task SaveAttunementAsync(IUnitOfWork uow, FlameAttunement a, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        u.SetTable(Attunements, u.Table<(CharacterId, string), FlameAttunement>(Attunements).SetItem((a.CharacterId, a.SiteId), a));
        return Task.CompletedTask;
    }
}

internal sealed class PostgresWorldRepository : IWorldRepository
{
    public async Task<bool> TryClaimAsync(IUnitOfWork uow, string nodeKey, CharacterId characterId, AccountId accountId, string kind, DateTimeOffset at, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO world.node_claims (node_key, character_id, account_id, kind, claimed_at) VALUES ($1, $2, $3, $4, $5) ON CONFLICT DO NOTHING",
            ct, nodeKey, characterId.Value, accountId.Value, kind, at) == 1;

    public async Task<int> CountClaimsSinceAsync(IUnitOfWork uow, CharacterId characterId, DateTimeOffset since, CancellationToken ct) =>
        (int)(await PostgresUnitOfWork.From(uow).QueryAsync("SELECT count(*) FROM world.node_claims WHERE character_id = $1 AND claimed_at >= $2",
            r => r.GetInt64(0), ct, characterId.Value, since)).Single();

    public async Task<IReadOnlySet<string>> ClaimedAmongAsync(IUnitOfWork uow, CharacterId characterId, IReadOnlyCollection<string> nodeKeys, CancellationToken ct) =>
        nodeKeys.Count == 0
            ? new HashSet<string>()
            : (await PostgresUnitOfWork.From(uow).QueryAsync("SELECT node_key FROM world.node_claims WHERE character_id = $1 AND node_key = ANY($2)",
                r => r.GetString(0), ct, characterId.Value, nodeKeys.ToArray())).ToHashSet();

    public Task<FlameAttunement?> FindAttunementAsync(IUnitOfWork uow, CharacterId characterId, string siteId, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync(
            "SELECT last_attuned_at, count FROM world.flame_attunements WHERE character_id = $1 AND site_id = $2",
            r => new FlameAttunement(characterId, siteId, r.GetFieldValue<DateTimeOffset>(0), r.GetInt32(1)), ct, characterId.Value, siteId);

    public Task SaveAttunementAsync(IUnitOfWork uow, FlameAttunement a, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO world.flame_attunements (character_id, site_id, last_attuned_at, count) VALUES ($1, $2, $3, $4) " +
            "ON CONFLICT (character_id, site_id) DO UPDATE SET last_attuned_at = EXCLUDED.last_attuned_at, count = EXCLUDED.count",
            ct, a.CharacterId.Value, a.SiteId, a.LastAttunedAt, a.Count);
}
