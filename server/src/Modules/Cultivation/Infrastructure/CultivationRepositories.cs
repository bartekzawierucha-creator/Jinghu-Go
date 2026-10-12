using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Cultivation.Application;
using JianghuGo.Modules.Cultivation.Domain;
using Npgsql;

namespace JianghuGo.Modules.Cultivation.Infrastructure;

internal sealed class InMemoryCultivationRepository : ICultivationRepository
{
    private const string Rewards = "cultivation.reward_grants";
    private const string States = "cultivation.states";
    private const string Attempts = "cultivation.breakthroughs";

    public Task<CultivationState?> FindAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<CharacterId, CultivationState>(States).GetValueOrDefault(characterId));

    public Task<bool> TrySaveAsync(IUnitOfWork uow, CultivationState state, int expectedVersion, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<CharacterId, CultivationState>(States);
        var current = t.GetValueOrDefault(state.CharacterId);
        if ((current?.Version ?? 0) != expectedVersion || state.Version != expectedVersion + 1 || state.Qi < 0)
        {
            return Task.FromResult(false);
        }

        u.SetTable(States, t.SetItem(state.CharacterId, state));
        return Task.FromResult(true);
    }

    public Task AddBreakthroughAsync(IUnitOfWork uow, BreakthroughAttempt attempt, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        u.SetTable(Attempts, u.Table<Guid, BreakthroughAttempt>(Attempts).Add(attempt.Id, attempt));
        return Task.CompletedTask;
    }

    public Task<bool> TryRecordRewardAsync(IUnitOfWork uow, Guid sourceId, CharacterId characterId, long qi, DateTimeOffset at, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<(Guid, CharacterId), long>(Rewards);
        if (t.ContainsKey((sourceId, characterId))) return Task.FromResult(false);
        u.SetTable(Rewards, t.Add((sourceId, characterId), qi));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<BreakthroughAttempt>> ListBreakthroughsAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        IReadOnlyList<BreakthroughAttempt> list = [.. InMemoryUnitOfWork.From(uow).Table<Guid, BreakthroughAttempt>(Attempts).Values
            .Where(a => a.CharacterId == characterId).OrderBy(a => a.Id)];
        return Task.FromResult(list);
    }
}

internal sealed class PostgresCultivationRepository : ICultivationRepository
{
    private const string Columns =
        "character_id, account_id, path, realm_index, stage, qi, version, deviation_until, meditation_started_at, daily_date, daily_meditation_qi, daily_steps, last_steps_report_at, updated_at";

    private static CultivationState Map(NpgsqlDataReader r) => new(
        new CharacterId(r.GetGuid(0)), new AccountId(r.GetGuid(1)), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetInt64(5), r.GetInt32(6),
        r.IsDBNull(7) ? null : r.GetFieldValue<DateTimeOffset>(7), r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8),
        r.GetFieldValue<DateOnly>(9), r.GetInt64(10), r.GetInt32(11), r.IsDBNull(12) ? null : r.GetFieldValue<DateTimeOffset>(12), r.GetFieldValue<DateTimeOffset>(13));

    public Task<CultivationState?> FindAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync($"SELECT {Columns} FROM cultivation.states WHERE character_id = $1", Map, ct, characterId.Value);

    public async Task<bool> TrySaveAsync(IUnitOfWork uow, CultivationState s, int expectedVersion, CancellationToken ct)
    {
        if (s.Version != expectedVersion + 1) return false;
        var pg = PostgresUnitOfWork.From(uow);
        object?[] args = [s.CharacterId.Value, s.AccountId.Value, s.Path, s.RealmIndex, s.Stage, s.Qi, s.Version, s.DeviationUntil, s.MeditationStartedAt,
            s.DailyDate, s.DailyMeditationQi, s.DailySteps, s.LastStepsReportAt, s.UpdatedAt, expectedVersion];
        if (expectedVersion == 0)
        {
            try
            {
                return await pg.ExecuteAsync(
                    $"INSERT INTO cultivation.states ({Columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14) " +
                    "ON CONFLICT (character_id) DO NOTHING", ct, args[..14]) == 1;
            }
            catch (DuplicateKeyException)
            {
                return false;
            }
        }

        return await pg.ExecuteAsync(
            "UPDATE cultivation.states SET realm_index = $4, stage = $5, qi = $6, version = $7, deviation_until = $8, meditation_started_at = $9, " +
            "daily_date = $10, daily_meditation_qi = $11, daily_steps = $12, last_steps_report_at = $13, updated_at = $14 " +
            "WHERE character_id = $1 AND account_id = $2 AND path = $3 AND version = $15", ct, args) == 1;
    }

    public async Task<bool> TryRecordRewardAsync(IUnitOfWork uow, Guid sourceId, CharacterId characterId, long qi, DateTimeOffset at, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO cultivation.reward_grants (source_id, character_id, qi, granted_at) VALUES ($1, $2, $3, $4) ON CONFLICT DO NOTHING",
            ct, sourceId, characterId.Value, qi, at) == 1;

    public Task AddBreakthroughAsync(IUnitOfWork uow, BreakthroughAttempt a, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO cultivation.breakthroughs (id, character_id, account_id, from_realm, from_stage, to_realm, to_stage, success, chance_ppm, roll, seed, qi_spent, rules_version, occurred_at) " +
            "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14)",
            ct, a.Id, a.CharacterId.Value, a.AccountId.Value, a.FromRealm, a.FromStage, a.ToRealm, a.ToStage, a.Success, a.ChancePpm, a.Roll,
            (decimal)a.Seed, a.QiSpent, a.RulesVersion, a.OccurredAt);

    public async Task<IReadOnlyList<BreakthroughAttempt>> ListBreakthroughsAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).QueryAsync(
            "SELECT id, account_id, from_realm, from_stage, to_realm, to_stage, success, chance_ppm, roll, seed, qi_spent, rules_version, occurred_at " +
            "FROM cultivation.breakthroughs WHERE character_id = $1 ORDER BY id",
            r => new BreakthroughAttempt(r.GetGuid(0), characterId, new AccountId(r.GetGuid(1)), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5),
                r.GetBoolean(6), r.GetInt32(7), r.GetInt32(8), (ulong)r.GetDecimal(9), r.GetInt64(10), r.GetString(11), r.GetFieldValue<DateTimeOffset>(12)),
            ct, characterId.Value);
}
