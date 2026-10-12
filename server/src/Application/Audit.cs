using JianghuGo.Domain;

namespace JianghuGo.Application;

/// <summary>Append-only record of a security- or economy-relevant change. Never updated or deleted.</summary>
public sealed record AuditEntry(
    Guid Id,
    AccountId? AccountId,
    string Action,
    string EntityType,
    string EntityId,
    string DataJson,
    string? CorrelationId,
    DateTimeOffset OccurredAt);

public interface IAuditLog
{
    Task AppendAsync(IUnitOfWork uow, AuditEntry entry, CancellationToken ct);
    Task<IReadOnlyList<AuditEntry>> ListForEntityAsync(IUnitOfWork uow, string entityType, string entityId, CancellationToken ct);
}

public interface ICorrelationContext
{
    string? CorrelationId { get; }
}
