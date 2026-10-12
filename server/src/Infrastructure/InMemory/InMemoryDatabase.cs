using System.Collections.Immutable;
using JianghuGo.Application;

namespace JianghuGo.Infrastructure.InMemory;

/// <summary>
/// Process-local transactional store used for development and automated tests while the PostgreSQL
/// adapter is pending (docs/decisions/0002). Guarantees: every unit of work is SERIALIZABLE (one at a
/// time, via an async lock) and atomic (copy-on-write immutable tables swapped in on commit).
/// Not durable: data is lost on restart. Never use outside development/tests.
/// </summary>
public sealed class InMemoryDatabase : IUnitOfWorkFactory, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ImmutableDictionary<string, object> _tables = ImmutableDictionary<string, object>.Empty;

    public async Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        return new InMemoryUnitOfWork(this, _tables);
    }

    internal void Commit(ImmutableDictionary<string, object> tables) => _tables = tables;

    internal void Release() => _gate.Release();

    public void Dispose() => _gate.Dispose();
}

public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly InMemoryDatabase _db;
    private ImmutableDictionary<string, object> _tables;
    private bool _completed;

    internal InMemoryUnitOfWork(InMemoryDatabase db, ImmutableDictionary<string, object> snapshot)
    {
        _db = db;
        _tables = snapshot;
    }

    public ImmutableDictionary<TKey, TRow> Table<TKey, TRow>(string name)
        where TKey : notnull
    {
        EnsureActive();
        return _tables.TryGetValue(name, out var t) ? (ImmutableDictionary<TKey, TRow>)t : ImmutableDictionary<TKey, TRow>.Empty;
    }

    public void SetTable<TKey, TRow>(string name, ImmutableDictionary<TKey, TRow> table)
        where TKey : notnull
    {
        EnsureActive();
        _tables = _tables.SetItem(name, table);
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        _db.Commit(_tables);
        _completed = true;
        _db.Release();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            _completed = true; // rollback: snapshot changes are discarded
            _db.Release();
        }

        return ValueTask.CompletedTask;
    }

    private void EnsureActive() => ObjectDisposedException.ThrowIf(_completed, this);

    public static InMemoryUnitOfWork From(IUnitOfWork uow) =>
        uow as InMemoryUnitOfWork ?? throw new InvalidOperationException("In-memory repository used with a non in-memory unit of work.");
}
