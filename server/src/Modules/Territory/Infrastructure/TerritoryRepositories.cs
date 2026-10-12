using JianghuGo.Application;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Territory.Application;
using JianghuGo.Modules.Territory.Domain;
using Npgsql;

namespace JianghuGo.Modules.Territory.Infrastructure;

internal sealed class InMemoryTerritoryRepository : ITerritoryRepository
{
    private const string Territories = "territory.territories";
    private const string Wars = "territory.wars";
    private const string WarBattles = "territory.war_battles";
    private const string Changes = "territory.ownership_changes";

    private static InMemoryUnitOfWork U(IUnitOfWork uow) => InMemoryUnitOfWork.From(uow);

    public Task<Domain.Territory?> FindAsync(IUnitOfWork uow, string key, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<string, Domain.Territory>(Territories).GetValueOrDefault(key));

    public Task<bool> TrySaveAsync(IUnitOfWork uow, Domain.Territory territory, int expectedVersion, CancellationToken ct)
    {
        var t = U(uow).Table<string, Domain.Territory>(Territories);
        var current = t.GetValueOrDefault(territory.Key);
        if ((current?.Version ?? 0) != expectedVersion || territory.Version != expectedVersion + 1) return Task.FromResult(false);
        U(uow).SetTable(Territories, t.SetItem(territory.Key, territory));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Domain.Territory>> ListOwnedAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct)
    {
        IReadOnlyList<Domain.Territory> list = [.. U(uow).Table<string, Domain.Territory>(Territories).Values.Where(t => t.OwnerSectId == sectId).OrderBy(t => t.Key, StringComparer.Ordinal)];
        return Task.FromResult(list);
    }

    public Task AddWarAsync(IUnitOfWork uow, War war, CancellationToken ct)
    {
        var t = U(uow).Table<Guid, War>(Wars);
        if (t.Values.Any(w => w.TerritoryKey == war.TerritoryKey && w.Status == WarStatuses.Declared)) throw new DuplicateKeyException("War already declared.");
        U(uow).SetTable(Wars, t.Add(war.Id, war));
        return Task.CompletedTask;
    }

    public Task<War?> FindWarAsync(IUnitOfWork uow, Guid id, CancellationToken ct) => Task.FromResult(U(uow).Table<Guid, War>(Wars).GetValueOrDefault(id));

    public Task<War?> FindOpenWarAsync(IUnitOfWork uow, string territoryKey, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, War>(Wars).Values.FirstOrDefault(w => w.TerritoryKey == territoryKey && w.Status == WarStatuses.Declared));

