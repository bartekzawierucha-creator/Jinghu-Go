using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Characters.Application;
using JianghuGo.Modules.Characters.Domain;
using JianghuGo.Modules.Identity.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Characters.Api;

public static class CharacterEndpoints
{
    public static IEndpointRouteBuilder MapCharacterEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/characters").WithTags("Characters").RequireAuthorization();

        g.MapGet("/", async (HttpContext http, CharacterService svc, CancellationToken ct) =>
            Results.Ok(new { items = await svc.ListAsync(http.User.GetAccountId(), ct) }));

        g.MapGet("/{characterId:guid}", async (Guid characterId, HttpContext http, CharacterService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));

        g.MapPost("/drafts", async (HttpContext http, CharacterService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetOrCreateDraftAsync(http.User.GetAccountId(), ct),
                o => o.Created ? Results.Created($"/api/v1/characters/drafts/{o.Draft.Id}", o.Draft) : Results.Ok(o.Draft)));

        g.MapGet("/drafts/current", async (HttpContext http, CharacterService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetActiveDraftAsync(http.User.GetAccountId(), ct), Results.Ok));

        g.MapPost("/drafts/{draftId:guid}/reroll", async (Guid draftId, RerollCommand body, [FromHeader(Name = "Idempotency-Key")] string? key,
                HttpContext http, CharacterService svc, CancellationToken ct) =>
            await GuardAsync(async () => ApiResults.From(await svc.RerollAsync(http.User.GetAccountId(), draftId, key, body, ct), Results.Ok)));

        g.MapPost("/drafts/{draftId:guid}/finalize", async (Guid draftId, FinalizeCommand body, [FromHeader(Name = "Idempotency-Key")] string? key,
                HttpContext http, CharacterService svc, CancellationToken ct) =>
            await GuardAsync(async () => ApiResults.From(await svc.FinalizeAsync(http.User.GetAccountId(), draftId, key, body, ct),
                c => Results.Created($"/api/v1/characters/{c.Id}", c))));

        app.MapGet("/api/v1/game-config/character-creation", (CharacterCreationConfig config) => Results.Ok(new
        {
            config.RulesVersion,
            config.Paths,
            stats = config.Stats.Select(s => new { s.Id, s.Min, s.Max }),
            traits = config.Traits.Select(t => new { t.Id, t.Category, t.Rarity, t.NamePl, t.NameEn, t.Modifiers }),
            config.Reroll,
            config.Name,
            config.MaxCharactersPerAccount,
        })).WithTags("Characters").RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GuardAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (JianghuGo.Application.DuplicateKeyException)
        {
            return ApiResults.IdempotencyRace();
        }
    }
}
