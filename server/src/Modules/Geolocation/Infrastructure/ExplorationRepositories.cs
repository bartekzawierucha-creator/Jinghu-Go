using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Geolocation.Application;
using JianghuGo.Modules.Geolocation.Domain;
using Npgsql;

namespace JianghuGo.Modules.Geolocation.Infrastructure;

internal sealed class InMemoryExplorationRepository : IExplorationRepository
{
    private const string Sessions = "geolocation.sessions";

    public Task<ExplorationSession?> FindActiveAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<Guid, ExplorationSession>(Sessions).Values
            .FirstOrDefault(s => s.CharacterId == characterId && s.Status == SessionStatuses.Active));

    public Task AddAsync(IUnitOfWork uow, ExplorationSession session, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<Guid, ExplorationSession>(Sessions);
        if (t.Values.Any(s => s.CharacterId == session.CharacterId && s.Status == SessionStatuses.Active))
            throw new DuplicateKeyException("Character already has an active session.");
        u.SetTable(Sessions, t.Add(session.Id, session));
        return Task.CompletedTask;
    }

    public Task<bool> TryUpdateAsync(IUnitOfWork uow, ExplorationSession session, int expectedVersion, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<Guid, ExplorationSession>(Sessions);
        if (!t.TryGetValue(session.Id, out var cur) || cur.Version != expectedVersion) return Task.FromResult(false);
        u.SetTable(Sessions, t.SetItem(session.Id, session));
        return Task.FromResult(true);
    }
}

/// <summary>Last position stored as PostGIS geography (SRID 4326) — the only location the server keeps.</summary>
internal sealed class PostgresExplorationRepository : IExplorationRepository
{
    private const string Columns =
        "id, character_id, account_id, status, started_at, last_seen_at, ended_at, ST_Y(last_position::geometry), ST_X(last_position::geometry), last_accuracy_m, " +
        "last_captured_at, last_received_at, last_speed_mps, relocation_until, distance_m, accepted_samples, dropped_samples, anomalies, flagged, rate_window_start, rate_window_count, version";

    private static ExplorationSession Map(NpgsqlDataReader r) => new(
        r.GetGuid(0), new CharacterId(r.GetGuid(1)), new AccountId(r.GetGuid(2)), r.GetString(3), r.GetFieldValue<DateTimeOffset>(4), r.GetFieldValue<DateTimeOffset>(5),
        r.IsDBNull(6) ? null : r.GetFieldValue<DateTimeOffset>(6),
        r.IsDBNull(7) ? null : new GeoPoint(r.GetDouble(7), r.GetDouble(8)),
        r.IsDBNull(9) ? null : r.GetDouble(9),
        r.IsDBNull(10) ? null : r.GetFieldValue<DateTimeOffset>(10), r.IsDBNull(11) ? null : r.GetFieldValue<DateTimeOffset>(11),
        r.GetDouble(12), r.IsDBNull(13) ? null : r.GetFieldValue<DateTimeOffset>(13), r.GetDouble(14), r.GetInt32(15), r.GetInt32(16), r.GetInt32(17), r.GetBoolean(18),
        r.GetFieldValue<DateTimeOffset>(19), r.GetInt32(20), r.GetInt32(21));

    public Task<ExplorationSession?> FindActiveAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync($"SELECT {Columns} FROM geolocation.sessions WHERE character_id = $1 AND status = 'active'", Map, ct, characterId.Value);

    public Task AddAsync(IUnitOfWork uow, ExplorationSession s, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO geolocation.sessions (id, character_id, account_id, status, started_at, last_seen_at, rate_window_start, version) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)",
            ct, s.Id, s.CharacterId.Value, s.AccountId.Value, s.Status, s.StartedAt, s.LastSeenAt, s.RateWindowStart, s.Version);

    public async Task<bool> TryUpdateAsync(IUnitOfWork uow, ExplorationSession s, int expectedVersion, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).ExecuteAsync(
            "UPDATE geolocation.sessions SET status = $2, last_seen_at = $3, ended_at = $4, " +
            "last_position = CASE WHEN $5::float8 IS NULL THEN NULL ELSE ST_SetSRID(ST_MakePoint($6::float8, $5::float8), 4326)::geography END, " +
            "last_accuracy_m = $7, last_captured_at = $8, last_received_at = $9, last_speed_mps = $10, relocation_until = $11, distance_m = $12, " +
            "accepted_samples = $13, dropped_samples = $14, anomalies = $15, flagged = $16, rate_window_start = $17, rate_window_count = $18, version = $19 " +
            "WHERE id = $1 AND version = $20",
            ct, s.Id, s.Status, s.LastSeenAt, s.EndedAt, s.LastPosition?.Lat, s.LastPosition?.Lon, s.LastAccuracyM, s.LastCapturedAt, s.LastReceivedAt,
            s.LastSpeedMps, s.RelocationUntil, s.DistanceM, s.AcceptedSamples, s.DroppedSamples, s.Anomalies, s.Flagged, s.RateWindowStart, s.RateWindowCount,
            s.Version, expectedVersion) == 1;
}
