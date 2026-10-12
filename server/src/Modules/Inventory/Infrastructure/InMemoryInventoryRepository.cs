using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Modules.Inventory.Application;
using JianghuGo.Modules.Inventory.Domain;

namespace JianghuGo.Modules.Inventory.Infrastructure;

internal sealed class InMemoryInventoryRepository : IInventoryRepository
{
    private const string Stacks = "inventory.stacks";
    private const string StackEntries = "inventory.stack_entries";
    private const string Wallets = "inventory.wallets";
    private const string WalletEntries = "inventory.wallet_entries";
    private const string Items = "inventory.items";
    private const string ItemEvents = "inventory.item_events";
    private const string Grants = "inventory.grants";

    private static InMemoryUnitOfWork U(IUnitOfWork uow) => InMemoryUnitOfWork.From(uow);

    public Task<long> QuantityAsync(IUnitOfWork uow, CharacterId characterId, string itemId, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<(CharacterId, string), long>(Stacks).GetValueOrDefault((characterId, itemId)));

    public Task<IReadOnlyList<(string ItemId, long Quantity)>> ListStacksAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        IReadOnlyList<(string, long)> list = [.. U(uow).Table<(CharacterId, string), long>(Stacks)
            .Where(kv => kv.Key.Item1 == characterId && kv.Value > 0).Select(kv => (kv.Key.Item2, kv.Value)).OrderBy(x => x.Item1, StringComparer.Ordinal)];
        return Task.FromResult(list);
    }

    public Task<Result<StackEntry>> PostStackAsync(IUnitOfWork uow, CharacterId characterId, string itemId, long delta, long maxStack, string reason, string referenceId, DateTimeOffset at, CancellationToken ct)
    {
        var u = U(uow);
        var stacks = u.Table<(CharacterId, string), long>(Stacks);
        var current = stacks.GetValueOrDefault((characterId, itemId));
        var after = checked(current + delta);
        if (after < 0) return Task.FromResult<Result<StackEntry>>(Error.InsufficientFunds("insufficient_items", $"Not enough {itemId} (have {current}, need {-delta})."));
        if (after > maxStack) return Task.FromResult<Result<StackEntry>>(Error.LimitReached("stack_full", $"A stack holds at most {maxStack}."));
        var entries = u.Table<Guid, StackEntry>(StackEntries);
        if (entries.Values.Any(e => e.CharacterId == characterId && e.ItemId == itemId && e.Reason == reason && e.ReferenceId == referenceId))
            throw new DuplicateKeyException("Stack entry already applied.");
        var entry = new StackEntry(Uuid7.New(), characterId, itemId, delta, after, reason, referenceId, at);
        u.SetTable(StackEntries, entries.Add(entry.Id, entry));
        u.SetTable(Stacks, stacks.SetItem((characterId, itemId), after));
        return Task.FromResult<Result<StackEntry>>(entry);
    }

    public Task<bool> EntryExistsAsync(IUnitOfWork uow, CharacterId characterId, string reason, string referenceId, CancellationToken ct) =>
        Task.FromResult(
            U(uow).Table<Guid, StackEntry>(StackEntries).Values.Any(e => e.CharacterId == characterId && e.Reason == reason && e.ReferenceId == referenceId)
            || U(uow).Table<Guid, WalletEntry>(WalletEntries).Values.Any(e => e.CharacterId == characterId && e.Reason == reason && e.ReferenceId == referenceId));

    public Task<long?> BalanceAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<CharacterId, long>(Wallets).TryGetValue(characterId, out var b) ? b : (long?)null);

    public Task<Result<WalletEntry>> PostWalletAsync(IUnitOfWork uow, CharacterId characterId, long delta, string reason, string referenceId, DateTimeOffset at, CancellationToken ct)
    {
        var u = U(uow);
        var wallets = u.Table<CharacterId, long>(Wallets);
        var current = wallets.GetValueOrDefault(characterId);
        var after = checked(current + delta);
        if (after < 0) return Task.FromResult<Result<WalletEntry>>(Error.InsufficientFunds("insufficient_funds", $"Not enough Spirit Stones (have {current}, need {-delta})."));
        var entries = u.Table<Guid, WalletEntry>(WalletEntries);
        if (entries.Values.Any(e => e.CharacterId == characterId && e.Reason == reason && e.ReferenceId == referenceId))
            throw new DuplicateKeyException("Wallet entry already applied.");
        var entry = new WalletEntry(Uuid7.New(), characterId, delta, after, reason, referenceId, at);
        u.SetTable(WalletEntries, entries.Add(entry.Id, entry));
        u.SetTable(Wallets, wallets.SetItem(characterId, after));
        return Task.FromResult<Result<WalletEntry>>(entry);
    }

    public Task<IReadOnlyList<WalletEntry>> ListWalletEntriesAsync(IUnitOfWork uow, CharacterId characterId, Guid? before, int limit, CancellationToken ct)
    {
        IReadOnlyList<WalletEntry> list = [.. U(uow).Table<Guid, WalletEntry>(WalletEntries).Values
            .Where(e => e.CharacterId == characterId && (before is null || e.Id.CompareTo(before.Value) < 0)).OrderByDescending(e => e.Id).Take(limit)];
        return Task.FromResult(list);
    }

    public Task AddInstanceAsync(IUnitOfWork uow, ItemInstance instance, CancellationToken ct)
    {
        U(uow).SetTable(Items, U(uow).Table<Guid, ItemInstance>(Items).Add(instance.Id, instance));
        return Task.CompletedTask;
    }

    public Task<ItemInstance?> FindInstanceAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, ItemInstance>(Items).GetValueOrDefault(id));

    public Task<bool> TryUpdateInstanceAsync(IUnitOfWork uow, ItemInstance instance, int expectedVersion, CancellationToken ct)
    {
        var t = U(uow).Table<Guid, ItemInstance>(Items);
        if (!t.TryGetValue(instance.Id, out var cur) || cur.Version != expectedVersion || instance.Version != expectedVersion + 1 || cur.ItemId != instance.ItemId)
            return Task.FromResult(false);
        U(uow).SetTable(Items, t.SetItem(instance.Id, instance));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<ItemInstance>> ListInstancesAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        IReadOnlyList<ItemInstance> list = [.. U(uow).Table<Guid, ItemInstance>(Items).Values.Where(i => i.CharacterId == characterId && i.State != InstanceStates.Destroyed).OrderBy(i => i.Id)];
        return Task.FromResult(list);
    }

    public Task AddItemEventAsync(IUnitOfWork uow, ItemEvent e, CancellationToken ct)
    {
        U(uow).SetTable(ItemEvents, U(uow).Table<Guid, ItemEvent>(ItemEvents).Add(e.Id, e));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ItemEvent>> ListItemEventsAsync(IUnitOfWork uow, Guid instanceId, CancellationToken ct)
    {
        IReadOnlyList<ItemEvent> list = [.. U(uow).Table<Guid, ItemEvent>(ItemEvents).Values.Where(e => e.ItemInstanceId == instanceId).OrderBy(e => e.Id)];
        return Task.FromResult(list);
    }

    public Task<bool> TryRecordGrantAsync(IUnitOfWork uow, Guid sourceId, CharacterId characterId, string kind, DateTimeOffset at, CancellationToken ct)
    {
        var t = U(uow).Table<(Guid, CharacterId), string>(Grants);
        if (t.ContainsKey((sourceId, characterId))) return Task.FromResult(false);
        U(uow).SetTable(Grants, t.Add((sourceId, characterId), kind));
        return Task.FromResult(true);
    }

    public Task<(int Wallets, int Stacks)> CountMismatchesAsync(IUnitOfWork uow, CancellationToken ct)
    {
        var u = U(uow);
        var walletSums = u.Table<Guid, WalletEntry>(WalletEntries).Values.GroupBy(e => e.CharacterId).ToDictionary(g => g.Key, g => g.Sum(e => e.Delta));
        var stackSums = u.Table<Guid, StackEntry>(StackEntries).Values.GroupBy(e => (e.CharacterId, e.ItemId)).ToDictionary(g => g.Key, g => g.Sum(e => e.Delta));
        var w = u.Table<CharacterId, long>(Wallets).Count(kv => walletSums.GetValueOrDefault(kv.Key) != kv.Value);
        var s = u.Table<(CharacterId, string), long>(Stacks).Count(kv => stackSums.GetValueOrDefault(kv.Key) != kv.Value);
        return Task.FromResult((w, s));
    }
}
