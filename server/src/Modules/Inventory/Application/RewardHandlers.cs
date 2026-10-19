using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Inventory.Domain;

namespace JianghuGo.Modules.Inventory.Application;

/// <summary>
/// Payload mirrors of events published by modules that depend on Inventory (Combat, World). Inventory cannot reference
/// them without a project cycle; the event JSON is the contract (pinned by the contract tests in both modules).
/// </summary>
internal static class EventMirrors
{
    public const string BattleFinished = "combat.battle_finished";
    public const string ItemsGathered = "world.items_gathered";
    public const string CombatItemsRewarded = "combat.items_rewarded";

    public sealed record RewardLine(string ItemId, int Quantity);

    public sealed record CombatItemsRewarded1(Guid AccountId, Guid CharacterId, string Source, string ReferenceId, long Stones, IReadOnlyList<RewardLine> Items);

    public sealed record BattleParticipant(Guid AccountId, Guid CharacterId, string Side, string Result, long RewardQi, int StonesPermille = 1000, int LuckPermille = 0, long? Stones = null);

    public sealed record BattleFinished1(Guid BattleId, string Kind, string Outcome, string? NpcId, IReadOnlyList<BattleParticipant> Participants);

    public sealed record GatheredItem(string ItemId, int Quantity);

    public sealed record ItemsGathered1(Guid AccountId, Guid CharacterId, string Source, IReadOnlyList<GatheredItem> Items);
}

/// <summary>Grants rewards exactly once per (event, character): inventory.grants is the guard, written in the same transaction.</summary>
internal static class RewardGrant
{
    public static async Task ApplyAsync(
        IInventoryRepository repo, InventoryLedger ledger, ICharacterDirectory characters, IAuditLog audit,
        IUnitOfWork uow, OutboxMessage message, Guid accountId, Guid characterId, string kind, string referenceId,
        long stones, IReadOnlyList<LootLine> items, ItemsConfig config, CancellationToken ct)
    {
        var account = new AccountId(accountId);
        var character = await characters.FindOwnedAsync(uow, account, characterId, ct);
        if (character is null) return;
        if (!await repo.TryRecordGrantAsync(uow, message.Id, character.Id, kind, message.OccurredAt, ct)) return;

        if (stones > 0)
        {
            var credited = await ledger.CreditAsync(uow, character.Id, stones, kind, referenceId, ct);
            if (!credited.IsSuccess) throw new InvalidOperationException("Reward credit failed: " + credited.Error!.Code);
        }

        var granted = new List<object>();
        var clipped = new List<object>();
        foreach (var line in items.Where(l => l.Quantity > 0))
        {
            if (config.Find(line.ItemId) is not { } def) continue;
            if (def.Stackable)
            {
                var (g, c) = await ledger.GrantItemsAsync(uow, character.Id, line.ItemId, line.Quantity, kind, referenceId, ct);
                if (g > 0) granted.Add(new { item = line.ItemId, quantity = g });
                if (c > 0) clipped.Add(new { item = line.ItemId, quantity = c });
            }
            else
            {
                for (var n = 0; n < line.Quantity; n++)
                {
                    var created = await ledger.CreateUncheckedAsync(uow, character.Id, account, line.ItemId, kind, referenceId, ct);
                    granted.Add(new { item = line.ItemId, instanceId = created.Id });
                }
            }
        }

        await audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), account, "inventory.reward_granted", "character", characterId.ToString(),
            JsonSerializer.Serialize(new { kind, referenceId, stones, granted, clipped }, Idempotency.Json), message.CorrelationId, message.OccurredAt), ct);
    }
}

/// <summary>Loot for rewarded PvE victories (only participants with RewardQi &gt; 0: the daily reward cap applies to loot too).</summary>
internal sealed class CombatLootHandler(IInventoryRepository repo, InventoryLedger ledger, ICharacterDirectory characters, IAuditLog audit, ItemsConfig config) : IOutboxHandler
{
    public string EventType => EventMirrors.BattleFinished;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<EventMirrors.BattleFinished1>();
        // Only ordinary PvE: tower floors and the world boss pay their own rewards (combat.items_rewarded).
        if (e.Kind != "pve" || e.NpcId is null || !config.LootTables.TryGetValue(e.NpcId, out var table)) return;
        foreach (var p in e.Participants.Where(p => p.RewardQi > 0))
        {
            // Seed from the outbox message id: generated server-side, never exposed, stable across handler retries.
            // stones grow with the realm the opponent was raised to; Fate (luck) raises every drop chance (1.6)
            var (stones, items) = InventoryRules.RollLoot(table, InventoryRules.SeedFrom(message.Id, p.CharacterId), p.LuckPermille);
            stones = p.Stones ?? (stones * Math.Max(1000, p.StonesPermille) / 1000); // 1.7: Combat sends the stones by threat tier
            await RewardGrant.ApplyAsync(repo, ledger, characters, audit, uow, message, p.AccountId, p.CharacterId,
                "loot", e.BattleId.ToString(), stones, items, config, ct);
        }
    }
}

/// <summary>Materials gathered from world nodes (the World module rolls quantities from its secret seed).</summary>
internal sealed class WorldItemsHandler(IInventoryRepository repo, InventoryLedger ledger, ICharacterDirectory characters, IAuditLog audit, ItemsConfig config) : IOutboxHandler
{
    public string EventType => EventMirrors.ItemsGathered;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<EventMirrors.ItemsGathered1>();
        return RewardGrant.ApplyAsync(repo, ledger, characters, audit, uow, message, e.AccountId, e.CharacterId,
            "gather", message.Id.ToString(), 0, [.. e.Items.Select(i => new LootLine(i.ItemId, i.Quantity))], config, ct);
    }
}

/// <summary>Tower of Trials milestones and world boss kill rewards (Combat decides who earned what; granted once per event).</summary>
internal sealed class CombatItemsHandler(IInventoryRepository repo, InventoryLedger ledger, ICharacterDirectory characters, IAuditLog audit, ItemsConfig config) : IOutboxHandler
{
    public string EventType => EventMirrors.CombatItemsRewarded;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<EventMirrors.CombatItemsRewarded1>();
        return RewardGrant.ApplyAsync(repo, ledger, characters, audit, uow, message, e.AccountId, e.CharacterId,
            e.Source, e.ReferenceId, e.Stones, [.. e.Items.Select(i => new LootLine(i.ItemId, i.Quantity))], config, ct);
    }
}
