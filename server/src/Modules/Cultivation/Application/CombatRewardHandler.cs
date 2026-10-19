using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Cultivation.Domain;

namespace JianghuGo.Modules.Cultivation.Application;

/// <summary>Credits Qi granted by another module's event, exactly once per (event, character) via cultivation.reward_grants.</summary>
internal static class QiCredit
{
    public static async Task CreditAsync(
        ICultivationRepository repo, ICharacterDirectory characters, IAuditLog audit, CultivationConfig config,
        IUnitOfWork uow, OutboxMessage message, Guid accountId, Guid characterId, long qi, object auditData, CancellationToken ct, bool realmScaled = false, int homeRealm = 0)
    {
        if (qi <= 0) return;
        var account = new AccountId(accountId);
        var character = await characters.FindOwnedAsync(uow, account, characterId, ct);
        if (character is null) return; // character gone: nothing to credit
        var stored = await repo.FindAsync(uow, character.Id, ct);
        // 1.6: flat rewards (world nodes, pills) follow the cultivation curve like the method's Qi per win does
        if (realmScaled && stored is not null) qi = CultivationRules.RealmScaledQi(config, qi, stored.RealmIndex - homeRealm);
        if (!await repo.TryRecordRewardAsync(uow, message.Id, character.Id, qi, message.OccurredAt, ct)) return;

        var today = CultivationRules.WorldDate(config, message.OccurredAt);
        var state = (stored ?? CultivationState.Initial(character.Id, account, character.Path, today, message.OccurredAt)).ForDay(today);
        var version = stored?.Version ?? 0;
        var updated = state with { Qi = checked(state.Qi + qi), Version = version + 1, UpdatedAt = message.OccurredAt };
        if (!await repo.TrySaveAsync(uow, updated, version, ct))
            throw new TransientConflictException("Cultivation state changed while crediting a reward.");
        // a character's first cultivation row: start its profile on the current ladder (an old row without one is migrated on load)
        if (stored is null && await repo.FindProfileAsync(uow, character.Id, ct) is null)
            await repo.TrySaveProfileAsync(uow, CultivationProfile.New(character.Id, character.Path, message.OccurredAt) with { Version = 1 }, 0, ct);
        await audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), account, "cultivation.reward_credited", "character", characterId.ToString(),
            JsonSerializer.Serialize(auditData, Idempotency.Json), message.CorrelationId, message.OccurredAt), ct);
    }
}

internal sealed class CombatRewardHandler(ICultivationRepository repo, ICharacterDirectory characters, IAuditLog audit, CultivationConfig config) : IOutboxHandler
{
    public string EventType => CombatEvents.BattleFinished;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<CombatEvents.BattleFinishedV1>();
        foreach (var p in e.Participants.Where(p => p.RewardQi > 0))
        {
            await QiCredit.CreditAsync(repo, characters, audit, config, uow, message, p.AccountId, p.CharacterId, p.RewardQi,
                new { source = "combat", battleId = e.BattleId, qi = p.RewardQi }, ct);
        }
    }
}

internal sealed class WorldRewardHandler(ICultivationRepository repo, ICharacterDirectory characters, IAuditLog audit, CultivationConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.World.Contracts.WorldEvents.QiReward;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.World.Contracts.WorldEvents.QiRewardV1>();
        return QiCredit.CreditAsync(repo, characters, audit, config, uow, message, e.AccountId, e.CharacterId, e.Qi, new { source = e.Source, qi = e.Qi }, ct, realmScaled: true);
    }
}

/// <summary>
/// Qi from consumed pills (inventory.consumable_used). 1.6: a pill is tuned for the realm its grade belongs to (mortal 0, spirit 1,
/// earth 2, heavenly 4, divine 6) and grows with the cultivation curve above it. 1.8: tolerance — every pill of the same kind
/// already taken this world day works 25 % weaker (the body needs time to absorb), so pills help without replacing cultivation.
/// </summary>
internal sealed class ConsumableQiHandler(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes, CultivationConfig config,
    JianghuGo.Modules.Inventory.Contracts.IItemCatalog items) : IOutboxHandler
{
    public const int TolerancePermille = 750;

    public static int HomeRealm(string? rarity) => rarity switch { "spirit" => 1, "earth" => 2, "heavenly" => 4, "divine" => 6, _ => 0 };

    public string EventType => JianghuGo.Modules.Inventory.Contracts.InventoryEvents.ConsumableUsed;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Inventory.Contracts.InventoryEvents.ConsumableUsedV1>();
        var home = HomeRealm(items.Find(e.ItemId)?.Rarity);
        return ProfileUpdate.ApplyAsync(scopes, uow, message, "consumable_qi", e.AccountId, e.CharacterId, l =>
        {
            var today = CultivationRules.WorldDate(config, message.OccurredAt);
            var taken = l.Profile.PillsTaken(e.ItemId, today);
            var perPill = CultivationRules.RealmScaledQi(config, e.Qi / Math.Max(1, e.Quantity), l.State.RealmIndex - home);
            long qi = 0;
            for (var n = 0; n < e.Quantity; n++) qi += (long)(perPill * Math.Pow(TolerancePermille / 1000.0, taken + n));
            var counts = new Dictionary<string, int>(l.Profile.PillDay == today ? l.Profile.PillsToday ?? new Dictionary<string, int>() : new Dictionary<string, int>())
            {
                [e.ItemId] = taken + e.Quantity,
            };
            return l with
            {
                State = l.State with { Qi = l.State.Qi + qi },
                Profile = l.Profile with { PillDay = today, PillsToday = counts, LifetimeQi = l.Profile.LifetimeQi + qi },
            };
        }, ct);
    }
}
