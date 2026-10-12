using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.Modules.Inventory.Domain;

namespace JianghuGo.Modules.Inventory.Application;

public sealed record StackView(string ItemId, string NamePl, string Type, string Rarity, long Quantity);

public sealed record InstanceView(Guid Id, string ItemId, string NamePl, string Rarity, string? Slot, int UpgradeLevel, string State, int Version,
    IReadOnlyDictionary<string, int> Stats);

public sealed record InventoryView(Guid CharacterId, string RulesVersion, long Balance, int SlotsUsed, int Capacity,
    IReadOnlyList<StackView> Stacks, IReadOnlyList<InstanceView> Items, IReadOnlyDictionary<string, Guid> Equipped, IReadOnlyDictionary<string, int> Bonuses);

public sealed record EquipCommand(Guid InstanceId, int ExpectedVersion);

public sealed record UseCommand(string ItemId, int Quantity);

public sealed record UseResult(string ItemId, int Quantity, long QiGranted, long Remaining);

public sealed record DiscardCommand(string? ItemId, int Quantity, Guid? InstanceId);

public sealed record WalletEntryView(Guid Id, long Delta, long BalanceAfter, string Reason, string ReferenceId, DateTimeOffset At);

public sealed record ItemEventView(Guid Id, string Kind, string Data, DateTimeOffset At);

