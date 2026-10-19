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

        g.MapPost("/tribulation", async (Guid characterId, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.StartTribulationAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/retreat", async (Guid characterId, RetreatCommand body, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.StartRetreatAsync(http.User.GetAccountId(), characterId, body, ct), Results.Ok));
        g.MapPost("/retreat/leave", async (Guid characterId, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.LeaveRetreatAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/compress", async (Guid characterId, CompressCommand body, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.CompressAsync(http.User.GetAccountId(), characterId, body, ct), Results.Ok));
        g.MapPost("/vessel", async (Guid characterId, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.UpgradeVesselAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/accelerator", async (Guid characterId, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.UseAcceleratorAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/method", async (Guid characterId, MethodCommand body, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.SetMethodAsync(http.User.GetAccountId(), characterId, body, ct), Results.Ok));
        g.MapPost("/method/learn", async (Guid characterId, MethodCommand body, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.LearnMethodAsync(http.User.GetAccountId(), characterId, body, ct), Results.Ok));
        g.MapPost("/auto-breakthrough", async (Guid characterId, AutoBreakthroughCommand body, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.SetAutoBreakthroughAsync(http.User.GetAccountId(), characterId, body, ct), Results.Ok));
        g.MapPost("/reincarnate", async (Guid characterId, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ReincarnateAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/summary/ack", async (Guid characterId, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.AcknowledgeSummaryAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        app.MapGet("/api/v1/cultivation/realms", (JianghuGo.Modules.Cultivation.Domain.CultivationConfig c) => Results.Ok(new
        {
            c.RulesVersion,
            c.StageNames,
            eras = c.Eras,
            realms = c.Realms.Select((r, i) => new { index = i, r.Id, r.NamePl, r.Hanzi, r.Era, r.Stages, r.UnlockPl, r.TribulationPl, r.TitlePl }),
            methods = c.Methods,
        })).WithTags("Cultivation").RequireAuthorization();

        g.MapGet("/breakthroughs", async (Guid characterId, HttpContext http, CultivationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.HistoryAsync(http.User.GetAccountId(), characterId, ct), list => Results.Ok(new
            {
                items = list.Select(a => new
                {
                    a.Id, a.FromRealm, a.FromStage, a.ToRealm, a.ToStage, a.Success,
                    chance = a.ChancePpm / 1_000_000.0, a.QiSpent, a.RulesVersion, a.OccurredAt,
                }),
            })));

        g.MapGet("/meridians", async (Guid characterId, HttpContext http, MeridianService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));

        g.MapPost("/meridians", async (Guid characterId, MeridianCommand body, HttpContext http, MeridianService svc, CancellationToken ct) =>
            ApiResults.From(await svc.OpenAsync(http.User.GetAccountId(), characterId, body, ct), Results.Ok));

        g.MapPost("/meridians/reset", async (Guid characterId, HttpContext http, MeridianService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ResetAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));

        g.MapGet("/ranking", async (Guid characterId, int? limit, HttpContext http, RankingService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(http.User.GetAccountId(), characterId, limit ?? 50, ct), Results.Ok));

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
