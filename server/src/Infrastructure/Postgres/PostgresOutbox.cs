using JianghuGo.Application;

namespace JianghuGo.Infrastructure.Postgres;

public sealed class PostgresOutbox : IOutbox
{
    public Task EnqueueAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO platform.outbox_messages (id, type, payload, correlation_id, occurred_at, available_at) VALUES ($1, $2, $3::jsonb, $4, $5, $5)",
            ct, message.Id, message.Type, message.PayloadJson, message.CorrelationId, message.OccurredAt);

    // SKIP LOCKED: several workers can drain the queue in parallel without blocking or double-claiming.
    public Task<PendingOutboxMessage?> ClaimNextAsync(IUnitOfWork uow, DateTimeOffset now, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync(
            "SELECT id, type, payload::text, occurred_at, correlation_id, attempts FROM platform.outbox_messages " +
            "WHERE status = 'pending' AND available_at <= $1 ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED",
            r => new PendingOutboxMessage(
                new OutboxMessage(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetFieldValue<DateTimeOffset>(3), r.IsDBNull(4) ? null : r.GetString(4)),
                r.GetInt32(5)),
            ct, now);

    public Task MarkProcessedAsync(IUnitOfWork uow, Guid id, DateTimeOffset now, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "UPDATE platform.outbox_messages SET status = 'processed', processed_at = $2 WHERE id = $1 AND status = 'pending'", ct, id, now);

    public Task MarkFailedAsync(IUnitOfWork uow, Guid id, string error, DateTimeOffset? retryAt, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "UPDATE platform.outbox_messages SET attempts = attempts + 1, last_error = left($2, 2000), " +
            "status = CASE WHEN $3::timestamptz IS NULL THEN 'dead' ELSE 'pending' END, available_at = coalesce($3::timestamptz, available_at) WHERE id = $1",
            ct, id, error, retryAt);

    public async Task<OutboxStats> GetStatsAsync(IUnitOfWork uow, DateTimeOffset now, CancellationToken ct) =>
        (await PostgresUnitOfWork.From(uow).QueryAsync(
            "SELECT count(*) FILTER (WHERE status = 'pending'), count(*) FILTER (WHERE status = 'dead'), min(occurred_at) FILTER (WHERE status = 'pending') " +
            "FROM platform.outbox_messages WHERE status <> 'processed'",
            r => new OutboxStats((int)r.GetInt64(0), (int)r.GetInt64(1), r.IsDBNull(2) ? null : now - r.GetFieldValue<DateTimeOffset>(2)),
            ct)).Single();
}
