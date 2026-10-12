namespace JianghuGo.Application;

/// <summary>
/// One atomic, isolated transaction. Changes become visible only after <see cref="CommitAsync"/>;
/// disposing without commit rolls everything back. Implementations: in-memory (serializable via a
/// single lock) and, later, PostgreSQL (NpgsqlTransaction, see docs/decisions/0002).
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IUnitOfWorkFactory
{
    Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken);
}
