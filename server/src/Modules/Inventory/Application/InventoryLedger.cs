using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.Modules.Inventory.Domain;

namespace JianghuGo.Modules.Inventory.Application;

/// <summary>
/// The only code that changes item stacks, unique items and wallets. Works inside the caller's unit of work.
/// Every quantity is validated here as well as at the API edge (acceptance #4), so no module can post a
/// negative or zero movement by mistake.
/// </summary>
public sealed class InventoryLedger(IInventoryRepository repo, ItemsConfig config, TimeProvider clock) : IInventory, IEquipmentStats
{
    public const string InitialGrantReason = "initial_grant";

    private static Error UnknownItem(string id) => Error.Validation("item_unknown", $"Unknown item '{id}'.");
    private static readonly Error QuantityInvalid = Error.Validation("quantity_invalid", "Quantity must be a positive whole number.");
    private static readonly Error BagFull = Error.LimitReached("bag_full", "Your bag is full.");
    private static readonly Error InstanceNotFound = Error.NotFound("item_not_found", "Item not found.");
    private static readonly Error Concurrent = Error.Conflict("version_conflict", "The item changed concurrently; reload and retry.");

    public Task<long> CountAsync(IUnitOfWork uow, CharacterId characterId, string itemId, CancellationToken ct) =>
        repo.QuantityAsync(uow, characterId, itemId, ct);

    public async Task<Result<long>> AddItemsAsync(IUnitOfWork uow, CharacterId characterId, string itemId, long quantity, string reason, string referenceId, CancellationToken ct)
    {
        if (config.Find(itemId) is not { Stackable: true }) return UnknownItem(itemId);
        if (quantity <= 0 || quantity > config.Bag.MaxStack) return QuantityInvalid;
        if (!await HasRoomAsync(uow, characterId, itemId, quantity, ct))
        {
            return await repo.QuantityAsync(uow, characterId, itemId, ct) > 0
                ? Error.LimitReached("stack_full", $"A stack holds at most {config.Bag.MaxStack}.")
                : BagFull;
        }

        var entry = await repo.PostStackAsync(uow, characterId, itemId, quantity, config.Bag.MaxStack, reason, referenceId, clock.GetUtcNow(), ct);
        return entry.IsSuccess ? entry.Value.QuantityAfter : entry.Error!;
    }

    public async Task<Result<long>> RemoveItemsAsync(IUnitOfWork uow, CharacterId characterId, string itemId, long quantity, string reason, string referenceId, CancellationToken ct)
    {
        if (config.Find(itemId) is not { Stackable: true }) return UnknownItem(itemId);
        if (quantity <= 0 || quantity > config.Bag.MaxStack) return QuantityInvalid;
        var entry = await repo.PostStackAsync(uow, characterId, itemId, -quantity, config.Bag.MaxStack, reason, referenceId, clock.GetUtcNow(), ct);
        return entry.IsSuccess ? entry.Value.QuantityAfter : entry.Error!;
    }

    /// <summary>
    /// Reward grants (loot, gathering) are never refused for a full bag: the player earned them. Stacks are clipped
    /// to the stack limit and the clipped remainder is returned so the caller can audit it.
    /// </summary>
    public async Task<(long Granted, long Clipped)> GrantItemsAsync(IUnitOfWork uow, CharacterId characterId, string itemId, long quantity, string reason, string referenceId, CancellationToken ct)
    {
        if (config.Find(itemId) is not { Stackable: true } || quantity <= 0) return (0, Math.Max(0, quantity));
        var room = config.Bag.MaxStack - await repo.QuantityAsync(uow, characterId, itemId, ct);
        var granted = Math.Min(quantity, room);
        if (granted > 0)
        {
            var entry = await repo.PostStackAsync(uow, characterId, itemId, granted, config.Bag.MaxStack, reason, referenceId, clock.GetUtcNow(), ct);
            if (!entry.IsSuccess) throw new InvalidOperationException($"Grant of {itemId} failed: {entry.Error!.Code}.");
        }

        return (Math.Max(0, granted), quantity - Math.Max(0, granted));
    }

    public async Task<bool> HasRoomAsync(IUnitOfWork uow, CharacterId characterId, string itemId, long quantity, CancellationToken ct)
    {
        var def = config.Find(itemId);
        if (def is null || quantity <= 0) return false;
        if (def.Stackable)
        {
            var have = await repo.QuantityAsync(uow, characterId, itemId, ct);
            if (have + quantity > config.Bag.MaxStack) return false;
            if (have > 0) return true;
        }
        else if (quantity != 1)
        {
            return false;
        }

        var (used, capacity) = await SlotsAsync(uow, characterId, ct);
        return used + 1 <= capacity;
    }

