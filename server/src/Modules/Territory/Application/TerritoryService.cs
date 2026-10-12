using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Geolocation.Contracts;
using JianghuGo.Modules.Sects.Contracts;
using JianghuGo.Modules.Territory.Contracts;
using JianghuGo.Modules.Territory.Domain;

namespace JianghuGo.Modules.Territory.Application;

public sealed record WarView(Guid Id, string TerritoryKey, Guid AttackerSectId, Guid DefenderSectId, string Status, string Phase,
    DateTimeOffset DeclaredAt, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int AttackerScore, int DefenderScore, Guid? WinnerSectId, DateTimeOffset? ResolvedAt);

public sealed record OwnershipChangeView(Guid? FromSectId, Guid? ToSectId, string Reason, Guid? WarId, DateTimeOffset At);

public sealed record TerritoryView(string Key, double SouthLat, double WestLon, double SizeDegrees, Guid? OwnerSectId, string? OwnerName,
    DateTimeOffset? ClaimedAt, DateTimeOffset? NextTributeAt, WarView? War, IReadOnlyList<OwnershipChangeView> History);

public sealed record TerritoryActorCommand(Guid CharacterId);

public sealed record SectTerritoriesView(Guid SectId, int Owned, int Max, IReadOnlyList<TerritoryView> Territories, IReadOnlyList<WarView> Wars);

