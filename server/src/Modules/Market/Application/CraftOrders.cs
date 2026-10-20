using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Identity.Api;
using JianghuGo.Modules.Inventory.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Market.Application;

// 2.3 — craft orders: "I need 5 Qi pills, I pay 300 stones". The reward is escrowed; whoever delivers the items gets it
// (minus the 5 % market tax) and the items go straight to the orderer's bag. The orderer may cancel an open order.

public sealed record CraftOrder(Guid Id, CharacterId OrdererId, string OrdererName, string ItemId, long Quantity, long Reward, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt, string Status, CharacterId? FulfilledBy, DateTimeOffset? ClosedAt);

public static class CraftOrderRules
{
    public const string Open = "open";
    public const string Fulfilled = "fulfilled";
    public const string Cancelled = "cancelled";
    public const int MaxOpenPerCharacter = 5;
    public const int Days = 7;
    public const int TaxPermille = 50;
}

public interface ICraftOrderRepository
{
    Task AddAsync(IUnitOfWork uow, CraftOrder o, CancellationToken ct);
    Task<CraftOrder?> FindAsync(IUnitOfWork uow, Guid id, CancellationToken ct);
    Task<IReadOnlyList<CraftOrder>> OpenAsync(IUnitOfWork uow, DateTimeOffset now, int limit, CancellationToken ct);
    Task<int> CountOpenByAsync(IUnitOfWork uow, CharacterId orderer, CancellationToken ct);
    Task<bool> TryCloseAsync(IUnitOfWork uow, Guid id, string status, CharacterId? by, DateTimeOffset at, CancellationToken ct);
}

public sealed record CraftOrderView(Guid Id, Guid OrdererCharacterId, string OrdererName, string ItemId, string ItemNamePl, long Quantity, long Reward,
    DateTimeOffset ExpiresAt, bool Mine, bool Expired);

public sealed record PlaceOrderCommand(Guid CharacterId, string? ItemId, long Quantity, long Reward);

public sealed record OrderActorCommand(Guid CharacterId);