    public Task<IReadOnlyList<War>> ListOpenWarsAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct)
    {
        IReadOnlyList<War> list = [.. U(uow).Table<Guid, War>(Wars).Values
            .Where(w => w.Status == WarStatuses.Declared && (w.AttackerSectId == sectId || w.DefenderSectId == sectId)).OrderBy(w => w.Id)];
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<War>> ListWarsAsync(IUnitOfWork uow, string territoryKey, int limit, CancellationToken ct)
    {
        IReadOnlyList<War> list = [.. U(uow).Table<Guid, War>(Wars).Values.Where(w => w.TerritoryKey == territoryKey).OrderByDescending(w => w.Id).Take(limit)];
        return Task.FromResult(list);
    }

    public Task<bool> TryUpdateWarAsync(IUnitOfWork uow, War war, int expectedVersion, CancellationToken ct)
    {
        var t = U(uow).Table<Guid, War>(Wars);
        if (!t.TryGetValue(war.Id, out var cur) || cur.Version != expectedVersion || war.Version != expectedVersion + 1 || cur.Status == WarStatuses.Resolved)
            return Task.FromResult(false);
        U(uow).SetTable(Wars, t.SetItem(war.Id, war));
        return Task.FromResult(true);
    }

    public Task<bool> TryRecordWarBattleAsync(IUnitOfWork uow, Guid warId, Guid battleId, Guid winnerSectId, DateTimeOffset at, CancellationToken ct)
    {
        var t = U(uow).Table<(Guid, Guid), Guid>(WarBattles);
        if (t.ContainsKey((warId, battleId))) return Task.FromResult(false);
        U(uow).SetTable(WarBattles, t.Add((warId, battleId), winnerSectId));
        return Task.FromResult(true);
    }

    public Task AddOwnershipChangeAsync(IUnitOfWork uow, OwnershipChange change, CancellationToken ct)
    {
        U(uow).SetTable(Changes, U(uow).Table<Guid, OwnershipChange>(Changes).Add(change.Id, change));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OwnershipChange>> ListOwnershipChangesAsync(IUnitOfWork uow, string territoryKey, int limit, CancellationToken ct)
    {
        IReadOnlyList<OwnershipChange> list = [.. U(uow).Table<Guid, OwnershipChange>(Changes).Values.Where(c => c.TerritoryKey == territoryKey).OrderByDescending(c => c.Id).Take(limit)];
        return Task.FromResult(list);
    }
}

internal sealed class PostgresTerritoryRepository : ITerritoryRepository
{
    private const string WarColumns =
        "id, territory_key, attacker_sect_id, defender_sect_id, status, declared_at, starts_at, ends_at, attacker_score, defender_score, winner_sect_id, resolved_at, version";

    private static PostgresUnitOfWork Pg(IUnitOfWork uow) => PostgresUnitOfWork.From(uow);

    private static Domain.Territory MapTerritory(NpgsqlDataReader r) => new(
        r.GetString(0), r.IsDBNull(1) ? null : r.GetGuid(1), r.IsDBNull(2) ? null : r.GetFieldValue<DateTimeOffset>(2),
        r.IsDBNull(3) ? null : r.GetFieldValue<DateTimeOffset>(3), r.GetInt32(4));

    private static War MapWar(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetString(1), r.GetGuid(2), r.GetGuid(3), r.GetString(4), r.GetFieldValue<DateTimeOffset>(5), r.GetFieldValue<DateTimeOffset>(6),
        r.GetFieldValue<DateTimeOffset>(7), r.GetInt32(8), r.GetInt32(9), r.IsDBNull(10) ? null : r.GetGuid(10),
        r.IsDBNull(11) ? null : r.GetFieldValue<DateTimeOffset>(11), r.GetInt32(12));

    public Task<Domain.Territory?> FindAsync(IUnitOfWork uow, string key, CancellationToken ct) =>
        Pg(uow).QuerySingleOrDefaultAsync("SELECT key, owner_sect_id, claimed_at, last_tribute_at, version FROM territory.territories WHERE key = $1", MapTerritory, ct, key);

    public async Task<bool> TrySaveAsync(IUnitOfWork uow, Domain.Territory t, int expectedVersion, CancellationToken ct)
    {
        if (t.Version != expectedVersion + 1) return false;
        if (expectedVersion == 0)
        {
            return await Pg(uow).ExecuteAsync(
                "INSERT INTO territory.territories (key, owner_sect_id, claimed_at, last_tribute_at, version) VALUES ($1, $2, $3, $4, $5) ON CONFLICT (key) DO NOTHING",
                ct, t.Key, t.OwnerSectId, t.ClaimedAt, t.LastTributeAt, t.Version) == 1;
        }

        return await Pg(uow).ExecuteAsync(
            "UPDATE territory.territories SET owner_sect_id = $2, claimed_at = $3, last_tribute_at = $4, version = $5 WHERE key = $1 AND version = $6",
            ct, t.Key, t.OwnerSectId, t.ClaimedAt, t.LastTributeAt, t.Version, expectedVersion) == 1;
    }

    public async Task<IReadOnlyList<Domain.Territory>> ListOwnedAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct) =>
        await Pg(uow).QueryAsync("SELECT key, owner_sect_id, claimed_at, last_tribute_at, version FROM territory.territories WHERE owner_sect_id = $1 ORDER BY key",
            MapTerritory, ct, sectId);

    public Task AddWarAsync(IUnitOfWork uow, War w, CancellationToken ct) =>
        Pg(uow).ExecuteAsync($"INSERT INTO territory.wars ({WarColumns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)",
            ct, w.Id, w.TerritoryKey, w.AttackerSectId, w.DefenderSectId, w.Status, w.DeclaredAt, w.StartsAt, w.EndsAt, w.AttackerScore, w.DefenderScore,
            w.WinnerSectId, w.ResolvedAt, w.Version);

    public Task<War?> FindWarAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        Pg(uow).QuerySingleOrDefaultAsync($"SELECT {WarColumns} FROM territory.wars WHERE id = $1", MapWar, ct, id);

    public Task<War?> FindOpenWarAsync(IUnitOfWork uow, string territoryKey, CancellationToken ct) =>
        Pg(uow).QuerySingleOrDefaultAsync($"SELECT {WarColumns} FROM territory.wars WHERE territory_key = $1 AND status = 'declared'", MapWar, ct, territoryKey);

    public async Task<IReadOnlyList<War>> ListOpenWarsAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct) =>
        await Pg(uow).QueryAsync($"SELECT {WarColumns} FROM territory.wars WHERE status = 'declared' AND (attacker_sect_id = $1 OR defender_sect_id = $1) ORDER BY id",
            MapWar, ct, sectId);

    public async Task<IReadOnlyList<War>> ListWarsAsync(IUnitOfWork uow, string territoryKey, int limit, CancellationToken ct) =>
        await Pg(uow).QueryAsync($"SELECT {WarColumns} FROM territory.wars WHERE territory_key = $1 ORDER BY id DESC LIMIT $2", MapWar, ct, territoryKey, limit);

    public async Task<bool> TryUpdateWarAsync(IUnitOfWork uow, War w, int expectedVersion, CancellationToken ct) =>
        w.Version == expectedVersion + 1 && await Pg(uow).ExecuteAsync(
            "UPDATE territory.wars SET status = $2, attacker_score = $3, defender_score = $4, winner_sect_id = $5, resolved_at = $6, version = $7 " +
            "WHERE id = $1 AND version = $8 AND status = 'declared'",
            ct, w.Id, w.Status, w.AttackerScore, w.DefenderScore, w.WinnerSectId, w.ResolvedAt, w.Version, expectedVersion) == 1;

    public async Task<bool> TryRecordWarBattleAsync(IUnitOfWork uow, Guid warId, Guid battleId, Guid winnerSectId, DateTimeOffset at, CancellationToken ct) =>
        await Pg(uow).ExecuteAsync(
            "INSERT INTO territory.war_battles (war_id, battle_id, winner_sect_id, counted_at) VALUES ($1, $2, $3, $4) ON CONFLICT DO NOTHING",
            ct, warId, battleId, winnerSectId, at) == 1;

    public Task AddOwnershipChangeAsync(IUnitOfWork uow, OwnershipChange c, CancellationToken ct) =>
        Pg(uow).ExecuteAsync(
            "INSERT INTO territory.ownership_changes (id, territory_key, from_sect_id, to_sect_id, reason, war_id, changed_at) VALUES ($1, $2, $3, $4, $5, $6, $7)",
            ct, c.Id, c.TerritoryKey, c.FromSectId, c.ToSectId, c.Reason, c.WarId, c.At);

    public async Task<IReadOnlyList<OwnershipChange>> ListOwnershipChangesAsync(IUnitOfWork uow, string territoryKey, int limit, CancellationToken ct) =>
        await Pg(uow).QueryAsync(
            "SELECT id, from_sect_id, to_sect_id, reason, war_id, changed_at FROM territory.ownership_changes WHERE territory_key = $1 ORDER BY id DESC LIMIT $2",
            r => new OwnershipChange(r.GetGuid(0), territoryKey, r.IsDBNull(1) ? null : r.GetGuid(1), r.IsDBNull(2) ? null : r.GetGuid(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetGuid(4), r.GetFieldValue<DateTimeOffset>(5)),
            ct, territoryKey, limit);
}
