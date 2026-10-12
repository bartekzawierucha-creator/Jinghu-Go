using System.Net;
using JianghuGo.Application;
using JianghuGo.Infrastructure.Outbox;
using JianghuGo.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JianghuGo.IntegrationTests;

[Category("Integration")]
public class OutboxTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<int> NotificationCountAsync(Player p) =>
        (await ApiHost.Json(await p.GetAsync("/api/v1/notifications")))["items"]!.AsArray().Count;

    private async Task<OutboxStats> StatsAsync(ApiHost host) =>
        await host.Service<IUnitOfWorkFactory>().RunAsync(uow => host.Service<IOutbox>().GetStatsAsync(uow, host.Clock.GetUtcNow(), default), default);

    private sealed class CrashingHandler(string type) : IOutboxHandler
    {
        public string EventType { get; } = type;
        public int Calls;

        public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("simulated crash after partial work");
        }
    }

    /// <summary>A dispatcher wired like the real one but with extra handlers (e.g. one that crashes).</summary>
    private OutboxDispatcher DispatcherWith(params IOutboxHandler[] extra)
    {
        var sp = _host.App.Services;
        return new OutboxDispatcher(sp.GetRequiredService<IUnitOfWorkFactory>(), sp.GetRequiredService<IOutbox>(),
            sp.GetServices<IOutboxHandler>().Concat(extra), _host.Clock,
            Options.Create(new OutboxOptions { MaxAttempts = 3 }), sp.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            NullLogger<OutboxDispatcher>.Instance);
    }

    [Fact]
    public async Task Character_creation_event_becomes_a_welcome_notification_after_dispatch()
    {
        var p = await _host.RegisterAndLoginAsync();
        await p.CreateCharacterAsync("Powitany");
        Assert.Equal(0, await NotificationCountAsync(p), "nothing is delivered before the worker runs");
        Assert.Equal(1, (await StatsAsync(_host)).Pending);

        Assert.Equal(1, await _host.DrainOutboxAsync());
        var page = await ApiHost.Json(await p.GetAsync("/api/v1/notifications"));
        var n = page["items"]!.AsArray().Single()!;
        Assert.Equal("welcome", n.Str("type"));
        Assert.Equal("Powitany", n["payload"].Str("name"));
        Assert.Equal(0, (await StatsAsync(_host)).Pending);
        Assert.Equal(0, await _host.DrainOutboxAsync(), "processed messages are not delivered again");
    }

    // Acceptance #10 (crash variant): a worker dying mid-handling loses nothing and duplicates nothing.
    [Fact]
    public async Task Handler_crash_rolls_back_and_message_is_redelivered_exactly_once()
    {
        var p = await _host.RegisterAndLoginAsync();
        await p.CreateCharacterAsync("Feniks");

        var crashing = new CrashingHandler(JianghuGo.Modules.Characters.Contracts.CharacterEvents.CharacterCreated);
        Assert.True(await DispatcherWith(crashing).ProcessNextAsync(default));
        Assert.Equal(1, crashing.Calls);
        Assert.Equal(0, await NotificationCountAsync(p), "the welcome handler ran first, but its write was rolled back with the crash");
        var stats = await StatsAsync(_host);
        Assert.Equal(1, stats.Pending);

        _host.Clock.Advance(TimeSpan.FromSeconds(5)); // past the retry backoff
        Assert.Equal(1, await _host.DrainOutboxAsync()); // "restarted" healthy worker
        Assert.Equal(1, await NotificationCountAsync(p));
    }

    // Acceptance #10 (restart variant): stop the whole process with events pending, start a new one on the same DB.
    [Fact]
    public async Task Pending_events_survive_a_full_process_restart()
    {
        if (_host.Database is null) throw new SkipException("requires durable storage (JIANGHU_TEST_STORAGE=Postgres)");
        var p = await _host.RegisterAndLoginAsync();
        await p.CreateCharacterAsync("Restart");
        var email = p.Email;
        var database = _host.Database;
        _host.KeepDatabase = true;
        await _host.DisposeAsync();                                      // "kill" API + in-process worker

        _host = await ApiHost.StartAsync(existingDatabase: database);    // new process, same database
        Assert.Equal(1, (await StatsAsync(_host)).Pending);
        Assert.Equal(1, await _host.DrainOutboxAsync());
        var again = new Player(_host, email, await _host.LoginAsync(email, ApiHost.Password));
        Assert.Equal(1, await NotificationCountAsync(again));
    }

    [Fact]
    public async Task Redelivered_message_does_not_duplicate_the_notification()
    {
        var p = await _host.RegisterAndLoginAsync();
        await p.CreateCharacterAsync("Dwakroc");
        var message = await _host.Service<IUnitOfWorkFactory>().RunAsync(async uow => (await _host.Service<IOutbox>().ClaimNextAsync(uow, _host.Clock.GetUtcNow(), default))!.Message, default);
        var handler = _host.Service<IEnumerable<IOutboxHandler>>().Single(h => h.EventType == message.Type);
        for (var i = 0; i < 2; i++)
        {
            await _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
            {
                await handler.HandleAsync(uow, message, default);
                await uow.CommitAsync(default);
            }, default);
        }

        Assert.Equal(1, await NotificationCountAsync(p));
    }

    [Fact]
    public async Task Persistently_failing_message_is_dead_lettered_and_health_degrades()
    {
        var p = await _host.RegisterAndLoginAsync();
        await p.CreateCharacterAsync("Pechowiec");
        var dispatcher = DispatcherWith(new CrashingHandler(JianghuGo.Modules.Characters.Contracts.CharacterEvents.CharacterCreated));
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await dispatcher.ProcessNextAsync(default));
            _host.Clock.Advance(TimeSpan.FromMinutes(10));
        }

        var stats = await StatsAsync(_host);
        Assert.Equal(0, stats.Pending);
        Assert.Equal(1, stats.DeadLettered);
        Assert.False(await dispatcher.ProcessNextAsync(default), "dead letters are not retried automatically");
        var ready = await _host.Client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        Assert.Equal("Degraded", await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Old_pending_events_degrade_readiness()
    {
        var p = await _host.RegisterAndLoginAsync();
        await p.CreateCharacterAsync("Czekajacy");
        Assert.Equal("Healthy", await (await _host.Client.GetAsync(new Uri("/health/ready", UriKind.Relative))).Content.ReadAsStringAsync());
        _host.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal("Degraded", await (await _host.Client.GetAsync(new Uri("/health/ready", UriKind.Relative))).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Notifications_are_paginated_private_and_can_be_marked_read()
    {
        var p = await _host.RegisterAndLoginAsync();
        await p.CreateCharacterAsync("Pierwsza");
        await p.CreateCharacterAsync("Druga");
        await p.CreateCharacterAsync("Trzecia");
        await _host.DrainOutboxAsync();

        var page1 = await ApiHost.Json(await p.GetAsync("/api/v1/notifications?limit=2"));
        Assert.Equal(2, page1["items"]!.AsArray().Count);
        Assert.Equal("Trzecia", page1["items"]![0]!["payload"].Str("name"), "newest first");
        var page2 = await ApiHost.Json(await p.GetAsync($"/api/v1/notifications?limit=2&before={page1.Str("nextBefore")}"));
        Assert.Equal(1, page2["items"]!.AsArray().Count);
        Assert.Null(page2["nextBefore"]);

        var id = page2["items"]![0].Str("id");
        Assert.Equal(HttpStatusCode.NoContent, (await p.PostAsync($"/api/v1/notifications/{id}/read")).StatusCode);
        Assert.Equal(2, (await ApiHost.Json(await p.GetAsync("/api/v1/notifications?unreadOnly=true")))["items"]!.AsArray().Count);

        var other = await _host.RegisterAndLoginAsync();
        Assert.Equal(0, await NotificationCountAsync(other));
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/v1/notifications/{id}/read")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await p.GetAsync("/api/v1/notifications?limit=500")).StatusCode);
    }
}
