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

public static class CombatEndpoints
{
    public static IEndpointRouteBuilder MapCombatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/combat/catalog", (CombatConfig c) => Results.Ok(new
        {
            c.RulesVersion,
            c.Slots,
            c.MaxTurns,
            c.TurnDeadlineSeconds,
            c.Distance,
            techniques = c.Techniques,
            paths = c.PathPerks,
            npcs = c.Npcs.Select(n => new { n.Id, n.NamePl, n.NameEn, n.RewardQi, n.Tier, n.Boss, n.WorldOnly, n.Tameable }),
            combo = c.Combo,
            elements = c.Elements is null ? null : new { c.Elements.Overcomes, c.Elements.AdvantagePermille, c.Elements.Paths, c.Elements.Npcs },
            arenaLeagues = c.Arena?.Leagues,
            worldBossNpcId = c.WorldBoss?.NpcId,
        })).WithTags("Combat").RequireAuthorization();

        var character = app.MapGroup("/api/v1/characters/{characterId:guid}/combat").WithTags("Combat").RequireAuthorization();
        character.MapGet("/bonuses", async (Guid characterId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.BonusesAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        character.MapGet("/loadout", async (Guid characterId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetLoadoutAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        character.MapPut("/loadout", async (Guid characterId, SetLoadoutCommand body, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.SetLoadoutAsync(http.User.GetAccountId(), characterId, body, ct), Results.Ok));
        character.MapGet("/pet", async (Guid characterId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetPetAsync(http.User.GetAccountId(), characterId, ct), pet => Results.Ok(new { pet })));
        character.MapPost("/pet/release", async (Guid characterId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ReleasePetAsync(http.User.GetAccountId(), characterId, ct), _ => Results.Ok(new { pet = (PetView?)null })));
        character.MapGet("/hunt", async (Guid characterId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetHuntAsync(http.User.GetAccountId(), characterId, ct), hunt => Results.Ok(new { hunt })));
        character.MapPost("/hunt", async (Guid characterId, StartHuntCommand body, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.StartHuntAsync(http.User.GetAccountId(), characterId, body, ct), hunt => Results.Ok(new { hunt })));
        character.MapPost("/hunt/claim", async (Guid characterId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ClaimHuntAsync(http.User.GetAccountId(), characterId, ct), hunt => Results.Ok(new { hunt })));
        character.MapGet("/tower", async (Guid characterId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetTowerAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        character.MapPost("/tower/start", async (Guid characterId, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, CombatService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.StartTowerAsync(http.User.GetAccountId(), characterId, key, ct), v => Results.Created($"/api/v1/combat/battles/{v.Id}", v))));
        character.MapGet("/world-boss", async (Guid characterId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetWorldBossAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        character.MapPost("/world-boss/start", async (Guid characterId, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, CombatService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.StartWorldBossAsync(http.User.GetAccountId(), characterId, key, ct), v => Results.Created($"/api/v1/combat/battles/{v.Id}", v))));
        character.MapPost("/world-boss/claim", async (Guid characterId, ClaimBossCommand body, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ClaimWorldBossAsync(http.User.GetAccountId(), characterId, body, ct), Results.Ok));
        character.MapGet("/arena", async (Guid characterId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetArenaAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        character.MapPost("/arena/challenge", async (Guid characterId, ArenaChallengeCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, CombatService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.ChallengeArenaAsync(http.User.GetAccountId(), characterId, key, body, ct), v => Results.Created($"/api/v1/combat/battles/{v.Id}", v))));
        character.MapPost("/arena/claim", async (Guid characterId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ClaimArenaWeekAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        character.MapGet("/battles", async (Guid characterId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ListOpenAsync(http.User.GetAccountId(), characterId, ct), list => Results.Ok(new { items = list })));

        var g = app.MapGroup("/api/v1/combat/battles").WithTags("Combat").RequireAuthorization();
        g.MapPost("/pve", async (StartPveCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, CombatService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.StartPveAsync(http.User.GetAccountId(), key, body, ct), v => Results.Created($"/api/v1/combat/battles/{v.Id}", v))));
        g.MapPost("/pvp", async (ChallengeCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, CombatService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.ChallengeAsync(http.User.GetAccountId(), key, body, ct), v => Results.Created($"/api/v1/combat/battles/{v.Id}", v))));
        g.MapGet("/{battleId:guid}", async (Guid battleId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetBattleAsync(http.User.GetAccountId(), battleId, ct), Results.Ok));
        g.MapPost("/{battleId:guid}/accept", async (Guid battleId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.AcceptAsync(http.User.GetAccountId(), battleId, ct), Results.Ok));
        g.MapPost("/{battleId:guid}/decline", async (Guid battleId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.DeclineAsync(http.User.GetAccountId(), battleId, ct), Results.Ok));
        g.MapPost("/{battleId:guid}/auto", async (Guid battleId, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, CombatService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.AutoActAsync(http.User.GetAccountId(), battleId, key, ct), Results.Ok)));
        g.MapPost("/{battleId:guid}/forfeit", async (Guid battleId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ForfeitAsync(http.User.GetAccountId(), battleId, ct), Results.Ok));
        g.MapPost("/{battleId:guid}/actions", async (Guid battleId, ActionCommand body, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, CombatService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.ActAsync(http.User.GetAccountId(), battleId, key, body, ct), Results.Ok)));
        g.MapGet("/{battleId:guid}/turns", async (Guid battleId, HttpContext http, CombatService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ListTurnsAsync(http.User.GetAccountId(), battleId, ct), list => Results.Ok(new { items = list })));
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
