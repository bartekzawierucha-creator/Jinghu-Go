using System.Globalization;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Combat.Domain;

namespace JianghuGo.Modules.Combat.Application;

public sealed record ArenaOpponentView(Guid CharacterId, string Name, int Rating, string LeagueId, string LeagueNamePl, string? Title, IReadOnlyList<string> Badges);

public sealed record ArenaView(
    int Rating,
    int Wins,
    int Losses,
    string LeagueId,
    string LeagueNamePl,
    string? NextLeagueNamePl,
    int? NextLeagueRating,
    int AttemptsLeft,
    int AttemptsPerDay,
    int Rank,
    IReadOnlyList<ArenaOpponentView> Opponents,
    IReadOnlyList<RankView> Top,
    bool WeeklyClaimable,
    long WeeklyStones,
    string Week);

public sealed record ArenaChallengeCommand(Guid OpponentCharacterId);

/// <summary>
/// Ranked arena: you fight the frozen copy (battle snapshot) of another player, played by the server AI. Ratings move
/// by Elo for both sides; leagues pay a weekly reward. Your own copy is refreshed every time you visit or fight.
/// </summary>
public sealed partial class CombatService
{
    private ArenaRules ArenaCfg => config.Arena ?? throw new InvalidOperationException("The arena is not configured.");

    private static readonly Error ArenaOff = Error.NotFound("arena_disabled", "The arena is not available.");

    public ArenaLeague LeagueOf(int rating) => ArenaCfg.Leagues.Where(l => rating >= l.MinRating).OrderByDescending(l => l.MinRating).First();

    public static string WeekOf(DateTimeOffset at) =>
        ISOWeek.GetYear(at.UtcDateTime).ToString("0000", CultureInfo.InvariantCulture) + "-W" + ISOWeek.GetWeekOfYear(at.UtcDateTime).ToString("00", CultureInfo.InvariantCulture);

    /// <summary>Your entry (created on the first visit with your current build), opponents near your rating, the leaderboard.</summary>
    public Task<Result<ArenaView>> GetArenaAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<ArenaView>>(async uow =>
        {
            if (config.Arena is null) return ArenaOff;
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var now = clock.GetUtcNow();
            var mine = await RefreshArenaAsync(uow, c, now, ct);
            var view = await ArenaViewAsync(uow, c, mine, now, ct);
            await uow.CommitAsync(ct);
            return view;
        }, ct);

    private async Task<ArenaEntry> RefreshArenaAsync(IUnitOfWork uow, CharacterSummary c, DateTimeOffset now, CancellationToken ct)
    {
        var stored = await repo.FindArenaAsync(uow, c.Id, ct);
        var defense = await SnapshotAsync(uow, c, ct);
        var entry = stored is null
            ? new ArenaEntry(c.Id, c.AccountId, c.Name, ArenaCfg.StartRating, 0, 0, defense, now, 1)
            : stored with { Name = c.Name, Defense = defense, UpdatedAt = now, Version = stored.Version + 1 };
        if (!await repo.TrySaveArenaAsync(uow, entry, stored?.Version ?? 0, ct)) throw new TransientConflictException("Arena entry changed concurrently.");
        return entry;
    }

    private async Task<int> ArenaAttemptsTodayAsync(IUnitOfWork uow, CharacterId id, DateTimeOffset now, CancellationToken ct)
    {
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        return await repo.CountBattlesOfKindSinceAsync(uow, id, BattleKinds.Arena, dayStart, ct);
    }

    private async Task<ArenaView> ArenaViewAsync(IUnitOfWork uow, CharacterSummary c, ArenaEntry mine, DateTimeOffset now, CancellationToken ct)
    {
        var a = ArenaCfg;
        var league = LeagueOf(mine.Rating);
        var next = a.Leagues.Where(l => l.MinRating > mine.Rating).OrderBy(l => l.MinRating).FirstOrDefault();
        var near = (await repo.ListArenaAsync(uow, mine.Rating - a.OpponentWindow, mine.Rating + a.OpponentWindow, 12, ct)).Where(e => e.CharacterId != c.Id).Take(5).ToList();
        if (near.Count < 3)
            near = [.. near.Concat((await repo.ListArenaAsync(uow, 0, 10000, 20, ct)).Where(e => e.CharacterId != c.Id && near.All(n => n.CharacterId != e.CharacterId))).Take(5)];
        var opponents = near.Select(e =>
        {
            var l = LeagueOf(e.Rating);
            return new ArenaOpponentView(e.CharacterId.Value, e.Name, e.Rating, l.Id, l.NamePl, e.Defense.Title, e.Defense.Badges ?? []);
        }).ToList();
        var top = (await repo.ListArenaAsync(uow, 0, 10000, RankingSize, ct)).Select((e, i) => new RankView(i + 1, e.CharacterId.Value, e.Name, e.Rating)).ToList();
        var rank = await repo.CountArenaAheadAsync(uow, mine, ct) + 1;
        var week = WeekOf(now);
        var attempts = await ArenaAttemptsTodayAsync(uow, c.Id, now, ct);
        var claimable = mine.Wins + mine.Losses > 0 && !await WeekClaimedAsync(uow, c.Id, week, ct);
        return new ArenaView(mine.Rating, mine.Wins, mine.Losses, league.Id, league.NamePl, next?.NamePl, next?.MinRating, Math.Max(0, a.AttemptsPerDay - attempts),
            a.AttemptsPerDay, rank, opponents, top, claimable, league.WeeklyStones, week);
    }

