using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Combat.Domain;

namespace JianghuGo.Modules.Combat.Application;

public sealed record SlotView(int Slot, string Kind, string? TechniqueId, string? NamePl, int Level, int QiCost, int Cooldown, string? Unavailable);

public sealed record KnownTechniqueView(string Id, string NamePl, string Slot, int Level, int Xp, int XpToNextLevel);

public sealed record LoadoutView(Guid CharacterId, int Version, IReadOnlyList<SlotView> Slots, IReadOnlyList<KnownTechniqueView> Known);

public sealed record EffectView(string Type, int Magnitude, int TurnsLeft);

public sealed record FighterView(string Name, int Hp, int MaxHp, int Qi, int MaxQi, IReadOnlyList<EffectView> Effects);

public sealed record BattleView(
    Guid Id,
    string Kind,
    string Status,
    string RulesVersion,
    int Version,
    string MySide,
    int Turn,
    int Distance,
    DateTimeOffset Deadline,
    FighterView? Me,
    FighterView? Opponent,
    IReadOnlyList<SlotView> MySlots,
    bool MyActionSubmitted,
    bool OpponentActionSubmitted,
    IReadOnlyList<TurnEvent> LastTurnEvents,
    string? Result,
    string? NpcId,
    Guid? OpponentCharacterId,
    string SeedHash,
    string? Seed,
    long RewardQi,
    string? TamedPetNpcId = null,
    IntentPreview? OpponentIntent = null,
    int? TowerFloor = null,
    int? BossCycle = null,
    int DamageDealt = 0);

public sealed record PetView(string NpcId, string NamePl, int Level, int Xp, int XpToNextLevel, IReadOnlyDictionary<string, int> Bonuses, DateTimeOffset TamedAt);

public sealed record SetLoadoutCommand(int ExpectedVersion, IReadOnlyList<string?>? Slots);

public sealed record StartPveCommand(Guid CharacterId, string? NpcId);

public sealed record ChallengeCommand(Guid CharacterId, Guid OpponentCharacterId);

public sealed record ActionCommand(int Turn, int? Slot, string? Direction);

public sealed record StartHuntCommand(string? NpcId);

/// <summary>An idle hunt as the player sees it: who, how long, what it has brought so far.</summary>
public sealed record HuntView(string NpcId, string NpcNamePl, int Tier, DateTimeOffset StartedAt, DateTimeOffset EndsAt, DateTimeOffset? NextBattleAt,
    bool Finished, int Battles, int Wins, int Losses, long Qi, int MinutesPerBattle, int MaxHours);

public sealed record TurnView(int Turn, Intent IntentA, Intent IntentB, bool ForcedA, bool ForcedB, IReadOnlyList<TurnEvent> Events, BattleState StateAfter, DateTimeOffset ResolvedAt);

