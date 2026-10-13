using System.Net;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

/// <summary>Shared helpers for sect and territory tests.</summary>
internal sealed class Cultivators(ApiHost host)
{
    private static int _names;

    public async Task<(Player P, JsonObject C)> NewAsync(long stones = 0)
    {
        var n = Interlocked.Increment(ref _names);
        var p = await host.RegisterAndLoginAsync();
        var c = await p.CreateCharacterAsync("Uczen " + new string([.. n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(d => (char)('a' + (d - '0')))]));
        if (stones > 0) await FundAsync(c, stones);
        return (p, c);
    }

    public Task FundAsync(JsonObject c, long stones) =>
        host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            var r = await host.Service<IInventory>().CreditAsync(uow, new CharacterId(Guid.Parse(c.Str("id"))), stones, "test", Guid.NewGuid().ToString(), CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            return r.Value;
        }, CancellationToken.None);

    /// <summary>
    /// Ends any running exploration session, starts a new one and sends two stationary samples, so the server holds a
    /// verified position there (a jump inside one session would rightly trigger the relocation cooldown).
    /// </summary>
    public async Task StandAtAsync(Player p, JsonObject c, GeoPoint at)
    {
        var id = c.Str("id");
        await p.PostAsync($"/api/v1/characters/{id}/exploration/stop");
        Assert.Equal(HttpStatusCode.OK, (await p.PostAsync($"/api/v1/characters/{id}/exploration/start")).StatusCode);
        for (var i = 0; i < 2; i++)
        {
            if (i > 0) host.Clock.Advance(TimeSpan.FromSeconds(10));
            var res = await p.PostAsync($"/api/v1/characters/{id}/exploration/samples",
                new { samples = new[] { new { lat = at.Lat, lon = at.Lon, accuracyM = 8.0, capturedAt = host.Clock.GetUtcNow() } } });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode, await res.Content.ReadAsStringAsync());
        }
    }

    public static async Task<JsonObject> Ok(HttpResponseMessage res, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, res.StatusCode, await res.Content.ReadAsStringAsync());
        return await ApiHost.Json(res);
    }

    public static async Task Problem(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal(status, res.StatusCode, body);
        Assert.Equal(code, JsonNode.Parse(body)!["code"]!.GetValue<string>(), body);
    }

    /// <summary>Founds a sect and fills it with members of the given roles. Returns (sectId, leader, others in order).</summary>
    public async Task<(string SectId, (Player P, JsonObject C) Leader, List<(Player P, JsonObject C)> Others)> SectAsync(string name, params string[] roles)
    {
        var leader = await NewAsync(stones: 5000);
        var sect = await Ok(await leader.P.PostAsync("/api/v1/sects", new { characterId = leader.C.Str("id"), name }, Player.NewKey()), HttpStatusCode.Created);
        var sectId = sect["sect"]!.Str("id");
        var others = new List<(Player, JsonObject)>();
        foreach (var role in roles)
        {
            var m = await NewAsync(stones: 1000);
            var app = await Ok(await m.P.PostAsync($"/api/v1/sects/{sectId}/applications", new { characterId = m.C.Str("id"), message = "Przyjmijcie mnie" }, Player.NewKey()),
                HttpStatusCode.Created);
            await Ok(await leader.P.PostAsync($"/api/v1/sects/applications/{app.Str("id")}/decision", new { characterId = leader.C.Str("id"), accept = true }));
            if (role != "member")
                await Ok(await leader.P.PostAsync($"/api/v1/sects/{sectId}/roles", new { characterId = leader.C.Str("id"), targetCharacterId = m.C.Str("id"), role }));
            others.Add(m);
        }

        return (sectId, leader, others);
    }
}

[Category("Integration")]
public class SectTests : IAsyncLifetime
{
    private ApiHost _host = null!;
    private Cultivators _c = null!;

