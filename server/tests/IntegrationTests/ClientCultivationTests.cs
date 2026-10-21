using System.Text.Json;
using JianghuGo.Client.Application;
using JianghuGo.Client.Infrastructure;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Client")]
public class ClientCultivationTests : IAsyncLifetime
{
    private readonly QueuedSeedSource _seeds = new();
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync(seeds: _seeds);
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private sealed class StjSerializer : IJsonSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public string Serialize(object value) => JsonSerializer.Serialize(value, Options);
        public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
    }

    [Fact]
    public async Task Meditate_across_token_expiry_break_through_and_receive_notification()
    {
        var api = new ApiClient(new HttpClientTransport(_host.NewClient()), new StjSerializer(), new InMemoryTokenStore());
        await api.RegisterAsync("kultywator@test.dev", ApiHost.Password, default);
        await api.LoginAsync("kultywator@test.dev", ApiHost.Password, default);
        var creation = new CharacterCreationFlow(api);
        await creation.StartAsync(default);
        var character = await creation.FinalizeAsync("Mistrz Qi", "qi", default);

        var flow = new CultivationFlow(api, character.Id);
        await flow.LoadAsync(default);
        await flow.StartMeditationAsync(default);
        Assert.True(flow.State!.IsMeditating);

        _host.Clock.Advance(TimeSpan.FromMinutes(45));             // access token (15 min) expires meanwhile
        var meditation = await flow.FinishMeditationAsync(default); // client refreshes transparently
        Assert.Equal(45, meditation.MinutesCounted);
        Assert.True(flow.State!.CanAttemptBreakthrough);

        _seeds.Enqueue(QueuedSeedSource.Find(success: true, flow.State.BreakthroughChance));
        var result = await flow.BreakthroughAsync(default);
        Assert.True(Assert.NotNull(result).Success);
        Assert.Equal(2, flow.State.Stage);

        await _host.DrainOutboxAsync();
        var page = await api.GetNotificationsAsync(unreadOnly: true, limit: 10, before: null, default);
        Assert.SequenceEqual(["breakthrough_succeeded", "welcome"], page.Items.Select(n => n.Type));
        await api.MarkNotificationReadAsync(page.Items[0].Id, default);
        Assert.Equal(1, (await api.GetNotificationsAsync(true, 10, null, default)).Items.Count);
    }

    [Fact]
    public async Task Meditation_forecast_matches_what_the_server_credits()
    {
        var api = new ApiClient(new HttpClientTransport(_host.NewClient()), new StjSerializer(), new InMemoryTokenStore());
        await api.RegisterAsync("prognoza@test.dev", ApiHost.Password, default);
        await api.LoginAsync("prognoza@test.dev", ApiHost.Password, default);
        var creation = new CharacterCreationFlow(api);
        await creation.StartAsync(default);
        var character = await creation.FinalizeAsync("Prorok", "qi", default);

        var flow = new CultivationFlow(api, character.Id);
        await flow.LoadAsync(default);
        Assert.True(flow.State!.MeditationQiPerMinute > 0, "the server reports the meditation rate");
        var before = MeditationForecast.For(flow.State, _host.Clock.GetUtcNow());
        Assert.Equal(0d, before.Progress);
        Assert.Equal((int)Math.Ceiling(flow.State.QiRequired / flow.State.MeditationQiPerMinute), before.MinutesToFull);

        await flow.StartMeditationAsync(default);
        _host.Clock.Advance(TimeSpan.FromMinutes(17));
        await flow.LoadAsync(default);
        var during = MeditationForecast.For(flow.State, _host.Clock.GetUtcNow());
        Assert.Equal(17, during.SessionMinutes);
        var result = await flow.FinishMeditationAsync(default);
        Assert.Equal(result.QiGained, during.PendingQi);
        var after = MeditationForecast.For(flow.State, _host.Clock.GetUtcNow());
        Assert.Equal(during.Progress, after.Progress);
        Assert.True(after.MinutesToFull < before.MinutesToFull, "less meditation needed after a session");
    }

    [Fact]
    public async Task Stale_breakthrough_reloads_state_instead_of_failing()
    {
        var api = new ApiClient(new HttpClientTransport(_host.NewClient()), new StjSerializer(), new InMemoryTokenStore());
        await api.RegisterAsync("stary@test.dev", ApiHost.Password, default);
        await api.LoginAsync("stary@test.dev", ApiHost.Password, default);
        var creation = new CharacterCreationFlow(api);
        await creation.StartAsync(default);
        var character = await creation.FinalizeAsync("Cierpliwy", "qi", default);

        var phone = new CultivationFlow(api, character.Id);
        await phone.LoadAsync(default);
        await phone.StartMeditationAsync(default);
        _host.Clock.Advance(TimeSpan.FromMinutes(60));
        await phone.FinishMeditationAsync(default);

        var tablet = new CultivationFlow(api, character.Id);
        await tablet.LoadAsync(default);
        _seeds.Enqueue(QueuedSeedSource.Find(success: true, tablet.State!.BreakthroughChance));
        Assert.NotNull(await tablet.BreakthroughAsync(default));

        Assert.Null(await phone.BreakthroughAsync(default));
        Assert.Equal(2, phone.State!.Stage);
    }

    [Fact]
    public async Task Breakthrough_survives_a_version_bump_that_did_not_change_the_stage()
    {
        // the bug players saw: the state's version moved on in the background (a meditation, rewards, a refresh)
        // and the breakthrough failed with "changed on another device" although nothing relevant changed
        var api = new ApiClient(new HttpClientTransport(_host.NewClient()), new StjSerializer(), new InMemoryTokenStore());
        await api.RegisterAsync("tlo@test.dev", ApiHost.Password, default);
        await api.LoginAsync("tlo@test.dev", ApiHost.Password, default);
        var creation = new CharacterCreationFlow(api);
        await creation.StartAsync(default);
        var character = await creation.FinalizeAsync("Wytrwały", "qi", default);

        var screen = new CultivationFlow(api, character.Id);
        await screen.LoadAsync(default);
        await screen.StartMeditationAsync(default);
        _host.Clock.Advance(TimeSpan.FromMinutes(60));
        await screen.FinishMeditationAsync(default);
        var stale = screen.State!.Version;

        // something else touches the state (same stage): a short meditation from the background
        var other = new CultivationFlow(api, character.Id);
        await other.LoadAsync(default);
        await other.StartMeditationAsync(default);
        _host.Clock.Advance(TimeSpan.FromMinutes(2));
        await other.FinishMeditationAsync(default);
        Assert.True(other.State!.Version != stale, "the version moved on");

        _seeds.Enqueue(QueuedSeedSource.Find(success: true, other.State.BreakthroughChance));
        var result = await screen.BreakthroughAsync(default);
        Assert.True(Assert.NotNull(result).Success);
        Assert.Equal(2, screen.State!.Stage);
    }
}
