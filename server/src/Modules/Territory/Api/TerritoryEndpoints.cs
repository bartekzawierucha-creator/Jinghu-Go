using JianghuGo.Application;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Identity.Api;
using JianghuGo.Modules.Territory.Application;
using JianghuGo.Modules.Territory.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Territory.Api;

public static class TerritoryEndpoints
{
    public static IEndpointRouteBuilder MapTerritoryEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/territories").WithTags("Territory").RequireAuthorization();

        g.MapGet("/rules", (TerritoryConfig c) => Results.Ok(c));

        g.MapGet("/{key}", async (string key, TerritoryService svc, CancellationToken ct) => ApiResults.From(await svc.GetAsync(key, ct), Results.Ok));

        // The territory is the one the character stands in (verified position) — clients cannot name a remote one.
        g.MapPost("/claim", async (TerritoryActorCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, TerritoryService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.ClaimAsync(http.User.GetAccountId(), key, body, ct), Results.Ok)));

        g.MapPost("/challenge", async (TerritoryActorCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, TerritoryService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.ChallengeAsync(http.User.GetAccountId(), key, body, ct), v => Results.Created($"/api/v1/territories/{v.TerritoryKey}", v))));

        g.MapPost("/{territoryKey}/tribute", async (string territoryKey, TerritoryActorCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http,
                TerritoryService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.TributeAsync(http.User.GetAccountId(), territoryKey, key, body, ct), Results.Ok)));

        app.MapGet("/api/v1/sects/{sectId:guid}/territories", async (Guid sectId, TerritoryService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ForSectAsync(sectId, ct), Results.Ok)).WithTags("Territory").RequireAuthorization();

        app.MapGet("/api/v1/characters/{characterId:guid}/territory/here", async (Guid characterId, HttpContext http, TerritoryService svc, CancellationToken ct) =>
            ApiResults.From(await svc.HereAsync(http.User.GetAccountId(), characterId, ct), Results.Ok)).WithTags("Territory").RequireAuthorization();
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
