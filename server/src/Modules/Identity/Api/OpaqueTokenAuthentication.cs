using System.Security.Claims;
using System.Text.Encodings.Web;
using JianghuGo.Domain;
using JianghuGo.Modules.Identity.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JianghuGo.Modules.Identity.Api;

public static class JianghuClaims
{
    public const string Scheme = "OpaqueBearer";
    public const string AccountId = "sub";
    public const string SessionId = "sid";

    public static AccountId GetAccountId(this ClaimsPrincipal user) =>
        new(Guid.Parse(user.FindFirstValue(AccountId) ?? throw new InvalidOperationException("Unauthenticated principal.")));

    public static SessionId GetSessionId(this ClaimsPrincipal user) =>
        new(Guid.Parse(user.FindFirstValue(SessionId) ?? throw new InvalidOperationException("Unauthenticated principal.")));
}

internal sealed class OpaqueTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IdentityService identity)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await identity.AuthenticateAsync(header["Bearer ".Length..].Trim(), Context.RequestAborted);
        if (session is null)
        {
            return AuthenticateResult.Fail("Invalid, expired or revoked access token.");
        }

        var identityClaims = new ClaimsIdentity(
            [
                new Claim(JianghuClaims.AccountId, session.AccountId.ToString()),
                new Claim(JianghuClaims.SessionId, session.SessionId.ToString()),
            ],
            Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identityClaims), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return base.HandleChallengeAsync(properties);
    }
}
