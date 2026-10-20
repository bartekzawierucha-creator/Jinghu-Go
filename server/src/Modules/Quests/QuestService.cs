using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.Modules.Quests.Domain;

namespace JianghuGo.Modules.Quests.Application;

public sealed record QuestView(string Id, string NamePl, string DescriptionPl, string Metric, long Target, long Progress, long Stones, bool Completed, bool Claimed, string? TitlePl,
    IReadOnlyDictionary<string, int>? Bonus = null, bool Secret = false, string? RiddlePl = null, string Rarity = "common", string? Until = null);

public sealed record StoryView(int Chapter, int Chapters, string Id, string TitlePl, string TextPl, string GoalPl, string Metric, long Target, long Progress, long Stones,
    string? Item, string? ItemNamePl, int ItemQuantity, bool Completed, bool Finished);

public sealed record CodexEntry(string NpcId, long Victories);

public sealed record ChallengeTierView(int Tier, long Threshold, long Stones, bool Reached, bool Claimed);

/// <summary>This week's world event: a personal goal and the community challenge everyone pushes together (1.9).</summary>
public sealed record WeeklyView(string Id, string NamePl, string DescriptionPl, string Metric, string Until, QuestView Personal, long CommunityTotal, long CommunityTarget,
    long MyContribution, long MinContribution, IReadOnlyList<ChallengeTierView> Tiers);

public sealed record PassLevelView(int Level, long Stones, string? Item, string? ItemNamePl, int ItemQuantity, int Tokens, bool Reached, bool Claimed);

/// <summary>The season pass (1.9): resets every quarter.</summary>
public sealed record SeasonPassView(string Season, string EndsOn, long Xp, int Level, int Levels, int XpPerLevel, int Claimable, IReadOnlyList<PassLevelView> Rewards,
    IReadOnlyDictionary<string, int> XpPerMetric);

public sealed record TitleView(string Id, string NamePl, string Rarity, string Source, bool Selected);

public sealed record SelectTitleCommand(string? TitleId);

public sealed record CheckinView(int Streak, int BestStreak, bool CheckedInToday, long NextReward);

public sealed record QuestBoard(string Day, IReadOnlyList<QuestView> Daily, IReadOnlyList<QuestView> Achievements, CheckinView Checkin, IReadOnlyList<string> Titles,
    IReadOnlyDictionary<string, int>? TitleBonuses = null, IReadOnlyList<QuestView>? Seasonal = null, StoryView? Story = null, IReadOnlyList<TitleView>? AllTitles = null,
    string? SelectedTitleId = null, int SecretsFound = 0, int SecretsTotal = 0, WeeklyView? Weekly = null, SeasonPassView? Pass = null, int DestinyTokens = 0, ZodiacView? Zodiac = null);

public sealed record ClaimResult(string QuestId, long Stones, long Balance, QuestBoard Board);

public sealed record CheckinResult(int Streak, long Stones, long Balance, QuestBoard Board);

public sealed record LeaderEntry(int Rank, Guid CharacterId, string Name, string Path, long Value);

