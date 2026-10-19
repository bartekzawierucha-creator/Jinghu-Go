using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.Modules.Inventory.Domain;

namespace JianghuGo.Modules.Inventory.Application;

public sealed record StackView(string ItemId, string NamePl, string Type, string Rarity, long Quantity);

public sealed record AffixView(string Id, string NamePl, string Stat, int Value);

public sealed record InstanceView(Guid Id, string ItemId, string NamePl, string Rarity, string? Slot, int UpgradeLevel, string State, int Version,
    IReadOnlyDictionary<string, int> Stats, string? BoundRef, IReadOnlyList<AffixView>? Affixes = null, string? SetId = null, string? SetNamePl = null,
    string? Quality = null, string? QualityPl = null, SpecialAffixView? Special = null);

/// <summary>A combat perk carried by a heavenly or divine item (1.8).</summary>
public sealed record SpecialAffixView(string Id, string NamePl, string Perk, int Value);

public sealed record SetView(string Id, string NamePl, int Worn, int Pieces, IReadOnlyDictionary<string, int> Active, IReadOnlyList<SetBonus> Bonuses);

public sealed record InventoryView(Guid CharacterId, string RulesVersion, long Balance, int SlotsUsed, int Capacity,
    IReadOnlyList<StackView> Stacks, IReadOnlyList<InstanceView> Items, IReadOnlyDictionary<string, Guid> Equipped, IReadOnlyDictionary<string, int> Bonuses,
    IReadOnlyList<SetView>? Sets = null);

public sealed record DismantleCommand(Guid InstanceId, int ExpectedVersion);

public sealed record DismantleResult(IReadOnlyList<StackView> Materials, InventoryView Inventory);

public sealed record EquipCommand(Guid InstanceId, int ExpectedVersion);

public sealed record MerchantOfferView(string ItemId, string NamePl, string Type, string Rarity, long Price);

/// <summary>The merchant's stock and what it pays: SellPrices per item id the character holds (tradable only).</summary>
public sealed record MerchantView(int SellPermille, IReadOnlyList<MerchantOfferView> Stock, IReadOnlyDictionary<string, long> SellPrices, long Balance, int MaxPerTrade);

/// <summary>Sell either a stack (itemId + quantity) or one unique item (instanceId + expectedVersion).</summary>
public sealed record MerchantSellCommand(string? ItemId, int Quantity, Guid? InstanceId, int ExpectedVersion);

public sealed record MerchantBuyCommand(string ItemId, int Quantity);

