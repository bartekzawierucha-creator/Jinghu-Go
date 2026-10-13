using System.Text.Json;
using JianghuGo.Client.Application;
using JianghuGo.Client.Infrastructure;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

/// <summary>The shared client core reads every MVP 1.1 endpoint (quests, meridians, rankings, pet, catalog, nearby).</summary>
[Category("Client")]
public class ClientProgressionTests : IAsyncLifetime
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
    public async Task Client_reads_quests_meridians_rankings_pet_and_catalog()
    {
        var api = new ApiClient(new HttpClientTransport(_host.NewClient()), new StjSerializer(), new InMemoryTokenStore());
        await api.RegisterAsync("droga@test.dev", ApiHost.Password, default);
        await api.LoginAsync("droga@test.dev", ApiHost.Password, default);
        var creation = new CharacterCreationFlow(api);
        await creation.StartAsync(default);
        var c = await creation.FinalizeAsync("Uczen Drogi", "qi", default);

        var board = await api.GetQuestsAsync(c.Id, default);
        Assert.Equal(3, board.Daily.Count);
        Assert.True(board.Achievements.Count >= 10);
        Assert.False(board.Checkin.CheckedInToday);
        var checkin = await api.CheckinAsync(c.Id, default);
        Assert.Equal(1, checkin.Streak);
        Assert.True(checkin.Board.Checkin.CheckedInToday);
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.ClaimQuestAsync(c.Id, "a_win_100", default));
        Assert.Equal("quest_not_completed", ex.Code);

        var meridians = await api.GetMeridiansAsync(c.Id, default);
        Assert.Equal(0, meridians.Free);

        var flow = new CultivationFlow(api, c.Id);
        await flow.LoadAsync(default);
        await flow.StartMeditationAsync(default);
        var ranking = await api.GetRankingAsync(c.Id, default);
        Assert.NotNull(ranking.Me);
        Assert.Equal(c.Id, ranking.Top[0].CharacterId);
        Assert.Equal("Uczen Drogi", ranking.Top[0].Name);

        Assert.Null((await api.GetPetAsync(c.Id, default)).Pet);
        var catalog = await api.GetCombatCatalogAsync(default);
        Assert.True(catalog.Npcs.Count(n => n.Boss && n.WorldOnly) >= 3);
        Assert.True(catalog.Npcs.Any(n => n.Tameable && !n.WorldOnly));
        Assert.Equal(0, (await api.GetLeadersAsync(c.Id, "pvp_won", default)).Items.Count);
        var noSession = await Assert.ThrowsAsync<ApiException>(() => api.GetNearbyCultivatorsAsync(c.Id, default));
        Assert.Equal("no_session", noSession.Code);
    }
}
