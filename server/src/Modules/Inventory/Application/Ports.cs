using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Inventory.Domain;

namespace JianghuGo.Modules.Inventory.Application;

public interface IInventoryRepository
{
    Task<long> QuantityAsync(IUnitOfWork uow, CharacterId characterId, string itemId, CancellationToken ct);
    Task<IReadOnlyList<(string ItemId, long Quantity)>> ListStacksAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    /// <summary>Appends a stack ledger line under a row lock. Errors: insufficient_items, stack_full. Duplicate (character, item, reason, reference) throws.</summary>
    Task<Result<StackEntry>> PostStackAsync(IUnitOfWork uow, CharacterId characterId, string itemId, long delta, long maxStack, string reason, string referenceId, DateTimeOffset at, CancellationToken ct);

    Task<bool> EntryExistsAsync(IUnitOfWork uow, CharacterId characterId, string reason, string referenceId, CancellationToken ct);

    Task<long?> BalanceAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    /// <summary>Appends a wallet ledger line under a row lock. Error: insufficient_funds. Duplicate (character, reason, reference) throws.</summary>
    Task<Result<WalletEntry>> PostWalletAsync(IUnitOfWork uow, CharacterId characterId, long delta, string reason, string referenceId, DateTimeOffset at, CancellationToken ct);

    Task<IReadOnlyList<WalletEntry>> ListWalletEntriesAsync(IUnitOfWork uow, CharacterId characterId, Guid? before, int limit, CancellationToken ct);

    Task AddInstanceAsync(IUnitOfWork uow, ItemInstance instance, CancellationToken ct);
    Task<ItemInstance?> FindInstanceAsync(IUnitOfWork uow, Guid id, CancellationToken ct);
    Task<bool> TryUpdateInstanceAsync(IUnitOfWork uow, ItemInstance instance, int expectedVersion, CancellationToken ct);
    Task<IReadOnlyList<ItemInstance>> ListInstancesAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
    Task AddItemEventAsync(IUnitOfWork uow, ItemEvent e, CancellationToken ct);
    Task<IReadOnlyList<ItemEvent>> ListItemEventsAsync(IUnitOfWork uow, Guid instanceId, CancellationToken ct);

    /// <summary>Records that an event's grant was applied to a character. False if it already was (exactly-once handlers).</summary>
    Task<bool> TryRecordGrantAsync(IUnitOfWork uow, Guid sourceId, CharacterId characterId, string kind, DateTimeOffset at, CancellationToken ct);

    /// <summary>Economy reconciliation: (wallets whose balance ≠ Σ ledger, stacks whose quantity ≠ Σ ledger). Both must be 0.</summary>
    Task<(int Wallets, int Stacks)> CountMismatchesAsync(IUnitOfWork uow, CancellationToken ct);

    Task<ActiveTalisman?> FindTalismanAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
    Task SetTalismanAsync(IUnitOfWork uow, ActiveTalisman talisman, CancellationToken ct);
}
