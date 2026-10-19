using JianghuGo.Application;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Identity.Api;
using JianghuGo.Modules.Market.Application;
using JianghuGo.Modules.Market.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Market.Api;

public static class MarketEndpoints
{
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/market").WithTags("Market").RequireAuthorization();

        g.MapGet("/rules", (MarketConfig c) => Results.Ok(c));

        g.MapGet("/listings", async (string? itemId, Guid? before, int? limit, MarketService svc, CancellationToken ct) =>
            ApiResults.From(await svc.BrowseAsync(itemId, before, limit, ct), list => Results.Ok(new { items = list, next = list.Count > 0 ? list[^1].Id : (Guid?)null })));

        g.MapGet("/search", async (string? q, string? type, string? rarity, long? maxPrice, string? sort, int? offset, int? limit, MarketService svc, CancellationToken ct) =>
            ApiResults.From(await svc.SearchAsync(q, type, rarity, maxPrice, sort, offset, limit, ct), list => Results.Ok(new { items = list })));

        g.MapGet("/prices/{itemId}", async (string itemId, MarketService svc, CancellationToken ct) =>
            ApiResults.From(await svc.PricesAsync(itemId, ct), Results.Ok));

        g.MapGet("/listings/{listingId:guid}", async (Guid listingId, MarketService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(listingId, ct), Results.Ok));

        g.MapPost("/listings", async (CreateListingCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, MarketService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.CreateAsync(http.User.GetAccountId(), key, body, ct),
                v => Results.Created($"/api/v1/market/listings/{v.Id}", v))));

        g.MapPost("/listings/{listingId:guid}/buy", async (Guid listingId, BuyCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, MarketService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.BuyAsync(http.User.GetAccountId(), listingId, key, body, ct), Results.Ok)));

        g.MapPost("/listings/{listingId:guid}/cancel", async (Guid listingId, BuyCommand body, HttpContext http, MarketService svc, CancellationToken ct) =>
            ApiResults.From(await svc.CancelAsync(http.User.GetAccountId(), listingId, body, ct), Results.Ok));

        app.MapGet("/api/v1/characters/{characterId:guid}/market/listings", async (Guid characterId, Guid? before, int? limit, HttpContext http, MarketService svc, CancellationToken ct) =>
            ApiResults.From(await svc.MineAsync(http.User.GetAccountId(), characterId, before, limit, ct),
                list => Results.Ok(new { items = list, next = list.Count > 0 ? list[^1].Listing.Id : (Guid?)null })))
            .WithTags("Market").RequireAuthorization();
        return app;
    }

    private static async Task<IResult> Guard(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (DuplicateKeyException)
        {
            return ApiResults.IdempotencyRace();
        }
    }
}