public sealed class QuestService(
    IUnitOfWorkFactory uowFactory,
    IQuestRepository repo,
    ICharacterDirectory characters,
    IInventory inventory,
    IItemCatalog catalog,
    IAuditLog audit,
    ICorrelationContext correlation,
    QuestsConfig config,
    TimeProvider clock,
    QuestTitles titles,
    JianghuGo.Modules.Cultivation.Contracts.ICultivationStatus cultivation,
    IDestinyTokens tokens)
{
    /// <summary>Daily quests and check-ins pay more as the cultivator climbs: +15 % per realm (1.6).</summary>
    public static long RealmStones(long stones, int realm) => stones * (1000 + (150L * Math.Clamp(realm, 0, 65))) / 1000;

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
            if (board.Story is { } story && story.Id == questId) return await ClaimStoryAsync(uow, accountId, c.Id, story, now, ct);
            if (questId == "pass" && board.Pass is { } pass) return await ClaimPassAsync(uow, accountId, c, pass, now, ct);
            if (board.Weekly is { } week && (questId == week.Id || questId.StartsWith(week.Id + "_t", StringComparison.Ordinal)))
                return await ClaimWeeklyAsync(uow, accountId, c.Id, week, questId, now, ct);
            var quest = board.Daily.Concat(board.Achievements).Concat(board.Seasonal ?? []).FirstOrDefault(q => q.Id == questId);
            if (quest is null) return Error.NotFound("quest_not_found", "This quest is not available today.");
            if (quest.Claimed) return Error.Conflict("quest_already_claimed", "Reward already claimed.");
            if (!quest.Completed) return Error.Conflict("quest_not_completed", "The quest is not completed yet.");
            var period = board.Daily.Contains(quest) ? board.Day : QuestRules.Life;
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

    private async Task<Result<ClaimResult>> ClaimStoryAsync(IUnitOfWork uow, AccountId accountId, CharacterId id, StoryView story, DateTimeOffset now, CancellationToken ct)
    {
        if (story.Finished) return Error.Conflict("story_finished", "The story is complete.");
        if (!story.Completed) return Error.Conflict("quest_not_completed", "The chapter's goal is not reached yet.");
        if (!await repo.TryClaimAsync(uow, id, QuestRules.Life, story.Id, now, ct)) return Error.Conflict("quest_already_claimed", "Reward already claimed.");
        var balance = await inventory.BalanceAsync(uow, id, ct);
        if (story.Stones > 0)
        {
            var credited = await inventory.CreditAsync(uow, id, story.Stones, "story", story.Id, ct);
            if (!credited.IsSuccess) return credited.Error!;
            balance = credited.Value;
        }

        if (story.Item is not null && story.ItemQuantity > 0)
        {
            var added = await inventory.AddItemsAsync(uow, id, story.Item, story.ItemQuantity, "story", story.Id, ct);
            if (!added.IsSuccess) return added.Error!;
        }

        await AuditAsync(uow, accountId, "quests.story_claimed", id.Value, $"{{\"chapter\":{story.Chapter}}}", now, ct);
        var after = await BoardAsync(uow, id, now, ct);
        await uow.CommitAsync(ct);
        return new ClaimResult(story.Id, story.Stones, balance, after);
    }

    /// <summary>Claims every reached, unclaimed pass level at once (stones, items, Destiny Tokens).</summary>
    private async Task<Result<ClaimResult>> ClaimPassAsync(IUnitOfWork uow, AccountId accountId, CharacterSummary c, SeasonPassView pass, DateTimeOffset now, CancellationToken ct)
    {
        var due = pass.Rewards.Where(r => r.Reached && !r.Claimed).ToList();
        if (due.Count == 0) return Error.Conflict("quest_not_completed", "No pass rewards to claim.");
        var period = "Q" + pass.Season;
        long stones = 0;
        foreach (var r in due)
        {
            if (!await repo.TryClaimAsync(uow, c.Id, period, "pass_" + r.Level, now, ct)) continue;
            stones += r.Stones;
            if (r.Item is not null && r.ItemQuantity > 0)
            {
                var added = await inventory.AddItemsAsync(uow, c.Id, r.Item, r.ItemQuantity, "season_pass", period + ":" + r.Level, ct);
                if (!added.IsSuccess) return added.Error!;
            }

            if (r.Tokens > 0)
            {
                var granted = await tokens.GrantOnceAsync(uow, accountId, r.Tokens, DestinyTokenSources.Season, period + ":" + r.Level, ct);
                if (!granted.IsSuccess) return granted.Error!;
            }
        }

        var balance = await inventory.BalanceAsync(uow, c.Id, ct);
        if (stones > 0)
        {
            var credited = await inventory.CreditAsync(uow, c.Id, stones, "season_pass", period + ":" + due[0].Level + "-" + due[^1].Level, ct);
            if (!credited.IsSuccess) return credited.Error!;
            balance = credited.Value;
        }

        await AuditAsync(uow, accountId, "quests.pass_claimed", c.Id.Value, $"{{\"season\":\"{pass.Season}\",\"levels\":{due.Count},\"stones\":{stones}}}", now, ct);
        var after = await BoardAsync(uow, c.Id, now, ct);
        await uow.CommitAsync(ct);
        return new ClaimResult("pass", stones, balance, after);
    }

    private async Task<Result<ClaimResult>> ClaimWeeklyAsync(IUnitOfWork uow, AccountId accountId, CharacterId id, WeeklyView week, string questId, DateTimeOffset now, CancellationToken ct)
    {
        var period = QuestRules.WeekPeriod(QuestRules.WorldDate(config, now));
        long stones;
        string? item = null;
        var quantity = 0;
        if (questId == week.Id)
        {
            if (week.Personal.Claimed) return Error.Conflict("quest_already_claimed", "Reward already claimed.");
            if (!week.Personal.Completed) return Error.Conflict("quest_not_completed", "The weekly goal is not reached yet.");
            stones = week.Personal.Stones;
            var def = config.WeeklyList.First(w => w.Id == week.Id);
            (item, quantity) = (def.Item, def.ItemQuantity);
        }
        else
        {
            var tier = week.Tiers.FirstOrDefault(t => questId == week.Id + "_t" + t.Tier);
            if (tier is null) return Error.NotFound("quest_not_found", "No such tier.");
            if (tier.Claimed) return Error.Conflict("quest_already_claimed", "Reward already claimed.");
            if (!tier.Reached) return Error.Conflict("quest_not_completed", "The community has not reached this tier yet.");
            if (week.MyContribution < week.MinContribution) return Error.Conflict("contribution_too_low", $"Contribute at least {week.MinContribution} to share the reward.");
            stones = tier.Stones;
        }

        if (!await repo.TryClaimAsync(uow, id, period, questId, now, ct)) return Error.Conflict("quest_already_claimed", "Reward already claimed.");
        var balance = await inventory.BalanceAsync(uow, id, ct);
        if (stones > 0)
        {
            var credited = await inventory.CreditAsync(uow, id, stones, "weekly_event", period + ":" + questId, ct);
            if (!credited.IsSuccess) return credited.Error!;
            balance = credited.Value;
        }

        if (item is not null && quantity > 0)
        {
            var added = await inventory.AddItemsAsync(uow, id, item, quantity, "weekly_event", period + ":" + questId, ct);
            if (!added.IsSuccess) return added.Error!;
        }

        await AuditAsync(uow, accountId, "quests.weekly_claimed", id.Value, $"{{\"quest\":\"{questId}\",\"period\":\"{period}\",\"stones\":{stones}}}", now, ct);
        var after = await BoardAsync(uow, id, now, ct);
        await uow.CommitAsync(ct);
        return new ClaimResult(questId, stones, balance, after);
    }

    /// <summary>The codex (2.3): species defeated, with lifetime victories.</summary>
    public Task<Result<IReadOnlyList<CodexEntry>>> CodexAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<CodexEntry>>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var rows = await repo.ListAsync(uow, c.Id, QuestRules.Life, ct);
            return Result<IReadOnlyList<CodexEntry>>.Success([.. rows.Where(r => r.Key.StartsWith("k_", StringComparison.Ordinal)).Select(r => new CodexEntry(r.Key[2..], r.Progress))]);
        }, ct);

    /// <summary>All titles the character has earned (achievements, seasons, arena leagues, realms, arts) and the one shown.</summary>
    public Task<Result<IReadOnlyList<TitleView>>> TitlesAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<TitleView>>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            return Result<IReadOnlyList<TitleView>>.Success(await titles.ListAsync(uow, c.Id, ct));
        }, ct);

    /// <summary>Chooses the title shown above the character (null hides it).</summary>
    public Task<Result<IReadOnlyList<TitleView>>> SelectTitleAsync(AccountId accountId, Guid characterId, SelectTitleCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<TitleView>>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var now = clock.GetUtcNow();
            if (cmd.TitleId is not null && (await titles.ListAsync(uow, c.Id, ct)).All(t => t.Id != cmd.TitleId))
                return Error.NotFound("title_not_earned", "You have not earned this title.");
            await repo.SetSelectedTitleAsync(uow, c.Id, cmd.TitleId, now, ct);
            await AuditAsync(uow, accountId, "quests.title_selected", characterId, cmd.TitleId is null ? "{}" : $"{{\"title\":\"{cmd.TitleId}\"}}", now, ct);
            var list = await titles.ListAsync(uow, c.Id, ct);
            await uow.CommitAsync(ct);
            return Result<IReadOnlyList<TitleView>>.Success(list);
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
            var stones = RealmStones(QuestRules.CheckinStones(config, streak), (await cultivation.StatusAsync(uow, c.Id.Value, ct)).Realm);
            var credited = await inventory.CreditAsync(uow, c.Id, stones, "checkin", QuestRules.Period(today), ct);
            if (!credited.IsSuccess) return credited.Error!;
            await repo.AddProgressAsync(uow, c.Id, QuestRules.Period(today), "m_" + Metrics.Checkins, 1, ct);
            await repo.AddProgressAsync(uow, c.Id, QuestRules.Life, "m_" + Metrics.Checkins, 1, ct);
            if (config.Pass?.XpPerMetric.ContainsKey(Metrics.Checkins) == true)
                await repo.AddProgressAsync(uow, c.Id, QuestRules.QuarterPeriod(today), "m_" + Metrics.Checkins, 1, ct);
            await repo.MaxProgressAsync(uow, c.Id, QuestRules.Life, "m_" + Metrics.CheckinStreak, streak, ct);
            if (config.StreakTokenList.FirstOrDefault(s => s.Days == streak) is { } bonus)
            {
                var granted = await tokens.GrantOnceAsync(uow, accountId, bonus.Tokens, DestinyTokenSources.Streak, c.Id.Value + ":" + QuestRules.Period(today), ct);
                if (!granted.IsSuccess) return granted.Error!;
            }
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
            var done = progress >= q.Target;
            // a secret stays a riddle until it is achieved
            return q.Secret && !done
                ? new QuestView(q.Id, "???", q.RiddlePl ?? "???", "secret", q.Target, 0, q.Stones, false, false, null, null, true, q.RiddlePl, q.TitleRarity)
                : new QuestView(q.Id, q.NamePl, q.DescriptionPl, q.Metric, q.Target, Math.Min(progress, q.Target), q.Stones, done, claimed, q.TitlePl, q.Bonus, q.Secret, q.RiddlePl, q.TitleRarity);
        }

        var realm = (await cultivation.StatusAsync(uow, id.Value, ct)).Realm;
        var daily = QuestRules.DailyFor(config, id.Value, today).Select(q => View(q, todayRows)).Select(v => v with { Stones = RealmStones(v.Stones, realm) }).ToList();
        var achievements = config.Achievements.Select(q => View(q, lifeRows)).ToList();
        var seasonRows = (await repo.ListAsync(uow, id, QuestRules.SeasonPeriod(today), ct)).ToDictionary(r => r.Key);
        var seasonal = config.SeasonalList.Where(q => QuestRules.SeasonActive(q, today) || lifeRows.ContainsKey("q_" + q.Id)).Select(q =>
        {
            var progress = seasonRows.GetValueOrDefault("m_" + q.Metric)?.Progress ?? 0;
            var claimed = lifeRows.ContainsKey("q_" + q.Id);
            return new QuestView(q.Id, q.NamePl, q.DescriptionPl, q.Metric, q.Target, Math.Min(progress, q.Target), q.Stones, claimed || progress >= q.Target, claimed, q.TitlePl,
                null, false, null, q.Rarity, q.To);
        }).ToList();
        StoryView? story = null;
        var chapters = config.StoryList;
        if (chapters.Count > 0)
        {
            var next = chapters.FirstOrDefault(ch => !lifeRows.ContainsKey("q_" + ch.Id));
            var ch = next ?? chapters[^1];
            var progress = lifeRows.GetValueOrDefault("m_" + ch.Metric)?.Progress ?? 0;
            story = new StoryView(ch.Number, chapters.Count, ch.Id, ch.TitlePl, ch.TextPl, ch.GoalPl, ch.Metric, ch.Target, Math.Min(progress, ch.Target), ch.Stones, ch.Item,
                ch.Item is null ? null : catalog.Find(ch.Item)?.NamePl ?? ch.Item, ch.ItemQuantity, progress >= ch.Target, next is null);
        }
        WeeklyView? weekly = null;
        if (QuestRules.WeeklyFor(config, today) is { } ev)
        {
            var period = QuestRules.WeekPeriod(today);
            var weekRows = (await repo.ListAsync(uow, id, period, ct)).ToDictionary(r => r.Key);
            var mine = weekRows.GetValueOrDefault("m_" + ev.Metric)?.Progress ?? 0;
            var until = QuestRules.Period(QuestRules.WeekEnd(today));
            var personal = new QuestView(ev.Id, ev.NamePl, ev.DescriptionPl, ev.Metric, ev.Target, Math.Min(mine, ev.Target), RealmStones(ev.Stones, realm), mine >= ev.Target,
                weekRows.ContainsKey("q_" + ev.Id), null, null, false, null, "epic", until);
            var total = ev.CommunityTarget > 0 ? await repo.SumAsync(uow, period, "m_" + ev.Metric, ct) : 0;
            var tiers = ev.Tiers.Select((t, i) => new ChallengeTierView(i + 1, t.Threshold, RealmStones(t.Stones, realm), total >= t.Threshold,
                weekRows.ContainsKey("q_" + ev.Id + "_t" + (i + 1)))).ToList();
            weekly = new WeeklyView(ev.Id, ev.NamePl, ev.DescriptionPl, ev.Metric, until, personal, total, ev.CommunityTarget, mine, ev.MinContribution, tiers);
        }

        SeasonPassView? passView = null;
        if (config.Pass is { } pass)
        {
            var qperiod = QuestRules.QuarterPeriod(today);
            var qrows = (await repo.ListAsync(uow, id, qperiod, ct)).ToDictionary(r => r.Key);
            var counters = pass.XpPerMetric.Keys.ToDictionary(m => m, m => qrows.GetValueOrDefault("m_" + m)?.Progress ?? 0);
            var xp = QuestRules.PassXp(pass, counters);
            var level = (int)Math.Min(pass.Levels, xp / pass.XpPerLevel);
            var rewards = Enumerable.Range(1, pass.Levels).Select(l =>
            {
                var m = pass.Milestones?.FirstOrDefault(x => x.Level == l);
                return new PassLevelView(l, RealmStones(pass.StonesPerLevel, realm), m?.Item, m?.Item is null ? null : catalog.Find(m.Item)?.NamePl ?? m.Item, m?.ItemQuantity ?? 0,
                    m?.Tokens ?? 0, l <= level, qrows.ContainsKey("q_pass_" + l));
            }).ToList();
            passView = new SeasonPassView(qperiod[1..], QuestRules.Period(QuestRules.QuarterEnd(today)), xp, level, pass.Levels, pass.XpPerLevel,
                rewards.Count(r => r.Reached && !r.Claimed), rewards, pass.XpPerMetric);
        }

        var checkin = await repo.FindCheckinAsync(uow, id, ct);
        var checkedToday = checkin?.LastDay == today;
        var nextStreak = checkedToday ? checkin!.Streak + 1 : QuestRules.NextStreak(checkin, today).Streak;
        var titleNames = achievements.Where(a => a.Claimed && a.TitlePl is not null).Select(a => a.TitlePl!).ToList();
        var titleBonuses = TitleBonuses.Sum(config, lifeRows.Keys);
        var all = await titles.ListAsync(uow, id, ct);
        return new QuestBoard(day, daily, achievements,
            new CheckinView(checkedToday ? checkin!.Streak : checkin is not null && checkin.LastDay == today.AddDays(-1) ? checkin.Streak : 0, checkin?.BestStreak ?? 0, checkedToday,
                RealmStones(QuestRules.CheckinStones(config, nextStreak), realm)), titleNames, titleBonuses, seasonal, story, all, all.FirstOrDefault(t => t.Selected)?.Id,
            config.Achievements.Count(q => q.Secret && (lifeRows.GetValueOrDefault("m_" + q.Metric)?.Progress ?? 0) >= q.Target), config.Achievements.Count(q => q.Secret), weekly, passView,
            await characters.FindAsync(uow, id.Value, ct) is { } owner ? await tokens.BalanceAsync(uow, owner.AccountId, ct) : 0, ZodiacOf(id.Value, today));
    }

    private static ZodiacView ZodiacOf(Guid characterId, DateOnly today)
    {
        var s = Zodiac.Of(characterId);
        var d = Zodiac.OfDay(today);
        var p = Physiques.Of(characterId);
        return new ZodiacView(s.Id, s.NamePl, s.Hanzi, s.Stat, d.Id == s.Id ? Zodiac.Bonus * 2 : Zodiac.Bonus, s.DescriptionPl, d.Id, d.NamePl, d.Id == s.Id,
            p?.Id, p?.NamePl, p?.DescriptionPl, p?.Rarity);
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, Guid characterId, string json, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "character", characterId.ToString(), json, correlation.CorrelationId, now), ct);
}

