using JianghuGo.Application;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.TestKit;
using Npgsql;

namespace JianghuGo.IntegrationTests;

/// <summary>
/// Regression for a defect found by the Stage 8 load test: a SERIALIZABLE conflict detected at COMMIT left the unit of
/// work thinking the transaction was still open, so disposal tried to roll back a finished transaction and the
/// resulting InvalidOperationException (HTTP 500) masked the retryable conflict.
/// </summary>
[Category("Database")]
public class PostgresUnitOfWorkTests
{
    [Fact]
    public async Task Commit_time_serialization_failure_is_retryable_and_disposal_is_clean()
    {
        TestDatabase.RequireConfigured();
        var name = await TestDatabase.CreateAsync();
        try
        {
            await using var ds = NpgsqlDataSource.Create(TestDatabase.ConnectionStringFor(name));
            await using (var setup = ds.CreateCommand("CREATE TABLE public.ssi_probe (k text PRIMARY KEY, v int NOT NULL); INSERT INTO public.ssi_probe VALUES ('a', 0), ('b', 0);"))
                await setup.ExecuteNonQueryAsync();
            var db = new PostgresDatabase(ds);

            // Classic write skew: each transaction reads the row the other one writes.
            var t1 = (PostgresUnitOfWork)await db.BeginAsync(CancellationToken.None);
            var t2 = (PostgresUnitOfWork)await db.BeginAsync(CancellationToken.None);
            await t1.QueryAsync("SELECT v FROM public.ssi_probe WHERE k = 'a'", r => r.GetInt32(0), CancellationToken.None);
            await t2.QueryAsync("SELECT v FROM public.ssi_probe WHERE k = 'b'", r => r.GetInt32(0), CancellationToken.None);
            await t1.ExecuteAsync("UPDATE public.ssi_probe SET v = 1 WHERE k = 'b'", CancellationToken.None);
            await t2.ExecuteAsync("UPDATE public.ssi_probe SET v = 1 WHERE k = 'a'", CancellationToken.None);
            await t1.CommitAsync(CancellationToken.None);
            await Assert.ThrowsAsync<TransientConflictException>(() => t2.CommitAsync(CancellationToken.None));
            await t2.DisposeAsync();                                // must not throw
            await t1.DisposeAsync();

            // And RunAsync turns such a conflict into a successful retry.
            var attempts = 0;
            var result = await db.RunAsync(async uow =>
            {
                attempts++;
                if (attempts == 1) throw new TransientConflictException("simulated");
                await uow.CommitAsync(CancellationToken.None);
                return attempts;
            }, CancellationToken.None);
            Assert.Equal(2, result);
        }
        finally
        {
            await TestDatabase.DropAsync(name);
        }
    }
}
