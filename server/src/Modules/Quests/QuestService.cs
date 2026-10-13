using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.Modules.Quests.Domain;

namespace JianghuGo.Modules.Quests.Application;

public sealed record QuestView(string Id, string NamePl, string DescriptionPl, string Metric, long Target, long Progress, long Stones, bool Completed, bool Claimed, string? TitlePl,
    IReadOnlyDictionary<string, int>? Bonus = null);

public sealed record CheckinView(int Streak, int BestStreak, bool CheckedInToday, long NextReward);

public sealed record QuestBoard(string Day, IReadOnlyList<QuestView> Daily, IReadOnlyList<QuestView> Achievements, CheckinView Checkin, IReadOnlyList<string> Titles,
    IReadOnlyDictionary<string, int>? TitleBonuses = null);

public sealed record ClaimResult(string QuestId, long Stones, long Balance, QuestBoard Board);

public sealed record CheckinResult(int Streak, long Stones, long Balance, QuestBoard Board);

public sealed record LeaderEntry(int Rank, Guid CharacterId, string Name, string Path, long Value);

public sealed class QuestService(
    IUnitOfWorkFactory uowFactory,
    IQuestRepository repo,
    ICharacterDirectory characters,
    IInventory inventory,
    IAuditLog audit,
    ICorrelationContext correlation,
    QuestsConfig config,
    TimeProvider clock)
{
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");

    public Task<Result<QuestBoard>> GetAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<QuestBoard>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            return await BoardAsync(uow, c.Id, clock.GetUtcNow(), ct);
        }, ct);

    /// <summary>Pays a completed quest once (claim row + ledger reference are both unique; a retry is a conflict).</summary>
    public Task<Result<ClaimResult>> ClaimAsync(AccountId accountId, Guid characterId, string questId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<ClaimResult>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var now = clock.GetUtcNow();
            var board = await BoardAsync(uow, c.Id, now, ct);
            var quest = board.Daily.Concat(board.Achievements).FirstOrDefault(q => q.Id == questId);
            if (quest is null) return Error.NotFound("quest_not_found", "This quest is not available today.");
            if (quest.Claimed) return Error.Conflict("quest_already_claimed", "Reward already claimed.");
            if (!quest.Completed) return Error.Conflict("quest_not_completed", "The quest is not completed yet.");
            var period = board.Achievements.Contains(quest) ? QuestRules.Life : board.Day;
            if (!await repo.TryClaimAsync(uow, c.Id, period, questId, now, ct)) return Error.Conflict("quest_already_claimed", "Reward already claimed.");
            var balance = await inventory.BalanceAsync(uow, c.Id, ct);
            if (quest.Stones > 0)
            {
                var credited = await inventory.CreditAsync(uow, c.Id, quest.Stones, "quest", period + ":" + questId, ct);
                if (!credited.IsSuccess) return credited.Error!;
                balance = credited.Value;
            }

            await AuditAsync(uow, accountId, "quests.claimed", characterId, $"{{\"quest\":\"{questId}\",\"period\":\"{period}\",\"stones\":{quest.Stones}}}", now, ct);
            var after = await BoardAsync(uow, c.Id, now, ct);
            await uow.CommitAsync(ct);
            return new ClaimResult(questId, quest.Stones, balance, after);
        }, ct);

    /// <summary>Daily check-in: once per world day; consecutive days grow the streak and its reward.</summary>
    public Task<Result<CheckinResult>> CheckinAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<CheckinResult>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var now = clock.GetUtcNow();
            var today = QuestRules.WorldDate(config, now);
            var current = await repo.FindCheckinAsync(uow, c.Id, ct);
            if (current?.LastDay == today) return Error.Conflict("already_checked_in", "Already checked in today.");
            var (streak, best) = QuestRules.NextStreak(current, today);
            await repo.SaveCheckinAsync(uow, new CheckinState(c.Id, today, streak, best, now), ct);
            var stones = QuestRules.CheckinStones(config, streak);
            var credited = await inventory.CreditAsync(uow, c.Id, stones, "checkin", QuestRules.Period(today), ct);
            if (!credited.IsSuccess) return credited.Error!;
            await repo.AddProgressAsync(uow, c.Id, QuestRules.Period(today), "m_" + Metrics.Checkins, 1, ct);
            await repo.AddProgressAsync(uow, c.Id, QuestRules.Life, "m_" + Metrics.Checkins, 1, ct);
            await AuditAsync(uow, accountId, "quests.checked_in", characterId, $"{{\"streak\":{streak},\"stones\":{stones}}}", now, ct);
            var after = await BoardAsync(uow, c.Id, now, ct);
            await uow.CommitAsync(ct);
            return new CheckinResult(streak, stones, credited.Value, after);
        }, ct);

    /// <summary>Lifetime leaderboard of one metric (e.g. pvp_won, bosses_won).</summary>
    public Task<Result<IReadOnlyList<LeaderEntry>>> LeadersAsync(AccountId accountId, Guid characterId, string metric, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<LeaderEntry>>>(async uow =>
        {
            if (!Metrics.All.Contains(metric)) return Error.Validation("unknown_metric", "Unknown metric.");
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var list = new List<LeaderEntry>();
            foreach (var (id, value) in await repo.TopLifeAsync(uow, "m_" + metric, 50, ct))
            {
                var who = await characters.FindAsync(uow, id.Value, ct);
                if (who is not null) list.Add(new LeaderEntry(list.Count + 1, id.Value, who.Name, who.Path, value));
            }

            return Result<IReadOnlyList<LeaderEntry>>.Success(list);
        }, ct);

    private async Task<QuestBoard> BoardAsync(IUnitOfWork uow, CharacterId id, DateTimeOffset now, CancellationToken ct)
    {
        var today = QuestRules.WorldDate(config, now);
        var day = QuestRules.Period(today);
        var todayRows = (await repo.ListAsync(uow, id, day, ct)).ToDictionary(r => r.Key);
        var lifeRows = (await repo.ListAsync(uow, id, QuestRules.Life, ct)).ToDictionary(r => r.Key);

        QuestView View(QuestDefinition q, Dictionary<string, ProgressRow> rows)
        {
            var progress = rows.GetValueOrDefault("m_" + q.Metric)?.Progress ?? 0;
            var claimed = rows.ContainsKey("q_" + q.Id);
            return new QuestView(q.Id, q.NamePl, q.DescriptionPl, q.Metric, q.Target, Math.Min(progress, q.Target), q.Stones, progress >= q.Target, claimed, q.TitlePl, q.Bonus);
        }

        var daily = QuestRules.DailyFor(config, id.Value, today).Select(q => View(q, todayRows)).ToList();
        var achievements = config.Achievements.Select(q => View(q, lifeRows)).ToList();
        var checkin = await repo.FindCheckinAsync(uow, id, ct);
        var checkedToday = checkin?.LastDay == today;
        var nextStreak = checkedToday ? checkin!.Streak + 1 : QuestRules.NextStreak(checkin, today).Streak;
        var titles = achievements.Where(a => a.Claimed && a.TitlePl is not null).Select(a => a.TitlePl!).ToList();
        var titleBonuses = TitleBonuses.Sum(config, lifeRows.Keys);
        return new QuestBoard(day, daily, achievements,
            new CheckinView(checkedToday ? checkin!.Streak : checkin is not null && checkin.LastDay == today.AddDays(-1) ? checkin.Streak : 0, checkin?.BestStreak ?? 0, checkedToday,
                QuestRules.CheckinStones(config, nextStreak)), titles, titleBonuses);
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, Guid characterId, string json, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "character", characterId.ToString(), json, correlation.CorrelationId, now), ct);
}

