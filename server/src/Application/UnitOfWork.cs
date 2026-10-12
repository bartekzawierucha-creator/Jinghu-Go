namespace JianghuGo.Application;

/// <summary>
/// One atomic, isolated (SERIALIZABLE) transaction. Changes become visible only after
/// <see cref="CommitAsync"/>; disposing without commit rolls everything back.
/// Implementations: in-memory (one transaction at a time) and PostgreSQL (SERIALIZABLE isolation).
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IUnitOfWorkFactory
{
    Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The transaction lost a race with a concurrent one (serialization failure / deadlock) and was rolled
/// back. Safe to retry the whole unit of work from the start.
/// </summary>
public sealed class TransientConflictException(string message, Exception? inner = null) : Exception(message, inner);

public static class UnitOfWorkFactoryExtensions
{
    public const int MaxAttempts = 25;

    /// <summary>
    /// Runs <paramref name="work"/> in a fresh unit of work, re-running it from scratch when the store reports a
    /// transient conflict. <paramref name="work"/> must commit itself; it must not have external side effects
    /// (HTTP calls, etc.) because it may run more than once. Effects happen only in the committed attempt.
    /// </summary>
    public static async Task<T> RunAsync<T>(this IUnitOfWorkFactory factory, Func<IUnitOfWork, Task<T>> work, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var uow = await factory.BeginAsync(ct);
                return await work(uow);
            }
            catch (TransientConflictException) when (attempt < MaxAttempts)
            {
                // Jittered backoff so racing transactions do not collide again in lockstep.
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1, 5 * Math.Min(attempt, 10))), ct);
            }
        }
    }

    public static Task RunAsync(this IUnitOfWorkFactory factory, Func<IUnitOfWork, Task> work, CancellationToken ct) =>
        factory.RunAsync<bool>(async uow =>
        {
            await work(uow);
            return true;
        }, ct);
}
