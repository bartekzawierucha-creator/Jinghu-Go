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

public sealed record ResetPasswordRequest(string? Email, string? RecoveryCode, string? NewPassword);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record DeleteAccountRequest(string? Password);

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

        auth.MapPost("/recovery-code", async (HttpContext http, IdentityService svc, CancellationToken ct) =>
                ApiResults.From(await svc.NewRecoveryCodeAsync(http.User.GetAccountId(), ct), code => Results.Ok(new { recoveryCode = code })))
            .RequireAuthorization().RequireRateLimiting(AuthRateLimitPolicy);

        auth.MapPost("/reset-password", async (ResetPasswordRequest body, IdentityService svc, CancellationToken ct) =>
                ApiResults.From(await svc.ResetPasswordAsync(body.Email, body.RecoveryCode, body.NewPassword, ct), _ => Results.NoContent()))
            .RequireRateLimiting(AuthRateLimitPolicy);

        auth.MapPost("/delete-account", async (DeleteAccountRequest body, HttpContext http, IdentityService svc, CancellationToken ct) =>
                ApiResults.From(await svc.DeleteAccountAsync(http.User.GetAccountId(), body.Password, ct), _ => Results.NoContent()))
            .RequireAuthorization().RequireRateLimiting(AuthRateLimitPolicy);

        auth.MapPost("/change-password", async (ChangePasswordRequest body, HttpContext http, IdentityService svc, CancellationToken ct) =>
                ApiResults.From(await svc.ChangePasswordAsync(http.User.GetAccountId(), http.User.GetSessionId(), body.CurrentPassword, body.NewPassword, ct), _ => Results.NoContent()))
            .RequireAuthorization().RequireRateLimiting(AuthRateLimitPolicy);

        return app;
    }
}
