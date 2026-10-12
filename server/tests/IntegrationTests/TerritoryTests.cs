using System.Net;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Integration")]
public class TerritoryTests : IAsyncLifetime
{
    private static readonly GeoPoint Krakow = new(50.0614, 19.9372);
    private static readonly GeoPoint Gdansk = new(54.3520, 18.6466);

    private ApiHost _host = null!;
    private Cultivators _c = null!;

    public async Task InitializeAsync()
    {
        _host = await ApiHost.StartAsync();
        _c = new Cultivators(_host);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static string Id((Player P, JsonObject C) x) => x.C.Str("id");

    private static Task<HttpResponseMessage> Claim((Player P, JsonObject C) x) =>
        x.P.PostAsync("/api/v1/territories/claim", new { characterId = Id(x) }, Player.NewKey());

    private static Task<HttpResponseMessage> Challenge((Player P, JsonObject C) x) =>
        x.P.PostAsync("/api/v1/territories/challenge", new { characterId = Id(x) }, Player.NewKey());

    private static async Task Donate(string sectId, (Player P, JsonObject C) x, long amount) =>
        await Cultivators.Ok(await x.P.PostAsync($"/api/v1/sects/{sectId}/donations", new { characterId = Id(x), amount }, Player.NewKey()));

    /// <summary>What the Combat module publishes when a PvP duel ends (the war scoring input).</summary>
    private Task PublishDuelAsync(Guid battleId, (Player P, JsonObject C) winner, (Player P, JsonObject C) loser) =>
        _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            var directory = _host.Service<JianghuGo.Modules.Characters.Contracts.ICharacterDirectory>();
            var wc = (await directory.FindAsync(uow, Guid.Parse(Id(winner)), CancellationToken.None))!;
            var lc = (await directory.FindAsync(uow, Guid.Parse(Id(loser)), CancellationToken.None))!;
            var w = new CombatEvents.Participant(wc.AccountId.Value, wc.Id.Value, "a", "won", 0);
            var l = new CombatEvents.Participant(lc.AccountId.Value, lc.Id.Value, "b", "lost", 0);
            await _host.Service<IOutbox>().EnqueueAsync(uow, OutboxMessage.Create(CombatEvents.BattleFinished,
                new CombatEvents.BattleFinishedV1(battleId, "pvp", "a_won", null, [w, l]), _host.Clock.GetUtcNow(), null), CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            return 0;
        }, CancellationToken.None);

    [Fact]
    public async Task Claiming_needs_rank_presence_treasury_and_room()
    {
        var (sectId, leader, others) = await _c.SectAsync("Sekta Wawel", "member");
        var member = others[0];

        var (loner, lc) = await _c.NewAsync();
        await _c.StandAtAsync(loner, lc, Krakow);
        await Cultivators.Problem(await Claim((loner, lc)), HttpStatusCode.Forbidden, "not_in_sect");

        await _c.StandAtAsync(member.P, member.C, Krakow);
        await Cultivators.Problem(await Claim(member), HttpStatusCode.Forbidden, "sect_permission_denied");

        var noPosition = await Claim(leader);                      // leader never sent a position
        Assert.Equal(HttpStatusCode.Conflict, noPosition.StatusCode, await noPosition.Content.ReadAsStringAsync());

        await _c.StandAtAsync(leader.P, leader.C, Krakow);
        await Cultivators.Problem(await Claim(leader), (HttpStatusCode)422, "insufficient_treasury");
        await Donate(sectId, leader, 1000);
        var claimed = await Cultivators.Ok(await Claim(leader));
        Assert.Equal(sectId, claimed.Str("ownerSectId"));
        Assert.Equal("claim", claimed["history"]![0]!.Str("reason"));
        await Cultivators.Problem(await Claim(leader), HttpStatusCode.Conflict, "already_owned");

        var here = await Cultivators.Ok(await leader.P.GetAsync($"/api/v1/characters/{Id(leader)}/territory/here"));
        Assert.Equal(claimed.Str("key"), here.Str("key"));
        var mine = await Cultivators.Ok(await leader.P.GetAsync($"/api/v1/sects/{sectId}/territories"));
        Assert.Equal(1, mine.Int("owned"));
        Assert.Equal(1, mine.Int("max"));                          // 1 base + 2 members / 5

        // a second territory needs a bigger sect
        await Cultivators.Ok(await leader.P.PostAsync($"/api/v1/sects/{sectId}/roles", new { characterId = Id(leader), targetCharacterId = Id(member), role = "elder" }));
        await _c.StandAtAsync(member.P, member.C, Gdansk);
        await Cultivators.Problem(await Claim(member), (HttpStatusCode)422, "territory_limit");

        // another sect cannot claim an owned territory
        var (rivalId, rival, _) = await _c.SectAsync("Sekta Smoka");
        await _c.StandAtAsync(rival.P, rival.C, Krakow);
        await Donate(rivalId, rival, 1000);
        await Cultivators.Problem(await Claim(rival), HttpStatusCode.Conflict, "territory_owned");
    }