/// <summary>
/// Territory control and GvG wars. A territory is a fixed map cell; its key always comes from the actor's VERIFIED
/// position, never from the client. Wars are declared, prepared, fought by PvP duels between the two sects during the
/// window, and resolved lazily on the next read or write after the window closes (no background job to lose).
/// </summary>
public sealed class TerritoryService(
    IUnitOfWorkFactory uowFactory,
    ITerritoryRepository repo,
    ISectDirectory sects,
    ICharacterDirectory characters,
    IPositionVerifier positions,
    IIdempotencyStore idempotency,
    IAuditLog audit,
    IOutbox outbox,
    ICorrelationContext correlation,
    TerritoryConfig config,
    TimeProvider clock)
{
    private static readonly Error Denied = Error.Forbidden("sect_permission_denied", "Your rank in the sect does not allow this.");
    private static readonly Error NoSect = Error.Forbidden("not_in_sect", "Only sect members can do this.");

    private sealed record Ctx(CharacterSummary Character, SectMembership Membership, SectInfo Sect, string Key, DateTimeOffset Now);

    public Task<Result<TerritoryView>> GetAsync(string key, CancellationToken ct) =>
        uowFactory.RunAsync<Result<TerritoryView>>(async uow =>
        {
            if (!TerritoryRules.IsKey(key)) return Error.Validation("territory_key_invalid", "Invalid territory key.");
            var changed = await ResolveDueAsync(uow, key, clock.GetUtcNow(), ct);
            var view = await ViewAsync(uow, key, ct);
            if (changed) await uow.CommitAsync(ct);
            return view;
        }, ct);

    /// <summary>The territory where the character stands now (verified position).</summary>
    public Task<Result<TerritoryView>> HereAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<TerritoryView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return Error.NotFound("character_not_found", "Character not found.");
            var now = clock.GetUtcNow();
            var check = await positions.CheckAsync(uow, accountId, c.Id, now, ct);
            if (check.Verified is not { } p) return Error.Conflict(check.Problem!, $"Your position cannot be used right now ({check.Problem}).");
            var key = TerritoryRules.KeyOf(config, p.Point);
            var changed = await ResolveDueAsync(uow, key, now, ct);
            var view = await ViewAsync(uow, key, ct);
            if (changed) await uow.CommitAsync(ct);
            return view;
        }, ct);

    public Task<Result<SectTerritoriesView>> ForSectAsync(Guid sectId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<SectTerritoriesView>>(async uow =>
        {
            var sect = await sects.FindAsync(uow, sectId, ct);
            if (sect is null) return Error.NotFound("sect_not_found", "Sect not found.");
            var now = clock.GetUtcNow();
            var changed = false;
            foreach (var w in await repo.ListOpenWarsAsync(uow, sectId, ct)) changed |= await ResolveDueAsync(uow, w.TerritoryKey, now, ct);
            var owned = await repo.ListOwnedAsync(uow, sectId, ct);
            var views = new List<TerritoryView>();
            foreach (var t in owned) views.Add(await ViewAsync(uow, t.Key, ct));
            var wars = await repo.ListOpenWarsAsync(uow, sectId, ct);
            var result = new SectTerritoriesView(sectId, owned.Count, TerritoryRules.MaxTerritories(config, sect.MemberCount), views, [.. wars.Select(w => View(w, now))]);
            if (changed) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>Claim the unowned territory you stand in for your sect (treasury pays). Leader or elder.</summary>
    public Task<Result<TerritoryView>> ClaimAsync(AccountId accountId, string? key, TerritoryActorCommand cmd, CancellationToken ct) =>
        Write<TerritoryView, object>(accountId, "territory.claim", key, cmd, async uow =>
        {
            var ctx = await ContextAsync(uow, accountId, cmd.CharacterId, SectActionNames.WageWar, ct);
            if (!ctx.IsSuccess) return ctx.Error!;
            var (c, m, sect, tkey, now) = (ctx.Value.Character, ctx.Value.Membership, ctx.Value.Sect, ctx.Value.Key, ctx.Value.Now);
            var territory = await repo.FindAsync(uow, tkey, ct);
            if (territory?.OwnerSectId == sect.Id) return Error.Conflict("already_owned", "Your sect already controls this territory.");
            if (territory?.OwnerSectId is { } owner && await sects.FindAsync(uow, owner, ct) is { Disbanded: false })
                return Error.Conflict("territory_owned", "Another sect controls this territory — challenge it instead.");
            if (await repo.FindOpenWarAsync(uow, tkey, ct) is not null) return Error.Conflict("war_in_progress", "A war is being fought over this territory.");
            if ((await repo.ListOwnedAsync(uow, sect.Id, ct)).Count >= TerritoryRules.MaxTerritories(config, sect.MemberCount))
                return Error.LimitReached("territory_limit", "Your sect controls as many territories as its size allows.");

            var reference = Uuid7.New().ToString();
            if (config.ClaimStones > 0)
            {
                var paid = await sects.DebitTreasuryAsync(uow, sect.Id, config.ClaimStones, "territory_claim", reference, ct);
                if (!paid.IsSuccess) return paid.Error!;
            }

            var version = territory?.Version ?? 0;
            var updated = new Domain.Territory(tkey, sect.Id, now, null, version + 1);
            if (!await repo.TrySaveAsync(uow, updated, version, ct)) return Error.Conflict("version_conflict", "The territory changed concurrently; retry.");
            await repo.AddOwnershipChangeAsync(uow, new OwnershipChange(Uuid7.New(), tkey, territory?.OwnerSectId, sect.Id, OwnershipReasons.Claim, null, now), ct);
            await AuditAsync(uow, accountId, "territory.claimed", tkey, new { sect = sect.Id, by = c.Id.Value, role = m.Role, previousOwner = territory?.OwnerSectId }, now, ct);
            return await ViewAsync(uow, tkey, ct);
        }, ct);

    /// <summary>Declare war on the territory you stand in. Treasury pays the challenge; the war starts after the preparation time.</summary>
    public Task<Result<WarView>> ChallengeAsync(AccountId accountId, string? key, TerritoryActorCommand cmd, CancellationToken ct) =>
        Write<WarView, object>(accountId, "territory.challenge", key, cmd, async uow =>
        {
            var ctx = await ContextAsync(uow, accountId, cmd.CharacterId, SectActionNames.WageWar, ct);
            if (!ctx.IsSuccess) return ctx.Error!;
            var (c, sect, tkey, now) = (ctx.Value.Character, ctx.Value.Sect, ctx.Value.Key, ctx.Value.Now);
            var territory = await repo.FindAsync(uow, tkey, ct);
            if (territory?.OwnerSectId is not { } defenderId) return Error.Conflict("territory_unowned", "Nobody controls this territory — claim it instead.");
            if (defenderId == sect.Id) return Error.Conflict("already_owned", "Your sect already controls this territory.");
            var defender = await sects.FindAsync(uow, defenderId, ct);
            if (defender is null || defender.Disbanded) return Error.Conflict("territory_unowned", "The owning sect has disbanded — claim the territory instead.");
            if (await repo.FindOpenWarAsync(uow, tkey, ct) is not null) return Error.Conflict("war_in_progress", "A war is already declared for this territory.");
            if ((await repo.ListOpenWarsAsync(uow, sect.Id, ct)).Any(w => w.AttackerSectId == sect.Id))
                return Error.LimitReached("war_limit", "Your sect is already attacking another territory.");
            if ((await repo.ListOwnedAsync(uow, sect.Id, ct)).Count >= TerritoryRules.MaxTerritories(config, sect.MemberCount))
                return Error.LimitReached("territory_limit", "Your sect could not hold another territory.");

            var war = new War(Uuid7.New(), tkey, sect.Id, defenderId, WarStatuses.Declared, now, now.AddHours(config.PreparationHours),
                now.AddHours(config.PreparationHours + config.WarHours), 0, 0, null, null, 1);
            if (config.ChallengeStones > 0)
            {
                var paid = await sects.DebitTreasuryAsync(uow, sect.Id, config.ChallengeStones, "territory_challenge", war.Id.ToString(), ct);
                if (!paid.IsSuccess) return paid.Error!;
            }

            await repo.AddWarAsync(uow, war, ct);
            await outbox.EnqueueAsync(uow, OutboxMessage.Create(TerritoryEvents.WarDeclared,
                new TerritoryEvents.WarDeclaredV1(war.Id, tkey, sect.Id, sect.Name, defenderId, defender.LeaderAccountId, war.StartsAt, war.EndsAt), now, correlation.CorrelationId), ct);
            await AuditAsync(uow, accountId, "territory.war_declared", tkey, new { war = war.Id, attacker = sect.Id, defender = defenderId, by = c.Id.Value }, now, ct);
            return View(war, now);
        }, ct);

    /// <summary>Leader or elder of the owning sect collects the territory's tribute into the treasury, once per interval.</summary>
    public Task<Result<TerritoryView>> TributeAsync(AccountId accountId, string territoryKey, string? key, TerritoryActorCommand cmd, CancellationToken ct) =>
        Write<TerritoryView, object>(accountId, "territory.tribute", key, new { territoryKey, cmd }, async uow =>
        {
            if (!TerritoryRules.IsKey(territoryKey)) return Error.Validation("territory_key_invalid", "Invalid territory key.");
            var c = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
            if (c is null) return Error.NotFound("character_not_found", "Character not found.");
            var m = await sects.MembershipAsync(uow, c.Id, ct);
            if (m is null) return NoSect;
            if (!sects.Can(m.Role, SectActionNames.CollectTribute)) return Denied;
            var now = clock.GetUtcNow();
            await ResolveDueAsync(uow, territoryKey, now, ct);
            var t = await repo.FindAsync(uow, territoryKey, ct);
            if (t?.OwnerSectId != m.SectId) return Error.Forbidden("not_owner", "Your sect does not control this territory.");
            if (t.LastTributeAt is { } last && last.AddHours(config.TributeIntervalHours) > now)
                return Error.Conflict("tribute_not_ready", $"Next tribute after {last.AddHours(config.TributeIntervalHours):O}.");
            var updated = t with { LastTributeAt = now, Version = t.Version + 1 };
            if (!await repo.TrySaveAsync(uow, updated, t.Version, ct)) return Error.Conflict("version_conflict", "The territory changed concurrently; retry.");
            if (config.TributeStones > 0)
            {
                var credited = await sects.CreditTreasuryAsync(uow, m.SectId, config.TributeStones, "territory_tribute", $"{territoryKey}:{now.ToUnixTimeSeconds()}", ct);
                if (!credited.IsSuccess) return credited.Error!;
            }

            await AuditAsync(uow, accountId, "territory.tribute_collected", territoryKey, new { sect = m.SectId, by = c.Id.Value, amount = config.TributeStones }, now, ct);
            return await ViewAsync(uow, territoryKey, ct);
        }, ct);

    /// <summary>Resolves the territory's war if its window has closed. True if anything was written.</summary>
    public async Task<bool> ResolveDueAsync(IUnitOfWork uow, string key, DateTimeOffset now, CancellationToken ct)
    {
        var war = await repo.FindOpenWarAsync(uow, key, ct);
        if (war is null || !war.Due(now)) return false;
        var winner = TerritoryRules.Winner(war);
        var resolved = war with { Status = WarStatuses.Resolved, WinnerSectId = winner, ResolvedAt = now, Version = war.Version + 1 };
        if (!await repo.TryUpdateWarAsync(uow, resolved, war.Version, ct)) throw new TransientConflictException("War changed while resolving.");

        if (winner == war.AttackerSectId)
        {
            var t = await repo.FindAsync(uow, key, ct);
            if (t is not null)
            {
                var transferred = t with { OwnerSectId = war.AttackerSectId, ClaimedAt = now, LastTributeAt = null, Version = t.Version + 1 };
                if (!await repo.TrySaveAsync(uow, transferred, t.Version, ct)) throw new TransientConflictException("Territory changed while resolving.");
                await repo.AddOwnershipChangeAsync(uow, new OwnershipChange(Uuid7.New(), key, t.OwnerSectId, war.AttackerSectId, OwnershipReasons.War, war.Id, now), ct);
            }
        }

        var leaders = new List<Guid>();
        foreach (var id in new[] { war.AttackerSectId, war.DefenderSectId })
        {
            if (await sects.FindAsync(uow, id, ct) is { Disbanded: false } s) leaders.Add(s.LeaderAccountId);
        }

        await outbox.EnqueueAsync(uow, OutboxMessage.Create(TerritoryEvents.WarResolved,
            new TerritoryEvents.WarResolvedV1(war.Id, key, war.AttackerSectId, war.DefenderSectId, winner, war.AttackerScore, war.DefenderScore, leaders),
            now, correlation.CorrelationId), ct);
        await AuditAsync(uow, null, "territory.war_resolved", key, new { war = war.Id, winner, war.AttackerScore, war.DefenderScore }, now, ct);
        return true;
    }

    private async Task<Result<Ctx>> ContextAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, string action, CancellationToken ct)
    {
        var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
        if (c is null) return Error.NotFound("character_not_found", "Character not found.");
        var m = await sects.MembershipAsync(uow, c.Id, ct);
        if (m is null) return NoSect;
        if (!sects.Can(m.Role, action)) return Denied;
        var sect = (await sects.FindAsync(uow, m.SectId, ct))!;
        var now = clock.GetUtcNow();
        var check = await positions.CheckAsync(uow, accountId, c.Id, now, ct);
        if (check.Verified is not { } p) return Error.Conflict(check.Problem!, $"You must be in the territory with a verified position ({check.Problem}).");
        var key = TerritoryRules.KeyOf(config, p.Point);
        await ResolveDueAsync(uow, key, now, ct);
        return new Ctx(c, m, sect, key, now);
    }

    private Task<Result<T>> Write<T, TRequest>(AccountId accountId, string operation, string? key, TRequest request, Func<IUnitOfWork, Task<Result<T>>> action, CancellationToken ct) =>
        uowFactory.RunAsync<Result<T>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, operation, key, request, () => action(uow), ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    private async Task<TerritoryView> ViewAsync(IUnitOfWork uow, string key, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var t = await repo.FindAsync(uow, key, ct);
        var owner = t?.OwnerSectId is { } id ? await sects.FindAsync(uow, id, ct) : null;
        if (owner is { Disbanded: true }) owner = null;
        var latest = await repo.ListWarsAsync(uow, key, 1, ct);
        var war = await repo.FindOpenWarAsync(uow, key, ct) ?? (latest.Count > 0 ? latest[0] : null);
        var history = await repo.ListOwnershipChangesAsync(uow, key, 20, ct);
        var (lat, lon, size) = TerritoryRules.Bounds(config, key);
        return new TerritoryView(key, lat, lon, size, owner?.Id, owner?.Name, owner is null ? null : t!.ClaimedAt,
            owner is null ? null : t!.LastTributeAt?.AddHours(config.TributeIntervalHours) ?? now,
            war is null ? null : View(war, now), [.. history.Select(h => new OwnershipChangeView(h.FromSectId, h.ToSectId, h.Reason, h.WarId, h.At))]);
    }

    private static WarView View(War w, DateTimeOffset now) => new(w.Id, w.TerritoryKey, w.AttackerSectId, w.DefenderSectId, w.Status,
        w.Status == WarStatuses.Resolved ? "resolved" : now < w.StartsAt ? "preparation" : now < w.EndsAt ? "battle" : "awaiting_resolution",
        w.DeclaredAt, w.StartsAt, w.EndsAt, w.AttackerScore, w.DefenderScore, w.WinnerSectId, w.ResolvedAt);

    private Task AuditAsync(IUnitOfWork uow, AccountId? accountId, string action, string territoryKey, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "territory", territoryKey,
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}

/// <summary>
/// War scoring: a finished PvP duel between members of two sects at war, fought inside the war window, gives the
/// winner's sect one point in every such war — exactly once per (war, battle).
/// </summary>
internal sealed class WarScoringHandler(ITerritoryRepository repo, ISectDirectory sects) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Combat.Contracts.CombatEvents.BattleFinished;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Combat.Contracts.CombatEvents.BattleFinishedV1>();
        if (e.Kind != "pvp" || e.Participants.Count != 2) return;
        var winner = e.Participants.FirstOrDefault(p => p.Result == "won");
        var loser = e.Participants.FirstOrDefault(p => p.Result == "lost");
        if (winner is null || loser is null) return;
        var ws = await sects.MembershipAsync(uow, new CharacterId(winner.CharacterId), ct);
        var ls = await sects.MembershipAsync(uow, new CharacterId(loser.CharacterId), ct);
        if (ws is null || ls is null || ws.SectId == ls.SectId) return;

        foreach (var war in await repo.ListOpenWarsAsync(uow, ws.SectId, ct))
        {
            var opponents = (war.AttackerSectId == ws.SectId && war.DefenderSectId == ls.SectId) || (war.DefenderSectId == ws.SectId && war.AttackerSectId == ls.SectId);
            if (!opponents || !war.InWindow(message.OccurredAt)) continue;
            if (!await repo.TryRecordWarBattleAsync(uow, war.Id, e.BattleId, ws.SectId, message.OccurredAt, ct)) continue;
            var scored = war.AttackerSectId == ws.SectId
                ? war with { AttackerScore = war.AttackerScore + 1, Version = war.Version + 1 }
                : war with { DefenderScore = war.DefenderScore + 1, Version = war.Version + 1 };
            if (!await repo.TryUpdateWarAsync(uow, scored, war.Version, ct)) throw new TransientConflictException("War changed while scoring.");
        }
    }
}
