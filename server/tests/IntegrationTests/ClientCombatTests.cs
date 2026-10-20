using System.Text.Json;
using JianghuGo.Client.Application;
using JianghuGo.Client.Infrastructure;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Client")]
public class ClientCombatTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private sealed class StjSerializer : IJsonSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public string Serialize(object value) => JsonSerializer.Serialize(value, Options);
        public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
    }

    private async Task<(ApiClient Api, CharacterDto Character)> PlayerAsync(string email, string path)
    {
        var api = new ApiClient(new HttpClientTransport(_host.NewClient()), new StjSerializer(), new InMemoryTokenStore());
        await api.RegisterAsync(email, ApiHost.Password, default);
        await api.LoginAsync(email, ApiHost.Password, default);
        var creation = new CharacterCreationFlow(api);
        await creation.StartAsync(default);
        return (api, await creation.FinalizeAsync("Klient " + path, path, default));
    }

    [Fact]
    public async Task Arrange_best_fills_the_loadout_with_the_strongest_unlocked_techniques_and_the_server_accepts_it()
    {
        var (api, character) = await PlayerAsync("uklad@test.dev", "qi");
        var loadout = await api.GetLoadoutAsync(character.Id, default);
        var best = LoadoutAdvisor.Best(loadout);
        Assert.Equal(loadout.Slots.Count, best.Count);
        Assert.Equal(best.Where(x => x != null).Count(), best.Where(x => x != null).Distinct().Count());
        foreach (var (slot, id) in loadout.Slots.OrderBy(s => s.Slot).Zip(best))
        {
            if (id == null) continue;
            var k = loadout.Known.Single(x => x.Id == id);
            Assert.False(k.Locked);
            Assert.Equal(slot.Kind, k.Slot);
        }

        var saved = await api.SetLoadoutAsync(character.Id, loadout.Version, best, default);
        static string Text(IEnumerable<string?> ids) => string.Join(",", ids.Select(x => x ?? "-"));
        Assert.Equal(Text(best), Text(saved.Slots.OrderBy(s => s.Slot).Select(s => s.TechniqueId)));
        // arranging again changes nothing (stable)
        Assert.Equal(Text(best), Text(LoadoutAdvisor.Best(saved)));
    }

    [Fact]
    public async Task Client_fights_a_pve_battle_to_the_end_using_only_slot_intents()
    {
        var (api, character) = await PlayerAsync("walka@test.dev", "flame");
        var flow = new CombatFlow(api);
        await flow.StartPveAsync(character.Id, "training_dummy", default);
        for (var i = 0; i < 60 && flow.Battle!.IsActive; i++)
        {
            var attack = flow.Battle.MySlots.Where(s => s.Kind == "attack" && s.Usable).OrderByDescending(s => s.QiCost).FirstOrDefault();
            var move = flow.Battle.MySlots.FirstOrDefault(s => s.Kind == "movement" && s.Usable);
            Assert.True(await (attack is not null
                ? flow.ActAsync(attack.Slot, null, default)
                : move is not null && flow.Battle.Distance > 1 ? flow.ActAsync(move.Slot, "advance", default) : flow.ActAsync(null, null, default)));
            Assert.True(flow.Battle.LastTurnEvents.Count > 0, "every resolved turn returns its timeline");
        }

        Assert.Equal("finished", flow.Battle!.Status);
        Assert.Equal("won", flow.Battle.Result);
        Assert.NotNull(flow.Battle.Seed);
    }

    [Fact]
    public async Task Stale_turn_from_a_second_device_refreshes_instead_of_failing()
    {
        var (a, ca) = await PlayerAsync("a@duel.dev", "qi");
        var (b, cb) = await PlayerAsync("b@duel.dev", "body");
        var challenge = await a.ChallengeAsync(ca.Id, cb.Id, default);
        await b.AcceptChallengeAsync(challenge.Id, default);

        var phone = new CombatFlow(a);
        var tablet = new CombatFlow(a);
        await phone.OpenAsync(challenge.Id, default);
        await tablet.OpenAsync(challenge.Id, default);
        Assert.True(await tablet.ActAsync(null, null, default));
        Assert.False(await phone.ActAsync(null, null, default), "second device's action for the same turn is refused");
        Assert.True(phone.Battle!.MyActionSubmitted);
    }
}