    [Fact]
    public async Task War_is_declared_scored_by_duels_in_the_window_and_resolved_once()
    {
        var (defId, defLeader, defOthers) = await _c.SectAsync("Sekta Obroncow", "member");
        var (attId, attLeader, attOthers) = await _c.SectAsync("Sekta Najezdzcow", "elder");
        var (defender, attacker) = (defOthers[0], attOthers[0]);
        await Donate(defId, defLeader, 1000);
        await Donate(attId, attLeader, 2000);
        await _c.StandAtAsync(defLeader.P, defLeader.C, Krakow);
        var territory = (await Cultivators.Ok(await Claim(defLeader))).Str("key");

        await _c.StandAtAsync(attacker.P, attacker.C, Krakow);     // elder of the attacking sect may declare
        var war = await Cultivators.Ok(await Challenge(attacker), HttpStatusCode.Created);
        Assert.Equal("preparation", war.Str("phase"));
        await Cultivators.Problem(await Challenge(attacker), HttpStatusCode.Conflict, "war_in_progress");
        await _c.StandAtAsync(defLeader.P, defLeader.C, Krakow);
        await Cultivators.Problem(await Challenge(defLeader), HttpStatusCode.Conflict, "already_owned");
        var attTreasury = (await Cultivators.Ok(await attLeader.P.GetAsync($"/api/v1/characters/{Id(attLeader)}/sect"))).Int("treasury");
        Assert.Equal(2000 - 500, attTreasury);

        await _host.DrainOutboxAsync();
        var defNotes = (await Cultivators.Ok(await defLeader.P.GetAsync("/api/v1/notifications")))["items"]!.AsArray();
        Assert.True(defNotes.Any(n => n!.Str("type") == "war_declared"), "the defending leader is warned");

        // duel during preparation: not counted
        await PublishDuelAsync(Guid.NewGuid(), attacker, defender);
        await _host.DrainOutboxAsync();

        _host.Clock.Advance(TimeSpan.FromHours(13));                // battle window
        var battle1 = Guid.NewGuid();
        await PublishDuelAsync(battle1, attacker, defender);
        await PublishDuelAsync(battle1, attacker, defender);        // same battle delivered twice: counts once
        await PublishDuelAsync(Guid.NewGuid(), attLeader, defLeader);
        await PublishDuelAsync(Guid.NewGuid(), defender, attacker);
        await _host.DrainOutboxAsync();

        foreach (var p in new[] { defLeader.P, attLeader.P, attacker.P }) await p.RefreshAsync();
        var during = await Cultivators.Ok(await defLeader.P.GetAsync($"/api/v1/territories/{territory}"));
        Assert.Equal("battle", during["war"]!.Str("phase"));
        Assert.Equal(2, during["war"]!.Int("attackerScore"));
        Assert.Equal(1, during["war"]!.Int("defenderScore"));

        _host.Clock.Advance(TimeSpan.FromHours(24));                // window closed
        await PublishDuelAsync(Guid.NewGuid(), defender, attacker); // too late: not counted
        await _host.DrainOutboxAsync();
        foreach (var p in new[] { defLeader.P, attLeader.P }) await p.RefreshAsync();
        var after = await Cultivators.Ok(await defLeader.P.GetAsync($"/api/v1/territories/{territory}"));
        Assert.Equal(attId, after.Str("ownerSectId"));
        Assert.Equal("resolved", after["war"]!.Str("status"));
        Assert.Equal(attId, after["war"]!.Str("winnerSectId"));
        Assert.Equal(1, after["war"]!.Int("defenderScore"));
        Assert.Equal("war", after["history"]![0]!.Str("reason"));
        Assert.Equal(defId, after["history"]![0]!.Str("fromSectId"));

        var again = await Cultivators.Ok(await attLeader.P.GetAsync($"/api/v1/territories/{territory}"));
        Assert.Equal(2, again["history"]!.AsArray().Count, "resolved exactly once");
        await _host.DrainOutboxAsync();
        foreach (var leader in new[] { defLeader, attLeader })
        {
            var notes = (await Cultivators.Ok(await leader.P.GetAsync("/api/v1/notifications")))["items"]!.AsArray();
            Assert.True(notes.Any(n => n!.Str("type") == "war_resolved"), "both leaders hear the result");
        }
    }

    [Fact]
    public async Task Tribute_goes_to_the_owner_once_per_interval()
    {
        var (sectId, leader, others) = await _c.SectAsync("Sekta Daniny", "member");
        await Donate(sectId, leader, 300);
        await _c.StandAtAsync(leader.P, leader.C, Gdansk);
        var key = (await Cultivators.Ok(await Claim(leader))).Str("key");
        var url = $"/api/v1/territories/{key}/tribute";

        await Cultivators.Problem(await others[0].P.PostAsync(url, new { characterId = Id(others[0]) }, Player.NewKey()), HttpStatusCode.Forbidden, "sect_permission_denied");
        var (_, otherLeader, _) = await _c.SectAsync("Sekta Obca");
        await Cultivators.Problem(await otherLeader.P.PostAsync(url, new { characterId = Id(otherLeader) }, Player.NewKey()), HttpStatusCode.Forbidden, "not_owner");

        await Cultivators.Ok(await leader.P.PostAsync(url, new { characterId = Id(leader) }, Player.NewKey()));
        await Cultivators.Problem(await leader.P.PostAsync(url, new { characterId = Id(leader) }, Player.NewKey()), HttpStatusCode.Conflict, "tribute_not_ready");
        Assert.Equal(300 - 200 + 50, (await Cultivators.Ok(await leader.P.GetAsync($"/api/v1/characters/{Id(leader)}/sect"))).Int("treasury"));
        await Cultivators.Problem(await leader.P.GetAsync("/api/v1/territories/not-a-key"), HttpStatusCode.BadRequest, "territory_key_invalid");
    }
}