public sealed class CraftOrderService(
    IUnitOfWorkFactory db,
    ICraftOrderRepository repo,
    ICharacterDirectory characters,
    IInventory inventory,
    IItemCatalog catalog,
    IIdempotencyStore idempotency,
    IAuditLog audit,
    ICorrelationContext correlation,
    Domain.MarketConfig config,
    TimeProvider clock)
{
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");
    private static readonly Error NotFound = Error.NotFound("order_not_found", "Order not found.");

    public Task<Result<IReadOnlyList<CraftOrderView>>> ListAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        db.RunAsync<Result<IReadOnlyList<CraftOrderView>>>(async uow =>
        {
            var me = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (me is null) return CharacterNotFound;
            var now = clock.GetUtcNow();
            return Result<IReadOnlyList<CraftOrderView>>.Success([.. (await repo.OpenAsync(uow, now, 100, ct)).Select(o => View(o, me.Id, now))]);
        }, ct);

    public Task<Result<CraftOrderView>> PlaceAsync(AccountId accountId, string? key, PlaceOrderCommand cmd, CancellationToken ct) =>
        db.RunAsync<Result<CraftOrderView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "market.order_place", key, cmd, async Task<Result<CraftOrderView>> () =>
            {
                var item = catalog.Find(cmd.ItemId);
                if (item is not { Stackable: true, Tradable: true }) return Error.Validation("item_not_orderable", "Only tradable stackable items can be ordered.");
                if (cmd.Quantity < 1 || cmd.Quantity > catalog.MaxStack) return Error.Validation("quantity_invalid", $"Quantity must be 1-{catalog.MaxStack}.");
                if (cmd.Reward < config.MinPrice || cmd.Reward > config.MaxPrice) return Error.Validation("price_invalid", $"Reward must be {config.MinPrice}-{config.MaxPrice}.");
                var me = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
                if (me is null) return CharacterNotFound;
                if (await repo.CountOpenByAsync(uow, me.Id, ct) >= CraftOrderRules.MaxOpenPerCharacter)
                    return Error.LimitReached("order_limit", $"At most {CraftOrderRules.MaxOpenPerCharacter} open orders.");
                var now = clock.GetUtcNow();
                var o = new CraftOrder(Uuid7.New(), me.Id, me.Name, item.Id, cmd.Quantity, cmd.Reward, now, now.AddDays(CraftOrderRules.Days), CraftOrderRules.Open, null, null);
                var paid = await inventory.DebitAsync(uow, me.Id, cmd.Reward, "order_escrow", o.Id.ToString(), ct);
                if (!paid.IsSuccess) return paid.Error!;
                await repo.AddAsync(uow, o, ct);
                await Audit(uow, accountId, "market.order_placed", o.Id, new { o.ItemId, o.Quantity, o.Reward }, now, ct);
                return View(o, me.Id, now);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>Delivers the items: they move to the orderer, the reward (minus tax) to the deliverer — atomically.</summary>
    public Task<Result<long>> FulfilAsync(AccountId accountId, Guid orderId, OrderActorCommand cmd, CancellationToken ct) =>
        db.RunAsync<Result<long>>(async uow =>
        {
            var me = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
            if (me is null) return CharacterNotFound;
            var o = await repo.FindAsync(uow, orderId, ct);
            var now = clock.GetUtcNow();
            if (o is null || o.Status != CraftOrderRules.Open || o.ExpiresAt <= now) return NotFound;
            var orderer = await characters.FindAsync(uow, o.OrdererId.Value, ct);
            if (orderer is null) return NotFound;
            if (orderer.AccountId == accountId) return Error.Conflict("own_order", "You cannot fulfil your own order.");
            if (!await inventory.HasRoomAsync(uow, o.OrdererId, o.ItemId, o.Quantity, ct)) return Error.Conflict("orderer_bag_full", "The orderer's bag is full.");
            if (!await repo.TryCloseAsync(uow, o.Id, CraftOrderRules.Fulfilled, me.Id, now, ct)) return NotFound;
            var reference = o.Id.ToString();
            var removed = await inventory.RemoveItemsAsync(uow, me.Id, o.ItemId, o.Quantity, "order_delivered", reference, ct);
            if (!removed.IsSuccess) return removed.Error!;
            var added = await inventory.AddItemsAsync(uow, o.OrdererId, o.ItemId, o.Quantity, "order_received", reference, ct);
            if (!added.IsSuccess) return added.Error!;
            var payout = o.Reward - (o.Reward * CraftOrderRules.TaxPermille / 1000);
            var credited = await inventory.CreditAsync(uow, me.Id, payout, "order_reward", reference, ct);
            if (!credited.IsSuccess) return credited.Error!;
            await Audit(uow, accountId, "market.order_fulfilled", o.Id, new { by = me.Id.Value, payout }, now, ct);
            await uow.CommitAsync(ct);
            return payout;
        }, ct);

    /// <summary>The orderer withdraws an open (or expired) order and gets the reward back.</summary>
    public Task<Result<long>> CancelAsync(AccountId accountId, Guid orderId, OrderActorCommand cmd, CancellationToken ct) =>
        db.RunAsync<Result<long>>(async uow =>
        {
            var me = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
            if (me is null) return CharacterNotFound;
            var o = await repo.FindAsync(uow, orderId, ct);
            if (o is null || o.OrdererId != me.Id) return NotFound;
            var now = clock.GetUtcNow();
            if (!await repo.TryCloseAsync(uow, o.Id, CraftOrderRules.Cancelled, null, now, ct)) return Error.Conflict("order_closed", "The order is already closed.");
            var credited = await inventory.CreditAsync(uow, me.Id, o.Reward, "order_refund", o.Id.ToString(), ct);
            if (!credited.IsSuccess) return credited.Error!;
            await Audit(uow, accountId, "market.order_cancelled", o.Id, new { o.Reward }, now, ct);
            await uow.CommitAsync(ct);
            return o.Reward;
        }, ct);

    private CraftOrderView View(CraftOrder o, CharacterId me, DateTimeOffset now) =>
        new(o.Id, o.OrdererId.Value, o.OrdererName, o.ItemId, catalog.Find(o.ItemId)?.NamePl ?? o.ItemId, o.Quantity, o.Reward, o.ExpiresAt, o.OrdererId == me, o.ExpiresAt <= now);

    private Task Audit(IUnitOfWork uow, AccountId accountId, string action, Guid id, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "craft_order", id.ToString(), JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}

internal sealed class InMemoryCraftOrderRepository : ICraftOrderRepository
{
    private const string Table = "market.craft_orders";

    private static InMemoryUnitOfWork U(IUnitOfWork uow) => InMemoryUnitOfWork.From(uow);

    public Task AddAsync(IUnitOfWork uow, CraftOrder o, CancellationToken ct)
    {
        U(uow).SetTable(Table, U(uow).Table<Guid, CraftOrder>(Table).Add(o.Id, o));
        return Task.CompletedTask;
    }

    public Task<CraftOrder?> FindAsync(IUnitOfWork uow, Guid id, CancellationToken ct) => Task.FromResult(U(uow).Table<Guid, CraftOrder>(Table).GetValueOrDefault(id));

    public Task<IReadOnlyList<CraftOrder>> OpenAsync(IUnitOfWork uow, DateTimeOffset now, int limit, CancellationToken ct)
    {
        IReadOnlyList<CraftOrder> l = [.. U(uow).Table<Guid, CraftOrder>(Table).Values.Where(o => o.Status == CraftOrderRules.Open).OrderByDescending(o => o.Id).Take(limit)];
        return Task.FromResult(l);
    }

    public Task<int> CountOpenByAsync(IUnitOfWork uow, CharacterId orderer, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, CraftOrder>(Table).Values.Count(o => o.OrdererId == orderer && o.Status == CraftOrderRules.Open));

    public Task<bool> TryCloseAsync(IUnitOfWork uow, Guid id, string status, CharacterId? by, DateTimeOffset at, CancellationToken ct)
    {
        var t = U(uow).Table<Guid, CraftOrder>(Table);
        if (!t.TryGetValue(id, out var o) || o.Status != CraftOrderRules.Open) return Task.FromResult(false);
        U(uow).SetTable(Table, t.SetItem(id, o with { Status = status, FulfilledBy = by, ClosedAt = at }));
        return Task.FromResult(true);
    }
}

internal sealed class PostgresCraftOrderRepository : ICraftOrderRepository
{
    private const string Columns = "id, orderer_id, orderer_name, item_id, quantity, reward, created_at, expires_at, status, fulfilled_by, closed_at";

    private static PostgresUnitOfWork P(IUnitOfWork uow) => PostgresUnitOfWork.From(uow);

    private static CraftOrder Map(Npgsql.NpgsqlDataReader r) => new(r.GetGuid(0), new CharacterId(r.GetGuid(1)), r.GetString(2), r.GetString(3), r.GetInt64(4), r.GetInt64(5),
        r.GetFieldValue<DateTimeOffset>(6), r.GetFieldValue<DateTimeOffset>(7), r.GetString(8), r.IsDBNull(9) ? null : new CharacterId(r.GetGuid(9)),
        r.IsDBNull(10) ? null : r.GetFieldValue<DateTimeOffset>(10));

    public Task AddAsync(IUnitOfWork uow, CraftOrder o, CancellationToken ct) =>
        P(uow).ExecuteAsync($"INSERT INTO market.craft_orders ({Columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)",
            ct, o.Id, o.OrdererId.Value, o.OrdererName, o.ItemId, o.Quantity, o.Reward, o.CreatedAt, o.ExpiresAt, o.Status, o.FulfilledBy?.Value, o.ClosedAt);

    public async Task<CraftOrder?> FindAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        (await P(uow).QueryAsync($"SELECT {Columns} FROM market.craft_orders WHERE id = $1", Map, ct, id)).FirstOrDefault();

    public async Task<IReadOnlyList<CraftOrder>> OpenAsync(IUnitOfWork uow, DateTimeOffset now, int limit, CancellationToken ct) =>
        await P(uow).QueryAsync($"SELECT {Columns} FROM market.craft_orders WHERE status = 'open' ORDER BY id DESC LIMIT $1", Map, ct, limit);

    public async Task<int> CountOpenByAsync(IUnitOfWork uow, CharacterId orderer, CancellationToken ct) =>
        (await P(uow).QueryAsync("SELECT count(*)::int FROM market.craft_orders WHERE orderer_id = $1 AND status = 'open'", r => r.GetInt32(0), ct, orderer.Value))[0];

    public async Task<bool> TryCloseAsync(IUnitOfWork uow, Guid id, string status, CharacterId? by, DateTimeOffset at, CancellationToken ct) =>
        await P(uow).ExecuteAsync("UPDATE market.craft_orders SET status = $2, fulfilled_by = $3, closed_at = $4 WHERE id = $1 AND status = 'open'", ct, id, status, by?.Value, at) == 1;
}

public static class CraftOrderEndpoints
{
    public static IEndpointRouteBuilder MapCraftOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/market/orders").WithTags("Market").RequireAuthorization();
        g.MapGet("/", async (Guid characterId, HttpContext http, CraftOrderService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ListAsync(http.User.GetAccountId(), characterId, ct), list => Results.Ok(new { items = list })));
        g.MapPost("/", async (PlaceOrderCommand body, [Microsoft.AspNetCore.Mvc.FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, CraftOrderService svc, CancellationToken ct) =>
            ApiResults.From(await svc.PlaceAsync(http.User.GetAccountId(), key, body, ct), Results.Ok));
        g.MapPost("/{orderId:guid}/fulfil", async (Guid orderId, OrderActorCommand body, HttpContext http, CraftOrderService svc, CancellationToken ct) =>
            ApiResults.From(await svc.FulfilAsync(http.User.GetAccountId(), orderId, body, ct), paid => Results.Ok(new { reward = paid })));
        g.MapPost("/{orderId:guid}/cancel", async (Guid orderId, OrderActorCommand body, HttpContext http, CraftOrderService svc, CancellationToken ct) =>
            ApiResults.From(await svc.CancelAsync(http.User.GetAccountId(), orderId, body, ct), refund => Results.Ok(new { refund })));
        return app;
    }
}
