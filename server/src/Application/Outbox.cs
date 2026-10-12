using JianghuGo.Domain;
using System.Text.Json;

namespace JianghuGo.Application;

/// <summary>
/// An integration event written in the SAME transaction as the state change that caused it
/// (transactional outbox). Delivered at-least-once to handlers; handlers make their effects
/// idempotent by <see cref="Id"/>, so redelivery never duplicates an effect.
/// </summary>
public sealed record OutboxMessage(Guid Id, string Type, string PayloadJson, DateTimeOffset OccurredAt, string? CorrelationId)
{
    public static OutboxMessage Create<T>(string type, T payload, DateTimeOffset now, string? correlationId) =>
        new(Uuid7.New(), type, JsonSerializer.Serialize(payload, Idempotency.Json), now, correlationId);

    public T Payload<T>() => JsonSerializer.Deserialize<T>(PayloadJson, Idempotency.Json)
        ?? throw new InvalidOperationException($"Outbox message {Id} has an empty payload.");
}

public sealed record PendingOutboxMessage(OutboxMessage Message, int Attempts);

public interface IOutbox
{
    Task EnqueueAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct);

    /// <summary>Claims the oldest due message (skipping ones locked by other workers), or null.</summary>
    Task<PendingOutboxMessage?> ClaimNextAsync(IUnitOfWork uow, DateTimeOffset now, CancellationToken ct);

    Task MarkProcessedAsync(IUnitOfWork uow, Guid id, DateTimeOffset now, CancellationToken ct);

    /// <summary>Records a failed attempt; the message becomes due again at <paramref name="retryAt"/> (null = dead letter).</summary>
    Task MarkFailedAsync(IUnitOfWork uow, Guid id, string error, DateTimeOffset? retryAt, CancellationToken ct);

    Task<OutboxStats> GetStatsAsync(IUnitOfWork uow, DateTimeOffset now, CancellationToken ct);
}

public sealed record OutboxStats(int Pending, int DeadLettered, TimeSpan? OldestPendingAge);

/// <summary>
/// Reacts to one event type. Runs inside the dispatcher's unit of work: everything written through
/// <paramref name="uow"/> commits atomically with marking the message processed. No external side effects here.
/// </summary>
public interface IOutboxHandler
{
    string EventType { get; }
    Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct);
}