/// <summary>Counts one event for one character exactly once (today's counter and the lifetime counter).</summary>
internal static class QuestCounter
{
    public static Task CountAsync(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config, IUnitOfWork uow, OutboxMessage message,
        Guid characterId, CancellationToken ct, params (string Metric, long Amount)[] increments) =>
        CountAsync(repo, characters, config, uow, message, characterId, null, ct, increments);

    public static async Task CountAsync(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config, IUnitOfWork uow, OutboxMessage message,
        Guid characterId, string? codexNpcId, CancellationToken ct, params (string Metric, long Amount)[] increments)
    {
        var items = increments.Where(i => i.Amount > 0).ToList();
        if (items.Count == 0) return;
        var c = await characters.FindAsync(uow, characterId, ct);
        if (c is null) return; // character deleted meanwhile
        if (!await repo.TryMarkCountedAsync(uow, c.Id, message.Id, message.OccurredAt, ct)) return;
        var date = QuestRules.WorldDate(config, message.OccurredAt);
        var day = QuestRules.Period(date);
        var season = config.SeasonalList.Any(q => QuestRules.SeasonActive(q, date)) ? QuestRules.SeasonPeriod(date) : null;
        var weekly = QuestRules.WeeklyFor(config, date);
        if (codexNpcId is { Length: > 0 and <= 60 } && System.Text.RegularExpressions.Regex.IsMatch(codexNpcId, "^[a-z0-9_]+$"))
            await repo.AddProgressAsync(uow, c.Id, QuestRules.Life, "k_" + codexNpcId, 1, ct);
        var quarter = config.Pass is null ? null : QuestRules.QuarterPeriod(date);
        foreach (var (metric, amount) in items)
        {
            if (Metrics.Max.Contains(metric))
            {
                await repo.MaxProgressAsync(uow, c.Id, QuestRules.Life, "m_" + metric, amount, ct);
                continue;
            }

            await repo.AddProgressAsync(uow, c.Id, day, "m_" + metric, amount, ct);
            await repo.AddProgressAsync(uow, c.Id, QuestRules.Life, "m_" + metric, amount, ct);
            if (season is not null) await repo.AddProgressAsync(uow, c.Id, season, "m_" + metric, amount, ct);
            if (weekly?.Metric == metric) await repo.AddProgressAsync(uow, c.Id, QuestRules.WeekPeriod(date), "m_" + metric, amount, ct);
            if (quarter is not null && config.Pass!.XpPerMetric.ContainsKey(metric)) await repo.AddProgressAsync(uow, c.Id, quarter, "m_" + metric, amount, ct);
        }
    }

