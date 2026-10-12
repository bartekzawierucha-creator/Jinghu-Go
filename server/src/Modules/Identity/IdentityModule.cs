using JianghuGo.Modules.Identity.Api;
using JianghuGo.Modules.Identity.Application;
using JianghuGo.Modules.Identity.Domain;
using JianghuGo.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentityModuleOptions>().Bind(configuration.GetSection(IdentityModuleOptions.Section))
            .Validate(o => o.AccessTokenLifetime > TimeSpan.Zero && o.AccessTokenLifetime <= TimeSpan.FromHours(1), "Access token lifetime must be in (0, 1h].")
            .Validate(o => o.RefreshTokenLifetime > o.AccessTokenLifetime, "Refresh token must outlive access token.")
            .Validate(o => o.MaxFailedLogins >= 3, "MaxFailedLogins must be >= 3.")
            .ValidateOnStart();

        // PBKDF2-HMAC-SHA512 (Identity v3 format), 100k iterations in .NET 10.
        services.AddSingleton<IPasswordHasher<Account>, PasswordHasher<Account>>();
        services.AddSingleton<IAccountRepository, InMemoryAccountRepository>();
        services.AddSingleton<ISessionRepository, InMemorySessionRepository>();
        services.AddScoped<IdentityService>();

        services.AddAuthentication(JianghuClaims.Scheme)
            .AddScheme<AuthenticationSchemeOptions, OpaqueTokenAuthenticationHandler>(JianghuClaims.Scheme, null);
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null) // health endpoints are anonymous; protected endpoints opt in explicitly
            .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(JianghuClaims.Scheme).RequireAuthenticatedUser().Build());
        return services;
    }
}
