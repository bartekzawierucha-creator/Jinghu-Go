using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Identity.Api;

public sealed record RegisterRequest(string? Email, string? Password);
public sealed record RegisterResponse(Guid AccountId);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record RefreshRequest(string? RefreshToken);

public static class IdentityEndpoints
{
    public const string AuthRateLimitPolicy = "auth";

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("Identity");

        auth.MapPost("/register", async (RegisterRequest body, IdentityService svc, CancellationToken ct) =>
                ApiResults.From(await svc.RegisterAsync(body.Email, body.Password, ct),
                    id => Results.Created((string?)null, new RegisterResponse(id.Value))))
            .RequireRateLimiting(AuthRateLimitPolicy);

        auth.MapPost("/login", async (LoginRequest body, IdentityService svc, CancellationToken ct) =>
                ApiResults.From(await svc.LoginAsync(body.Email, body.Password, ct), Results.Ok))
            .RequireRateLimiting(AuthRateLimitPolicy);

        auth.MapPost("/refresh", async (RefreshRequest body, IdentityService svc, CancellationToken ct) =>
                ApiResults.From(await svc.RefreshAsync(body.RefreshToken, ct), Results.Ok))
            .RequireRateLimiting(AuthRateLimitPolicy);

        auth.MapPost("/logout", async (HttpContext http, IdentityService svc, CancellationToken ct) =>
            {
                await svc.LogoutAsync(http.User.GetSessionId(), ct);
                return Results.NoContent();
            })
            .RequireAuthorization();

        return app;
    }
}
