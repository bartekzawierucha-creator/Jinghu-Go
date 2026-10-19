using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Combat.Domain;
using JianghuGo.Modules.Inventory.Contracts;

namespace JianghuGo.Modules.Combat.Application;

public sealed record RankView(int Rank, Guid CharacterId, string Name, long Value);

public sealed record RewardView(string ItemId, string NamePl, int Quantity);

public sealed record TowerFloorView(int Floor, string NpcId, string NamePl, int Tier, bool Guardian, long Qi, long Stones, IReadOnlyList<RewardView> Items);

/// <summary>Tower of Trials as the player sees it: how high they are, the next floor, the next milestone reward, the leaderboard.</summary>
public sealed record TowerView(int BestFloor, int Floors, TowerFloorView? Next, TowerFloorView? NextMilestone, IReadOnlyList<RankView> Top, RankView? Me);

/// <summary>The world boss of the current cycle with the player's part in it.</summary>
public sealed record WorldBossView(
    int Cycle,
    string NpcId,
    string NamePl,
    int PoolHp,
    int HpLeft,
    bool Defeated,
    DateTimeOffset EndsAt,
    int AttemptsLeft,
    int AttemptsPerDay,
    long MyDamage,
    int? MyRank,
    bool CanClaim,
    bool Claimed,
    int MinDamageForKillReward,
    long KillStones,
    IReadOnlyList<RewardView> KillItems,
    IReadOnlyList<RankView> Top,
    int? UnclaimedCycle);

public sealed record ClaimBossCommand(int? Cycle);

public sealed partial class CombatService
{
    public const int RankingSize = 10;

    // ---------------------------------------------------------------- shared

    private async Task<Result<Battle>> StartNpcBattleAsync(IUnitOfWork uow, CharacterSummary c, string kind, NpcDefinition npc, string context, DateTimeOffset now, CancellationToken ct)
    {
        if (await HasActiveBattleAsync(uow, c.Id, now, ct)) return Error.Conflict("already_in_battle", "This character is already fighting.");
        var a = await SnapshotAsync(uow, c, ct);
        var b = NpcCombatant(npc);
        var battle = NewBattle(kind, BattleStatuses.Active, c, null, npc.Id, now) with
        {
            CombatantA = a,
            CombatantB = b,
            State = CombatEngine.Start(config, a, b),
            Deadline = now.AddSeconds(config.TurnDeadlineSeconds),
            Context = context,
        };
        await repo.AddBattleAsync(uow, battle, ct);
        await AuditAsync(uow, c.AccountId, $"combat.{kind}_started", battle.Id, new { characterId = c.Id.Value, npc = npc.Id, context, battle.SeedHash }, now, ct);
        return battle;
    }

    private Task EnqueueRewardAsync(IUnitOfWork uow, Battle battle, string source, string referenceId, long stones, IReadOnlyList<RewardItem> items, DateTimeOffset at, CancellationToken ct) =>
        stones <= 0 && items.Count == 0
            ? Task.CompletedTask
            : outbox.EnqueueAsync(uow, OutboxMessage.Create(CombatEvents.ItemsRewarded,
                new CombatEvents.ItemsRewardedV1(battle.AccountA.Value, battle.CharacterA.Value, source, referenceId, stones, Lines(items)), at, correlation.CorrelationId), ct);

    private static IReadOnlyList<CombatEvents.RewardLine> Lines(IReadOnlyList<RewardItem> items) => [.. items.Select(i => new CombatEvents.RewardLine(i.Item, i.Quantity))];

    private IReadOnlyList<RewardView> Views(IReadOnlyList<RewardItem> list) => [.. list.Select(i => new RewardView(i.Item, items.Find(i.Item)?.NamePl ?? i.Item, i.Quantity))];

    // ---------------------------------------------------------------- heavenly tribulation (Cultivation)

