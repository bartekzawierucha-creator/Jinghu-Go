using JianghuGo.Application;
using JianghuGo.Domain;

namespace JianghuGo.Modules.Inventory.Contracts;

public sealed record InstanceInfo(Guid Id, Guid CharacterId, Guid AccountId, string ItemId, int UpgradeLevel, string State, int Version, bool Tradable, string? BoundRef);

/// <summary>
/// Asset operations for other modules (Crafting, Market). Everything runs inside the CALLER's unit of work, so
/// "take materials + charge stones + create item" is one atomic transaction (acceptance #5). Quantities must be
/// positive (acceptance #4). (reason, referenceId) is unique per character and ledger line: the same operation can
/// never be applied twice — a repeat throws <see cref="DuplicateKeyException"/> and rolls the whole transaction back.
/// </summary>
public interface IInventory
{
    Task<long> CountAsync(IUnitOfWork uow, CharacterId characterId, string itemId, CancellationToken ct);

    /// <summary>Player-driven gain (crafting output, purchase): respects bag capacity and stack size.</summary>
    Task<Result<long>> AddItemsAsync(IUnitOfWork uow, CharacterId characterId, string itemId, long quantity, string reason, string referenceId, CancellationToken ct);
    Task<Result<long>> RemoveItemsAsync(IUnitOfWork uow, CharacterId characterId, string itemId, long quantity, string reason, string referenceId, CancellationToken ct);

    /// <summary>Whether one more unit of <paramref name="itemId"/> fits (existing stack with room, or a free bag slot).</summary>
    Task<bool> HasRoomAsync(IUnitOfWork uow, CharacterId characterId, string itemId, long quantity, CancellationToken ct);

    /// <param name="boundRef">Immutable reference stored with the item (a scroll's technique id); null for ordinary items.</param>
    Task<Result<InstanceInfo>> CreateInstanceAsync(IUnitOfWork uow, CharacterId characterId, AccountId accountId, string itemId, string reason, string referenceId, CancellationToken ct, string? boundRef = null);

    /// <summary>Uses up a unique item from the owner's bag (e.g. a scroll when its technique is learned).</summary>
    Task<Result<InstanceInfo>> ConsumeInstanceAsync(IUnitOfWork uow, Guid instanceId, int expectedVersion, string reason, CancellationToken ct);
    Task<InstanceInfo?> FindInstanceAsync(IUnitOfWork uow, Guid instanceId, CancellationToken ct);
    Task<Result<InstanceInfo>> SetUpgradeLevelAsync(IUnitOfWork uow, Guid instanceId, int expectedVersion, int level, string reason, CancellationToken ct);

    /// <summary>bag → listed (market escrow) or listed → bag (cancel / expiry).</summary>
    Task<Result<InstanceInfo>> SetListedAsync(IUnitOfWork uow, Guid instanceId, int expectedVersion, bool listed, string reason, CancellationToken ct);

    /// <summary>Moves a listed (escrowed) unique item into another character's bag.</summary>
    Task<Result<InstanceInfo>> TransferListedAsync(IUnitOfWork uow, Guid instanceId, int expectedVersion, CharacterId to, AccountId toAccount, string reason, CancellationToken ct);

    Task<long> BalanceAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
    Task<Result<long>> CreditAsync(IUnitOfWork uow, CharacterId characterId, long amount, string reason, string referenceId, CancellationToken ct);
    Task<Result<long>> DebitAsync(IUnitOfWork uow, CharacterId characterId, long amount, string reason, string referenceId, CancellationToken ct);
}

/// <summary>Read-only item catalog for other modules (the item rules stay owned by Inventory).</summary>
public sealed record CatalogItem(string Id, string NamePl, string Type, string Rarity, bool Stackable, bool Tradable, string? Slot);

public interface IItemCatalog
{
    CatalogItem? Find(string? itemId);
    int MaxStack { get; }
}

/// <summary>Stat bonuses from equipped items (Combat adds them to the battle snapshot).</summary>
public interface IEquipmentStats
{
    Task<IReadOnlyDictionary<string, int>> BonusesAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
}

public static class InventoryEvents
{
    public const string ConsumableUsed = "inventory.consumable_used";

    public sealed record ConsumableUsedV1(Guid AccountId, Guid CharacterId, string ItemId, int Quantity, long Qi);
}