    public async Task<(int Used, int Capacity)> SlotsAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        var stacks = await repo.ListStacksAsync(uow, characterId, ct);
        var instances = await repo.ListInstancesAsync(uow, characterId, ct);
        return (InventoryRules.SlotsUsed(stacks.Count, instances), InventoryRules.Capacity(config, instances.Where(i => i.State == InstanceStates.Equipped)));
    }

    public async Task<Result<InstanceInfo>> CreateInstanceAsync(IUnitOfWork uow, CharacterId characterId, AccountId accountId, string itemId, string reason, string referenceId, CancellationToken ct, string? boundRef = null)
    {
        if (config.Find(itemId) is not { Stackable: false } def) return UnknownItem(itemId);
        if ((def.Type == ItemTypes.Scroll) != (boundRef is not null)) return Error.Validation("bound_ref_invalid", "Scrolls (and only scrolls) carry a reference.");
        if (!await HasRoomAsync(uow, characterId, itemId, 1, ct)) return BagFull;
        return Info(await CreateUncheckedAsync(uow, characterId, accountId, itemId, reason, referenceId, ct, boundRef));
    }

    public async Task<Result<InstanceInfo>> ConsumeInstanceAsync(IUnitOfWork uow, Guid instanceId, int expectedVersion, string reason, CancellationToken ct)
    {
        var i = await repo.FindInstanceAsync(uow, instanceId, ct);
        if (i is null || i.State == InstanceStates.Destroyed) return InstanceNotFound;
        if (i.State != InstanceStates.Bag) return Error.Conflict("item_not_in_bag", "Only items in the bag can be used.");
        var updated = i with { State = InstanceStates.Destroyed, Version = expectedVersion + 1 };
        if (!await repo.TryUpdateInstanceAsync(uow, updated, expectedVersion, ct)) return Concurrent;
        await EventAsync(uow, updated, "consumed", new { reason }, ct);
        return Info(updated);
    }

    /// <summary>Creates a unique item regardless of bag space (reward grants only).</summary>
    public async Task<ItemInstance> CreateUncheckedAsync(IUnitOfWork uow, CharacterId characterId, AccountId accountId, string itemId, string reason, string referenceId, CancellationToken ct, string? boundRef = null)
    {
        var now = clock.GetUtcNow();
        var instance = new ItemInstance(Uuid7.New(), characterId, accountId, itemId, 0, InstanceStates.Bag, 1, now, boundRef);
        await repo.AddInstanceAsync(uow, instance, ct);
        await EventAsync(uow, instance, "created", new { reason, referenceId }, ct);
        return instance;
    }

    public async Task<InstanceInfo?> FindInstanceAsync(IUnitOfWork uow, Guid instanceId, CancellationToken ct)
    {
        var i = await repo.FindInstanceAsync(uow, instanceId, ct);
        return i is null || i.State == InstanceStates.Destroyed ? null : Info(i);
    }

    public async Task<Result<InstanceInfo>> SetUpgradeLevelAsync(IUnitOfWork uow, Guid instanceId, int expectedVersion, int level, string reason, CancellationToken ct)
    {
        var i = await repo.FindInstanceAsync(uow, instanceId, ct);
        if (i is null || i.State == InstanceStates.Destroyed) return InstanceNotFound;
        if (i.State == InstanceStates.Listed) return Error.Conflict("item_listed", "The item is listed on the market.");
        if (level is < 0 or > 100) return Error.Validation("upgrade_level_invalid", "Upgrade level out of range.");
        var updated = i with { UpgradeLevel = level, Version = expectedVersion + 1 };
        if (!await repo.TryUpdateInstanceAsync(uow, updated, expectedVersion, ct)) return Concurrent;
        await EventAsync(uow, updated, "upgrade_level_set", new { from = i.UpgradeLevel, to = level, reason }, ct);
        return Info(updated);
    }

    public async Task<Result<InstanceInfo>> SetListedAsync(IUnitOfWork uow, Guid instanceId, int expectedVersion, bool listed, string reason, CancellationToken ct)
    {
        var i = await repo.FindInstanceAsync(uow, instanceId, ct);
        if (i is null || i.State == InstanceStates.Destroyed) return InstanceNotFound;
        var from = listed ? InstanceStates.Bag : InstanceStates.Listed;
        if (i.State != from)
        {
            return listed
                ? Error.Conflict("item_not_in_bag", "Only unequipped items in your bag can be listed.")
                : Error.Conflict("item_not_listed", "The item is not listed.");
        }

        if (listed && !config.Item(i.ItemId).Tradable) return Error.Validation("item_not_tradable", "This item cannot be traded.");
        var updated = i with { State = listed ? InstanceStates.Listed : InstanceStates.Bag, Version = expectedVersion + 1 };
        if (!await repo.TryUpdateInstanceAsync(uow, updated, expectedVersion, ct)) return Concurrent;
        await EventAsync(uow, updated, listed ? "listed" : "unlisted", new { reason }, ct);
        return Info(updated);
    }

    public async Task<Result<InstanceInfo>> TransferListedAsync(IUnitOfWork uow, Guid instanceId, int expectedVersion, CharacterId to, AccountId toAccount, string reason, CancellationToken ct)
    {
        var i = await repo.FindInstanceAsync(uow, instanceId, ct);
        if (i is null || i.State == InstanceStates.Destroyed) return InstanceNotFound;
        if (i.State != InstanceStates.Listed) return Error.Conflict("item_not_listed", "The item is not in escrow.");
        var updated = i with { CharacterId = to, AccountId = toAccount, State = InstanceStates.Bag, Version = expectedVersion + 1 };
        if (!await repo.TryUpdateInstanceAsync(uow, updated, expectedVersion, ct)) return Concurrent;
        await EventAsync(uow, updated, "transferred", new { from = i.CharacterId.Value, to = to.Value, reason }, ct);
        return Info(updated);
    }

    public async Task<long> BalanceAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        await repo.BalanceAsync(uow, characterId, ct) ?? config.Currency.InitialGrant;

    public async Task<Result<long>> CreditAsync(IUnitOfWork uow, CharacterId characterId, long amount, string reason, string referenceId, CancellationToken ct)
    {
        if (amount <= 0) return Error.Validation("amount_invalid", "Amount must be positive.");
        await EnsureWalletAsync(uow, characterId, ct);
        var entry = await repo.PostWalletAsync(uow, characterId, amount, reason, referenceId, clock.GetUtcNow(), ct);
        return entry.IsSuccess ? entry.Value.BalanceAfter : entry.Error!;
    }

    public async Task<Result<long>> DebitAsync(IUnitOfWork uow, CharacterId characterId, long amount, string reason, string referenceId, CancellationToken ct)
    {
        if (amount <= 0) return Error.Validation("amount_invalid", "Amount must be positive.");
        await EnsureWalletAsync(uow, characterId, ct);
        var entry = await repo.PostWalletAsync(uow, characterId, -amount, reason, referenceId, clock.GetUtcNow(), ct);
        return entry.IsSuccess ? entry.Value.BalanceAfter : entry.Error!;
    }

    /// <summary>The starting purse is a real ledger line, written the first time the wallet is touched.</summary>
    private async Task EnsureWalletAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        if (await repo.BalanceAsync(uow, characterId, ct) is not null) return;
        if (config.Currency.InitialGrant > 0)
        {
            var r = await repo.PostWalletAsync(uow, characterId, config.Currency.InitialGrant, InitialGrantReason, characterId.Value.ToString(), clock.GetUtcNow(), ct);
            if (!r.IsSuccess) throw new InvalidOperationException("Initial grant failed: " + r.Error!.Code);
        }
    }

    public async Task<IReadOnlyDictionary<string, int>> BonusesAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        InventoryRules.Bonuses(config, (await repo.ListInstancesAsync(uow, characterId, ct)).Where(i => i.State == InstanceStates.Equipped));

    public InstanceInfo Info(ItemInstance i) =>
        new(i.Id, i.CharacterId.Value, i.AccountId.Value, i.ItemId, i.UpgradeLevel, i.State, i.Version, config.Find(i.ItemId)?.Tradable ?? false, i.BoundRef);

    public Task EventAsync(IUnitOfWork uow, ItemInstance i, string kind, object data, CancellationToken ct) =>
        repo.AddItemEventAsync(uow, new ItemEvent(Uuid7.New(), i.Id, i.CharacterId, kind, JsonSerializer.Serialize(data, Idempotency.Json), clock.GetUtcNow()), ct);
}
