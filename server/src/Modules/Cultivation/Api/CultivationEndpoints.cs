using JianghuGo.Application;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Cultivation.Application;
using JianghuGo.Modules.Identity.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Cultivation.Api;

public static class CultivationEndpoints
{
    public static IEndpointRouteBuilder MapCultivationEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/characters/{characterId:guid}/cultivation").WithTags("Cultivation").RequireAuthorization();

        g.MapGet("/", async (Guid characterId, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));

        g.MapPost("/meditation/start", async (Guid characterId, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.StartMeditationAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));

        g.MapPost("/meditation/finish", async (Guid characterId, [FromHeader(Name = "Idempotency-Key")] string? key,
                HttpContext http, CultivationService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.FinishMeditationAsync(http.User.GetAccountId(), characterId, key, ct), Results.Ok)));

        g.MapPost("/steps", async (Guid characterId, StepsCommand body, [FromHeader(Name = "Idempotency-Key")] string? key,
                HttpContext http, CultivationService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.ReportStepsAsync(http.User.GetAccountId(), characterId, key, body, ct), Results.Ok)));

        g.MapPost("/breakthrough", async (Guid characterId, BreakthroughCommand body, [FromHeader(Name = "Idempotency-Key")] string? key,
                HttpContext http, CultivationService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.BreakthroughAsync(http.User.GetAccountId(), characterId, key, body, ct), Results.Ok)));

        g.MapGet("/breakthroughs", async (Guid characterId, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.HistoryAsync(http.User.GetAccountId(), characterId, ct), list => Results.Ok(new
            {
                items = list.Select(a => new
                {
                    a.Id, a.FromRealm, a.FromStage, a.ToRealm, a.ToStage, a.Success,
                    chance = a.ChancePpm / 1_000_000.0, a.QiSpent, a.RulesVersion, a.OccurredAt,
                }),
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