/// <summary>Counts one event for one character exactly once (today's counter and the lifetime counter).</summary>
internal static class QuestCounter
{
    public static async Task CountAsync(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config, IUnitOfWork uow, OutboxMessage message,
        Guid characterId, CancellationToken ct, params (string Metric, long Amount)[] increments)
    {
        var items = increments.Where(i => i.Amount > 0).ToList();
        if (items.Count == 0) return;
        var c = await characters.FindAsync(uow, characterId, ct);
        if (c is null) return; // character deleted meanwhile
        if (!await repo.TryMarkCountedAsync(uow, c.Id, message.Id, message.OccurredAt, ct)) return;
        var day = QuestRules.Period(QuestRules.WorldDate(config, message.OccurredAt));
        foreach (var (metric, amount) in items)
        {
            await repo.AddProgressAsync(uow, c.Id, day, "m_" + metric, amount, ct);
            await repo.AddProgressAsync(uow, c.Id, QuestRules.Life, "m_" + metric, amount, ct);
        }
    }
}

/// <summary>Stat bonuses of claimed titles, summed. Combat adds them to the battle snapshot.</summary>
internal static class TitleBonuses
{
    public static IReadOnlyDictionary<string, int> Sum(QuestsConfig config, IEnumerable<string> lifeKeys)
    {
        var claimed = lifeKeys.Where(k => k.StartsWith("q_", StringComparison.Ordinal)).Select(k => k[2..]).ToHashSet();
        var sum = new Dictionary<string, int>();
        foreach (var a in config.Achievements.Where(a => a.Bonus is not null && claimed.Contains(a.Id)))
            foreach (var (stat, v) in a.Bonus!) sum[stat] = sum.GetValueOrDefault(stat) + v;
        return sum;
    }
}

internal sealed class TitleCombatBonuses(IQuestRepository repo, QuestsConfig config) : JianghuGo.Modules.Combat.Contracts.ICombatStatBonuses
{
    public async Task<IReadOnlyDictionary<string, int>> BonusesAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct) =>
        TitleBonuses.Sum(config, (await repo.ListAsync(uow, new CharacterId(characterId), QuestRules.Life, ct)).Select(r => r.Key));
}
