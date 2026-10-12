using JianghuGo.Application;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Identity.Api;
using JianghuGo.Modules.World.Application;
using JianghuGo.Modules.World.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.World.Api;

public static class WorldEndpoints
{
    public static IEndpointRouteBuilder MapWorldEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/world/flame-sites", (WorldConfig c) =>
                Results.Ok(new { items = c.Flame.Sites.Select(s => new { s.Id, s.NamePl, s.Lat, s.Lon }), c.Flame.RadiusM, c.Flame.CooldownHours }))
            .WithTags("World").RequireAuthorization();

        app.MapGet("/api/v1/characters/{characterId:guid}/world/nearby", async (Guid characterId, HttpContext http, WorldService svc, CancellationToken ct) =>
            ApiResults.From(await svc.NearbyAsync(http.User.GetAccountId(), characterId, ct), Results.Ok)).WithTags("World").RequireAuthorization();

        // Node keys travel in the body, not the URL: they encode the map cell, and URLs end up in access logs.
        app.MapPost("/api/v1/world/interactions", async (InteractCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, WorldService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.InteractAsync(http.User.GetAccountId(), key, body, ct), Results.Ok))).WithTags("World").RequireAuthorization();

        app.MapPost("/api/v1/world/flame-attunements", async (AttuneCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, WorldService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.AttuneAsync(http.User.GetAccountId(), key, body, ct), Results.Ok))).WithTags("World").RequireAuthorization();
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
