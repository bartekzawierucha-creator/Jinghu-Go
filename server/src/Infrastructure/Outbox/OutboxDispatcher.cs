using System.Diagnostics.Metrics;
using JianghuGo.Application;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JianghuGo.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public int MaxAttempts { get; set; } = 8;
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan MaxHealthyLag { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Drains the outbox one message per transaction: claim → run handlers → mark processed → commit.
/// A crash or restart at any point before commit rolls everything back and the message is simply
/// claimed again later (at-least-once delivery, exactly-once effects for DB-only handlers).
/// </summary>
public sealed class OutboxDispatcher(
    IUnitOfWorkFactory uowFactory,
    IOutbox outbox,
    IEnumerable<IOutboxHandler> handlers,
    TimeProvider clock,
    IOptions<OutboxOptions> options,
    IMeterFactory meters,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public const string MeterName = "JianghuGo.Outbox";

    private readonly ILookup<string, IOutboxHandler> _handlers = handlers.ToLookup(h => h.EventType, StringComparer.Ordinal);
    private readonly Meter _meter = meters.Create(MeterName);
    private Counter<long>? _processed;
    private Counter<long>? _failed;
    private Counter<long>? _dead;
    private Histogram<double>? _lag;

    private sealed class HandlerFailure(PendingOutboxMessage pending, Exception inner) : Exception(inner.Message, inner)
    {
        public PendingOutboxMessage Pending { get; } = pending;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox dispatcher started ({HandlerCount} handlers)", _handlers.Sum(g => g.Count()));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await ProcessNextAsync(stoppingToken))
                {
                    await Task.Delay(options.Value.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Storage outage etc.: keep the worker alive, back off.
                logger.LogError(ex, "Outbox dispatcher loop error");
                await Task.Delay(options.Value.PollInterval * 5, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    /// <summary>Processes at most one due message. Returns false when nothing was due.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        EnsureInstruments();
        try
        {
            return await uowFactory.RunAsync(async uow =>
            {
                var now = clock.GetUtcNow();
                var pending = await outbox.ClaimNextAsync(uow, now, ct);
                if (pending is null)
                {
                    return false;
                }

                try
                {
                    var matched = _handlers[pending.Message.Type].ToList();
                    if (matched.Count == 0)
                    {
                        logger.LogWarning("No handler for outbox event {Type} ({Id}); marking processed", pending.Message.Type, pending.Message.Id);
                    }

                    foreach (var handler in matched)
                    {
                        await handler.HandleAsync(uow, pending.Message, ct);
                    }

                    await outbox.MarkProcessedAsync(uow, pending.Message.Id, now, ct);
                    await uow.CommitAsync(ct);
                }
                catch (Exception ex) when (ex is not TransientConflictException and not OperationCanceledException)
                {
                    throw new HandlerFailure(pending, ex);
                }

                _processed!.Add(1, new KeyValuePair<string, object?>("type", pending.Message.Type));
                _lag!.Record((now - pending.Message.OccurredAt).TotalSeconds);
                return true;
            }, ct);
        }
        catch (HandlerFailure failure)
        {
            await RecordFailureAsync(failure, ct);
            return true;
        }
    }

    /// <summary>Test/ops helper: processes until nothing is due. Returns the number processed.</summary>
    public async Task<int> DrainAsync(CancellationToken ct)
    {
        var n = 0;
        while (await ProcessNextAsync(ct))
        {
            n++;
        }

        return n;
    }

    private async Task RecordFailureAsync(HandlerFailure failure, CancellationToken ct)
    {
        var msg = failure.Pending.Message;
        var attempt = failure.Pending.Attempts + 1;
        var now = clock.GetUtcNow();
        DateTimeOffset? retryAt = attempt >= options.Value.MaxAttempts
            ? null
            : now + TimeSpan.FromSeconds(Math.Min(options.Value.MaxBackoff.TotalSeconds, Math.Pow(2, attempt)));
        logger.LogError(failure.InnerException, "Outbox handler failed for {Type} {Id} (attempt {Attempt}){DeadLetter}",
            msg.Type, msg.Id, attempt, retryAt is null ? " - moved to dead letter" : string.Empty);
        await uowFactory.RunAsync(async uow =>
        {
            await outbox.MarkFailedAsync(uow, msg.Id, failure.InnerException!.GetType().Name + ": " + failure.InnerException.Message, retryAt, ct);
            await uow.CommitAsync(ct);
        }, ct);
        (retryAt is null ? _dead : _failed)!.Add(1, new KeyValuePair<string, object?>("type", msg.Type));
    }

    private void EnsureInstruments()
    {
        _processed ??= _meter.CreateCounter<long>("jianghu.outbox.processed");
        _failed ??= _meter.CreateCounter<long>("jianghu.outbox.failed");
        _dead ??= _meter.CreateCounter<long>("jianghu.outbox.dead_lettered");
        _lag ??= _meter.CreateHistogram<double>("jianghu.outbox.lag", unit: "s");
    }
}

/// <summary>Degraded when events wait too long (worker down/stuck) or any message is dead-lettered.</summary>
public sealed class OutboxHealthCheck(IUnitOfWorkFactory uowFactory, IOutbox outbox, TimeProvider clock, IOptions<OutboxOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var stats = await uowFactory.RunAsync(uow => outbox.GetStatsAsync(uow, clock.GetUtcNow(), cancellationToken), cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["pending"] = stats.Pending,
            ["deadLettered"] = stats.DeadLettered,
            ["oldestPendingSeconds"] = stats.OldestPendingAge?.TotalSeconds ?? 0,
        };
        if (stats.DeadLettered > 0)
        {
            return HealthCheckResult.Degraded($"{stats.DeadLettered} dead-lettered outbox message(s).", data: data);
        }

        return stats.OldestPendingAge > options.Value.MaxHealthyLag
            ? HealthCheckResult.Degraded($"Oldest pending outbox message is {stats.OldestPendingAge:g} old.", data: data)
            : HealthCheckResult.Healthy(data: data);
    }
}
