using JianghuGo.Application;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Identity.Api;
using JianghuGo.Modules.Sects.Application;
using JianghuGo.Modules.Sects.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Sects.Api;

public static class SectEndpoints
{
    public static IEndpointRouteBuilder MapSectEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/sects").WithTags("Sects").RequireAuthorization();

        g.MapGet("/rules", (SectsConfig c) => Results.Ok(c));

        g.MapGet("/", async (Guid? before, int? limit, SectService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ListAsync(before, limit, ct), list => Results.Ok(new { items = list, next = list.Count > 0 ? list[^1].Id : (Guid?)null })));

        g.MapPost("/", async (FoundSectCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, SectService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.FoundAsync(http.User.GetAccountId(), key, body, ct), v => Results.Created($"/api/v1/sects/{v.Sect.Id}", v))));

        g.MapGet("/{sectId:guid}/development", async (Guid sectId, Guid characterId, HttpContext http, SectDevelopmentService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(http.User.GetAccountId(), sectId, characterId, ct), Results.Ok));

        g.MapPost("/{sectId:guid}/buildings", async (Guid sectId, BuildCommand body, HttpContext http, SectDevelopmentService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.BuildAsync(http.User.GetAccountId(), sectId, body, ct), Results.Ok)));

        g.MapPost("/{sectId:guid}/missions/claim", async (Guid sectId, MissionClaimCommand body, HttpContext http, SectDevelopmentService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.ClaimMissionAsync(http.User.GetAccountId(), sectId, body, ct), Results.Ok)));

        g.MapPost("/{sectId:guid}/boss/fight", async (Guid sectId, ActorCommand body, HttpContext http, SectDevelopmentService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.FightBossAsync(http.User.GetAccountId(), sectId, body, ct), Results.Ok)));

        g.MapGet("/{sectId:guid}", async (Guid sectId, SectService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(sectId, ct), Results.Ok));

        g.MapGet("/{sectId:guid}/history", async (Guid sectId, Guid characterId, Guid? before, int? limit, HttpContext http, SectService svc, CancellationToken ct) =>
            ApiResults.From(await svc.HistoryAsync(http.User.GetAccountId(), sectId, characterId, before, limit, ct), list => Results.Ok(new { items = list })));

        g.MapGet("/{sectId:guid}/chat", async (Guid sectId, Guid characterId, Guid? after, int? limit, HttpContext http, SectService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ChatAsync(http.User.GetAccountId(), sectId, characterId, after, limit, ct), list => Results.Ok(new { items = list })));

        g.MapPost("/{sectId:guid}/chat", async (Guid sectId, PostChatCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, SectService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.PostChatAsync(http.User.GetAccountId(), sectId, key, body, ct), v => Results.Created($"/api/v1/sects/{sectId}/chat", v))));

        g.MapPost("/{sectId:guid}/applications", async (Guid sectId, ApplyCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, SectService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.ApplyAsync(http.User.GetAccountId(), sectId, key, body, ct), v => Results.Created($"/api/v1/sects/{sectId}", v))));

        g.MapPost("/applications/{applicationId:guid}/decision", async (Guid applicationId, DecideCommand body, HttpContext http, SectService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.DecideAsync(http.User.GetAccountId(), applicationId, body, ct), Results.Ok)));

        g.MapPost("/applications/{applicationId:guid}/withdraw", async (Guid applicationId, ActorCommand body, HttpContext http, SectService svc, CancellationToken ct) =>
            ApiResults.From(await svc.WithdrawAsync(http.User.GetAccountId(), applicationId, body, ct), Results.Ok));

        g.MapPost("/{sectId:guid}/leave", async (Guid sectId, ActorCommand body, HttpContext http, SectService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.LeaveAsync(http.User.GetAccountId(), sectId, body, ct), Results.Ok)));

        g.MapPost("/{sectId:guid}/kick", async (Guid sectId, TargetCommand body, HttpContext http, SectService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.KickAsync(http.User.GetAccountId(), sectId, body, ct), Results.Ok)));

        g.MapPost("/{sectId:guid}/roles", async (Guid sectId, RoleCommand body, HttpContext http, SectService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.SetRoleAsync(http.User.GetAccountId(), sectId, body, ct), Results.Ok)));

        g.MapPost("/{sectId:guid}/donations", async (Guid sectId, DonateCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, SectService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.DonateAsync(http.User.GetAccountId(), sectId, key, body, ct), Results.Ok)));

        g.MapPost("/{sectId:guid}/seat", async (Guid sectId, ActorCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, SectService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.SetSeatAsync(http.User.GetAccountId(), sectId, key, body, ct), Results.Ok)));

        app.MapGet("/api/v1/characters/{characterId:guid}/sect", async (Guid characterId, HttpContext http, SectService svc, CancellationToken ct) =>
            ApiResults.From(await svc.MineAsync(http.User.GetAccountId(), characterId, ct), Results.Ok)).WithTags("Sects").RequireAuthorization();
        return app;
    }

    // Duplicate keys here are either an idempotency race or a uniqueness race (name, membership): both are retryable conflicts.
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
