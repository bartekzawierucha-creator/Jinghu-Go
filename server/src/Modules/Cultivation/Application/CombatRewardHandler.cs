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
        IUnitOfWork uow, OutboxMessage message, Guid accountId, Guid characterId, long qi, object auditData, CancellationToken ct)
    {
        if (qi <= 0) return;
        var account = new AccountId(accountId);
        var character = await characters.FindOwnedAsync(uow, account, characterId, ct);
        if (character is null) return; // character gone: nothing to credit
        if (!await repo.TryRecordRewardAsync(uow, message.Id, character.Id, qi, message.OccurredAt, ct)) return;

        var today = CultivationRules.WorldDate(config, message.OccurredAt);
        var stored = await repo.FindAsync(uow, character.Id, ct);
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
        return QiCredit.CreditAsync(repo, characters, audit, config, uow, message, e.AccountId, e.CharacterId, e.Qi, new { source = e.Source, qi = e.Qi }, ct);
    }
}

/// <summary>Qi from consumed pills (inventory.consumable_used).</summary>
internal sealed class ConsumableQiHandler(ICultivationRepository repo, ICharacterDirectory characters, IAuditLog audit, CultivationConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Inventory.Contracts.InventoryEvents.ConsumableUsed;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Inventory.Contracts.InventoryEvents.ConsumableUsedV1>();
        return QiCredit.CreditAsync(repo, characters, audit, config, uow, message, e.AccountId, e.CharacterId, e.Qi,
            new { source = "consumable", item = e.ItemId, e.Quantity, qi = e.Qi }, ct);
    }
}
