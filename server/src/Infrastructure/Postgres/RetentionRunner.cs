using System.Diagnostics.Metrics;
using JianghuGo.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JianghuGo.Infrastructure.Postgres;

/// <summary>
/// Applies every registered <see cref="RetentionPolicy"/> on a schedule (Worker only; PostgreSQL only — the in-memory store
/// is not durable). Each batch is its own short transaction, so purging never holds long locks.
/// </summary>
public sealed class RetentionRunner(
    PostgresDatabase database,
    IEnumerable<RetentionPolicy> policies,
    TimeProvider clock,
    IConfiguration configuration,
    ILogger<RetentionRunner> logger) : BackgroundService
{
    private static readonly Meter Meter = new("JianghuGo.Retention");
    private static readonly Counter<long> Purged = Meter.CreateCounter<long>("jianghu.retention.purged");

    public async Task<IReadOnlyDictionary<string, int>> RunOnceAsync(CancellationToken ct)
    {
        var result = new Dictionary<string, int>();
        foreach (var policy in policies)
        {
            var total = 0;
            while (true)
            {
                var deleted = await database.RunAsync(async uow =>
                {
                    var n = await PostgresUnitOfWork.From(uow).ExecuteAsync(policy.DeleteBatchSql, ct, clock.GetUtcNow());
                    await uow.CommitAsync(ct);
                    return n;
                }, ct);
                total += deleted;
                if (deleted < policy.BatchSize) break;
            }

            result[policy.Name] = total;
            if (total > 0)
            {
                Purged.Add(total, new KeyValuePair<string, object?>("policy", policy.Name));
                logger.LogInformation("Retention {Policy}: purged {Count} rows", policy.Name, total);
            }
        }

        return result;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(configuration.GetValue("Retention:IntervalMinutes", 60));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Retention run failed; retrying next interval");
            }

            await Task.Delay(interval, clock, stoppingToken);
        }
    }
}
