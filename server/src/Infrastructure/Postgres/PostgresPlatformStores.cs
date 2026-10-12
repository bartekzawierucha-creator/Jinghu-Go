using JianghuGo.Application;
using JianghuGo.Domain;

namespace JianghuGo.Infrastructure.Postgres;

public sealed class PostgresIdempotencyStore : IIdempotencyStore
{
    public Task<IdempotencyRecord?> FindAsync(IUnitOfWork uow, AccountId accountId, string operation, string key, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync(
            "SELECT request_hash, response_json::text, created_at FROM platform.idempotency_records WHERE account_id = $1 AND operation = $2 AND key = $3",
            r => new IdempotencyRecord(accountId, operation, key, r.GetString(0), r.GetString(1), r.GetFieldValue<DateTimeOffset>(2)),
            ct, accountId.Value, operation, key);

    public Task AddAsync(IUnitOfWork uow, IdempotencyRecord record, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO platform.idempotency_records (account_id, operation, key, request_hash, response_json, created_at) VALUES ($1, $2, $3, $4, $5::jsonb, $6)",
            ct, record.AccountId.Value, record.Operation, record.Key, record.RequestHash, record.ResponseJson, record.CreatedAt);
}

public sealed class PostgresAuditLog : IAuditLog
{
    public Task AppendAsync(IUnitOfWork uow, AuditEntry entry, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO platform.audit_log (id, account_id, action, entity_type, entity_id, data, correlation_id, occurred_at) VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7, $8)",
            ct, entry.Id, entry.AccountId?.Value, entry.Action, entry.EntityType, entry.EntityId, entry.DataJson, entry.CorrelationId, entry.OccurredAt);

    public async Task<IReadOnlyList<AuditEntry>> ListForEntityAsync(IUnitOfWork uow, string entityType, string entityId, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).QueryAsync(
            "SELECT id, account_id, action, data::text, correlation_id, occurred_at FROM platform.audit_log WHERE entity_type = $1 AND entity_id = $2 ORDER BY occurred_at, id",
            r => new AuditEntry(r.GetGuid(0), r.IsDBNull(1) ? null : new AccountId(r.GetGuid(1)), r.GetString(2), entityType, entityId,
                r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetFieldValue<DateTimeOffset>(5)),
            ct, entityType, entityId);
}