    public async Task InitializeAsync()
    {
        _host = await ApiHost.StartAsync();
        _c = new Cultivators(_host);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Founding_costs_the_fee_names_are_unique_and_one_sect_per_character()
    {
        var (p, c) = await _c.NewAsync(stones: 1000);
        var url = "/api/v1/sects";
        await Cultivators.Problem(await p.PostAsync(url, new { characterId = c.Str("id"), name = "<b>" }, Player.NewKey()), HttpStatusCode.BadRequest, "sect_name_invalid");
        var key = Player.NewKey();
        var founded = await Cultivators.Ok(await p.PostAsync(url, new { characterId = c.Str("id"), name = "Sekta Wudang" }, key), HttpStatusCode.Created);
        Assert.Equal(founded.Compact(), (await Cultivators.Ok(await p.PostAsync(url, new { characterId = c.Str("id"), name = "Sekta Wudang" }, key), HttpStatusCode.Created)).Compact());
        Assert.Equal("leader", founded.Str("myRole"));
        var inventory = await Cultivators.Ok(await p.GetAsync($"/api/v1/characters/{c.Str("id")}/inventory"));
        Assert.Equal(1100 - 500, inventory.Int("balance"), "founding fee charged once");
        await Cultivators.Problem(await p.PostAsync(url, new { characterId = c.Str("id"), name = "Druga Sekta" }, Player.NewKey()), HttpStatusCode.Conflict, "already_in_sect");

        var (p2, c2) = await _c.NewAsync(stones: 1000);
        await Cultivators.Problem(await p2.PostAsync(url, new { characterId = c2.Str("id"), name = "sekta  wudang".Replace("  ", " ") }, Player.NewKey()),
            HttpStatusCode.Conflict, "sect_name_taken");
        var (p3, c3) = await _c.NewAsync();
        await Cultivators.Problem(await p3.PostAsync(url, new { characterId = c3.Str("id"), name = "Biedna Sekta" }, Player.NewKey()), (HttpStatusCode)422, "insufficient_funds");
        await Cultivators.Problem(await p3.PostAsync(url, new { characterId = c2.Str("id"), name = "Cudza Sekta" }, Player.NewKey()), HttpStatusCode.NotFound, "character_not_found");
    }

    /// <summary>The permission matrix, checked on the server for every action (Stage 7: "testy uprawnień").</summary>
    [Fact]
    public async Task Roles_decide_what_members_may_do()
    {
        var (sectId, leader, others) = await _c.SectAsync("Sekta Emei", "elder", "member", "member");
        var (elder, member, member2) = (others[0], others[1], others[2]);
        string Id((Player P, JsonObject C) x) => x.C.Str("id");

        // applications: members cannot decide, elders can
        var (applicant, ac) = await _c.NewAsync();
        var app = await Cultivators.Ok(await applicant.PostAsync($"/api/v1/sects/{sectId}/applications", new { characterId = ac.Str("id"), message = "" }, Player.NewKey()),
            HttpStatusCode.Created);
        await Cultivators.Problem(await member.P.PostAsync($"/api/v1/sects/applications/{app.Str("id")}/decision", new { characterId = Id(member), accept = true }),
            HttpStatusCode.Forbidden, "sect_permission_denied");
        await Cultivators.Problem(await applicant.PostAsync($"/api/v1/sects/applications/{app.Str("id")}/decision", new { characterId = ac.Str("id"), accept = true }),
            HttpStatusCode.Forbidden, "not_a_member");
        await Cultivators.Ok(await elder.P.PostAsync($"/api/v1/sects/applications/{app.Str("id")}/decision", new { characterId = Id(elder), accept = false }));
        await Cultivators.Problem(await leader.P.PostAsync($"/api/v1/sects/applications/{app.Str("id")}/decision", new { characterId = Id(leader), accept = true }),
            HttpStatusCode.Conflict, "application_closed");

        // roles: only the leader
        await Cultivators.Problem(await elder.P.PostAsync($"/api/v1/sects/{sectId}/roles", new { characterId = Id(elder), targetCharacterId = Id(member), role = "elder" }),
            HttpStatusCode.Forbidden, "sect_permission_denied");

        // kick: strictly lower rank only
        await Cultivators.Problem(await member.P.PostAsync($"/api/v1/sects/{sectId}/kick", new { characterId = Id(member), targetCharacterId = Id(member2) }),
            HttpStatusCode.Forbidden, "sect_permission_denied");
        await Cultivators.Problem(await elder.P.PostAsync($"/api/v1/sects/{sectId}/kick", new { characterId = Id(elder), targetCharacterId = Id(leader) }),
            HttpStatusCode.Forbidden, "sect_permission_denied");
        var afterKick = await Cultivators.Ok(await elder.P.PostAsync($"/api/v1/sects/{sectId}/kick", new { characterId = Id(elder), targetCharacterId = Id(member2) }));
        Assert.Equal(3, afterKick.Int("memberCount"));

        // a character cannot act for a sect through someone else's character
        await Cultivators.Problem(await member.P.PostAsync($"/api/v1/sects/{sectId}/kick", new { characterId = Id(leader), targetCharacterId = Id(elder) }),
            HttpStatusCode.NotFound, "character_not_found");

        // leadership transfer: the old leader becomes an elder
        var transferred = await Cultivators.Ok(await leader.P.PostAsync($"/api/v1/sects/{sectId}/roles", new { characterId = Id(leader), targetCharacterId = Id(elder), role = "leader" }));
        Assert.Equal(Id(elder), transferred.Str("leaderCharacterId"));
        var roles = transferred["members"]!.AsArray().ToDictionary(m => m!.Str("characterId"), m => m!.Str("role"));
        Assert.Equal("leader", roles[Id(elder)]);
        Assert.Equal("elder", roles[Id(leader)]);

        var history = await Cultivators.Ok(await elder.P.GetAsync($"/api/v1/sects/{sectId}/history?characterId={Id(elder)}"));
        var kinds = history["items"]!.AsArray().Select(e => e!.Str("kind")).ToList();
        foreach (var k in new[] { "founded", "joined", "role_changed", "kicked", "leadership_transferred", "application_rejected" })
            Assert.True(kinds.Contains(k), $"history records {k}: {string.Join(",", kinds)}");
        await Cultivators.Problem(await applicant.GetAsync($"/api/v1/sects/{sectId}/history?characterId={ac.Str("id")}"), HttpStatusCode.Forbidden, "not_a_member");
    }

    [Fact]
    public async Task Donations_move_stones_into_the_treasury_and_count_as_contribution()
    {
        var (sectId, leader, others) = await _c.SectAsync("Sekta Hua", "member");
        var member = others[0];
        var url = $"/api/v1/sects/{sectId}/donations";
        foreach (var amount in new long[] { 0, -10 })
            await Cultivators.Problem(await member.P.PostAsync(url, new { characterId = member.C.Str("id"), amount }, Player.NewKey()), HttpStatusCode.BadRequest, "amount_invalid");
        await Cultivators.Problem(await member.P.PostAsync(url, new { characterId = member.C.Str("id"), amount = 5000 }, Player.NewKey()), (HttpStatusCode)422, "insufficient_funds");

        var key = Player.NewKey();
        var donated = await Cultivators.Ok(await member.P.PostAsync(url, new { characterId = member.C.Str("id"), amount = 300 }, key));
        Assert.Equal(donated.Compact(), (await Cultivators.Ok(await member.P.PostAsync(url, new { characterId = member.C.Str("id"), amount = 300 }, key))).Compact());
        Assert.Equal(300, donated.Int("treasury"));
        var balance = (await Cultivators.Ok(await member.P.GetAsync($"/api/v1/characters/{member.C.Str("id")}/inventory"))).Int("balance");
        Assert.Equal(1100 - 300, balance);
        var mine = await Cultivators.Ok(await leader.P.GetAsync($"/api/v1/characters/{leader.C.Str("id")}/sect"));
        Assert.Equal(300, mine.Int("treasury"));
        Assert.Equal(300, mine["sect"]!["members"]!.AsArray().Single(m => m!.Str("characterId") == member.C.Str("id"))!.Int("contribution"));

        var (outsider, oc) = await _c.NewAsync(stones: 100);
        await Cultivators.Problem(await outsider.PostAsync(url, new { characterId = oc.Str("id"), amount = 10 }, Player.NewKey()), HttpStatusCode.Forbidden, "not_a_member");
        await Cultivators.Problem(await outsider.GetAsync($"/api/v1/characters/{oc.Str("id")}/sect"), HttpStatusCode.NotFound, "not_in_sect");
    }

    [Fact]
    public async Task Chat_is_for_members_only_validated_rate_limited_and_pollable()
    {
        var (sectId, leader, others) = await _c.SectAsync("Sekta Emei", "member");
        var member = others[0];
        var url = $"/api/v1/sects/{sectId}/chat";
        var first = await Cultivators.Ok(await leader.P.PostAsync(url, new { characterId = leader.C.Str("id"), text = "  Witajcie, uczniowie!  " }, Player.NewKey()), HttpStatusCode.Created);
        Assert.Equal("Witajcie, uczniowie!", first.Str("text"));
        Assert.Equal(leader.C.Str("name"), first.Str("name"));
        var key = Player.NewKey();
        var reply = await Cultivators.Ok(await member.P.PostAsync(url, new { characterId = member.C.Str("id"), text = "Mistrzu!" }, key), HttpStatusCode.Created);
        var replay = await Cultivators.Ok(await member.P.PostAsync(url, new { characterId = member.C.Str("id"), text = "Mistrzu!" }, key), HttpStatusCode.Created);
        Assert.Equal(reply.Str("id"), replay.Str("id")); // idempotent retry, no duplicate line

        var all = (await Cultivators.Ok(await member.P.GetAsync($"{url}?characterId={member.C.Str("id")}")))["items"]!.AsArray();
        Assert.Equal(2, all.Count);
        Assert.Equal(first.Str("id"), all[0]!.Str("id")); // oldest first
        var since = (await Cultivators.Ok(await leader.P.GetAsync($"{url}?characterId={leader.C.Str("id")}&after={first.Str("id")}")))["items"]!.AsArray();
        Assert.Equal(1, since.Count);
        Assert.Equal("Mistrzu!", since[0]!.Str("text"));

        await Cultivators.Problem(await member.P.PostAsync(url, new { characterId = member.C.Str("id"), text = "   " }, Player.NewKey()), HttpStatusCode.BadRequest, "chat_message_invalid");
        await Cultivators.Problem(await member.P.PostAsync(url, new { characterId = member.C.Str("id"), text = new string('x', 301) }, Player.NewKey()), HttpStatusCode.BadRequest, "chat_message_invalid");
        await Cultivators.Problem(await member.P.PostAsync(url, new { characterId = member.C.Str("id"), text = "a\nb" }, Player.NewKey()), HttpStatusCode.BadRequest, "chat_message_invalid");

        var (outsider, oc) = await _c.NewAsync();
        await Cultivators.Problem(await outsider.PostAsync(url, new { characterId = oc.Str("id"), text = "Hej" }, Player.NewKey()), HttpStatusCode.Forbidden, "not_a_member");
        await Cultivators.Problem(await outsider.GetAsync($"{url}?characterId={oc.Str("id")}"), HttpStatusCode.Forbidden, "not_a_member");
        await Cultivators.Problem(await outsider.GetAsync($"{url}?characterId={member.C.Str("id")}"), HttpStatusCode.NotFound, "character_not_found");

        for (var i = 0; i < 4; i++) // 5 lines in 30 s with the greeting
            await Cultivators.Ok(await leader.P.PostAsync(url, new { characterId = leader.C.Str("id"), text = "Linia " + i }, Player.NewKey()), HttpStatusCode.Created);
        await Cultivators.Problem(await leader.P.PostAsync(url, new { characterId = leader.C.Str("id"), text = "Za dużo" }, Player.NewKey()),
            (HttpStatusCode)422, "chat_rate_limited");
        _host.Clock.Advance(TimeSpan.FromSeconds(31));
        await leader.P.RefreshAsync();
        await Cultivators.Ok(await leader.P.PostAsync(url, new { characterId = leader.C.Str("id"), text = "Już można" }, Player.NewKey()), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Leaving_applications_and_disbanding()
    {
        var (sectId, leader, others) = await _c.SectAsync("Sekta Tai", "member");
        var member = others[0];
        await Cultivators.Problem(await leader.P.PostAsync($"/api/v1/sects/{sectId}/leave", new { characterId = leader.C.Str("id") }), HttpStatusCode.Conflict, "leader_must_transfer");
        await Cultivators.Ok(await member.P.PostAsync($"/api/v1/sects/{sectId}/leave", new { characterId = member.C.Str("id") }));

        // applying to several sects; joining one withdraws the others
        var (_, leader2, _) = await _c.SectAsync("Sekta Kunlun");
        var a1 = await Cultivators.Ok(await member.P.PostAsync($"/api/v1/sects/{sectId}/applications", new { characterId = member.C.Str("id") }, Player.NewKey()), HttpStatusCode.Created);
        var sect2 = (await Cultivators.Ok(await leader2.P.GetAsync($"/api/v1/characters/{leader2.C.Str("id")}/sect")))["sect"]!.Str("id");
        var a2 = await Cultivators.Ok(await member.P.PostAsync($"/api/v1/sects/{sect2}/applications", new { characterId = member.C.Str("id") }, Player.NewKey()), HttpStatusCode.Created);
        await Cultivators.Problem(await member.P.PostAsync($"/api/v1/sects/{sect2}/applications", new { characterId = member.C.Str("id") }, Player.NewKey()),
            HttpStatusCode.Conflict, "application_pending");
        await Cultivators.Ok(await leader2.P.PostAsync($"/api/v1/sects/applications/{a2.Str("id")}/decision", new { characterId = leader2.C.Str("id"), accept = true }));
        await Cultivators.Problem(await leader.P.PostAsync($"/api/v1/sects/applications/{a1.Str("id")}/decision", new { characterId = leader.C.Str("id"), accept = true }),
            HttpStatusCode.Conflict, "application_closed");

        // the last member leaving disbands the sect; its name stays taken
        var gone = await Cultivators.Ok(await leader.P.PostAsync($"/api/v1/sects/{sectId}/leave", new { characterId = leader.C.Str("id") }));
        Assert.True(gone["disbanded"]!.GetValue<bool>());
        await Cultivators.Problem(await member.P.PostAsync($"/api/v1/sects/{sectId}/applications", new { characterId = member.C.Str("id") }, Player.NewKey()),
            HttpStatusCode.NotFound, "sect_not_found");
        var (p, c) = await _c.NewAsync(stones: 1000);
        await Cultivators.Problem(await p.PostAsync("/api/v1/sects", new { characterId = c.Str("id"), name = "Sekta Tai" }, Player.NewKey()), HttpStatusCode.Conflict, "sect_name_taken");
    }

    [Fact]
    public async Task Seat_needs_the_leader_a_verified_position_and_treasury_funds()
    {
        var (sectId, leader, others) = await _c.SectAsync("Sekta Shaolin", "elder");
        var seatUrl = $"/api/v1/sects/{sectId}/seat";
        var here = new GeoPoint(34.50764, 112.93554);
        await _c.StandAtAsync(others[0].P, others[0].C, here);
        await Cultivators.Problem(await others[0].P.PostAsync(seatUrl, new { characterId = others[0].C.Str("id") }, Player.NewKey()), HttpStatusCode.Forbidden, "sect_permission_denied");
        await _c.StandAtAsync(leader.P, leader.C, here);
        await Cultivators.Problem(await leader.P.PostAsync(seatUrl, new { characterId = leader.C.Str("id") }, Player.NewKey()), (HttpStatusCode)422, "insufficient_treasury");
        await Cultivators.Ok(await leader.P.PostAsync($"/api/v1/sects/{sectId}/donations", new { characterId = leader.C.Str("id"), amount = 400 }, Player.NewKey()));
        var seated = await Cultivators.Ok(await leader.P.PostAsync(seatUrl, new { characterId = leader.C.Str("id") }, Player.NewKey()));
        Assert.Equal(34.508, seated["sect"]!["seatLat"]!.GetValue<double>(), "stored rounded, never the raw fix");
        Assert.Equal(112.936, seated["sect"]!["seatLon"]!.GetValue<double>());
        Assert.Equal(100, seated.Int("treasury"));
        await Cultivators.Problem(await leader.P.PostAsync(seatUrl, new { characterId = leader.C.Str("id") }, Player.NewKey()), HttpStatusCode.Conflict, "seat_cooldown");
    }
}