    /// <summary>Marks a one-off fact (e.g. an arena league title) once per event.</summary>
    public static async Task FlagAsync(IQuestRepository repo, ICharacterDirectory characters, IUnitOfWork uow, OutboxMessage message, Guid characterId, string key, CancellationToken ct)
    {
        var c = await characters.FindAsync(uow, characterId, ct);
        if (c is null) return;
        if (!await repo.TryMarkCountedAsync(uow, c.Id, message.Id, message.OccurredAt, ct)) return;
        await repo.MaxProgressAsync(uow, c.Id, QuestRules.Life, key, 1, ct);
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
    public string SourcePl => "Tytuły";

    public bool ResonatesWithRealm => true;

    public async Task<IReadOnlyDictionary<string, int>> BonusesAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct) =>
        TitleBonuses.Sum(config, (await repo.ListAsync(uow, new CharacterId(characterId), QuestRules.Life, ct)).Select(r => r.Key));
}

/// <summary>Every title a character owns, from this module (achievements, seasons, arena leagues) and the other modules' sources.</summary>
public sealed class QuestTitles(IQuestRepository repo, QuestsConfig config, IEnumerable<ITitleSource> sources)
{
    public async Task<IReadOnlyList<TitleView>> ListAsync(IUnitOfWork uow, CharacterId id, CancellationToken ct)
    {
        var life = (await repo.ListAsync(uow, id, QuestRules.Life, ct)).Select(r => r.Key).ToHashSet();
        var selected = await repo.FindSelectedTitleAsync(uow, id, ct);
        var list = new List<(string Id, string Name, string Rarity, string Source)>();
        foreach (var a in config.Achievements.Where(a => a.TitlePl is not null && life.Contains("q_" + a.Id))) list.Add(("q_" + a.Id, a.TitlePl!, a.TitleRarity, a.Secret ? "secret" : "achievement"));
        foreach (var s in config.SeasonalList.Where(s => life.Contains("q_" + s.Id))) list.Add(("q_" + s.Id, s.TitlePl, s.Rarity, "season"));
        foreach (var t in config.ArenaTitleList.Where(t => life.Contains("l_" + t.League))) list.Add(("league_" + t.League, t.TitlePl, t.Rarity, "arena"));
        foreach (var source in sources)
            foreach (var t in await source.TitlesAsync(uow, id.Value, ct)) list.Add((t.Id, t.NamePl, t.Rarity, t.Source));
        return [.. list.DistinctBy(t => t.Id).Select(t => new TitleView(t.Id, t.Name, t.Rarity, t.Source, t.Id == selected))];
    }
}

/// <summary>The chosen title above the character (if still owned) — shown in battle and to other players.</summary>
internal sealed class QuestTitleDecoration(QuestTitles titles) : ICharacterDecorationProvider
{
    public async Task<CharacterDecoration> DecorateAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct)
    {
        var chosen = (await titles.ListAsync(uow, new CharacterId(characterId), ct)).FirstOrDefault(t => t.Selected);
        return chosen is null ? CharacterDecoration.None : new CharacterDecoration(chosen.NamePl, chosen.Rarity, []);
    }
}
