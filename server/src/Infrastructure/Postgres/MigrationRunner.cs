using System.Security.Cryptography;
using Npgsql;

namespace JianghuGo.Infrastructure.Postgres;

/// <summary>
/// Same contract as database/migrate.sh (shared table public.schema_migrations, lowercase SHA-256 of the file):
/// applies V*__*.sql in ordinal order, one transaction per file, refuses if an applied file changed.
/// A session-level advisory lock prevents two deploys from migrating concurrently.
/// </summary>
public static class MigrationRunner
{
    private const long LockKey = 0x4A69616E67687500; // "Jianghu\0"

    public static async Task<IReadOnlyList<string>> RunAsync(NpgsqlDataSource dataSource, string migrationsDirectory, CancellationToken ct = default)
    {
        var files = Directory.GetFiles(migrationsDirectory, "V*__*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList();
        if (files.Count == 0)
        {
            throw new InvalidOperationException($"No migrations found in '{migrationsDirectory}'.");
        }

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await Exec(conn, $"SELECT pg_advisory_lock({LockKey})", ct);
        try
        {
            await Exec(conn, """
                CREATE TABLE IF NOT EXISTS public.schema_migrations (
                  version text PRIMARY KEY, checksum char(64) NOT NULL, applied_at timestamptz NOT NULL DEFAULT now())
                """, ct);

            var applied = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var cmd = new NpgsqlCommand("SELECT version, checksum FROM public.schema_migrations", conn))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    applied[r.GetString(0)] = r.GetString(1);
                }
            }

            var newlyApplied = new List<string>();
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var version = name[..name.IndexOf("__", StringComparison.Ordinal)];
                var bytes = await File.ReadAllBytesAsync(file, ct);
                var checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (applied.TryGetValue(version, out var existing))
                {
                    if (existing != checksum)
                    {
                        throw new InvalidOperationException($"{name} was modified after being applied (checksum mismatch). Create a new migration instead.");
                    }

                    continue;
                }

                await using var tx = await conn.BeginTransactionAsync(ct);
#pragma warning disable CA2100 // migration files are trusted repository content
                await using (var cmd = new NpgsqlCommand(System.Text.Encoding.UTF8.GetString(bytes), conn, tx))
#pragma warning restore CA2100
                {
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await using (var cmd = new NpgsqlCommand("INSERT INTO public.schema_migrations (version, checksum) VALUES ($1, $2)", conn, tx))
                {
                    cmd.Parameters.Add(new NpgsqlParameter { Value = version });
                    cmd.Parameters.Add(new NpgsqlParameter { Value = checksum });
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
                newlyApplied.Add(name);
            }

            return newlyApplied;
        }
        finally
        {
            await Exec(conn, $"SELECT pg_advisory_unlock({LockKey})", CancellationToken.None);
        }
    }

    private static async Task Exec(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
