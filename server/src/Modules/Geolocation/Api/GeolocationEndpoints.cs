using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Geolocation.Application;
using JianghuGo.Modules.Identity.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Geolocation.Api;

public static class GeolocationEndpoints
{
    public static IEndpointRouteBuilder MapGeolocationEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/characters/{characterId:guid}/exploration").WithTags("Exploration").RequireAuthorization();
        g.MapGet("/", async (Guid characterId, HttpContext http, GeolocationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/start", async (Guid characterId, HttpContext http, GeolocationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.StartAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/stop", async (Guid characterId, HttpContext http, GeolocationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.StopAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/samples", async (Guid characterId, SamplesCommand body, HttpContext http, GeolocationService svc, CancellationToken ct) =>
            ApiResults.From(await svc.SubmitAsync(http.User.GetAccountId(), characterId, body, ct), Results.Ok));
        return app;
    }
}
