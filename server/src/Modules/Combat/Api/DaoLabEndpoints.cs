using JianghuGo.Application;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Combat.Application;
using JianghuGo.Modules.Combat.Domain;
using JianghuGo.Modules.Identity.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Combat.Api;

public static class DaoLabEndpoints
{
    public static IEndpointRouteBuilder MapDaoLabEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/dao-lab").WithTags("DaoLab").RequireAuthorization();

        g.MapGet("/rules", (DaoLabConfig c) => Results.Ok(c));

        // Stateless: lets the client show the budget while the player edits a design.
        g.MapPost("/preview", (DaoDesign body, DaoLabService svc) => Results.Ok(svc.Preview(body)));

        g.MapGet("/scrolls/{techniqueId}", async (string techniqueId, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ScrollInfoAsync(techniqueId, ct), Results.Ok));

        g.MapGet("/techniques/{techniqueId}", async (string techniqueId, DaoLabService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(techniqueId, ct), Results.Ok));

        g.MapPost("/techniques/{techniqueId}/scrolls", async (string techniqueId, InscribeCommand body, [FromHeader(Name = "Idempotency-Key")] string? key,
                HttpContext http, DaoLabService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.InscribeAsync(http.User.GetAccountId(), techniqueId, key, body, ct), v => Results.Created($"/api/v1/dao-lab/techniques/{techniqueId}", v))));

        var c = app.MapGroup("/api/v1/characters/{characterId:guid}/dao-lab").WithTags("DaoLab").RequireAuthorization();

        c.MapGet("/techniques", async (Guid characterId, HttpContext http, DaoLabService svc, CancellationToken ct) =>
            ApiResults.From(await svc.KnownAsync(http.User.GetAccountId(), characterId, ct), list => Results.Ok(new { items = list })));

        c.MapPost("/designs", async (Guid characterId, DaoDesign body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, DaoLabService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.DesignAsync(http.User.GetAccountId(), characterId, key, body, ct),
                v => Results.Created($"/api/v1/dao-lab/techniques/{v.Technique.Id}", v))));

        c.MapPost("/scrolls/{instanceId:guid}/learn", async (Guid characterId, Guid instanceId, LearnCommand body, [FromHeader(Name = "Idempotency-Key")] string? key,
                HttpContext http, DaoLabService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.LearnAsync(http.User.GetAccountId(), characterId, instanceId, key, body, ct), Results.Ok)));

        c.MapPost("/meditate", async (Guid characterId, bool? useToken, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, CombatService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.MeditateOnDaoAsync(http.User.GetAccountId(), characterId, key, ct, useToken ?? false), Results.Ok)));
        c.MapPost("/transmute", async (Guid characterId, TransmuteCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, CombatService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.TransmuteAsync(http.User.GetAccountId(), characterId, key, body, ct), Results.Ok)));

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
