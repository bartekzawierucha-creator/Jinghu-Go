using System.Diagnostics;
using System.Threading.RateLimiting;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Composition;
using JianghuGo.Infrastructure.Outbox;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Characters.Api;
using JianghuGo.Modules.Combat.Api;
using JianghuGo.Modules.Crafting.Api;
using JianghuGo.Modules.Cultivation.Api;
using JianghuGo.Modules.Geolocation.Api;
using JianghuGo.Modules.Inventory.Api;
using JianghuGo.Modules.Market.Api;
using JianghuGo.Modules.Sects.Api;
using JianghuGo.Modules.Territory.Api;
using JianghuGo.Modules.World.Api;
using JianghuGo.Modules.Identity.Api;
using JianghuGo.Modules.Notifications;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace JianghuGo.Api;

/// <summary>Composition root. Used by Program and by integration tests (real Kestrel, real pipeline).</summary>
public static class JianghuApi
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const long MaxRequestBodyBytes = 64 * 1024;

    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
        configure?.Invoke(builder);

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(o =>
        {
            o.IncludeScopes = true;
            o.UseUtcTimestamp = true;
            o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
        });

        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
            k.AddServerHeader = false;
        });

        var services = builder.Services;
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
        {
            ctx.ProblemDetails.Extensions["correlationId"] = ctx.HttpContext.TraceIdentifier;
        });
        services.AddHttpContextAccessor();
        services.AddExceptionHandler<JianghuGo.Infrastructure.Http.ConflictExceptionHandler>();
        // Strict request bodies: unknown fields (e.g. a client-sent "stats" or "damage") are rejected, not ignored.
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow);
        services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
        services.AddJianghuCore(builder.Configuration, builder.Environment);
        AddRateLimiting(builder);

        // In development and single-instance setups the outbox is drained inside the API process;
        // production runs JianghuGo.Worker instead (Worker:RunInProcess=false).
        if (builder.Configuration.GetValue("Worker:RunInProcess", false))
        {
            services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());
        }

        services.AddHealthChecks()
            .AddCheck<StorageHealthCheck>("storage", tags: ["ready"])
            .AddCheck<OutboxHealthCheck>("outbox", tags: ["ready"])
            .AddCheck<JianghuGo.Modules.Inventory.Application.EconomyHealthCheck>("economy", tags: ["economy"]);

        var app = builder.Build();

        app.UseExceptionHandler(); // unhandled -> 500 problem+json, details never leaked
        app.UseStatusCodePages();
        app.Use(CorrelationMiddleware);
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .WithMetadata(new HttpMethodMetadata(["GET"]));
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })
            .WithMetadata(new HttpMethodMetadata(["GET"]));
        app.MapHealthChecks("/health/economy", new HealthCheckOptions { Predicate = c => c.Tags.Contains("economy") })
            .WithMetadata(new HttpMethodMetadata(["GET"]));
        app.MapIdentityEndpoints();
        app.MapCharacterEndpoints();
        app.MapCultivationEndpoints();
        app.MapCombatEndpoints();
        app.MapDaoLabEndpoints();
        app.MapGeolocationEndpoints();
        app.MapWorldEndpoints();
        app.MapInventoryEndpoints();
        app.MapCraftingEndpoints();
        app.MapMarketEndpoints();
        app.MapSectEndpoints();
        app.MapTerritoryEndpoints();
        app.MapNotificationEndpoints();
        return app;
    }

    /// <summary>`JianghuGo.Api migrate`: applies database/migrations and exits (deployment step, never at web startup).</summary>
    public static async Task<int> MigrateAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
        var connectionString = builder.Configuration.GetConnectionString("Jianghu")
            ?? throw new InvalidOperationException("ConnectionStrings:Jianghu is required.");
        var dir = builder.Configuration["Storage:MigrationsPath"] ?? Path.Combine(AppContext.BaseDirectory, "migrations");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var applied = await MigrationRunner.RunAsync(dataSource, dir);
        Console.WriteLine(applied.Count == 0 ? "Migrations up to date." : "Applied: " + string.Join(", ", applied));
        return 0;
    }

    private static void AddRateLimiting(WebApplicationBuilder builder)
    {
        var authPermits = builder.Configuration.GetValue("RateLimiting:AuthPermitsPerMinute", 10);
        var globalPermits = builder.Configuration.GetValue("RateLimiting:GlobalPermitsPerMinute", 300);

        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await Results.Problem(statusCode: 429, title: "rate_limited", detail: "Too many requests.",
                        extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" })
                    .ExecuteAsync(ctx.HttpContext);
            };

            // Per client IP for unauthenticated auth endpoints (brute force / credential stuffing).
            o.AddPolicy(IdentityEndpoints.AuthRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "auth:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            // Global: per account when authenticated, otherwise per IP.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var key = http.User.Identity?.IsAuthenticated == true
                    ? "acct:" + http.User.FindFirst(JianghuClaims.AccountId)?.Value
                    : "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = globalPermits,
                    TokensPerPeriod = globalPermits,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
        });
    }

    private static async Task CorrelationMiddleware(HttpContext http, RequestDelegate next)
    {
        // Accept a well-formed client id (for end-to-end tracing), otherwise use the server trace id.
        var incoming = http.Request.Headers[CorrelationHeader].ToString();
        var id = incoming.Length is > 0 and <= 64 && incoming.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? incoming
            : Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier;
        http.TraceIdentifier = id;
        http.Response.Headers[CorrelationHeader] = id;
        http.Response.Headers.XContentTypeOptions = "nosniff";

        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JianghuGo.Request");
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            var sw = Stopwatch.StartNew();
            await next(http);
            // Path only: query strings and bodies may contain secrets or location data.
            logger.LogInformation("HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs:0.0} ms",
                http.Request.Method, http.Request.Path.Value, http.Response.StatusCode, sw.Elapsed.TotalMilliseconds);
        }
    }

    private sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
    {
        public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;
    }

    private sealed class StorageHealthCheck(IUnitOfWorkFactory uowFactory) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await using var uow = await uowFactory.BeginAsync(cts.Token);
                return HealthCheckResult.Healthy();
            }
            catch (OperationCanceledException)
            {
                return HealthCheckResult.Unhealthy("Storage did not respond within 2s.");
            }
        }
    }
}
