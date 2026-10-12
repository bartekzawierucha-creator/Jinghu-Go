using JianghuGo.Application;
using JianghuGo.Domain;

namespace JianghuGo.Infrastructure.InMemory;

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private const string TableName = "idempotency_records";

    public Task<IdempotencyRecord?> FindAsync(IUnitOfWork uow, AccountId accountId, string operation, string key, CancellationToken ct)
    {
        var table = InMemoryUnitOfWork.From(uow).Table<(AccountId, string, string), IdempotencyRecord>(TableName);
        return Task.FromResult(table.GetValueOrDefault((accountId, operation, key)));
    }

    public Task AddAsync(IUnitOfWork uow, IdempotencyRecord record, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var table = u.Table<(AccountId, string, string), IdempotencyRecord>(TableName);
        var pk = (record.AccountId, record.Operation, record.Key);
        if (table.ContainsKey(pk))
        {
            throw new DuplicateKeyException("Idempotency record already exists.");
        }

        u.SetTable(TableName, table.Add(pk, record));
        return Task.CompletedTask;
    }
}

public sealed class InMemoryAuditLog : IAuditLog
{
    private const string TableName = "audit_log";

    public Task AppendAsync(IUnitOfWork uow, AuditEntry entry, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var table = u.Table<Guid, AuditEntry>(TableName);
        u.SetTable(TableName, table.Add(entry.Id, entry)); // Add throws on duplicate id: entries are immutable
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> ListForEntityAsync(IUnitOfWork uow, string entityType, string entityId, CancellationToken ct)
    {
        var table = InMemoryUnitOfWork.From(uow).Table<Guid, AuditEntry>(TableName);
        IReadOnlyList<AuditEntry> list = [.. table.Values
            .Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)];
        return Task.FromResult(list);
    }
}
