using JianghuGo.Infrastructure.Postgres;
using JianghuGo.TestKit;
using Npgsql;

namespace JianghuGo.IntegrationTests;

/// <summary>
/// PostgreSQL for tests. Set JIANGHU_TEST_POSTGRES to an admin connection string (role that may CREATE DATABASE
/// and CREATE EXTENSION postgis), e.g. "Host=127.0.0.1;Username=jianghu;Password=…;Database=postgres".
/// Migrations run once into a template database; every test host gets a fresh copy (CREATE DATABASE … TEMPLATE).
/// </summary>
public static class TestDatabase
{
    public const string EnvVar = "JIANGHU_TEST_POSTGRES";
    public static readonly string? AdminConnectionString = Environment.GetEnvironmentVariable(EnvVar);
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(AdminConnectionString);

    /// <summary>"Postgres" when JIANGHU_TEST_STORAGE=Postgres (the whole suite then runs on PostgreSQL), else in-memory.</summary>
    public static bool SuiteUsesPostgres => string.Equals(Environment.GetEnvironmentVariable("JIANGHU_TEST_STORAGE"), "Postgres", StringComparison.OrdinalIgnoreCase);

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _template;

    public static string MigrationsDirectory => Path.Combine(AppContext.BaseDirectory, "migrations");

    public static void RequireConfigured()
    {
        if (!IsConfigured) throw new SkipException($"{EnvVar} not set");
    }

    public static async Task<string> CreateAsync()
    {
        RequireConfigured();
        await Gate.WaitAsync();
        try
        {
            if (_template is null)
            {
                var name = $"jianghu_tpl_{Environment.ProcessId}";
                await AdminExecAsync($"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
                await AdminExecAsync($"CREATE DATABASE {name}");
                await using (var ds = NpgsqlDataSource.Create(ConnectionStringFor(name)))
                {
                    await MigrationRunner.RunAsync(ds, MigrationsDirectory);
                }

                NpgsqlConnection.ClearAllPools(); // template must have no open connections
                _template = name;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => AdminExecAsync($"DROP DATABASE IF EXISTS {name} WITH (FORCE)").GetAwaiter().GetResult();
            }

            var db = $"jianghu_t_{Guid.NewGuid():N}";
            await AdminExecAsync($"CREATE DATABASE {db} TEMPLATE {_template}");
            return db;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task CreateEmptyAsync(string db) => await AdminExecAsync($"CREATE DATABASE {db}");

    public static async Task DropAsync(string db)
    {
        NpgsqlConnection.ClearAllPools();
        await AdminExecAsync($"DROP DATABASE IF EXISTS {db} WITH (FORCE)");
    }

    public static string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = database }.ConnectionString;

    private static async Task AdminExecAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();
#pragma warning disable CA2100 // test-only DDL with generated identifiers
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        await cmd.ExecuteNonQueryAsync();
    }
}
