using System.Diagnostics;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.TestKit;
using Npgsql;

namespace JianghuGo.IntegrationTests;

[Category("Database")]
public class MigrationTests : IAsyncLifetime
{
    private string _db = null!;
    private NpgsqlDataSource _ds = null!;

    public async Task InitializeAsync()
    {
        TestDatabase.RequireConfigured();
        _db = $"jianghu_m_{Guid.NewGuid():N}";
        await TestDatabase.CreateEmptyAsync(_db);
        _ds = NpgsqlDataSource.Create(TestDatabase.ConnectionStringFor(_db));
    }

    public async Task DisposeAsync()
    {
        if (_ds is not null) await _ds.DisposeAsync();
        if (_db is not null) await TestDatabase.DropAsync(_db);
    }

    private static string CopyMigrations()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jg-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(TestDatabase.MigrationsDirectory)) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
        return dir;
    }

    [Fact]
    public async Task Applies_all_then_is_a_no_op()
    {
        var first = await MigrationRunner.RunAsync(_ds, TestDatabase.MigrationsDirectory);
        Assert.Equal(Directory.GetFiles(TestDatabase.MigrationsDirectory, "V*.sql").Length, first.Count);
        Assert.Equal(0, (await MigrationRunner.RunAsync(_ds, TestDatabase.MigrationsDirectory)).Count);
    }

    [Fact]
    public async Task Refuses_modified_applied_migration()
    {
        await MigrationRunner.RunAsync(_ds, TestDatabase.MigrationsDirectory);
        var dir = CopyMigrations();
        File.AppendAllText(Path.Combine(dir, "V0001__platform.sql"), "\n-- tampered\n");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => MigrationRunner.RunAsync(_ds, dir));
        Assert.Contains("checksum mismatch", ex.Message);
    }

    [Fact]
    public async Task Failed_migration_is_rolled_back_completely()
    {
        var dir = CopyMigrations();
        File.WriteAllText(Path.Combine(dir, "V9999__broken.sql"), "CREATE TABLE platform.half_done (id int);\nSELECT * FROM no_such_table;\n");
        await Assert.ThrowsAsync<PostgresException>(() => MigrationRunner.RunAsync(_ds, dir));
        await using var cmd = _ds.CreateCommand("SELECT to_regclass('platform.half_done') IS NULL AND NOT EXISTS (SELECT 1 FROM public.schema_migrations WHERE version = 'V9999')");
        Assert.True((bool)(await cmd.ExecuteScalarAsync())!, "partial migration must not persist");
    }

    [Fact]
    public async Task Concurrent_migrators_apply_each_file_once()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => MigrationRunner.RunAsync(_ds, TestDatabase.MigrationsDirectory)));
        Assert.Equal(Directory.GetFiles(TestDatabase.MigrationsDirectory, "V*.sql").Length, results.Sum(r => r.Count));
    }

    [Fact]
    public async Task Shell_migrator_and_dotnet_migrator_share_checksums()
    {
        if (!File.Exists("/usr/bin/psql") && !File.Exists("/usr/local/bin/psql")) throw new SkipException("psql not installed");
        var b = new NpgsqlConnectionStringBuilder(TestDatabase.ConnectionStringFor(_db));
        var url = $"postgres://{Uri.EscapeDataString(b.Username!)}:{Uri.EscapeDataString(b.Password ?? "")}@{b.Host}:{b.Port}/{b.Database}";
        var dir = CopyMigrations();
        // migrate.sh expects ./migrations next to itself
        var root = Path.Combine(Path.GetTempPath(), "jg-sh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.Move(dir, Path.Combine(root, "migrations"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "db", "migrate.sh"), Path.Combine(root, "migrate.sh"));
        var psi = new ProcessStartInfo("bash", [Path.Combine(root, "migrate.sh")]) { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.Environment["DATABASE_URL"] = url;
        using var p = Process.Start(psi)!;
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        Assert.Equal(0, p.ExitCode, err);
        Assert.Equal(0, (await MigrationRunner.RunAsync(_ds, TestDatabase.MigrationsDirectory)).Count, "dotnet runner must see shell-applied migrations as done");
    }

    // Acceptance #12: upgrading a database that already holds data loses nothing.
    [Fact]
    public async Task Upgrade_from_stage2_schema_preserves_existing_data()
    {
        var stage2 = CopyMigrations();
        foreach (var f in Directory.GetFiles(stage2).Where(f => string.CompareOrdinal(Path.GetFileName(f), "V0004") >= 0)) File.Delete(f);
        Assert.Equal(3, (await MigrationRunner.RunAsync(_ds, stage2)).Count);

        await using (var cmd = _ds.CreateCommand("""
            INSERT INTO identity.accounts (id, email, password_hash, created_at) VALUES ('00000000-0000-7000-8000-00000000aaaa', 'stary@test.dev', 'h', now());
            INSERT INTO characters.destiny_token_entries (id, account_id, delta, balance_after, reason, reference_id, created_at)
              VALUES (gen_random_uuid(), '00000000-0000-7000-8000-00000000aaaa', 10, 10, 'initial_grant', 'x', now());
            INSERT INTO characters.drafts (id, account_id, version, status, rules_version, last_seed, background_trait, bloodline_trait, constitution_trait, stats, reroll_count, created_at, updated_at)
              VALUES ('00000000-0000-7000-8000-00000000dddd', '00000000-0000-7000-8000-00000000aaaa', 2, 'finalized', 'char-creation-1', 7, 'bg_farmer_son', 'bl_mortal', 'co_ordinary',
                '[{"id":"strength","value":10},{"id":"spirit","value":10},{"id":"agility","value":10},{"id":"constitution","value":10},{"id":"insight","value":10},{"id":"willpower","value":10},{"id":"focus","value":10},{"id":"elementalAffinity","value":5},{"id":"fate","value":5}]',
                0, now(), now());
            INSERT INTO characters.characters (id, account_id, name, normalized_name, path, rules_version, background_trait, bloodline_trait, constitution_trait, stats, source_draft_id, created_at)
              SELECT '00000000-0000-7000-8000-00000000cccc', account_id, 'Weteran', 'WETERAN', 'body', rules_version, background_trait, bloodline_trait, constitution_trait, stats, id, now()
              FROM characters.drafts;
            """))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        var applied = await MigrationRunner.RunAsync(_ds, TestDatabase.MigrationsDirectory);
        Assert.True(applied.Count >= 3, "stage-3 migrations applied on top");
        await using var check = _ds.CreateCommand("""
            SELECT (SELECT count(*) FROM identity.accounts) = 1
               AND (SELECT name FROM characters.characters) = 'Weteran'
               AND (SELECT balance FROM characters.destiny_token_balances) = 10
               AND NOT EXISTS (SELECT 1 FROM characters.destiny_ledger_mismatches)
            """);
        Assert.True((bool)(await check.ExecuteScalarAsync())!, "pre-existing rows intact after upgrade");

        // ...and the old character can immediately use the new schema.
        await using var use = _ds.CreateCommand("""
            INSERT INTO cultivation.states (character_id, account_id, path, realm_index, stage, qi, version, daily_date, daily_meditation_qi, daily_steps, updated_at)
            VALUES ('00000000-0000-7000-8000-00000000cccc', '00000000-0000-7000-8000-00000000aaaa', 'body', 0, 1, 0, 1, current_date, 0, 0, now())
            """);
        Assert.Equal(1, await use.ExecuteNonQueryAsync());
    }
}