    public async Task<Result<Guid>> StartTribulationAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, int realm, int powerPermille, string namePl, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
        if (c is null) return CharacterNotFound;
        if (config.Npc("heavenly_tribulation") is not { } template) return Error.NotFound("tribulation_unavailable", "Tribulations are not configured.");
        var npc = TribulationNpc(config, template, realm, powerPermille, namePl);
        var started = await StartNpcBattleAsync(uow, c, BattleKinds.Tribulation, npc, "trib:" + realm, now, ct);
        return started.IsSuccess ? started.Value.Id : started.Error!;
    }

    public async Task<Result<Guid>> StartSpiritAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, string npcId, int realm, int powerPermille, Guid reference, string namePl, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
        if (c is null) return CharacterNotFound;
        if (config.Npc(npcId) is not { } template) return Error.NotFound("npc_not_found", "Unknown spirit.");
        var npc = TribulationNpc(config, template, realm, powerPermille, namePl) with { Id = template.Id };
        var started = await StartNpcBattleAsync(uow, c, BattleKinds.Spirit, npc, "spirit:" + reference.ToString("D"), now, ct);
        return started.IsSuccess ? started.Value.Id : started.Error!;
    }

    /// <summary>The sky grows with the realm: every stat ≈ 9 per realm above the template, times the power (‰).</summary>
    /// <summary>
    /// A tribulation (or a flame spirit / sect boss avatar) at a realm: the template raised (or lowered) from its home realm to
    /// that realm by the scaling rules, times the power factor. Legacy rule (+9 to every stat per realm) without scaling rules.
    /// </summary>
    public static NpcDefinition TribulationNpc(CombatConfig config, NpcDefinition template, int realm, int powerPermille, string namePl) =>
        config.Scaling is { } sc
            ? sc.AtRealm(template, realm, powerPermille) with { NamePl = namePl }
            : template with
            {
                NamePl = namePl,
                Stats = template.Stats.ToDictionary(s => s.Key, s => (int)((long)(s.Value + (9 * realm)) * powerPermille / 1000)),
                HpOverride = null,
                Loadout = [.. template.Loadout.Select(s => s is null ? null : s with { Level = Math.Clamp(1 + (realm / 6), 1, 10) })],
            };

    // ---------------------------------------------------------------- Tower of Trials

    private TowerRules Tower => config.Tower ?? throw new InvalidOperationException("The tower is not configured.");

    private static readonly Error TowerOff = Error.NotFound("tower_disabled", "The Tower of Trials is not available.");

    public Task<Result<TowerView>> GetTowerAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<TowerView>>(async uow =>
        {
            if (config.Tower is null) return TowerOff;
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            return await TowerViewAsync(uow, c, ct);
        }, ct);

    /// <summary>Fights the next floor (best cleared + 1). Idempotent per key like every battle start.</summary>
    public Task<Result<BattleView>> StartTowerAsync(AccountId accountId, Guid characterId, string? key, CancellationToken ct) =>
        uowFactory.RunAsync<Result<BattleView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "combat.tower.start", key, new { characterId }, async Task<Result<BattleView>> () =>
            {
                if (config.Tower is null) return TowerOff;
                var now = clock.GetUtcNow();
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                var floor = ((await repo.FindTowerAsync(uow, c.Id, ct))?.BestFloor ?? 0) + 1;
                if (floor > Tower.Floors) return Error.Conflict("tower_completed", "Every floor of the tower is already cleared.");
                var started = await StartNpcBattleAsync(uow, c, BattleKinds.Tower, FloorNpc(floor), "tower:" + floor, now, ct);
                return started.IsSuccess ? await ViewAsync(uow, started.Value, 'a', ct) : started.Error!;
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    private NpcDefinition FloorNpc(int floor)
    {
        var npc = config.Npc(Tower.NpcFor(floor)) ?? throw new InvalidOperationException("Tower opponent missing.");
        // past the first span the floors climb the realms: every FloorsPerRealm floors the opponents are a realm stronger
        if (config.Scaling is { } sc && Tower.FloorsPerRealm > 0 && floor > Tower.PoolSpanFloors)
            npc = sc.AtRealm(npc, sc.Home(npc) + ((floor - 1) / Tower.FloorsPerRealm));
        return Tower.Scaled(npc, floor);
    }

    /// <summary>A won floor that is the next one up raises the record and pays its first-clear rewards (Qi through the battle event).</summary>
    private async Task<long> TowerFinishedAsync(IUnitOfWork uow, Battle battle, string outcome, DateTimeOffset at, CancellationToken ct)
    {
        if (outcome != Outcomes.AWon || battle.TowerFloor is not { } floor || config.Tower is null) return 0;
        var progress = await repo.FindTowerAsync(uow, battle.CharacterA, ct);
        if (floor != (progress?.BestFloor ?? 0) + 1) return 0;
        var name = battle.CombatantA?.Name ?? "?";
        var next = progress is null
            ? new TowerProgress(battle.CharacterA, battle.AccountA, name, floor, at, 1)
            : progress with { Name = name, BestFloor = floor, UpdatedAt = at, Version = progress.Version + 1 };
        if (!await repo.TrySaveTowerAsync(uow, next, progress?.Version ?? 0, ct)) throw new TransientConflictException("Tower progress changed concurrently.");
        if (Tower.RewardFor(floor) is { } reward) await EnqueueRewardAsync(uow, battle, "tower", "floor-" + floor, reward.Stones, reward.Items, at, ct);
        await AuditAsync(uow, battle.AccountA, "combat.tower_floor_cleared", battle.Id, new { floor }, at, ct);
        return TowerQi(floor);
    }

    /// <summary>Qi for a first clear; past the first span it follows the cultivation curve of the realm the floor stands for.</summary>
    private long TowerQi(int floor) =>
        config.Scaling is { } sc && Tower.FloorsPerRealm > 0 && floor > Tower.PoolSpanFloors
            ? sc.Qi((long)Tower.QiPerFloor * Tower.PoolSpanFloors, (floor - 1) / Tower.FloorsPerRealm)
            : (long)Tower.QiPerFloor * floor;

    private TowerFloorView FloorView(int floor)
    {
        var npc = FloorNpc(floor);
        var reward = Tower.RewardFor(floor);
        return new TowerFloorView(floor, npc.Id, npc.NamePl, npc.Tier, Tower.IsBossFloor(floor), TowerQi(floor), reward?.Stones ?? 0, Views(reward?.Items ?? []));
    }

    private async Task<TowerView> TowerViewAsync(IUnitOfWork uow, CharacterSummary c, CancellationToken ct)
    {
        var mine = await repo.FindTowerAsync(uow, c.Id, ct);
        var best = mine?.BestFloor ?? 0;
        var next = best < Tower.Floors ? FloorView(best + 1) : null;
        var milestone = Tower.Rewards.Where(r => r.Floor > best).OrderBy(r => r.Floor).Select(r => FloorView(r.Floor)).FirstOrDefault();
        var top = (await repo.ListTowerTopAsync(uow, RankingSize, ct)).Select((p, i) => new RankView(i + 1, p.CharacterId.Value, p.Name, p.BestFloor)).ToList();
        RankView? me = mine is null ? null : new RankView(await repo.CountTowerAheadAsync(uow, mine, ct) + 1, c.Id.Value, c.Name, mine.BestFloor);
        return new TowerView(best, Tower.Floors, next, milestone, top, me);
    }

    // ---------------------------------------------------------------- world boss

    private WorldBossRules Boss => config.WorldBoss ?? throw new InvalidOperationException("The world boss is not configured.");

    private static readonly Error BossOff = Error.NotFound("world_boss_disabled", "The world boss is not available.");

    private async Task<WorldBossState> CurrentBossAsync(IUnitOfWork uow, int cycle, CancellationToken ct)
    {
        if (await repo.FindBossAsync(uow, cycle, ct) is { } existing) return existing;
        var fresh = new WorldBossState(cycle, Boss.NpcId, Boss.PoolHp, Boss.PoolHp, null, 1);
        if (await repo.TrySaveBossAsync(uow, fresh, 0, ct)) return fresh;
        return await repo.FindBossAsync(uow, cycle, ct) ?? throw new TransientConflictException("World boss row race.");
    }

    private async Task<int> AttemptsTodayAsync(IUnitOfWork uow, CharacterId characterId, int cycle, DateTimeOffset now, CancellationToken ct)
    {
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        return await repo.CountBattlesSinceAsync(uow, characterId, BattleKinds.Boss, "boss:" + cycle, dayStart, ct);
    }

    public Task<Result<WorldBossView>> GetWorldBossAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<WorldBossView>>(async uow =>
        {
            if (config.WorldBoss is null) return BossOff;
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var view = await BossViewAsync(uow, c, clock.GetUtcNow(), ct);
            await uow.CommitAsync(ct); // the cycle's boss row is created lazily
            return view;
        }, ct);

    public Task<Result<BattleView>> StartWorldBossAsync(AccountId accountId, Guid characterId, string? key, CancellationToken ct) =>
        uowFactory.RunAsync<Result<BattleView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "combat.world_boss.start", key, new { characterId }, async Task<Result<BattleView>> () =>
            {
                if (config.WorldBoss is null) return BossOff;
                var now = clock.GetUtcNow();
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                var cycle = Boss.CycleOf(now);
                var boss = await CurrentBossAsync(uow, cycle, ct);
                if (boss.HpLeft <= 0) return Error.Conflict("world_boss_defeated", "The world boss of this cycle is already defeated.");
                if (await AttemptsTodayAsync(uow, c.Id, cycle, now, ct) >= Boss.AttemptsPerDay)
                    return Error.LimitReached("world_boss_attempts", $"At most {Boss.AttemptsPerDay} attempts a day.");
                var (npc, hpScale) = await BossAtRealmAsync(uow, c.Id, ct);
                // the phase is as tough for everyone: its HP grows with the attacker's realm, the damage is counted back on the pool scale
                var phase = npc with { HpOverride = (int)Math.Min(int.MaxValue, Math.Max(1, (long)Math.Min(Boss.PhaseHp, boss.HpLeft) * hpScale / 1000)) };
                var started = await StartNpcBattleAsync(uow, c, BattleKinds.Boss, phase, "boss:" + cycle, now, ct);
                return started.IsSuccess ? await ViewAsync(uow, started.Value, 'a', ct) : started.Error!;
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>The boss raised to the attacker's realm and the HP factor (‰) that came with it.</summary>
    private async Task<(NpcDefinition Npc, long HpScale)> BossAtRealmAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        // the world boss is a shared event: everyone fights it at their own realm (weaker below its home, stronger above)
        var template = config.Npc(Boss.NpcId)!;
        var npc = config.Scaling is { } sc ? sc.AtRealm(template, await RealmOfAsync(uow, characterId, ct)) : template;
        var scale = template.HpOverride is { } baseHp && npc.HpOverride is { } hp && baseHp > 0 ? Math.Max(50L, (long)hp * 1000 / baseHp) : 1000L;
        return (npc, scale);
    }

    /// <summary>The damage dealt in the battle comes off the shared pool (never below zero) and is added to the player's tally; Qi for the damage.</summary>
    private async Task<long> BossFinishedAsync(IUnitOfWork uow, Battle battle, DateTimeOffset at, CancellationToken ct)
    {
        if (battle.BossCycle is not { } cycle || config.WorldBoss is null || battle.State is not { } state) return 0;
        var boss = await CurrentBossAsync(uow, cycle, ct);
        // damage back on the pool scale (the phase's HP was raised by the attacker's realm)
        var (_, hpScale) = await BossAtRealmAsync(uow, battle.CharacterA, ct);
        var dealt = (int)Math.Min(int.MaxValue, (long)Math.Max(0, state.B.MaxHp - Math.Max(0, state.B.Hp)) * 1000 / hpScale);
        var counted = Math.Min(dealt, boss.HpLeft);
        if (counted > 0)
        {
            var left = boss.HpLeft - counted;
            var updated = boss with { HpLeft = left, DefeatedAt = left == 0 ? at : null, Version = boss.Version + 1 };
            if (!await repo.TrySaveBossAsync(uow, updated, boss.Version, ct)) throw new TransientConflictException("World boss changed concurrently.");
        }

        var mine = await repo.FindBossDamageAsync(uow, cycle, battle.CharacterA, ct);
        var name = battle.CombatantA?.Name ?? "?";
        var row = mine is null
            ? new WorldBossDamage(cycle, battle.CharacterA, battle.AccountA, name, counted, 1, null, 1)
            : mine with { Name = name, Damage = mine.Damage + counted, Attempts = mine.Attempts + 1, Version = mine.Version + 1 };
        if (!await repo.TrySaveBossDamageAsync(uow, row, mine?.Version ?? 0, ct)) throw new TransientConflictException("World boss tally changed concurrently.");
        await AuditAsync(uow, battle.AccountA, "combat.world_boss_hit", battle.Id, new { cycle, damage = counted }, at, ct);
        var qi = Boss.QiFor(counted);
        if (config.Scaling is { } sc) qi = sc.Qi(qi, sc.RealmsAbove(config.Npc(Boss.NpcId)!, await RealmOfAsync(uow, battle.CharacterA, ct)));
        return qi;
    }

    /// <summary>Kill reward for a defeated boss, once per character and cycle (this cycle or an earlier one).</summary>
    public Task<Result<WorldBossView>> ClaimWorldBossAsync(AccountId accountId, Guid characterId, ClaimBossCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<WorldBossView>>(async uow =>
        {
            if (config.WorldBoss is null) return BossOff;
            var now = clock.GetUtcNow();
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var cycle = cmd.Cycle ?? Boss.CycleOf(now);
            var boss = await repo.FindBossAsync(uow, cycle, ct);
            var mine = await repo.FindBossDamageAsync(uow, cycle, c.Id, ct);
            if (boss is not { HpLeft: 0 }) return Error.Conflict("world_boss_alive", "The boss is not defeated yet.");
            if (mine is null || mine.Damage < Boss.MinDamageForKillReward)
                return Error.Conflict("world_boss_no_share", $"At least {Boss.MinDamageForKillReward} damage is needed for the reward.");
            if (mine.ClaimedAt is not null) return Error.Conflict("world_boss_claimed", "The reward was already collected.");
            if (!await repo.TrySaveBossDamageAsync(uow, mine with { ClaimedAt = now, Version = mine.Version + 1 }, mine.Version, ct))
                throw new TransientConflictException("World boss tally changed concurrently.");
            await outbox.EnqueueAsync(uow, OutboxMessage.Create(CombatEvents.ItemsRewarded,
                new CombatEvents.ItemsRewardedV1(accountId.Value, c.Id.Value, "world_boss", "cycle-" + cycle, Boss.KillStones, Lines(Boss.KillItems)), now, correlation.CorrelationId), ct);
            await AuditAsync(uow, accountId, "combat.world_boss_claimed", Guid.Empty, new { characterId, cycle }, now, ct);
            var view = await BossViewAsync(uow, c, now, ct);
            await uow.CommitAsync(ct);
            return view;
        }, ct);

    private async Task<WorldBossView> BossViewAsync(IUnitOfWork uow, CharacterSummary c, DateTimeOffset now, CancellationToken ct)
    {
        var cycle = Boss.CycleOf(now);
        var boss = await CurrentBossAsync(uow, cycle, ct);
        var npc = config.Npc(Boss.NpcId)!;
        var mine = await repo.FindBossDamageAsync(uow, cycle, c.Id, ct);
        var attempts = await AttemptsTodayAsync(uow, c.Id, cycle, now, ct);
        var top = (await repo.ListBossTopAsync(uow, cycle, RankingSize, ct)).Select((d, i) => new RankView(i + 1, d.CharacterId.Value, d.Name, d.Damage)).ToList();
        int? rank = mine is null ? null : await repo.CountBossAheadAsync(uow, mine, ct) + 1;
        var eligible = mine is not null && mine.Damage >= Boss.MinDamageForKillReward;

        int? unclaimed = null;
        if (cycle > 0 && await repo.FindBossAsync(uow, cycle - 1, ct) is { HpLeft: 0 }
            && await repo.FindBossDamageAsync(uow, cycle - 1, c.Id, ct) is { ClaimedAt: null } previous && previous.Damage >= Boss.MinDamageForKillReward)
            unclaimed = cycle - 1;

        return new WorldBossView(cycle, npc.Id, npc.NamePl, boss.PoolHp, boss.HpLeft, boss.HpLeft == 0, Boss.CycleEnd(cycle),
            boss.HpLeft == 0 ? 0 : Math.Max(0, Boss.AttemptsPerDay - attempts), Boss.AttemptsPerDay, mine?.Damage ?? 0, rank,
            boss.HpLeft == 0 && eligible && mine!.ClaimedAt is null, mine?.ClaimedAt is not null, Boss.MinDamageForKillReward, Boss.KillStones, Views(Boss.KillItems), top,
            unclaimed);
    }
}