public sealed class InventoryService(
    IUnitOfWorkFactory uowFactory,
    IInventoryRepository repo,
    InventoryLedger ledger,
    ICharacterDirectory characters,
    IIdempotencyStore idempotency,
    IAuditLog audit,
    IOutbox outbox,
    ICorrelationContext correlation,
    ItemsConfig config,
    TimeProvider clock)
{
    public const int MaxUsePerRequest = 10;

    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");
    private static readonly Error ItemNotFound = Error.NotFound("item_not_found", "Item not found.");
    private static readonly Error Concurrent = Error.Conflict("version_conflict", "The item changed concurrently; reload and retry.");

    public Task<Result<InventoryView>> GetAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<InventoryView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            return c is null ? CharacterNotFound : await ViewAsync(uow, c.Id, ct);
        }, ct);

    /// <summary>Equips a unique item from the bag; an item already in that slot goes back to the bag (swap).</summary>
    public Task<Result<InventoryView>> EquipAsync(AccountId accountId, Guid characterId, EquipCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<InventoryView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var instances = await repo.ListInstancesAsync(uow, c.Id, ct);
            var item = instances.FirstOrDefault(i => i.Id == cmd.InstanceId);
            if (item is null) return ItemNotFound;
            if (item.Version != cmd.ExpectedVersion) return Concurrent;
            if (item.State == InstanceStates.Equipped) return await ViewAsync(uow, c.Id, ct);
            if (item.State != InstanceStates.Bag) return Error.Conflict("item_listed", "The item is listed on the market.");
            var slot = InventoryRules.SlotOf(config, item.ItemId)!;
            var previous = instances.FirstOrDefault(i => i.State == InstanceStates.Equipped && InventoryRules.SlotOf(config, i.ItemId) == slot);

            var after = instances.Select(i => i.Id == item.Id ? i with { State = InstanceStates.Equipped }
                : previous is not null && i.Id == previous.Id ? i with { State = InstanceStates.Bag } : i).ToList();
            if (await OverflowGrowsAsync(uow, c.Id, instances, after, ct)) return Error.LimitReached("bag_full", "Your bag cannot hold the swapped item.");

            if (previous is not null && !await MoveAsync(uow, previous, InstanceStates.Bag, "unequipped", ct)) return Concurrent;
            if (!await MoveAsync(uow, item, InstanceStates.Equipped, "equipped", ct)) return Concurrent;
            await AuditAsync(uow, accountId, "inventory.equipped", c.Id, new { instanceId = item.Id, item.ItemId, slot, replaced = previous?.Id }, ct);
            var view = await ViewAsync(uow, c.Id, ct);
            await uow.CommitAsync(ct);
            return view;
        }, ct);

    public Task<Result<InventoryView>> UnequipAsync(AccountId accountId, Guid characterId, EquipCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<InventoryView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var instances = await repo.ListInstancesAsync(uow, c.Id, ct);
            var item = instances.FirstOrDefault(i => i.Id == cmd.InstanceId);
            if (item is null) return ItemNotFound;
            if (item.Version != cmd.ExpectedVersion) return Concurrent;
            if (item.State != InstanceStates.Equipped) return await ViewAsync(uow, c.Id, ct);
            var after = instances.Select(i => i.Id == item.Id ? i with { State = InstanceStates.Bag } : i).ToList();
            if (await OverflowGrowsAsync(uow, c.Id, instances, after, ct)) return Error.LimitReached("bag_full", "Your bag has no room for this item.");
            if (!await MoveAsync(uow, item, InstanceStates.Bag, "unequipped", ct)) return Concurrent;
            await AuditAsync(uow, accountId, "inventory.unequipped", c.Id, new { instanceId = item.Id, item.ItemId }, ct);
            var view = await ViewAsync(uow, c.Id, ct);
            await uow.CommitAsync(ct);
            return view;
        }, ct);

    /// <summary>Consumes pills. Qi is credited by Cultivation from the outbox event, exactly once.</summary>
    public Task<Result<UseResult>> UseAsync(AccountId accountId, Guid characterId, string? key, UseCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<UseResult>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "inventory.use", key, new { characterId, cmd }, async Task<Result<UseResult>> () =>
            {
                if (cmd.Quantity is < 1 or > MaxUsePerRequest) return Error.Validation("quantity_invalid", $"Quantity must be 1-{MaxUsePerRequest}.");
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                if (config.Find(cmd.ItemId)?.Consumable is not { } effect) return Error.Validation("item_not_consumable", "This item cannot be used.");
                var reference = Uuid7.New().ToString();
                var removed = await ledger.RemoveItemsAsync(uow, c.Id, cmd.ItemId, cmd.Quantity, "use", reference, ct);
                if (!removed.IsSuccess) return removed.Error!;
                var qi = checked(effect.Qi * cmd.Quantity);
                var now = clock.GetUtcNow();
                await outbox.EnqueueAsync(uow, OutboxMessage.Create(InventoryEvents.ConsumableUsed,
                    new InventoryEvents.ConsumableUsedV1(accountId.Value, c.Id.Value, cmd.ItemId, cmd.Quantity, qi), now, correlation.CorrelationId), ct);
                await AuditAsync(uow, accountId, "inventory.consumable_used", c.Id, new { cmd.ItemId, cmd.Quantity, qi }, ct);
                return new UseResult(cmd.ItemId, cmd.Quantity, qi, removed.Value);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>Destroys items to free bag space. Stackables by (itemId, quantity); unique items by instanceId.</summary>
    public Task<Result<InventoryView>> DiscardAsync(AccountId accountId, Guid characterId, string? key, DiscardCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<InventoryView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "inventory.discard", key, new { characterId, cmd }, async Task<Result<InventoryView>> () =>
            {
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                if ((cmd.ItemId is null) == (cmd.InstanceId is null))
                    return Error.Validation("discard_target_invalid", "Give either itemId with quantity, or instanceId.");
                if (cmd.InstanceId is { } id)
                {
                    var item = await repo.FindInstanceAsync(uow, id, ct);
                    if (item is null || item.CharacterId != c.Id || item.State == InstanceStates.Destroyed) return ItemNotFound;
                    if (item.State != InstanceStates.Bag) return Error.Conflict("item_not_in_bag", "Unequip the item (or cancel its listing) first.");
                    if (!await MoveAsync(uow, item, InstanceStates.Destroyed, "discarded", ct)) return Concurrent;
                }
                else
                {
                    if (cmd.Quantity < 1) return Error.Validation("quantity_invalid", "Quantity must be positive.");
                    var removed = await ledger.RemoveItemsAsync(uow, c.Id, cmd.ItemId!, cmd.Quantity, "discard", Uuid7.New().ToString(), ct);
                    if (!removed.IsSuccess) return removed.Error!;
                }

                await AuditAsync(uow, accountId, "inventory.discarded", c.Id, new { cmd.ItemId, cmd.Quantity, cmd.InstanceId }, ct);
                return await ViewAsync(uow, c.Id, ct);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    public Task<Result<IReadOnlyList<WalletEntryView>>> WalletAsync(AccountId accountId, Guid characterId, Guid? before, int? limit, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<WalletEntryView>>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var take = Math.Clamp(limit ?? 50, 1, 200);
            var rows = await repo.ListWalletEntriesAsync(uow, c.Id, before, take, ct);
            return Result<IReadOnlyList<WalletEntryView>>.Success([.. rows.Select(e => new WalletEntryView(e.Id, e.Delta, e.BalanceAfter, e.Reason, e.ReferenceId, e.At))]);
        }, ct);

    /// <summary>Provenance of a unique item, visible to its current owner.</summary>
    public Task<Result<IReadOnlyList<ItemEventView>>> HistoryAsync(AccountId accountId, Guid characterId, Guid instanceId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<ItemEventView>>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var item = await repo.FindInstanceAsync(uow, instanceId, ct);
            if (item is null || item.CharacterId != c.Id || item.State == InstanceStates.Destroyed) return ItemNotFound;
            var events = await repo.ListItemEventsAsync(uow, instanceId, ct);
            return Result<IReadOnlyList<ItemEventView>>.Success([.. events.Select(e => new ItemEventView(e.Id, e.Kind, e.DataJson, e.At))]);
        }, ct);

    private async Task<bool> OverflowGrowsAsync(IUnitOfWork uow, CharacterId characterId, IReadOnlyList<ItemInstance> before, IReadOnlyList<ItemInstance> after, CancellationToken ct)
    {
        var stacks = (await repo.ListStacksAsync(uow, characterId, ct)).Count;
        int Overflow(IReadOnlyList<ItemInstance> set) =>
            InventoryRules.SlotsUsed(stacks, set) - InventoryRules.Capacity(config, set.Where(i => i.State == InstanceStates.Equipped));
        var o = Overflow(after);
        return o > 0 && o > Overflow(before);
    }

    private async Task<bool> MoveAsync(IUnitOfWork uow, ItemInstance item, string state, string eventKind, CancellationToken ct)
    {
        var updated = item with { State = state, Version = item.Version + 1 };
        if (!await repo.TryUpdateInstanceAsync(uow, updated, item.Version, ct)) return false;
        await ledger.EventAsync(uow, updated, eventKind, new { }, ct);
        return true;
    }

    private async Task<InventoryView> ViewAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        var stacks = await repo.ListStacksAsync(uow, characterId, ct);
        var instances = await repo.ListInstancesAsync(uow, characterId, ct);
        var equipped = instances.Where(i => i.State == InstanceStates.Equipped).ToList();
        return new InventoryView(
            characterId.Value,
            config.RulesVersion,
            await ledger.BalanceAsync(uow, characterId, ct),
            InventoryRules.SlotsUsed(stacks.Count, instances),
            InventoryRules.Capacity(config, equipped),
            [.. stacks.Select(s => { var d = config.Item(s.ItemId); return new StackView(s.ItemId, d.NamePl, d.Type, d.Rarity, s.Quantity); })],
            [.. instances.Select(i =>
            {
                var d = config.Item(i.ItemId);
                var stats = d.Equipment?.Stats.ToDictionary(s => s.Key, s => InventoryRules.ScaledStat(config, s.Value, i.UpgradeLevel)) ?? [];
                return new InstanceView(i.Id, i.ItemId, d.NamePl, d.Rarity, d.Equipment?.Slot, i.UpgradeLevel, i.State, i.Version, stats);
            })],
            equipped.ToDictionary(i => InventoryRules.SlotOf(config, i.ItemId)!, i => i.Id),
            InventoryRules.Bonuses(config, equipped));
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, CharacterId characterId, object data, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "character", characterId.ToString(),
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, clock.GetUtcNow()), ct);
}
