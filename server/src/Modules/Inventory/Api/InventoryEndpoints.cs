using JianghuGo.Application;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Identity.Api;
using JianghuGo.Modules.Inventory.Application;
using JianghuGo.Modules.Inventory.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Inventory.Api;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/items", (ItemsConfig c) => Results.Ok(new
        {
            c.RulesVersion,
            currency = new { c.Currency.Id, c.Currency.NamePl, c.Currency.NameEn },
            c.Bag.BaseCapacity,
            c.Bag.MaxStack,
            c.UpgradeBonusPercentPerLevel,
            items = c.Items.Select(i => new
            {
                i.Id, i.NamePl, i.NameEn, i.Type, i.Rarity, i.Tradable, i.Stackable,
                qi = i.Consumable?.Qi,
                slot = i.Equipment?.Slot,
                stats = i.Equipment?.Stats,
                bagBonus = i.Equipment?.BagBonus,
            }),
        })).WithTags("Inventory").RequireAuthorization();

        var g = app.MapGroup("/api/v1/characters/{characterId:guid}/inventory").WithTags("Inventory").RequireAuthorization();

        g.MapGet("/", async (Guid characterId, HttpContext http, InventoryService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));

        g.MapPost("/equip", async (Guid characterId, EquipCommand body, HttpContext http, InventoryService svc, CancellationToken ct) =>
            ApiResults.From(await svc.EquipAsync(http.User.GetAccountId(), characterId, body, ct), Results.Ok));

        g.MapPost("/unequip", async (Guid characterId, EquipCommand body, HttpContext http, InventoryService svc, CancellationToken ct) =>
            ApiResults.From(await svc.UnequipAsync(http.User.GetAccountId(), characterId, body, ct), Results.Ok));

        g.MapPost("/use", async (Guid characterId, UseCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, InventoryService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.UseAsync(http.User.GetAccountId(), characterId, key, body, ct), Results.Ok)));

        g.MapPost("/discard", async (Guid characterId, DiscardCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, InventoryService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.DiscardAsync(http.User.GetAccountId(), characterId, key, body, ct), Results.Ok)));

        g.MapGet("/wallet", async (Guid characterId, Guid? before, int? limit, HttpContext http, InventoryService svc, CancellationToken ct) =>
            ApiResults.From(await svc.WalletAsync(http.User.GetAccountId(), characterId, before, limit, ct), list => Results.Ok(new
            {
                items = list,
                next = list.Count > 0 ? list[^1].Id : (Guid?)null,
            })));

        g.MapGet("/items/{instanceId:guid}/history", async (Guid characterId, Guid instanceId, HttpContext http, InventoryService svc, CancellationToken ct) =>
            ApiResults.From(await svc.HistoryAsync(http.User.GetAccountId(), characterId, instanceId, ct), list => Results.Ok(new { items = list })));

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
