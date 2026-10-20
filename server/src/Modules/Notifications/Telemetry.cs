using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Notifications;

// 2.5 — crash reports: the app sends the exception type and a few stack frames after a crash (no account, character,
// location or free text). Operators see counts on /ops/metrics and the latest reports on /ops/crashes.

public sealed record CrashReport(Guid Id, string AppVersion, int AndroidSdk, string ExceptionType, string Stack, DateTimeOffset At);

public sealed record CrashCommand(string? AppVersion, int AndroidSdk, string? ExceptionType, string? Stack);

public interface ICrashRepository
{
    Task AddAsync(IUnitOfWork uow, CrashReport r, CancellationToken ct);
    Task<IReadOnlyList<CrashReport>> LatestAsync(IUnitOfWork uow, int limit, CancellationToken ct);
    Task<long> CountSinceAsync(IUnitOfWork uow, DateTimeOffset since, CancellationToken ct);
}

public static class CrashRules
{
    /// <summary>Keeps only safe characters and at most 12 stack lines of "at Namespace.Type.Method" (file paths and arguments dropped).</summary>
    public static string CleanStack(string? stack)
    {
        var lines = (stack ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("at ", StringComparison.Ordinal))
            .Select(l => { var i = l.IndexOf('('); return i > 0 ? l[..i] : l; })
            .Select(l => new string(l.Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or ' ' or '<' or '>' or '`' or '+').ToArray()))
            .Take(12);
        var s = string.Join("\n", lines);
        return s.Length > 2000 ? s[..2000] : s;
    }

    public static string? CleanType(string? type)
    {
        var t = (type ?? string.Empty).Trim();
        return t.Length is 0 or > 120 || !t.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '`' or '+') ? null : t;
    }
}

internal sealed class InMemoryCrashRepository : ICrashRepository
{
    private const string Table = "telemetry.crashes";

    public Task AddAsync(IUnitOfWork uow, CrashReport r, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        u.SetTable(Table, u.Table<Guid, CrashReport>(Table).Add(r.Id, r));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CrashReport>> LatestAsync(IUnitOfWork uow, int limit, CancellationToken ct)
    {
        IReadOnlyList<CrashReport> l = [.. InMemoryUnitOfWork.From(uow).Table<Guid, CrashReport>(Table).Values.OrderByDescending(r => r.Id).Take(limit)];
        return Task.FromResult(l);
    }

    public Task<long> CountSinceAsync(IUnitOfWork uow, DateTimeOffset since, CancellationToken ct) =>
        Task.FromResult((long)InMemoryUnitOfWork.From(uow).Table<Guid, CrashReport>(Table).Values.Count(r => r.At >= since));
}

internal sealed class PostgresCrashRepository : ICrashRepository
{
    public Task AddAsync(IUnitOfWork uow, CrashReport r, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO social.crash_reports (id, app_version, android_sdk, exception_type, stack, created_at) VALUES ($1, $2, $3, $4, $5, $6)",
            ct, r.Id, r.AppVersion, r.AndroidSdk, r.ExceptionType, r.Stack, r.At);

    public async Task<IReadOnlyList<CrashReport>> LatestAsync(IUnitOfWork uow, int limit, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).QueryAsync(
            "SELECT id, app_version, android_sdk, exception_type, stack, created_at FROM social.crash_reports ORDER BY id DESC LIMIT $1",
            r => new CrashReport(r.GetGuid(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetFieldValue<DateTimeOffset>(5)), ct, limit);

    public async Task<long> CountSinceAsync(IUnitOfWork uow, DateTimeOffset since, CancellationToken ct) =>
        (await PostgresUnitOfWork.From(uow).QueryAsync("SELECT count(*) FROM social.crash_reports WHERE created_at >= $1", r => r.GetInt64(0), ct, since))[0];
}

internal sealed class CrashesMetric(ICrashRepository repo) : IOpsMetric
{
    public string Name => "crashes_24h";

    public Task<long> ValueAsync(IUnitOfWork uow, DateTimeOffset now, CancellationToken ct) => repo.CountSinceAsync(uow, now.AddDays(-1), ct);
}

public static class TelemetryEndpoints
{
    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/telemetry/crash", async (CrashCommand body, IUnitOfWorkFactory db, ICrashRepository repo, TimeProvider clock, CancellationToken ct) =>
        {
            var type = CrashRules.CleanType(body.ExceptionType);
            var version = (body.AppVersion ?? string.Empty).Trim();
            if (type is null || version.Length is 0 or > 20 || !version.All(ch => char.IsDigit(ch) || ch == '.') || body.AndroidSdk is < 21 or > 100)
                return Results.BadRequest();
            await db.RunAsync(async uow =>
            {
                await repo.AddAsync(uow, new CrashReport(Uuid7.New(), version, body.AndroidSdk, type, CrashRules.CleanStack(body.Stack), clock.GetUtcNow()), ct);
                await uow.CommitAsync(ct);
            }, ct);
            return Results.NoContent();
        }).WithTags("Operations").RequireAuthorization();
        return app;
    }
}