    private Task<bool> WeekClaimedAsync(IUnitOfWork uow, CharacterId id, string week, CancellationToken ct) => repo.ArenaWeekClaimedAsync(uow, id, week, ct);

    public Task<Result<BattleView>> ChallengeArenaAsync(AccountId accountId, Guid characterId, string? key, ArenaChallengeCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<BattleView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "combat.arena.challenge", key, new { characterId, cmd }, async Task<Result<BattleView>> () =>
            {
                if (config.Arena is null) return ArenaOff;
                var now = clock.GetUtcNow();
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                if (cmd.OpponentCharacterId == characterId) return Error.Validation("cannot_challenge_self", "You cannot fight your own copy.");
                var foe = await repo.FindArenaAsync(uow, new CharacterId(cmd.OpponentCharacterId), ct);
                if (foe is null) return Error.NotFound("opponent_not_found", "This cultivator is not in the arena.");
                if (await ArenaAttemptsTodayAsync(uow, c.Id, now, ct) >= ArenaCfg.AttemptsPerDay)
                    return Error.LimitReached("arena_attempts", $"At most {ArenaCfg.AttemptsPerDay} arena fights a day.");
                if (await HasActiveBattleAsync(uow, c.Id, now, ct)) return Error.Conflict("already_in_battle", "This character is already fighting.");
                await RefreshArenaAsync(uow, c, now, ct);
                var a = await SnapshotAsync(uow, c, ct);
                var b = foe.Defense with { Name = foe.Name };
                var battle = NewBattle(BattleKinds.Arena, BattleStatuses.Active, c, null, "arena_copy", now) with
                {
                    CombatantA = a,
                    CombatantB = b,
                    State = CombatEngine.Start(config, a, b),
                    Deadline = now.AddSeconds(config.TurnDeadlineSeconds),
                    Context = "arena:" + foe.CharacterId.Value,
                };
                await repo.AddBattleAsync(uow, battle, ct);
                await AuditAsync(uow, accountId, "combat.arena_started", battle.Id, new { characterId, opponent = foe.CharacterId.Value, battle.SeedHash }, now, ct);
                return await ViewAsync(uow, battle, 'a', ct);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>Elo for both sides (the defender's copy wins or loses rating too); Qi for a win.</summary>
    private async Task<long> ArenaFinishedAsync(IUnitOfWork uow, Battle battle, string outcome, DateTimeOffset at, CancellationToken ct)
    {
        if (config.Arena is null || battle.Context is not { } ctx || !Guid.TryParse(ctx[6..], out var foeId)) return 0;
        var me = await repo.FindArenaAsync(uow, battle.CharacterA, ct);
        var foe = await repo.FindArenaAsync(uow, new CharacterId(foeId), ct);
        if (me is null || foe is null) return 0;
        var score = outcome == Outcomes.AWon ? 1.0 : outcome == Outcomes.Draw ? 0.5 : 0.0;
        var expected = 1.0 / (1.0 + Math.Pow(10, (foe.Rating - me.Rating) / 400.0));
        var delta = (int)Math.Round(ArenaCfg.KFactor * (score - expected));
        var won = outcome == Outcomes.AWon;
        var lost = outcome == Outcomes.BWon;
        var meNext = me with { Rating = Math.Max(0, me.Rating + delta), Wins = me.Wins + (won ? 1 : 0), Losses = me.Losses + (lost ? 1 : 0), UpdatedAt = at, Version = me.Version + 1 };
        var foeNext = foe with { Rating = Math.Max(0, foe.Rating - delta), Version = foe.Version + 1 };
        if (!await repo.TrySaveArenaAsync(uow, meNext, me.Version, ct) || !await repo.TrySaveArenaAsync(uow, foeNext, foe.Version, ct))
            throw new TransientConflictException("Arena ratings changed concurrently.");
        await AuditAsync(uow, battle.AccountA, "combat.arena_finished", battle.Id, new { outcome, delta, rating = meNext.Rating, opponent = foeId }, at, ct);
        return won ? ArenaCfg.QiPerWin : 0;
    }

    /// <summary>The league's weekly stones, once per ISO week (for anyone who fought in the arena).</summary>
    public Task<Result<ArenaView>> ClaimArenaWeekAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<ArenaView>>(async uow =>
        {
            if (config.Arena is null) return ArenaOff;
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var now = clock.GetUtcNow();
            var mine = await repo.FindArenaAsync(uow, c.Id, ct);
            if (mine is null || mine.Wins + mine.Losses == 0) return Error.Conflict("arena_no_fights", "Fight in the arena first.");
            var week = WeekOf(now);
            if (!await repo.TryClaimArenaWeekAsync(uow, c.Id, week, now, ct)) return Error.Conflict("arena_claimed", "This week's reward was already collected.");
            var league = LeagueOf(mine.Rating);
            await outbox.EnqueueAsync(uow, OutboxMessage.Create(CombatEvents.ItemsRewarded,
                new CombatEvents.ItemsRewardedV1(accountId.Value, c.Id.Value, "arena", "week-" + week, league.WeeklyStones, []), now, correlation.CorrelationId), ct);
            await outbox.EnqueueAsync(uow, OutboxMessage.Create(CombatEvents.ArenaLeague,
                new CombatEvents.ArenaLeagueV1(accountId.Value, c.Id.Value, league.Id, mine.Rating), now, correlation.CorrelationId), ct);
            var view = await ArenaViewAsync(uow, c, mine, now, ct) with { WeeklyClaimable = false };
            await uow.CommitAsync(ct);
            return view;
        }, ct);
}
