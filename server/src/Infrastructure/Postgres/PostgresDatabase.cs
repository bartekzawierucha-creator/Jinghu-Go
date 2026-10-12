using System.Data;
using JianghuGo.Application;
using Npgsql;

namespace JianghuGo.Infrastructure.Postgres;

/// <summary>
/// Every unit of work is a SERIALIZABLE transaction. Serialization failures and deadlocks surface as
/// <see cref="TransientConflictException"/> and are retried by <see cref="UnitOfWorkFactoryExtensions.RunAsync{T}"/>,
/// which gives the same observable semantics as the in-memory store (docs/decisions/0005).
/// </summary>
public sealed class PostgresDatabase(NpgsqlDataSource dataSource) : IUnitOfWorkFactory
{
    public NpgsqlDataSource DataSource { get; } = dataSource;

    public async Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken)
    {
        var connection = await DataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            return new PostgresUnitOfWork(connection, tx);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

public sealed class PostgresUnitOfWork : IUnitOfWork
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _tx;
    private bool _completed;

    internal PostgresUnitOfWork(NpgsqlConnection connection, NpgsqlTransaction tx)
    {
        _connection = connection;
        _tx = tx;
    }

    public static PostgresUnitOfWork From(IUnitOfWork uow) =>
        uow as PostgresUnitOfWork ?? throw new InvalidOperationException("PostgreSQL repository used with a non-PostgreSQL unit of work.");

    /// <summary>Executes a statement. Positional parameters: $1, $2, … (null → SQL NULL).</summary>
    public async Task<int> ExecuteAsync(string sql, CancellationToken ct, params object?[] args)
    {
        await using var cmd = Command(sql, args);
        return await Translate(() => cmd.ExecuteNonQueryAsync(ct));
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> map, CancellationToken ct, params object?[] args)
    {
        await using var cmd = Command(sql, args);
        return await Translate(async () =>
        {
            var list = new List<T>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(map(reader));
            }

            return list;
        });
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, Func<NpgsqlDataReader, T> map, CancellationToken ct, params object?[] args)
    {
        var list = await QueryAsync(sql, map, ct, args);
        return list.Count switch
        {
            0 => default,
            1 => list[0],
            _ => throw new InvalidOperationException("Query returned more than one row."),
        };
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        await Translate(async () =>
        {
            await _tx.CommitAsync(cancellationToken);
            return 0;
        });
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            _completed = true;
            try
            {
                await _tx.RollbackAsync();
            }
            catch (NpgsqlException)
            {
                // connection already broken: the server rolls back on its own
            }
        }

        await _tx.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private NpgsqlCommand Command(string sql, object?[] args)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
#pragma warning disable CA2100 // SQL text is always a constant in repositories; values go through parameters
        var cmd = new NpgsqlCommand(sql, _connection, _tx);
#pragma warning restore CA2100
        foreach (var a in args)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = a ?? DBNull.Value });
        }

        return cmd;
    }

    private static async Task<T> Translate<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            throw new TransientConflictException($"Transaction conflict ({e.SqlState}).", e);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateKeyException($"Unique constraint '{e.ConstraintName}' violated.");
        }
    }
}
