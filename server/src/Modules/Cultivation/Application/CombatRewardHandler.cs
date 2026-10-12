using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Cultivation.Domain;

namespace JianghuGo.Modules.Cultivation.Application;

/// <summary>Credits Qi won in battle. Exactly once per (event, character) thanks to cultivation.reward_grants.</summary>
internal sealed class CombatRewardHandler(ICultivationRepository repo, ICharacterDirectory characters, IAuditLog audit, CultivationConfig config) : IOutboxHandler
{
    public string EventType => CombatEvents.BattleFinished;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<CombatEvents.BattleFinishedV1>();
        foreach (var p in e.Participants.Where(p => p.RewardQi > 0))
        {
            var account = new AccountId(p.AccountId);
            var character = await characters.FindOwnedAsync(uow, account, p.CharacterId, ct);
            if (character is null) continue; // character gone: nothing to credit
            if (!await repo.TryRecordRewardAsync(uow, message.Id, character.Id, p.RewardQi, message.OccurredAt, ct)) continue;

            var today = CultivationRules.WorldDate(config, message.OccurredAt);
            var stored = await repo.FindAsync(uow, character.Id, ct);
            var state = (stored ?? CultivationState.Initial(character.Id, account, character.Path, today, message.OccurredAt)).ForDay(today);
            var version = stored?.Version ?? 0;
            var updated = state with { Qi = checked(state.Qi + p.RewardQi), Version = version + 1, UpdatedAt = message.OccurredAt };
            if (!await repo.TrySaveAsync(uow, updated, version, ct))
                throw new TransientConflictException("Cultivation state changed while crediting a combat reward.");
            await audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), account, "cultivation.combat_reward", "character", p.CharacterId.ToString(),
                JsonSerializer.Serialize(new { battleId = e.BattleId, qi = p.RewardQi }, Idempotency.Json), message.CorrelationId, message.OccurredAt), ct);
        }
    }
}