public sealed partial class CombatService(
    IUnitOfWorkFactory uowFactory,
    ICombatRepository repo,
    ICharacterDirectory characters,
    IEquipmentStats equipment,
    IEnumerable<ICombatStatBonuses> statBonuses,
    IIdempotencyStore idempotency,
    IAuditLog audit,
    IOutbox outbox,
    ICorrelationContext correlation,
    IRandomSeedSource seeds,
    CombatConfig config,
    TimeProvider clock,
    IItemCatalog items) : ICombatEncounters
{
    public const int MaxConsecutiveTimeouts = 3;
    public const int MaxPendingChallenges = 3;

    private static readonly Error BattleNotFound = Error.NotFound("battle_not_found", "Battle not found.");
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");
    private static readonly Error Concurrent = Error.Conflict("version_conflict", "The battle changed concurrently; reload and retry.");

    // ---------------------------------------------------------------- loadout & mastery

    public Task<Result<LoadoutView>> GetLoadoutAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<LoadoutView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            return c is null ? CharacterNotFound : await LoadoutViewAsync(uow, c, ct);
        }, ct);

    public Task<Result<LoadoutView>> SetLoadoutAsync(AccountId accountId, Guid characterId, SetLoadoutCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<LoadoutView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            if (cmd.Slots is null) return Error.Validation("loadout_invalid", "slots are required.");
            if (LoadoutRules.Validate(config, c.Path, cmd.Slots, await DaoTechniquesAsync(uow, c, ct)) is { } problem) return Error.Validation("loadout_invalid", problem);

            var stored = await repo.FindLoadoutAsync(uow, c.Id, ct);
            var version = stored?.Version ?? 0;
            if (cmd.ExpectedVersion != version) return Error.Conflict("version_conflict", "The loadout changed; reload it.");
            if (!await repo.TrySaveLoadoutAsync(uow, new Loadout(c.Id, accountId, [.. cmd.Slots], version + 1), version, ct)) return Error.Conflict("version_conflict", "The loadout changed; reload it.");
            var view = await LoadoutViewAsync(uow, c, ct);
            await uow.CommitAsync(ct);
            return view;
        }, ct);

    private async Task<(IReadOnlyList<string?> Slots, int Version)> CurrentLoadoutAsync(IUnitOfWork uow, CharacterSummary c, CancellationToken ct)
    {
        var stored = await repo.FindLoadoutAsync(uow, c.Id, ct);
        return stored is null ? ([.. config.StarterLoadouts[c.Path].Cast<string?>()], 0) : (stored.Slots, stored.Version);
    }

    /// <summary>Dao Lab techniques this character knows, by id.</summary>
    private async Task<IReadOnlyDictionary<string, TechniqueDefinition>> DaoTechniquesAsync(IUnitOfWork uow, CharacterSummary c, CancellationToken ct) =>
        (await repo.ListKnownCustomAsync(uow, c.Id, ct)).ToDictionary(x => x.Technique.Id, x => x.Technique.Definition);

    private async Task<LoadoutView> LoadoutViewAsync(IUnitOfWork uow, CharacterSummary c, CancellationToken ct)
    {
        var (slots, version) = await CurrentLoadoutAsync(uow, c, ct);
        var mastery = (await repo.ListMasteryAsync(uow, c.Id, ct)).ToDictionary(m => m.TechniqueId);
        var dao = await DaoTechniquesAsync(uow, c, ct);
        int Level(string id) => mastery.TryGetValue(id, out var m) ? m.Level : 1;
        var slotViews = slots.Select((id, i) =>
        {
            var t = id is null ? null : dao.GetValueOrDefault(id) ?? config.Technique(id);
            return new SlotView(i, SlotKinds.Layout[i], id, t?.NamePl, id is null ? 0 : Level(id), t?.QiCost ?? 0, 0, null);
        }).ToList();
        var known = config.KnownBy(c.Path).Concat(dao.Values).Select(t =>
        {
            var m = mastery.GetValueOrDefault(t.Id);
            var level = m?.Level ?? 1;
            return new KnownTechniqueView(t.Id, t.NamePl, t.Kind, level, m?.Xp ?? 0,
                level >= config.Progression.MaxTechniqueLevel ? 0 : level * config.Progression.XpPerLevel);
        }).ToList();
        return new LoadoutView(c.Id.Value, version, slotViews, known);
    }

    private async Task<Combatant> SnapshotAsync(IUnitOfWork uow, CharacterSummary c, CancellationToken ct)
    {
        var (slots, _) = await CurrentLoadoutAsync(uow, c, ct);
        var mastery = (await repo.ListMasteryAsync(uow, c.Id, ct)).ToDictionary(m => m.TechniqueId, m => m.Level);
        var dao = await DaoTechniquesAsync(uow, c, ct);
        // Equipment bonuses are frozen into the snapshot: changing gear mid-battle has no effect.
        var bonuses = new Dictionary<string, int>(await equipment.BonusesAsync(uow, c.Id, ct));
        foreach (var provider in statBonuses)
        {
            foreach (var (stat, value) in await provider.BonusesAsync(uow, c.Id.Value, ct)) bonuses[stat] = bonuses.GetValueOrDefault(stat) + value;
        }

        if (await repo.FindPetAsync(uow, c.Id, ct) is { } pet && config.Npc(pet.NpcId) is { } species)
        {
            foreach (var (stat, value) in SpiritPetRules.Bonuses(species, pet.Level)) bonuses[stat] = bonuses.GetValueOrDefault(stat) + value;
        }

        var stats = c.Stats.ToDictionary(s => s.Key, s => s.Value + bonuses.GetValueOrDefault(s.Key));
        return new Combatant(c.Name, stats, [.. slots.Select(id => id is null ? null : new EquippedTechnique(id, mastery.GetValueOrDefault(id, 1), dao.GetValueOrDefault(id)))],
            null, config.PathHpBonusPermille[c.Path], config.Perks(c.Path));
    }

    private static Combatant NpcCombatant(NpcDefinition n) =>
        new(n.NamePl, n.Stats, [.. n.Loadout.Select(s => s is null ? null : new EquippedTechnique(s.Id, s.Level))], n.HpOverride);

    // ---------------------------------------------------------------- starting battles

    public Task<Result<BattleView>> StartPveAsync(AccountId accountId, string? key, StartPveCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<BattleView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "combat.pve.start", key, cmd, async Task<Result<BattleView>> () =>
            {
                if (cmd.NpcId is not null && config.Npc(cmd.NpcId) is { WorldOnly: true })
                    return Error.Validation("npc_world_only", "This opponent can only be found in the world.");
                var started = await StartPveCoreAsync(uow, accountId, cmd.CharacterId, cmd.NpcId, ct);
                return started.IsSuccess ? await ViewAsync(uow, started.Value, 'a', ct) : started.Error!;
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    public async Task<Result<Guid>> StartEncounterAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, string npcId, CancellationToken ct)
    {
        var started = await StartPveCoreAsync(uow, accountId, characterId, npcId, ct);
        return started.IsSuccess ? started.Value.Id : started.Error!;
    }

    private async Task<Result<Battle>> StartPveCoreAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, string? npcId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
        if (c is null) return CharacterNotFound;
        if (npcId is null || config.Npc(npcId) is not { } npc) return Error.Validation("npc_not_found", "Unknown opponent.");
        if (await HasActiveBattleAsync(uow, c.Id, now, ct)) return Error.Conflict("already_in_battle", "This character is already fighting.");

        var a = await SnapshotAsync(uow, c, ct);
        var b = NpcCombatant(npc);
        var battle = NewBattle(BattleKinds.Pve, BattleStatuses.Active, c, null, npc.Id, now) with
        {
            CombatantA = a,
            CombatantB = b,
            State = CombatEngine.Start(config, a, b),
            Deadline = now.AddSeconds(config.TurnDeadlineSeconds),
        };
        await repo.AddBattleAsync(uow, battle, ct);
        await AuditAsync(uow, accountId, "combat.pve_started", battle.Id, new { characterId = c.Id.Value, npc = npc.Id, battle.SeedHash }, now, ct);
        return battle;
    }

    public Task<Result<BattleView>> ChallengeAsync(AccountId accountId, string? key, ChallengeCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<BattleView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "combat.pvp.challenge", key, cmd, async Task<Result<BattleView>> () =>
            {
                var now = clock.GetUtcNow();
                var c = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
                if (c is null) return CharacterNotFound;
                var opponent = await characters.FindAsync(uow, cmd.OpponentCharacterId, ct);
                if (opponent is null) return Error.NotFound("opponent_not_found", "Opponent not found.");
                if (opponent.AccountId == accountId) return Error.Validation("cannot_challenge_self", "You cannot challenge your own characters.");
                var open = await repo.ListOpenBattlesAsync(uow, c.Id, ct);
                if (open.Count(b => b.Status == BattleStatuses.PendingAcceptance && b.CharacterA == c.Id && b.Deadline > now) >= MaxPendingChallenges)
                    return Error.LimitReached("too_many_challenges", $"At most {MaxPendingChallenges} pending challenges per character.");

                var battle = NewBattle(BattleKinds.Pvp, BattleStatuses.PendingAcceptance, c, opponent, null, now) with
                {
                    Deadline = now.AddSeconds(config.ChallengeExpirySeconds),
                };
                await repo.AddBattleAsync(uow, battle, ct);
                await outbox.EnqueueAsync(uow, OutboxMessage.Create(CombatEvents.ChallengeReceived,
                    new CombatEvents.ChallengeReceivedV1(battle.Id, accountId.Value, c.Id.Value, c.Name, opponent.AccountId.Value, opponent.Id.Value), now, correlation.CorrelationId), ct);
                await AuditAsync(uow, accountId, "combat.pvp_challenged", battle.Id, new { characterId = c.Id.Value, opponent = opponent.Id.Value }, now, ct);
                return await ViewAsync(uow, battle, 'a', ct);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    public Task<Result<BattleView>> AcceptAsync(AccountId accountId, Guid battleId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<BattleView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var battle = await LoadAndAdvanceAsync(uow, battleId, now, ct);
            if (battle is null || battle.SideOf(accountId) is not { } side) return BattleNotFound;
            if (battle.Status == BattleStatuses.Active && side == 'b')
            {
                var current = await ViewAsync(uow, battle, side, ct); // already accepted: idempotent
                await uow.CommitAsync(ct);
                return current;
            }

            if (side != 'b' || battle.Status != BattleStatuses.PendingAcceptance)
                return Error.Conflict("battle_not_pending", "This challenge cannot be accepted.");
            if (await HasActiveBattleAsync(uow, battle.CharacterA, now, ct) || await HasActiveBattleAsync(uow, battle.CharacterB!.Value, now, ct))
                return Error.Conflict("already_in_battle", "One of the fighters is already in another battle.");

            var ca = await characters.FindAsync(uow, battle.CharacterA.Value, ct);
            var cb = await characters.FindAsync(uow, battle.CharacterB!.Value.Value, ct);
            var a = await SnapshotAsync(uow, ca!, ct);
            var b = await SnapshotAsync(uow, cb!, ct);
            var started = battle with
            {
                Status = BattleStatuses.Active,
                CombatantA = a,
                CombatantB = b,
                State = CombatEngine.Start(config, a, b),
                Deadline = now.AddSeconds(config.TurnDeadlineSeconds),
                Version = battle.Version + 1,
                UpdatedAt = now,
            };
            if (!await repo.TryUpdateBattleAsync(uow, started, battle.Version, ct)) return Concurrent;
            await AuditAsync(uow, accountId, "combat.pvp_accepted", battle.Id, new { battle.SeedHash }, now, ct);
            var startedView = await ViewAsync(uow, started, side, ct);
            await uow.CommitAsync(ct);
            return startedView;
        }, ct);

    public Task<Result<BattleView>> DeclineAsync(AccountId accountId, Guid battleId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<BattleView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var battle = await LoadAndAdvanceAsync(uow, battleId, now, ct);
            if (battle is null || battle.SideOf(accountId) is not { } side) return BattleNotFound;
            if (battle.Status != BattleStatuses.PendingAcceptance) return Error.Conflict("battle_not_pending", "Only a pending challenge can be declined or withdrawn.");
            var declined = battle with { Status = BattleStatuses.Declined, Version = battle.Version + 1, UpdatedAt = now, FinishedAt = now };
            if (!await repo.TryUpdateBattleAsync(uow, declined, battle.Version, ct)) return Concurrent;
            var declinedView = await ViewAsync(uow, declined, side, ct);
            await uow.CommitAsync(ct);
            return declinedView;
        }, ct);

    /// <summary>Gives up an active battle: the other side wins at once (PvE: no reward, no penalty beyond the loss).</summary>
    public Task<Result<BattleView>> ForfeitAsync(AccountId accountId, Guid battleId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<BattleView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var battle = await LoadAndAdvanceAsync(uow, battleId, now, ct);
            if (battle is null || battle.SideOf(accountId) is not { } side) return BattleNotFound;
            if (battle.Status != BattleStatuses.Active) return Error.Conflict("battle_not_active", $"The battle is {battle.Status}.");
            var outcome = side == 'a' ? Outcomes.BWon : Outcomes.AWon;
            var finished = await FinishAsync(uow, battle with { State = battle.State! with { Outcome = outcome } }, battle.Version, now, ct);
            if (finished is null) return Concurrent;
            await AuditAsync(uow, accountId, "combat.forfeited", battle.Id, new { side = side.ToString() }, now, ct);
            var view = await ViewAsync(uow, finished, side, ct);
            await uow.CommitAsync(ct);
            return view;
        }, ct);

    // ---------------------------------------------------------------- turns

    public Task<Result<BattleView>> GetBattleAsync(AccountId accountId, Guid battleId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<BattleView>>(async uow =>
        {
            var battle = await LoadAndAdvanceAsync(uow, battleId, clock.GetUtcNow(), ct);
            if (battle is null || battle.SideOf(accountId) is not { } side) return BattleNotFound;
            var view = await ViewAsync(uow, battle, side, ct);
            await uow.CommitAsync(ct); // persists any turns resolved by elapsed deadlines
            return view;
        }, ct);

    public Task<Result<IReadOnlyList<TurnView>>> ListTurnsAsync(AccountId accountId, Guid battleId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<TurnView>>>(async uow =>
        {
            var battle = await repo.FindBattleAsync(uow, battleId, ct);
            if (battle is null || battle.SideOf(accountId) is null) return BattleNotFound;
            var turns = await repo.ListTurnsAsync(uow, battleId, ct);
            return Result<IReadOnlyList<TurnView>>.Success([.. turns.Select(t => new TurnView(t.Turn, t.IntentA, t.IntentB, t.ForcedA, t.ForcedB, t.Events, t.StateAfter, t.ResolvedAt))]);
        }, ct);

    public Task<Result<IReadOnlyList<BattleView>>> ListOpenAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<BattleView>>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var views = new List<BattleView>();
            foreach (var b in await repo.ListOpenBattlesAsync(uow, c.Id, ct))
            {
                var advanced = await LoadAndAdvanceAsync(uow, b.Id, now, ct);
                if (advanced is { Status: BattleStatuses.Active or BattleStatuses.PendingAcceptance })
                    views.Add(await ViewAsync(uow, advanced, advanced.SideOf(accountId)!.Value, ct));
            }

            await uow.CommitAsync(ct);
            return Result<IReadOnlyList<BattleView>>.Success(views);
        }, ct);

    /// <summary>
    /// Submit this turn's intent. The client names the turn it is acting in (acceptance #7) and sends only an intent:
    /// never damage, hit or outcome (#6). One action per side per turn (#8); a retried request with the same
    /// Idempotency-Key replays the stored response.
    /// </summary>
    public Task<Result<BattleView>> ActAsync(AccountId accountId, Guid battleId, string? key, ActionCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<BattleView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "combat.action", key, new { battleId, cmd }, async Task<Result<BattleView>> () =>
            {
                var now = clock.GetUtcNow();
                var battle = await LoadAndAdvanceAsync(uow, battleId, now, ct);
                if (battle is null || battle.SideOf(accountId) is not { } side) return BattleNotFound;
                if (battle.Status != BattleStatuses.Active) return Error.Conflict("battle_not_active", $"The battle is {battle.Status}.");
                var state = battle.State!;
                if (cmd.Turn != state.Turn) return Error.Conflict("turn_mismatch", $"It is turn {state.Turn}, not {cmd.Turn}.");

                var intents = await repo.ListIntentsAsync(uow, battle.Id, state.Turn, ct);
                if (intents.Any(i => i.Side == side)) return Error.Conflict("action_already_submitted", "You already acted this turn.");

                var intent = new Intent(cmd.Slot, cmd.Direction);
                var me = side == 'a' ? battle.CombatantA! : battle.CombatantB!;
                if (!state.Of(side).IsStunned && CombatEngine.Validate(config, me, state.Of(side), state.Distance, intent) is { } code)
                    return Error.Validation(code, $"Action not allowed: {code}.");

                await repo.AddIntentAsync(uow, new SubmittedIntent(battle.Id, state.Turn, side, intent, now), ct);
                var other = intents.FirstOrDefault(i => i.Side != side);
                battle = side == 'a' ? battle with { TimeoutsA = 0 } : battle with { TimeoutsB = 0 };
                if (BattleKinds.AgainstNpc(battle.Kind) || other is not null)
                {
                    var (ia, ib) = side == 'a' ? (intent, other?.Intent) : (other!.Intent, intent);
                    battle = await ResolveAsync(uow, battle, ia, ib, false, false, now, ct)
                        ?? throw new TransientConflictException("Battle changed while resolving the turn.");
                }

                return await ViewAsync(uow, battle, side, ct);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>Loads a battle and applies everything that happened by time alone: expired challenges and missed turn deadlines.</summary>
    private async Task<Battle?> LoadAndAdvanceAsync(IUnitOfWork uow, Guid battleId, DateTimeOffset now, CancellationToken ct)
    {
        var battle = await repo.FindBattleAsync(uow, battleId, ct);
        if (battle is null) return null;

        if (battle.Status == BattleStatuses.PendingAcceptance && now > battle.Deadline)
        {
            var expired = battle with { Status = BattleStatuses.Expired, Version = battle.Version + 1, UpdatedAt = now, FinishedAt = now };
            return await repo.TryUpdateBattleAsync(uow, expired, battle.Version, ct) ? expired : throw new TransientConflictException("expire race");
        }

        // A missed deadline resolves the turn with "rest" for whoever did not act. Disconnecting never voids the battle;
        // MaxConsecutiveTimeouts missed turns in a row forfeit it.
        while (battle.Status == BattleStatuses.Active && now > battle.Deadline)
        {
            var intents = await repo.ListIntentsAsync(uow, battle.Id, battle.State!.Turn, ct);
            var a = intents.FirstOrDefault(i => i.Side == 'a')?.Intent;
            var b = intents.FirstOrDefault(i => i.Side == 'b')?.Intent;
            var forcedA = a is null;
            var forcedB = battle.Kind == BattleKinds.Pvp && b is null;
            battle = battle with
            {
                TimeoutsA = forcedA ? battle.TimeoutsA + 1 : 0,
                TimeoutsB = forcedB ? battle.TimeoutsB + 1 : 0,
            };
            if (battle.TimeoutsA >= MaxConsecutiveTimeouts || battle.TimeoutsB >= MaxConsecutiveTimeouts)
            {
                var outcome = (battle.TimeoutsA >= MaxConsecutiveTimeouts, battle.TimeoutsB >= MaxConsecutiveTimeouts) switch
                {
                    (true, true) => Outcomes.Draw,
                    (true, false) => Outcomes.BWon,
                    _ => Outcomes.AWon,
                };
                var forfeited = battle with { State = battle.State with { Outcome = outcome } };
                battle = await FinishAsync(uow, forfeited, battle.Version, battle.Deadline, ct) ?? throw new TransientConflictException("forfeit race");
                break;
            }

            battle = await ResolveAsync(uow, battle, a ?? Intent.Rest, b ?? (forcedB ? Intent.Rest : null), forcedA, forcedB, battle.Deadline, ct)
                     ?? throw new TransientConflictException("timeout race");
        }

        return battle;
    }

    /// <summary>Resolves the current turn. <paramref name="ib"/> null in PvE = let the AI choose.</summary>
    private async Task<Battle?> ResolveAsync(IUnitOfWork uow, Battle battle, Intent ia, Intent? ib, bool forcedA, bool forcedB, DateTimeOffset at, CancellationToken ct)
    {
        var state = battle.State!;
        if (BattleKinds.AgainstNpc(battle.Kind)) ib = NpcIntent(battle);

        var (next, events) = CombatEngine.ResolveTurn(config, battle.CombatantA!, battle.CombatantB!, state, ia, ib!, CombatEngine.TurnRng(battle.Seed, state.Turn));
        await repo.AddTurnAsync(uow, new TurnRecord(battle.Id, state.Turn, ia, ib!, forcedA, forcedB, events, next, at), ct);

        var updated = battle with { State = next, Deadline = at.AddSeconds(config.TurnDeadlineSeconds), UpdatedAt = at };
        if (next.Outcome is not null)
        {
            return await FinishAsync(uow, updated, battle.Version, at, ct);
        }

        updated = updated with { Version = battle.Version + 1 };
        return await repo.TryUpdateBattleAsync(uow, updated, battle.Version, ct) ? updated : null;
    }

    /// <summary>
    /// The AI's choice for side B this turn. It depends only on the battle seed, the turn and B's state at the start of
    /// the turn (a separate stream keeps the resolution stream identical in replays), so it can be shown to the player
    /// before they act — the telegraph — and is exactly what B then does.
    /// </summary>
    private Intent NpcIntent(Battle battle) =>
        CombatAi.Choose(config, battle.CombatantB!, battle.State!.B, battle.State.Distance, CombatEngine.TurnRng(battle.Seed ^ 0xA1A1A1A1A1A1A1A1UL, battle.State.Turn));

    private async Task<Battle?> FinishAsync(IUnitOfWork uow, Battle battle, int expectedVersion, DateTimeOffset at, CancellationToken ct)
    {
        var outcome = battle.State!.Outcome!;
        long reward = battle.Kind switch
        {
            BattleKinds.Tower => await TowerFinishedAsync(uow, battle, outcome, at, ct),
            BattleKinds.Boss => await BossFinishedAsync(uow, battle, at, ct),
            _ => 0,
        };
        if (battle.Kind == BattleKinds.Pve && outcome == Outcomes.AWon && config.Npc(battle.NpcId!) is { } npc && npc.RewardQi > 0
            && await repo.CountRewardedPveWinsSinceAsync(uow, battle.CharacterA, at.AddDays(-1), ct) < config.Progression.DailyRewardedPveWins)
        {
            reward = npc.RewardQi;
        }

        var finished = battle with
        {
            Status = BattleStatuses.Finished,
            Version = expectedVersion + 1,
            RewardQi = reward,
            FinishedAt = at,
            UpdatedAt = at,
        };
        if (!await repo.TryUpdateBattleAsync(uow, finished, expectedVersion, ct)) return null;

        // Spirit pets: a won PvE battle may tame a tameable beast (if the cultivator has none) or train the current pet.
        if (battle.Kind == BattleKinds.Pve && outcome == Outcomes.AWon && config.Npc(battle.NpcId!) is { } foe)
        {
            var pet = await repo.FindPetAsync(uow, battle.CharacterA, ct);
            if (pet is null)
            {
                if (foe.Tameable && SpiritPetRules.Tamed(battle.Seed, SpiritPetRules.TameChancePermille(battle.CombatantA!.Stats.GetValueOrDefault("fate"))))
                    await repo.SavePetAsync(uow, new SpiritPet(battle.CharacterA, foe.Id, 0, battle.Id, at), ct);
            }
            else
            {
                await repo.SavePetAsync(uow, pet with { Xp = pet.Xp + SpiritPetRules.XpForWin(foe) }, ct);
            }
        }

        // Technique mastery: every executed (non-forced) use of an equipped technique earns XP.
        var turns = await repo.ListTurnsAsync(uow, battle.Id, ct);
        await GrantXpAsync(uow, battle.CharacterA, battle.CombatantA!, turns.Where(t => !t.ForcedA).Select(t => t.IntentA), ct);
        if (battle.Kind == BattleKinds.Pvp)
            await GrantXpAsync(uow, battle.CharacterB!.Value, battle.CombatantB!, turns.Where(t => !t.ForcedB).Select(t => t.IntentB), ct);

        static string ResultFor(char side, string outcome) =>
            outcome == Outcomes.Draw ? "draw" : (outcome == Outcomes.AWon) == (side == 'a') ? "won" : "lost";
        var participants = new List<CombatEvents.Participant>
        {
            new(battle.AccountA.Value, battle.CharacterA.Value, "a", ResultFor('a', outcome), reward),
        };
        if (battle.Kind == BattleKinds.Pvp)
            participants.Add(new(battle.AccountB!.Value.Value, battle.CharacterB!.Value.Value, "b", ResultFor('b', outcome), 0));

        await outbox.EnqueueAsync(uow, OutboxMessage.Create(CombatEvents.BattleFinished,
            new CombatEvents.BattleFinishedV1(battle.Id, battle.Kind, outcome, battle.NpcId, participants, battle.NpcId is { } nid && config.Npc(nid)?.Boss == true), at, correlation.CorrelationId), ct);
        await AuditAsync(uow, battle.AccountA, "combat.battle_finished", battle.Id,
            new { outcome, battle.State.Turn, reward, seed = battle.Seed.ToString(CultureInfo.InvariantCulture) }, at, ct);
        return finished;
    }

    private async Task GrantXpAsync(IUnitOfWork uow, CharacterId characterId, Combatant combatant, IEnumerable<Intent> intents, CancellationToken ct)
    {
        var uses = intents.Where(i => i.Slot is { } s && combatant.Slots[s] is not null)
            .GroupBy(i => combatant.Slots[i.Slot!.Value]!.Id)
            .ToDictionary(g => g.Key, g => g.Count());
        if (uses.Count == 0) return;
        var mastery = (await repo.ListMasteryAsync(uow, characterId, ct)).ToDictionary(m => m.TechniqueId);
        var p = config.Progression;
        foreach (var (techniqueId, count) in uses)
        {
            var m = mastery.GetValueOrDefault(techniqueId) ?? new TechniqueMastery(characterId, techniqueId, 1, 0);
            var (level, xp) = (m.Level, m.Xp + (count * p.XpPerUse));
            while (level < p.MaxTechniqueLevel && xp >= level * p.XpPerLevel)
            {
                xp -= level * p.XpPerLevel;
                level++;
            }

            if (level >= p.MaxTechniqueLevel) xp = 0;
            await repo.SaveMasteryAsync(uow, m with { Level = level, Xp = xp }, ct);
        }
    }

    private async Task<bool> HasActiveBattleAsync(IUnitOfWork uow, CharacterId characterId, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var b in await repo.ListOpenBattlesAsync(uow, characterId, ct))
        {
            if (b.Status != BattleStatuses.Active) continue;
            var advanced = await LoadAndAdvanceAsync(uow, b.Id, now, ct); // a stale battle may finish by forfeit right here
            if (advanced?.Status == BattleStatuses.Active) return true;
        }

        return false;
    }

    private Battle NewBattle(string kind, string status, CharacterSummary a, CharacterSummary? b, string? npcId, DateTimeOffset now)
    {
        var seed = seeds.NextSeed();
        return new Battle(Uuid7.New(), kind, status, config.RulesVersion, seed, SeedHash(seed), a.Id, a.AccountId, b?.Id, b?.AccountId, npcId,
            null, null, null, 1, now, 0, 0, 0, now, now, null);
    }

    /// <summary>Commitment to the seed, shown during the battle; the seed itself is revealed when it ends.</summary>
    public static string SeedHash(ulong seed) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed.ToString(CultureInfo.InvariantCulture))));

    private async Task<BattleView> ViewAsync(IUnitOfWork uow, Battle b, char side, CancellationToken ct)
    {
        var state = b.State;
        var intents = state is null || b.Status != BattleStatuses.Active ? [] : await repo.ListIntentsAsync(uow, b.Id, state.Turn, ct);
        var lastTurn = state is null ? null : await LastTurnAsync(uow, b.Id, ct);
        var me = side == 'a' ? b.CombatantA : b.CombatantB;
        var foe = side == 'a' ? b.CombatantB : b.CombatantA;

        FighterView? Fighter(Combatant? c, Fighter? f) => c is null || f is null
            ? null
            : new FighterView(c.Name, f.Hp, f.MaxHp, f.Qi, f.MaxQi, [.. f.Effects.Select(e => new EffectView(e.Type, e.Magnitude, e.TurnsLeft))]);

        var slots = me is null || state is null
            ? []
            : me.Slots.Select((eq, i) =>
            {
                var t = eq is null ? null : config.Technique(eq);
                var probe = eq is null ? null : CombatEngine.Validate(config, me, state.Of(side), state.Distance, new Intent(i, null));
                return new SlotView(i, SlotKinds.Layout[i], eq?.Id, t?.NamePl, eq?.Level ?? 0, t?.QiCost ?? 0, state.Of(side).Cooldowns[i], eq is null ? "slot_empty" : probe);
            }).ToList();

        string? result = state?.Outcome is null ? null
            : state.Outcome == Outcomes.Draw ? "draw" : (state.Outcome == Outcomes.AWon) == (side == 'a') ? "won" : "lost";
        var finished = b.Status is BattleStatuses.Finished;
        var telegraph = b.Status == BattleStatuses.Active && side == 'a' && state is { Outcome: null } && BattleKinds.AgainstNpc(b.Kind)
            ? CombatAi.Preview(config, b.CombatantB!, state.B, NpcIntent(b))
            : null;

        return new BattleView(b.Id, b.Kind, b.Status, b.RulesVersion, b.Version, side.ToString(), state?.Turn ?? 0, state?.Distance ?? config.Distance.Start,
            b.Deadline, Fighter(me, state?.Of(side)), Fighter(foe, state?.Of(side == 'a' ? 'b' : 'a')), slots,
            intents.Any(i => i.Side == side), intents.Any(i => i.Side != side),
            lastTurn?.Events ?? [], result, b.NpcId,
            side == 'a' ? b.CharacterB?.Value : b.CharacterA.Value,
            b.SeedHash, finished ? b.Seed.ToString(CultureInfo.InvariantCulture) : null, b.RewardQi,
            finished && side == 'a' && b.Kind == BattleKinds.Pve && await repo.FindPetAsync(uow, b.CharacterA, ct) is { } tamed && tamed.TamedInBattle == b.Id ? tamed.NpcId : null,
            telegraph, b.TowerFloor, b.BossCycle, b.Kind == BattleKinds.Boss && state is not null ? Math.Max(0, state.B.MaxHp - state.B.Hp) : 0);
    }

    // ---------------------------------------------------------------- automatic play

    /// <summary>Plays this turn for the player with the same AI that drives NPCs (auto-battle). The server picks; the client only asks.</summary>
    public async Task<Result<BattleView>> AutoActAsync(AccountId accountId, Guid battleId, string? key, CancellationToken ct)
    {
        var picked = await uowFactory.RunAsync<Result<ActionCommand>>(async uow =>
        {
            var battle = await LoadAndAdvanceAsync(uow, battleId, clock.GetUtcNow(), ct);
            if (battle is null || battle.SideOf(accountId) is not { } side) return BattleNotFound;
            if (battle.Status != BattleStatuses.Active) return Error.Conflict("battle_not_active", $"The battle is {battle.Status}.");
            var state = battle.State!;
            var me = side == 'a' ? battle.CombatantA! : battle.CombatantB!;
            var foe = BattleKinds.AgainstNpc(battle.Kind) && side == 'a' ? CombatAi.Preview(config, battle.CombatantB!, state.B, NpcIntent(battle)) : null;
            var intent = CombatAi.ChooseAgainst(config, me, state.Of(side), state.Distance, foe, new DeterministicRandom(seeds.NextSeed()));
            await uow.CommitAsync(ct); // persists turns resolved by elapsed deadlines
            return new ActionCommand(state.Turn, intent.Slot, intent.Direction);
        }, ct);
        return picked.IsSuccess ? await ActAsync(accountId, battleId, key, picked.Value, ct) : picked.Error!;
    }

    // ---------------------------------------------------------------- idle hunts

    public const int MaxBattlesPerSettle = 60;

    public Task<Result<HuntView>> StartHuntAsync(AccountId accountId, Guid characterId, StartHuntCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<HuntView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            if (cmd.NpcId is null || config.Npc(cmd.NpcId) is not { } npc) return Error.Validation("npc_not_found", "Unknown opponent.");
            if (npc.WorldOnly) return Error.Validation("npc_world_only", "This opponent can only be found in the world.");
            if (await repo.FindHuntAsync(uow, c.Id, ct) is not null) return Error.Conflict("hunt_in_progress", "Collect the current hunt first.");
            var hunt = new IdleHunt(c.Id, accountId, npc.Id, now, now, 0, 0, 0, 0, 1);
            if (!await repo.TrySaveHuntAsync(uow, hunt, 0, ct)) return Error.Conflict("hunt_in_progress", "Collect the current hunt first.");
            await AuditAsync(uow, accountId, "combat.hunt_started", Guid.Empty, new { characterId, npc = npc.Id }, now, ct);
            await uow.CommitAsync(ct);
            return HuntViewOf(hunt, now);
        }, ct);

    /// <summary>The hunt with every battle that is due by now already fought (null when there is no hunt).</summary>
    public Task<Result<HuntView?>> GetHuntAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<HuntView?>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            if (await repo.FindHuntAsync(uow, c.Id, ct) is not { } hunt) return (HuntView?)null;
            hunt = await SettleHuntAsync(uow, c, hunt, now, ct);
            await uow.CommitAsync(ct);
            return HuntViewOf(hunt, now);
        }, ct);

    /// <summary>Ends the hunt (fighting what is due first) and returns its final tally. Rewards were credited per battle.</summary>
    public Task<Result<HuntView>> ClaimHuntAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<HuntView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            if (await repo.FindHuntAsync(uow, c.Id, ct) is not { } hunt) return Error.NotFound("no_hunt", "There is no hunt to collect.");
            hunt = await SettleHuntAsync(uow, c, hunt, now, ct);
            await repo.DeleteHuntAsync(uow, c.Id, ct);
            await AuditAsync(uow, accountId, "combat.hunt_collected", Guid.Empty,
                new { characterId, npc = hunt.NpcId, hunt.Battles, hunt.Wins, hunt.Losses, hunt.Qi }, now, ct);
            await uow.CommitAsync(ct);
            return HuntViewOf(hunt, now) with { Finished = true, NextBattleAt = null };
        }, ct);

    /// <summary>
    /// Fights every battle due since the last settlement (one per interval, until the hunt's time is up) as ordinary PvE
    /// battles played by the AI on both sides, at the moment each one was due. Rewards, loot, pet, quest progress and the
    /// daily rewarded-wins cap therefore work exactly as for battles played by hand.
    /// </summary>
    private async Task<IdleHunt> SettleHuntAsync(IUnitOfWork uow, CharacterSummary c, IdleHunt hunt, DateTimeOffset now, CancellationToken ct)
    {
        var rules = config.IdleRules;
        var interval = TimeSpan.FromMinutes(rules.MinutesPerBattle);
        var end = now < hunt.EndsAt(rules) ? now : hunt.EndsAt(rules);
        if (hunt.SettledAt + interval > end || config.Npc(hunt.NpcId) is not { } npc) return hunt;

        var a = await SnapshotAsync(uow, c, ct);
        var b = NpcCombatant(npc);
        var h = hunt;
        for (var n = 0; n < MaxBattlesPerSettle && h.SettledAt + interval <= end; n++)
        {
            var at = h.SettledAt + interval;
            // Stored as finished from the start: an idle battle is never "active", so it neither blocks nor is blocked by a
            // battle the player is fighting by hand (one active battle per character).
            var battle = NewBattle(BattleKinds.Pve, BattleStatuses.Finished, c, null, npc.Id, at) with
            {
                CombatantA = a,
                CombatantB = b,
                State = CombatEngine.Start(config, a, b),
                Deadline = at.AddSeconds(config.TurnDeadlineSeconds),
                FinishedAt = at,
            };
            await repo.AddBattleAsync(uow, battle, ct);
            for (var guard = 0; battle.State!.Outcome is null && guard <= config.MaxTurns + 1; guard++)
            {
                var st = battle.State!;
                var ia = CombatAi.Choose(config, a, st.A, st.Distance, CombatEngine.TurnRng(battle.Seed ^ 0xC3C3C3C3C3C3C3C3UL, st.Turn));
                battle = await ResolveAsync(uow, battle, ia, null, false, false, at, ct) ?? throw new TransientConflictException("idle battle race");
            }

            var won = battle.State!.Outcome == Outcomes.AWon;
            var lost = battle.State.Outcome == Outcomes.BWon;
            h = h with
            {
                SettledAt = at, Battles = h.Battles + 1, Wins = h.Wins + (won ? 1 : 0), Losses = h.Losses + (lost ? 1 : 0), Qi = h.Qi + battle.RewardQi,
            };
        }

        h = h with { Version = hunt.Version + 1 };
        if (!await repo.TrySaveHuntAsync(uow, h, hunt.Version, ct)) throw new TransientConflictException("hunt changed while settling");
        return h;
    }

    private HuntView HuntViewOf(IdleHunt h, DateTimeOffset now)
    {
        var rules = config.IdleRules;
        var ends = h.EndsAt(rules);
        var next = h.SettledAt.AddMinutes(rules.MinutesPerBattle);
        var npc = config.Npc(h.NpcId);
        return new HuntView(h.NpcId, npc?.NamePl ?? h.NpcId, npc?.Tier ?? 1, h.StartedAt, ends, next <= ends ? next : null, now >= ends || next > ends,
            h.Battles, h.Wins, h.Losses, h.Qi, rules.MinutesPerBattle, rules.MaxHours);
    }

    // ---------------------------------------------------------------- spirit pet

    public Task<Result<PetView?>> GetPetAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<PetView?>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var pet = await repo.FindPetAsync(uow, c.Id, ct);
            return pet is null || config.Npc(pet.NpcId) is not { } species ? (PetView?)null : PetViewOf(pet, species);
        }, ct);

    public Task<Result<PetView?>> ReleasePetAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<PetView?>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            if (await repo.FindPetAsync(uow, c.Id, ct) is null) return Error.NotFound("pet_not_found", "No spirit pet to release.");
            await repo.DeletePetAsync(uow, c.Id, ct);
            await AuditAsync(uow, accountId, "combat.pet_released", characterId, new { characterId }, clock.GetUtcNow(), ct);
            await uow.CommitAsync(ct);
            return (PetView?)null;
        }, ct);

    private static PetView PetViewOf(SpiritPet pet, NpcDefinition species) =>
        new(pet.NpcId, species.NamePl, pet.Level, pet.Xp, pet.Level >= SpiritPetRules.MaxLevel ? 0 : pet.Level * SpiritPetRules.XpPerLevel,
            SpiritPetRules.Bonuses(species, pet.Level), pet.TamedAt);

    private async Task<TurnRecord?> LastTurnAsync(IUnitOfWork uow, Guid battleId, CancellationToken ct)
    {
        var turns = await repo.ListTurnsAsync(uow, battleId, ct);
        return turns.Count == 0 ? null : turns[^1];
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, Guid battleId, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "battle", battleId.ToString(),
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}
