using System.Net;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.TestKit;
using Npgsql;

namespace JianghuGo.IntegrationTests;

[Category("Operations")]
public class RetentionTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<long> CountAsync(string sql)
    {
        await using var ds = NpgsqlDataSource.Create(TestDatabase.ConnectionStringFor(_host.Database!));
        await using var cmd = ds.CreateCommand(sql);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Expired operational data is purged per docs/operations/data-retention.md; ledgers and audit are never touched.</summary>
    [Fact]
    public async Task Expired_operational_rows_are_purged_and_ledgers_kept()
    {
        if (_host.Database is null) throw new SkipException("requires durable storage (JIANGHU_TEST_STORAGE=Postgres)");
        var p = await _host.RegisterAndLoginAsync();
        var c = await p.CreateCharacterAsync("Kronikarz");
        await _host.DrainOutboxAsync();                             // welcome notification, processed outbox message
        var runner = _host.Service<RetentionRunner>();

        var first = await runner.RunOnceAsync(CancellationToken.None);
        Assert.True(first.Values.All(n => n == 0), "nothing is old yet");
        var ledgerBefore = await CountAsync("SELECT count(*) FROM characters.destiny_token_entries");
        var auditBefore = await CountAsync("SELECT count(*) FROM platform.audit_log");

        _host.Clock.Advance(TimeSpan.FromDays(91));
        var purged = await runner.RunOnceAsync(CancellationToken.None);
        Assert.True(purged["platform.idempotency_records"] >= 1, "idempotency records older than 7 days");
        Assert.True(purged["platform.outbox_messages"] >= 1, "processed outbox messages older than 7 days");
        Assert.True(purged["notifications.notifications"] >= 1, "notifications older than 90 days");
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM platform.idempotency_records"));
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM notifications.notifications"));
        Assert.Equal(ledgerBefore, await CountAsync("SELECT count(*) FROM characters.destiny_token_entries"));
        Assert.Equal(auditBefore, await CountAsync("SELECT count(*) FROM platform.audit_log"));
    }
}
