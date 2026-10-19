using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.Modules.Market.Contracts;
using JianghuGo.Modules.Market.Domain;

namespace JianghuGo.Modules.Market.Application;

public interface IMarketRepository
{
    Task AddAsync(IUnitOfWork uow, Listing listing, CancellationToken ct);
    Task<Listing?> FindAsync(IUnitOfWork uow, Guid id, CancellationToken ct);

    /// <summary>Optimistic update: false if the stored version is not <paramref name="expectedVersion"/> (someone else got there first).</summary>
    Task<bool> TryUpdateAsync(IUnitOfWork uow, Listing listing, int expectedVersion, CancellationToken ct);

    Task<int> CountActiveAsync(IUnitOfWork uow, CharacterId seller, CancellationToken ct);
    Task<IReadOnlyList<Listing>> BrowseAsync(IUnitOfWork uow, string? itemId, Guid? before, int limit, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyList<Listing>> ListBySellerAsync(IUnitOfWork uow, CharacterId seller, Guid? before, int limit, CancellationToken ct);

    /// <summary>Active listings among <paramref name="itemIds"/> (null = all), cheapest unit price or newest first (1.9 search).</summary>
    Task<IReadOnlyList<Listing>> SearchAsync(IUnitOfWork uow, IReadOnlyList<string>? itemIds, long? maxPrice, string sort, int offset, int limit, DateTimeOffset now, CancellationToken ct);

    /// <summary>Sales of one item since a moment, newest first.</summary>
    Task<IReadOnlyList<Listing>> SalesAsync(IUnitOfWork uow, string itemId, DateTimeOffset since, int limit, CancellationToken ct);
}

/// <summary>Price history of an item (1.9): sales of the last 7 days and the cheapest offer now. Unit prices = price ÷ quantity.</summary>
public sealed record PriceView(string ItemId, string ItemNamePl, int Sales7d, long Volume7d, long? AverageUnitPrice7d, long? MinUnitPrice7d, long? MaxUnitPrice7d,
    long? LowestActiveUnitPrice, int ActiveListings, IReadOnlyList<SaleView> RecentSales);

public sealed record SaleView(long UnitPrice, long Quantity, DateTimeOffset At);

public sealed record CreateListingCommand(Guid CharacterId, string? ItemId, long Quantity, Guid? InstanceId, int? ExpectedVersion, long Price);

public sealed record BuyCommand(Guid CharacterId);

public sealed record ListingView(Guid Id, string ItemId, string ItemNamePl, string Rarity, long Quantity, Guid? InstanceId, int UpgradeLevel, long Price,
    Guid SellerCharacterId, string SellerName, string Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Expired, string? InstanceRef);

public sealed record MyListingView(ListingView Listing, long Fee, long Tax, long? Proceeds, Guid? BuyerCharacterId, DateTimeOffset? ClosedAt);

public sealed record PurchaseResult(ListingView Listing, long Paid, long Balance);

public sealed class MarketService(
    IUnitOfWorkFactory uowFactory,
    IMarketRepository repo,
    ICharacterDirectory characters,
    IInventory inventory,
    IItemCatalog catalog,
    IIdempotencyStore idempotency,
    IAuditLog audit,
    IOutbox outbox,
    ICorrelationContext correlation,
    MarketConfig config,
    TimeProvider clock)
{
    public const int MaxPageSize = 100;

    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");
    private static readonly Error ListingNotFound = Error.NotFound("listing_not_found", "Listing not found.");
    private static readonly Error Unavailable = Error.Conflict("listing_unavailable", "This listing has already been sold, cancelled or has expired.");

    /// <summary>Fee and escrow in one transaction: either the goods are listed and the fee is paid, or nothing changed.</summary>
    public Task<Result<ListingView>> CreateAsync(AccountId accountId, string? key, CreateListingCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<ListingView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "market.list", key, cmd, async Task<Result<ListingView>> () =>
            {
                if (cmd.Price < config.MinPrice || cmd.Price > config.MaxPrice)
                    return Error.Validation("price_invalid", $"Price must be {config.MinPrice}-{config.MaxPrice} Spirit Stones.");
                if ((cmd.ItemId is null) == (cmd.InstanceId is null))
                    return Error.Validation("listing_target_invalid", "List either itemId with quantity, or one instanceId.");
                var c = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
                if (c is null) return CharacterNotFound;
                if (await repo.CountActiveAsync(uow, c.Id, ct) >= config.MaxActiveListingsPerCharacter)
                    return Error.LimitReached("listing_limit", $"At most {config.MaxActiveListingsPerCharacter} active listings (reclaim expired ones by cancelling).");

                var now = clock.GetUtcNow();
                var id = Uuid7.New();
                var reference = id.ToString();
                string itemId;
                long quantity;
                var level = 0;
                string? instanceRef = null;
                if (cmd.InstanceId is { } instanceId)
                {
                    var item = await inventory.FindInstanceAsync(uow, instanceId, ct);
                    if (item is null || item.CharacterId != c.Id.Value) return Error.NotFound("item_not_found", "Item not found.");
                    if (cmd.ExpectedVersion is not { } version) return Error.Validation("expected_version_required", "expectedVersion is required for unique items.");
                    var listed = await inventory.SetListedAsync(uow, instanceId, version, true, "market:" + reference, ct);
                    if (!listed.IsSuccess) return listed.Error!;
                    (itemId, quantity, level, instanceRef) = (item.ItemId, 1, item.UpgradeLevel, item.BoundRef);
                }
                else
                {
                    var def = catalog.Find(cmd.ItemId);
                    if (def is not { Stackable: true }) return Error.Validation("item_unknown", "Unknown stackable item.");
                    if (!def.Tradable) return Error.Validation("item_not_tradable", "This item cannot be traded.");
                    if (cmd.Quantity < 1 || cmd.Quantity > catalog.MaxStack) return Error.Validation("quantity_invalid", $"Quantity must be 1-{catalog.MaxStack}.");
                    var escrowed = await inventory.RemoveItemsAsync(uow, c.Id, def.Id, cmd.Quantity, "market_escrow", reference, ct);
                    if (!escrowed.IsSuccess) return escrowed.Error!;
                    (itemId, quantity) = (def.Id, cmd.Quantity);
                }

                var fee = MarketRules.ListingFee(config, cmd.Price);
                if (fee > 0)
                {
                    var paid = await inventory.DebitAsync(uow, c.Id, fee, "market_fee", reference, ct);
                    if (!paid.IsSuccess) return paid.Error!;
                }

                var listing = new Listing(id, c.Id, accountId, c.Name, itemId, quantity, cmd.InstanceId, level, cmd.Price, fee, ListingStatuses.Active,
                    null, null, 0, 1, now, now.AddHours(config.ListingHours), null, instanceRef);
                await repo.AddAsync(uow, listing, ct);
                await AuditAsync(uow, accountId, "market.listed", id, new { characterId = c.Id.Value, itemId, quantity, cmd.InstanceId, cmd.Price, fee }, now, ct);
                return View(listing, now);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>
    /// Atomic purchase: buyer pays, seller receives price − tax, goods leave escrow, listing closes — all or nothing.
    /// Two concurrent buyers race on the listing version; exactly one wins (acceptance #3).
    /// </summary>
    public Task<Result<PurchaseResult>> BuyAsync(AccountId accountId, Guid listingId, string? key, BuyCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<PurchaseResult>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "market.buy", key, new { listingId, cmd }, async Task<Result<PurchaseResult>> () =>
            {
                var buyer = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
                if (buyer is null) return CharacterNotFound;
                var now = clock.GetUtcNow();
                var listing = await repo.FindAsync(uow, listingId, ct);
                if (listing is null) return ListingNotFound;
                if (!listing.IsBuyable(now)) return Unavailable;
                if (listing.SellerAccountId == accountId) return Error.Conflict("own_listing", "You cannot buy your own listing.");

                var tax = MarketRules.SaleTax(config, listing.Price);
                var closed = listing with
                {
                    Status = ListingStatuses.Sold, BuyerCharacterId = buyer.Id, BuyerAccountId = accountId, Tax = tax,
                    Version = listing.Version + 1, ClosedAt = now,
                };
                if (!await repo.TryUpdateAsync(uow, closed, listing.Version, ct)) return Unavailable;

                var reference = listing.Id.ToString();
                var paid = await inventory.DebitAsync(uow, buyer.Id, listing.Price, "market_purchase", reference, ct);
                if (!paid.IsSuccess) return paid.Error!;
                if (listing.InstanceId is { } instanceId)
                {
                    var item = await inventory.FindInstanceAsync(uow, instanceId, ct)
                        ?? throw new InvalidOperationException($"Escrowed item {instanceId} of listing {listing.Id} is missing.");
                    var moved = await inventory.TransferListedAsync(uow, instanceId, item.Version, buyer.Id, accountId, "market:" + reference, ct);
                    if (!moved.IsSuccess) return moved.Error!;
                }
                else
                {
                    var added = await inventory.AddItemsAsync(uow, buyer.Id, listing.ItemId, listing.Quantity, "market_purchase", reference, ct);
                    if (!added.IsSuccess) return added.Error!;
                }

                var proceeds = listing.Price - tax;
                var credited = await inventory.CreditAsync(uow, listing.SellerCharacterId, proceeds, "market_sale", reference, ct);
                if (!credited.IsSuccess) throw new InvalidOperationException("Seller credit failed: " + credited.Error!.Code);

                await outbox.EnqueueAsync(uow, OutboxMessage.Create(MarketEvents.ListingSold,
                    new MarketEvents.ListingSoldV1(listing.SellerAccountId.Value, listing.SellerCharacterId.Value, listing.Id, listing.ItemId, listing.Quantity,
                        listing.UpgradeLevel, listing.Price, tax, proceeds), now, correlation.CorrelationId), ct);
                await AuditAsync(uow, accountId, "market.bought", listing.Id,
                    new { buyer = buyer.Id.Value, seller = listing.SellerCharacterId.Value, listing.ItemId, listing.Quantity, listing.Price, tax }, now, ct);
                return new PurchaseResult(View(closed, now), listing.Price, paid.Value);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>Seller withdraws an active (or expired) listing; goods return from escrow. The listing fee is not refunded.</summary>
    public Task<Result<ListingView>> CancelAsync(AccountId accountId, Guid listingId, BuyCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<ListingView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
            if (c is null) return CharacterNotFound;
            var listing = await repo.FindAsync(uow, listingId, ct);
            if (listing is null || listing.SellerCharacterId != c.Id) return ListingNotFound;
            if (listing.Status != ListingStatuses.Active) return Unavailable;

            var now = clock.GetUtcNow();
            var closed = listing with { Status = ListingStatuses.Cancelled, Version = listing.Version + 1, ClosedAt = now };
            if (!await repo.TryUpdateAsync(uow, closed, listing.Version, ct)) return Unavailable;
            var reference = listing.Id.ToString();
            if (listing.InstanceId is { } instanceId)
            {
                var item = await inventory.FindInstanceAsync(uow, instanceId, ct)
                    ?? throw new InvalidOperationException($"Escrowed item {instanceId} of listing {listing.Id} is missing.");
                var back = await inventory.SetListedAsync(uow, instanceId, item.Version, false, "market:" + reference, ct);
                if (!back.IsSuccess) return back.Error!;
            }
            else
            {
                var back = await inventory.AddItemsAsync(uow, c.Id, listing.ItemId, listing.Quantity, "market_return", reference, ct);
                if (!back.IsSuccess) return back.Error!;
            }

            await AuditAsync(uow, accountId, "market.cancelled", listing.Id, new { characterId = c.Id.Value, expired = listing.ExpiresAt <= now }, now, ct);
            var view = View(closed, now);
            await uow.CommitAsync(ct);
            return view;
        }, ct);

    public Task<Result<IReadOnlyList<ListingView>>> BrowseAsync(string? itemId, Guid? before, int? limit, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<ListingView>>>(async uow =>
        {
            if (itemId is not null && catalog.Find(itemId) is null) return Error.Validation("item_unknown", "Unknown item.");
            var now = clock.GetUtcNow();
            var rows = await repo.BrowseAsync(uow, itemId, before, Math.Clamp(limit ?? 50, 1, MaxPageSize), now, ct);
            return Result<IReadOnlyList<ListingView>>.Success([.. rows.Select(l => View(l, now))]);
        }, ct);

    public static readonly string[] Sorts = ["newest", "unit_price", "price"];

    /// <summary>Search by name (any part, case-insensitive), type, rarity and a price ceiling; paged by offset.</summary>
    public Task<Result<IReadOnlyList<ListingView>>> SearchAsync(string? q, string? type, string? rarity, long? maxPrice, string? sort, int? offset, int? limit, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<ListingView>>>(async uow =>
        {
            var order = sort ?? "newest";
            if (!Sorts.Contains(order)) return Error.Validation("sort_invalid", "sort: newest, unit_price or price.");
            if (q is { Length: > 40 } || offset is < 0 or > 10_000) return Error.Validation("search_invalid", "Query too long or offset out of range.");
            IReadOnlyList<string>? ids = null;
            if (!string.IsNullOrWhiteSpace(q) || type is not null || rarity is not null)
            {
                var term = q?.Trim();
                ids = [.. catalog.All.Where(i => (term is null || term.Length == 0 || i.NamePl.Contains(term, StringComparison.OrdinalIgnoreCase) || i.Id.Contains(term, StringComparison.OrdinalIgnoreCase))
                    && (type is null || i.Type == type) && (rarity is null || i.Rarity == rarity)).Select(i => i.Id)];
                if (ids.Count == 0) return Result<IReadOnlyList<ListingView>>.Success([]);
            }

            var now = clock.GetUtcNow();
            var rows = await repo.SearchAsync(uow, ids, maxPrice, order, offset ?? 0, Math.Clamp(limit ?? 50, 1, MaxPageSize), now, ct);
            return Result<IReadOnlyList<ListingView>>.Success([.. rows.Select(l => View(l, now))]);
        }, ct);

    public Task<Result<PriceView>> PricesAsync(string itemId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<PriceView>>(async uow =>
        {
            if (catalog.Find(itemId) is not { } item) return Error.NotFound("item_unknown", "Unknown item.");
            var now = clock.GetUtcNow();
            var sales = await repo.SalesAsync(uow, itemId, now.AddDays(-7), 500, ct);
            var units = sales.Select(s => s.Price / Math.Max(1, s.Quantity)).ToList();
            var volume = sales.Sum(s => s.Quantity);
            var active = await repo.SearchAsync(uow, [itemId], null, "unit_price", 0, MaxPageSize, now, ct);
            return new PriceView(itemId, item.NamePl, sales.Count, volume,
                volume == 0 ? null : sales.Sum(s => s.Price) / volume, units.Count == 0 ? null : units.Min(), units.Count == 0 ? null : units.Max(),
                active.Count == 0 ? null : active.Min(l => l.Price / Math.Max(1, l.Quantity)), active.Count,
                [.. sales.Take(10).Select(s => new SaleView(s.Price / Math.Max(1, s.Quantity), s.Quantity, s.ClosedAt ?? s.CreatedAt))]);
        }, ct);

    public Task<Result<ListingView>> GetAsync(Guid listingId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<ListingView>>(async uow =>
            await repo.FindAsync(uow, listingId, ct) is { } l ? View(l, clock.GetUtcNow()) : ListingNotFound, ct);

    public Task<Result<IReadOnlyList<MyListingView>>> MineAsync(AccountId accountId, Guid characterId, Guid? before, int? limit, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<MyListingView>>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var now = clock.GetUtcNow();
            var rows = await repo.ListBySellerAsync(uow, c.Id, before, Math.Clamp(limit ?? 50, 1, MaxPageSize), ct);
            return Result<IReadOnlyList<MyListingView>>.Success([.. rows.Select(l => new MyListingView(View(l, now), l.Fee, l.Tax,
                l.Status == ListingStatuses.Sold ? l.Price - l.Tax : null, l.BuyerCharacterId?.Value, l.ClosedAt))]);
        }, ct);

    private ListingView View(Listing l, DateTimeOffset now)
    {
        var item = catalog.Find(l.ItemId);
        return new ListingView(l.Id, l.ItemId, item?.NamePl ?? l.ItemId, item?.Rarity ?? "mortal", l.Quantity, l.InstanceId, l.UpgradeLevel, l.Price,
            l.SellerCharacterId.Value, l.SellerName, l.Status, l.CreatedAt, l.ExpiresAt, l.Status == ListingStatuses.Active && l.ExpiresAt <= now, l.InstanceRef);
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, Guid listingId, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "listing", listingId.ToString(),
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}
