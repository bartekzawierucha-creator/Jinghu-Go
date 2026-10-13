using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Quests.Domain;

namespace JianghuGo.Modules.Quests.Application;

public sealed record ProgressRow(string Key, long Progress, DateTimeOffset? ClaimedAt);

/// <summary>
/// Counters and claims share quests.progress: counter rows are keyed "m_&lt;metric&gt;", claim rows "q_&lt;questId&gt;".
/// </summary>
public interface IQuestRepository
{
    /// <summary>False when this event was already counted for the character (outbox redelivery).</summary>
    Task<bool> TryMarkCountedAsync(IUnitOfWork uow, CharacterId characterId, Guid messageId, DateTimeOffset at, CancellationToken ct);
    Task AddProgressAsync(IUnitOfWork uow, CharacterId characterId, string period, string key, long delta, CancellationToken ct);
    Task<IReadOnlyList<ProgressRow>> ListAsync(IUnitOfWork uow, CharacterId characterId, string period, CancellationToken ct);

    /// <summary>False if already claimed.</summary>
    Task<bool> TryClaimAsync(IUnitOfWork uow, CharacterId characterId, string period, string questId, DateTimeOffset at, CancellationToken ct);
    Task<CheckinState?> FindCheckinAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
    Task SaveCheckinAsync(IUnitOfWork uow, CheckinState state, CancellationToken ct);
    Task<IReadOnlyList<(CharacterId CharacterId, long Value)>> TopLifeAsync(IUnitOfWork uow, string key, int limit, CancellationToken ct);
}

internal sealed class InMemoryQuestRepository : IQuestRepository
{
    private const string Progress = "quests.progress";
    private const string Counted = "quests.counted_events";
    private const string Checkins = "quests.checkins";

    public Task<bool> TryMarkCountedAsync(IUnitOfWork uow, CharacterId characterId, Guid messageId, DateTimeOffset at, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<(CharacterId, Guid), DateTimeOffset>(Counted);
        if (t.ContainsKey((characterId, messageId))) return Task.FromResult(false);
        u.SetTable(Counted, t.Add((characterId, messageId), at));
        return Task.FromResult(true);
    }

    public Task AddProgressAsync(IUnitOfWork uow, CharacterId characterId, string period, string key, long delta, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<(CharacterId, string, string), ProgressRow>(Progress);
        var cur = t.GetValueOrDefault((characterId, period, key)) ?? new ProgressRow(key, 0, null);
        u.SetTable(Progress, t.SetItem((characterId, period, key), cur with { Progress = cur.Progress + delta }));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProgressRow>> ListAsync(IUnitOfWork uow, CharacterId characterId, string period, CancellationToken ct)
    {
        IReadOnlyList<ProgressRow> list = [.. InMemoryUnitOfWork.From(uow).Table<(CharacterId, string, string), ProgressRow>(Progress)
            .Where(kv => kv.Key.Item1 == characterId && kv.Key.Item2 == period).Select(kv => kv.Value)];
        return Task.FromResult(list);
    }

    public Task<bool> TryClaimAsync(IUnitOfWork uow, CharacterId characterId, string period, string questId, DateTimeOffset at, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<(CharacterId, string, string), ProgressRow>(Progress);
        var key = "q_" + questId;
        if (t.ContainsKey((characterId, period, key))) return Task.FromResult(false);
        u.SetTable(Progress, t.Add((characterId, period, key), new ProgressRow(key, 0, at)));
        return Task.FromResult(true);
    }

    public Task<CheckinState?> FindCheckinAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<CharacterId, CheckinState>(Checkins).GetValueOrDefault(characterId));

    public Task SaveCheckinAsync(IUnitOfWork uow, CheckinState state, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        u.SetTable(Checkins, u.Table<CharacterId, CheckinState>(Checkins).SetItem(state.CharacterId, state));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(CharacterId CharacterId, long Value)>> TopLifeAsync(IUnitOfWork uow, string key, int limit, CancellationToken ct)
    {
        IReadOnlyList<(CharacterId, long)> list = [.. InMemoryUnitOfWork.From(uow).Table<(CharacterId, string, string), ProgressRow>(Progress)
            .Where(kv => kv.Key.Item2 == QuestRules.Life && kv.Key.Item3 == key && kv.Value.Progress > 0)
            .OrderByDescending(kv => kv.Value.Progress).Take(limit).Select(kv => (kv.Key.Item1, kv.Value.Progress))];
        return Task.FromResult(list);
    }
}

internal sealed class PostgresQuestRepository : IQuestRepository
{
    public async Task<bool> TryMarkCountedAsync(IUnitOfWork uow, CharacterId characterId, Guid messageId, DateTimeOffset at, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO quests.counted_events (character_id, message_id, counted_at) VALUES ($1, $2, $3) ON CONFLICT DO NOTHING",
            ct, characterId.Value, messageId, at) == 1;

    public Task AddProgressAsync(IUnitOfWork uow, CharacterId characterId, string period, string key, long delta, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO quests.progress (character_id, period, quest_id, progress) VALUES ($1, $2, $3, $4) " +
            "ON CONFLICT (character_id, period, quest_id) DO UPDATE SET progress = quests.progress.progress + EXCLUDED.progress",
            ct, characterId.Value, period, key, delta);

    public async Task<IReadOnlyList<ProgressRow>> ListAsync(IUnitOfWork uow, CharacterId characterId, string period, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).QueryAsync(
            "SELECT quest_id, progress, claimed_at FROM quests.progress WHERE character_id = $1 AND period = $2",
            r => new ProgressRow(r.GetString(0), r.GetInt64(1), r.IsDBNull(2) ? null : r.GetFieldValue<DateTimeOffset>(2)), ct, characterId.Value, period);

    public async Task<bool> TryClaimAsync(IUnitOfWork uow, CharacterId characterId, string period, string questId, DateTimeOffset at, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO quests.progress (character_id, period, quest_id, progress, claimed_at) VALUES ($1, $2, $3, 0, $4) ON CONFLICT DO NOTHING",
            ct, characterId.Value, period, "q_" + questId, at) == 1;

    public Task<CheckinState?> FindCheckinAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync(
            "SELECT last_day, streak, best_streak, updated_at FROM quests.checkins WHERE character_id = $1",
            r => new CheckinState(characterId, r.GetFieldValue<DateOnly>(0), r.GetInt32(1), r.GetInt32(2), r.GetFieldValue<DateTimeOffset>(3)), ct, characterId.Value);

    public Task SaveCheckinAsync(IUnitOfWork uow, CheckinState s, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO quests.checkins (character_id, last_day, streak, best_streak, updated_at) VALUES ($1, $2, $3, $4, $5) " +
            "ON CONFLICT (character_id) DO UPDATE SET last_day = EXCLUDED.last_day, streak = EXCLUDED.streak, best_streak = EXCLUDED.best_streak, updated_at = EXCLUDED.updated_at",
            ct, s.CharacterId.Value, s.LastDay, s.Streak, s.BestStreak, s.UpdatedAt);

    public async Task<IReadOnlyList<(CharacterId CharacterId, long Value)>> TopLifeAsync(IUnitOfWork uow, string key, int limit, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).QueryAsync(
            "SELECT character_id, progress FROM quests.progress WHERE period = 'life' AND quest_id = $1 AND progress > 0 ORDER BY progress DESC, character_id LIMIT $2",
            r => (new CharacterId(r.GetGuid(0)), r.GetInt64(1)), ct, key, limit);
}
