using System.Net;
using JianghuGo.Domain;
using JianghuGo.Modules.World.Application;
using JianghuGo.Modules.World.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

/// <summary>
/// Stage 8: one player journey across all modules through the public API — explore, gather, trade, cultivate,
/// found a sect, recruit, fund it and claim the territory — ending with clean economy reconciliation.
/// The only shortcut is a test grant of Spirit Stones for the founding fee (earning 500 stones by play takes days).
/// </summary>
[Category("EndToEnd")]
public class EndToEndTests : IAsyncLifetime
{
    private static readonly GeoPoint Warsaw = new(52.2297, 21.0122);
    private ApiHost _host = null!;
    private Cultivators _c = null!;

    public async Task InitializeAsync()
    {
        _host = await ApiHost.StartAsync();
        _c = new Cultivators(_host);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private WorldNode FindHerb()
    {
        var config = _host.Service<WorldConfig>();
        var seed = _host.Service<WorldSecret>().Seed;
        for (var i = 0; i < 400; i++)
        {
            var probe = new GeoPoint(Warsaw.Lat + ((i % 20) * 0.01), Warsaw.Lon + ((i / 20) * 0.01));
            var node = WorldGenerator.Nearby(config, seed, probe, _host.Clock.GetUtcNow()).FirstOrDefault(x => x.Kind == "spirit_herb");
            if (node is not null) return node;
        }

        throw new AssertionException("no spirit herb near Warsaw for this seed");
    }

    [Fact]
    public async Task A_cultivator_explores_trades_founds_a_sect_and_claims_land()
    {
        var hero = await _c.NewAsync();
        var friend = await _c.NewAsync();
        string Id((Player P, System.Text.Json.Nodes.JsonObject C) x) => x.C.Str("id");

        // explore and gather
        var herb = FindHerb();
        await _c.StandAtAsync(hero.P, hero.C, herb.Point);
        var gathered = await Cultivators.Ok(await hero.P.PostAsync("/api/v1/world/interactions", new { characterId = Id(hero), nodeKey = herb.Key }, Player.NewKey()));
        var herbs = gathered["items"]!.AsArray().Single()!.Int("quantity");
        await _host.DrainOutboxAsync();
        var bag = await Cultivators.Ok(await hero.P.GetAsync($"/api/v1/characters/{Id(hero)}/inventory"));
        Assert.Equal(herbs, bag["stacks"]!.AsArray().Single(s => s!.Str("itemId") == "spirit_herb")!.Int("quantity"));
        var qiAfterHarvest = (await Cultivators.Ok(await hero.P.GetAsync($"/api/v1/characters/{Id(hero)}/cultivation"))).Int("qi");
        Assert.True(qiAfterHarvest > 0, "the herb also gave Qi");

        // trade the herbs to a friend
        var listing = await Cultivators.Ok(await hero.P.PostAsync("/api/v1/market/listings",
            new { characterId = Id(hero), itemId = "spirit_herb", quantity = herbs, price = 40 }, Player.NewKey()), HttpStatusCode.Created);
        await Cultivators.Ok(await friend.P.PostAsync($"/api/v1/market/listings/{listing.Str("id")}/buy", new { characterId = Id(friend) }, Player.NewKey()));
        await _host.DrainOutboxAsync();
        var notes = (await Cultivators.Ok(await hero.P.GetAsync("/api/v1/notifications")))["items"]!.AsArray();
        Assert.True(notes.Any(n => n!.Str("type") == "market_sold"));

        // cultivate
        await Cultivators.Ok(await hero.P.PostAsync($"/api/v1/characters/{Id(hero)}/cultivation/meditation/start"));
        _host.Clock.Advance(TimeSpan.FromMinutes(30));
        await hero.P.RefreshAsync();
        await friend.P.RefreshAsync();
        var meditation = await Cultivators.Ok(await hero.P.PostAsync($"/api/v1/characters/{Id(hero)}/cultivation/meditation/finish", null, Player.NewKey()));
        Assert.True(meditation.Int("qiGained") > 0);

        // found a sect, recruit, fund, claim the land where the herb grew
        await _c.FundAsync(hero.C, 1000);
        var sect = await Cultivators.Ok(await hero.P.PostAsync("/api/v1/sects", new { characterId = Id(hero), name = "Sekta Mazowsza" }, Player.NewKey()), HttpStatusCode.Created);
        var sectId = sect["sect"]!.Str("id");
        var application = await Cultivators.Ok(await friend.P.PostAsync($"/api/v1/sects/{sectId}/applications", new { characterId = Id(friend), message = "Razem!" }, Player.NewKey()),
            HttpStatusCode.Created);
        await Cultivators.Ok(await hero.P.PostAsync($"/api/v1/sects/applications/{application.Str("id")}/decision", new { characterId = Id(hero), accept = true }));
        await _host.DrainOutboxAsync();
        Assert.True((await Cultivators.Ok(await friend.P.GetAsync("/api/v1/notifications")))["items"]!.AsArray().Any(n => n!.Str("type") == "sect_application_accepted"));
        await Cultivators.Ok(await friend.P.PostAsync($"/api/v1/sects/{sectId}/donations", new { characterId = Id(friend), amount = 50 }, Player.NewKey()));
        await Cultivators.Ok(await hero.P.PostAsync($"/api/v1/sects/{sectId}/donations", new { characterId = Id(hero), amount = 300 }, Player.NewKey()));

        await _c.StandAtAsync(hero.P, hero.C, herb.Point);
        var territory = await Cultivators.Ok(await hero.P.PostAsync("/api/v1/territories/claim", new { characterId = Id(hero) }, Player.NewKey()));
        Assert.Equal(sectId, territory.Str("ownerSectId"));
        var mine = await Cultivators.Ok(await hero.P.GetAsync($"/api/v1/characters/{Id(hero)}/sect"));
        Assert.Equal(350 - 200, mine.Int("treasury"));

        Assert.Equal(HttpStatusCode.OK, (await _host.Client.GetAsync(new Uri("/health/economy", UriKind.Relative))).StatusCode);
        Assert.Equal(0, await _host.DrainOutboxAsync());
    }
}
