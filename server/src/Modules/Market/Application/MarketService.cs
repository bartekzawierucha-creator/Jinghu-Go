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
}

public sealed record CreateListingCommand(Guid CharacterId, string? ItemId, long Quantity, Guid? InstanceId, int? ExpectedVersion, long Price);

public sealed record BuyCommand(Guid CharacterId);

public sealed record ListingView(Guid Id, string ItemId, string ItemNamePl, string Rarity, long Quantity, Guid? InstanceId, int UpgradeLevel, long Price,
    Guid SellerCharacterId, string SellerName, string Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Expired);

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
                if (cmd.InstanceId is { } instanceId)
                {
                    var item = await inventory.FindInstanceAsync(uow, instanceId, ct);
                    if (item is null || item.CharacterId != c.Id.Value) return Error.NotFound("item_not_found", "Item not found.");
                    if (cmd.ExpectedVersion is not { } version) return Error.Validation("expected_version_required", "expectedVersion is required for unique items.");
                    var listed = await inventory.SetListedAsync(uow, instanceId, version, true, "market:" + reference, ct);
                    if (!listed.IsSuccess) return listed.Error!;
                    (itemId, quantity, level) = (item.ItemId, 1, item.UpgradeLevel);
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
                    null, null, 0, 1, now, now.AddHours(config.ListingHours), null);
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
            l.SellerCharacterId.Value, l.SellerName, l.Status, l.CreatedAt, l.ExpiresAt, l.Status == ListingStatuses.Active && l.ExpiresAt <= now);
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, Guid listingId, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "listing", listingId.ToString(),
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}