public sealed record MerchantTradeResult(long Stones, long Balance, InventoryView Inventory);

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
    public const string ScrollItem = "technique_scroll";
    public const string EssenceItem = "dao_essence";
    public const int EssencePerScroll = 2;

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
            if (InventoryRules.SlotOf(config, item.ItemId) is not { } slot) return Error.Validation("item_not_equippable", "This item cannot be equipped.");
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

    /// <summary>Breaks a piece of equipment from the bag into materials (by rarity; upgraded pieces give back refining stones).</summary>
    public Task<Result<DismantleResult>> DismantleAsync(AccountId accountId, Guid characterId, string? key, DismantleCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<DismantleResult>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "inventory.dismantle", key, new { characterId, cmd }, async Task<Result<DismantleResult>> () =>
            {
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                var item = await repo.FindInstanceAsync(uow, cmd.InstanceId, ct);
                if (item is null || item.CharacterId != c.Id || item.State == InstanceStates.Destroyed) return ItemNotFound;
                if (item.Version != cmd.ExpectedVersion) return Concurrent;
                if (item.State != InstanceStates.Bag) return Error.Conflict("item_not_in_bag", "Unequip the item (or cancel its listing) first.");
                var scroll = item.ItemId == ScrollItem && item.BoundRef is not null && config.Find(EssenceItem) is not null;
                if (config.Find(item.ItemId)?.Equipment is null && !scroll) return Error.Validation("item_not_dismantlable", "Only equipment and technique scrolls can be dismantled.");
                if (!await MoveAsync(uow, item, InstanceStates.Destroyed, "dismantled", ct)) return Concurrent;
                // 1.8: a technique scroll breaks into Dao essence (transmuted into new scrolls in the Dao Lab)
                var materials = scroll ? [new LootLine(EssenceItem, EssencePerScroll)] : InventoryRules.Dismantle(config, item);
                foreach (var m in materials)
                {
                    var added = await ledger.AddItemsAsync(uow, c.Id, m.ItemId, m.Quantity, "dismantle", item.Id.ToString(), ct);
                    if (!added.IsSuccess) return added.Error!;
                }

                await AuditAsync(uow, accountId, "inventory.dismantled", c.Id, new { instanceId = item.Id, item.ItemId, materials }, ct);
                var views = materials.Select(m => { var d = config.Item(m.ItemId); return new StackView(m.ItemId, d.NamePl, d.Type, d.Rarity, m.Quantity); }).ToList();
                return new DismantleResult(views, await ViewAsync(uow, c.Id, ct));
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    // ---------------------------------------------------------------- merchant (1.6)

    private static readonly Error NoMerchant = Error.NotFound("merchant_unavailable", "There is no merchant.");

    public Task<Result<MerchantView>> MerchantAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<MerchantView>>(async uow =>
        {
            if (config.Merchant is not { } m) return NoMerchant;
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var held = (await repo.ListStacksAsync(uow, c.Id, ct)).Select(x => x.ItemId)
                .Concat((await repo.ListInstancesAsync(uow, c.Id, ct)).Where(i => i.State == InstanceStates.Bag).Select(i => i.ItemId)).Distinct();
            var prices = held.Select(config.Find).Where(d => d is { Tradable: true }).ToDictionary(d => d!.Id, d => m.SellPrice(d!));
            var stock = m.Stock.Select(o => { var d = config.Item(o.Item); return new MerchantOfferView(o.Item, d.NamePl, d.Type, d.Rarity, o.Price); }).ToList();
            return new MerchantView(m.SellPermille, stock, prices, await ledger.BalanceAsync(uow, c.Id, ct), m.MaxPerTrade);
        }, ct);

    /// <summary>Sells tradable items to the merchant for stones (unique items: from the bag only, upgrades raise the price).</summary>
    public Task<Result<MerchantTradeResult>> SellToMerchantAsync(AccountId accountId, Guid characterId, string? key, MerchantSellCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<MerchantTradeResult>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "inventory.merchant_sell", key, new { characterId, cmd }, async Task<Result<MerchantTradeResult>> () =>
            {
                if (config.Merchant is not { } m) return NoMerchant;
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                if ((cmd.ItemId is null) == (cmd.InstanceId is null)) return Error.Validation("sell_target_invalid", "Give either itemId with quantity, or instanceId.");
                var reference = Uuid7.New().ToString();
                long stones;
                if (cmd.InstanceId is { } id)
                {
                    var item = await repo.FindInstanceAsync(uow, id, ct);
                    if (item is null || item.CharacterId != c.Id || item.State == InstanceStates.Destroyed) return ItemNotFound;
                    if (item.Version != cmd.ExpectedVersion) return Concurrent;
                    if (item.State != InstanceStates.Bag) return Error.Conflict("item_not_in_bag", "Unequip the item (or cancel its listing) first.");
                    var d = config.Item(item.ItemId);
                    if (!d.Tradable) return Error.Validation("item_not_tradable", "The merchant does not buy this item.");
                    if (!await MoveAsync(uow, item, InstanceStates.Destroyed, "sold_to_merchant", ct)) return Concurrent;
                    stones = m.SellPrice(d, item.UpgradeLevel);
                }
                else
                {
                    if (cmd.Quantity < 1 || cmd.Quantity > m.MaxPerTrade) return Error.Validation("quantity_invalid", $"Sell 1-{m.MaxPerTrade} at a time.");
                    var d = config.Find(cmd.ItemId);
                    if (d is null) return ItemNotFound;
                    if (!d.Tradable) return Error.Validation("item_not_tradable", "The merchant does not buy this item.");
                    var removed = await ledger.RemoveItemsAsync(uow, c.Id, d.Id, cmd.Quantity, "merchant_sell", reference, ct);
                    if (!removed.IsSuccess) return removed.Error!;
                    stones = m.SellPrice(d) * cmd.Quantity;
                }

                var balance = await ledger.BalanceAsync(uow, c.Id, ct);
                if (stones > 0)
                {
                    var credited = await ledger.CreditAsync(uow, c.Id, stones, "merchant_sell", reference, ct);
                    if (!credited.IsSuccess) return credited.Error!;
                    balance = credited.Value;
                }

                await AuditAsync(uow, accountId, "inventory.sold_to_merchant", c.Id, new { cmd.ItemId, cmd.Quantity, cmd.InstanceId, stones }, ct);
                return new MerchantTradeResult(stones, balance, await ViewAsync(uow, c.Id, ct));
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>Buys goods from the merchant's stock (stackable goods only; the bag must have room).</summary>
    public Task<Result<MerchantTradeResult>> BuyFromMerchantAsync(AccountId accountId, Guid characterId, string? key, MerchantBuyCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<MerchantTradeResult>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "inventory.merchant_buy", key, new { characterId, cmd }, async Task<Result<MerchantTradeResult>> () =>
            {
                if (config.Merchant is not { } m) return NoMerchant;
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                if (m.Offer(cmd.ItemId) is not { } offer) return Error.NotFound("not_in_stock", "The merchant does not sell this.");
                if (cmd.Quantity < 1 || cmd.Quantity > m.MaxPerTrade) return Error.Validation("quantity_invalid", $"Buy 1-{m.MaxPerTrade} at a time.");
                if (!await ledger.HasRoomAsync(uow, c.Id, offer.Item, cmd.Quantity, ct)) return Error.Conflict("bag_full", "There is no room in the bag.");
                var reference = Uuid7.New().ToString();
                var cost = checked(offer.Price * cmd.Quantity);
                var debited = await ledger.DebitAsync(uow, c.Id, cost, "merchant_buy", reference, ct);
                if (!debited.IsSuccess) return debited.Error!;
                var added = await ledger.AddItemsAsync(uow, c.Id, offer.Item, cmd.Quantity, "merchant_buy", reference, ct);
                if (!added.IsSuccess) return added.Error!;
                await AuditAsync(uow, accountId, "inventory.bought_from_merchant", c.Id, new { cmd.ItemId, cmd.Quantity, cost }, ct);
                return new MerchantTradeResult(-cost, debited.Value, await ViewAsync(uow, c.Id, ct));
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
                var grade = ItemQuality.Of(i.Id);
                var stats = d.Equipment?.Stats.ToDictionary(s => s.Key, s => InventoryRules.ScaledStat(config, s.Value, i.UpgradeLevel) * grade.StatPermille / 1000) ?? [];
                var set = config.SetOf(i.ItemId);
                var special = InventoryRules.SpecialAffix(config, i);
                return new InstanceView(i.Id, i.ItemId, d.NamePl, d.Rarity, d.Equipment?.Slot, i.UpgradeLevel, i.State, i.Version, stats, i.BoundRef,
                    [.. InventoryRules.Affixes(config, i).Select(a => new AffixView(a.Id, a.NamePl, a.Stat, a.Value))], set?.Id, set?.NamePl,
                    d.Equipment is null ? null : grade.Id, d.Equipment is null ? null : grade.NamePl,
                    special is null ? null : new SpecialAffixView(special.Id, special.NamePl, special.Perk, special.Value));
            })],
            equipped.ToDictionary(i => InventoryRules.SlotOf(config, i.ItemId)!, i => i.Id),
            InventoryRules.Bonuses(config, equipped),
            [.. config.SetList.Where(s => instances.Any(i => i.State != InstanceStates.Destroyed && s.Items.Contains(i.ItemId))).Select(s =>
            {
                var worn = s.Items.Count(id => equipped.Any(e => e.ItemId == id));
                var active = InventoryRules.ActiveSets(config, equipped).FirstOrDefault(a => a.Set.Id == s.Id).Stats ?? new Dictionary<string, int>();
                return new SetView(s.Id, s.NamePl, worn, s.Items.Count, active, s.Bonuses);
            })]);
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, CharacterId characterId, object data, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "character", characterId.ToString(),
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, clock.GetUtcNow()), ct);
}
