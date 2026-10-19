using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Arts.Application;
using JianghuGo.Modules.Identity.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Arts.Api;

public static class ArtsEndpoints
{
    public static IEndpointRouteBuilder MapArtsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/characters/{characterId:guid}/arts").WithTags("Arts").RequireAuthorization();
        g.MapGet("/", async (Guid characterId, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/notices/dismiss", async (Guid characterId, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.DismissNoticesAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));

        g.MapPost("/flames/traces", async (Guid characterId, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.FollowTracesAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/flames/subdue", async (Guid characterId, FlameCommand cmd, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.SubdueAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapPost("/flames/absorb", async (Guid characterId, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.AbsorbAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/flames/feed", async (Guid characterId, FeedFlameCommand cmd, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.FeedFlameAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapPut("/flames/slots", async (Guid characterId, SlotsCommand cmd, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.SetSlotsAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapPost("/flames/fuse", async (Guid characterId, FuseCommand cmd, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.FuseAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));

        g.MapPost("/venoms/absorb", async (Guid characterId, VenomCommand cmd, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.AbsorbVenomAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapPost("/venoms/cure", async (Guid characterId, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.CureAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/coatings", async (Guid characterId, CoatingCommand cmd, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ApplyCoatingAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));

        g.MapPost("/gu/hatch", async (Guid characterId, GuHatchCommand cmd, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.HatchGuAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapPost("/gu/feed", async (Guid characterId, GuSlotCommand cmd, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.FeedGuAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapPost("/gu/release", async (Guid characterId, GuSlotCommand cmd, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ReleaseGuAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));

        g.MapPost("/tempering/bath", async (Guid characterId, BathCommand cmd, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.StartBathAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapPost("/tempering/advance", async (Guid characterId, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.AdvanceTemperAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));

        g.MapPost("/sword/echoes", async (Guid characterId, EchoCommand cmd, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.OfferEchoesAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapPost("/branch", async (Guid characterId, BranchCommand cmd, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ChooseBranchAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapPost("/capstone", async (Guid characterId, BranchCommand cmd, HttpContext http, ArtsService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ChooseCapstoneAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        return app;
    }
}
