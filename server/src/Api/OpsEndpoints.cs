using System.Security.Cryptography;
using System.Text;
using JianghuGo.Application;
using JianghuGo.Modules.Notifications;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace JianghuGo.Api;

/// <summary>
/// Operators only (2.0): aggregate analytics and the moderation queue. Enabled only when Ops:ApiKey is configured
/// (sent as X-Ops-Key, compared in constant time); otherwise every route answers 404.
/// </summary>
public static class OpsEndpoints
{
    public sealed record DecisionRequest(string? Decision);

    private static readonly string[] Decisions = ["dismissed", "warned", "renamed", "muted", "banned"];

    public static IEndpointRouteBuilder MapOpsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/ops").WithTags("Operations").ExcludeFromDescription();

        g.MapGet("/metrics", async (HttpContext http, IConfiguration cfg, IUnitOfWorkFactory db, IEnumerable<IOpsMetric> metrics, TimeProvider clock, CancellationToken ct) =>
        {
            if (!Authorized(http, cfg)) return Results.NotFound();
            var now = clock.GetUtcNow();
            var values = new SortedDictionary<string, long>(StringComparer.Ordinal);
            await db.RunAsync(async uow =>
            {
                foreach (var m in metrics) values[m.Name] = await m.ValueAsync(uow, now, ct);
            }, ct);
            return Results.Ok(new { at = now, values });
        });

        g.MapGet("/crashes", async (HttpContext http, IConfiguration cfg, IUnitOfWorkFactory db, ICrashRepository repo, CancellationToken ct) =>
        {
            if (!Authorized(http, cfg)) return Results.NotFound();
            var items = await db.RunAsync(uow => repo.LatestAsync(uow, 100, ct), ct);
            return Results.Ok(new { items });
        });

        g.MapGet("/reports", async (HttpContext http, IConfiguration cfg, IUnitOfWorkFactory db, ISocialRepository repo, CancellationToken ct) =>
        {
            if (!Authorized(http, cfg)) return Results.NotFound();
            var items = await db.RunAsync(uow => repo.OpenReportsAsync(uow, 100, ct), ct);
            return Results.Ok(new { items = items.Select(r => new { r.Id, reporter = r.Reporter.Value, reported = r.Reported.Value, r.Reason, r.Excerpt, r.CreatedAt }) });
        });

        g.MapPost("/reports/{id:guid}/decision", async (Guid id, DecisionRequest body, HttpContext http, IConfiguration cfg, IUnitOfWorkFactory db, ISocialRepository repo,
            IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            if (!Authorized(http, cfg)) return Results.NotFound();
            if (body.Decision is null || !Decisions.Contains(body.Decision)) return Results.BadRequest(new { error = "decision: " + string.Join(", ", Decisions) });
            var now = clock.GetUtcNow();
            var done = await db.RunAsync(async uow =>
            {
                var ok = await repo.DecideReportAsync(uow, id, body.Decision, now, ct);
                if (ok)
                {
                    await audit.AppendAsync(uow, new AuditEntry(JianghuGo.Domain.Uuid7.New(), null, "moderation.decided", "report", id.ToString(),
                        "{\"decision\":\"" + body.Decision + "\"}", null, now), ct);
                    await uow.CommitAsync(ct);
                }

                return ok;
            }, ct);
            return done ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }

    private static bool Authorized(HttpContext http, IConfiguration cfg)
    {
        var key = cfg["Ops:ApiKey"];
        if (string.IsNullOrEmpty(key) || key.Length < 24) return false;
        var given = http.Request.Headers["X-Ops-Key"].ToString();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(key));
    }
}
