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
}
