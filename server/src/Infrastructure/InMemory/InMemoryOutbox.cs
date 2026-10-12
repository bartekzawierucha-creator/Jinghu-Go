using JianghuGo.Application;

namespace JianghuGo.Infrastructure.InMemory;

public sealed class InMemoryOutbox : IOutbox
{
    private const string TableName = "platform.outbox";

    private sealed record Row(OutboxMessage Message, int Attempts, DateTimeOffset AvailableAt, DateTimeOffset? ProcessedAt, bool Dead, string? LastError);

    public Task EnqueueAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        u.SetTable(TableName, u.Table<Guid, Row>(TableName).Add(message.Id, new Row(message, 0, message.OccurredAt, null, false, null)));
        return Task.CompletedTask;
    }

    public Task<PendingOutboxMessage?> ClaimNextAsync(IUnitOfWork uow, DateTimeOffset now, CancellationToken ct)
    {
        var row = InMemoryUnitOfWork.From(uow).Table<Guid, Row>(TableName).Values
            .Where(r => r.ProcessedAt is null && !r.Dead && r.AvailableAt <= now)
            .OrderBy(r => r.Message.Id)
            .FirstOrDefault();
        return Task.FromResult(row is null ? null : new PendingOutboxMessage(row.Message, row.Attempts));
    }

    public Task MarkProcessedAsync(IUnitOfWork uow, Guid id, DateTimeOffset now, CancellationToken ct) =>
        Update(uow, id, r => r with { ProcessedAt = now });

    public Task MarkFailedAsync(IUnitOfWork uow, Guid id, string error, DateTimeOffset? retryAt, CancellationToken ct) =>
        Update(uow, id, r => r with { Attempts = r.Attempts + 1, LastError = error, Dead = retryAt is null, AvailableAt = retryAt ?? r.AvailableAt });

    public Task<OutboxStats> GetStatsAsync(IUnitOfWork uow, DateTimeOffset now, CancellationToken ct)
    {
        var rows = InMemoryUnitOfWork.From(uow).Table<Guid, Row>(TableName).Values.ToList();
        var pending = rows.Where(r => r.ProcessedAt is null && !r.Dead).ToList();
        return Task.FromResult(new OutboxStats(pending.Count, rows.Count(r => r.Dead),
            pending.Count == 0 ? null : now - pending.Min(r => r.Message.OccurredAt)));
    }

    private static Task Update(IUnitOfWork uow, Guid id, Func<Row, Row> change)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<Guid, Row>(TableName);
        u.SetTable(TableName, t.SetItem(id, change(t[id])));
        return Task.CompletedTask;
    }
}
