using JianghuGo.Application;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Crafting.Application;
using JianghuGo.Modules.Crafting.Domain;
using JianghuGo.Modules.Identity.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Crafting.Api;

public static class CraftingEndpoints
{
    public static IEndpointRouteBuilder MapCraftingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/crafting/recipes", (CraftingConfig c) => Results.Ok(new
        {
            c.RulesVersion,
            c.AlchemyInsightBonusPermille,
            maxTimesPerRequest = CraftingRules.MaxTimesPerRequest,
            items = c.Recipes,
            upgrade = new
            {
                c.Upgrade.MaxLevel,
                c.Upgrade.Material,
                levels = c.Upgrade.SuccessPermilleByTargetLevel.Select((p, i) => new { level = i + 1, successPermille = p, stones = CraftingRules.UpgradeStones(c, i + 1) }),
            },
        })).WithTags("Crafting").RequireAuthorization();

        var g = app.MapGroup("/api/v1/characters/{characterId:guid}/crafting").WithTags("Crafting").RequireAuthorization();

        g.MapPost("/craft", async (Guid characterId, CraftCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, CraftingService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.CraftAsync(http.User.GetAccountId(), characterId, key, body, ct), Results.Ok)));

        g.MapPost("/upgrade", async (Guid characterId, UpgradeCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, CraftingService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.UpgradeAsync(http.User.GetAccountId(), characterId, key, body, ct), Results.Ok)));

        g.MapGet("/log", async (Guid characterId, Guid? before, int? limit, HttpContext http, CraftingService svc, CancellationToken ct) =>
            ApiResults.From(await svc.LogAsync(http.User.GetAccountId(), characterId, before, limit, ct), list => Results.Ok(new
            {
                items = list.Select(e => new
                {
                    e.Id, e.Kind, e.Subject, e.Attempts, e.Successes, e.ChancePermille, seed = e.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    e.StonesSpent, e.FromLevel, e.ToLevel, e.RulesVersion, at = e.At,
                }),
                next = list.Count > 0 ? list[^1].Id : (Guid?)null,
            })));

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
