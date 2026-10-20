using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

/// <summary>1.9–2.5 community features: friends, messages, mail with gifts, blocks, bounties, craft orders, seasons, erasure.</summary>
[Category("Integration")]
public class SocialTests : IAsyncLifetime
{
    private ApiHost _host = null!;
    private static int _names;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<(Player P, JsonObject C, string Name)> NewAsync()
    {
        var n = Interlocked.Increment(ref _names);
        var name = "Social " + new string([.. n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(d => (char)('a' + (d - '0')))]);
        var p = await _host.RegisterAndLoginAsync();
        return (p, await p.CreateCharacterAsync(name), name);
    }

    private static async Task<JsonObject> Ok(HttpResponseMessage res, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, res.StatusCode, await res.Content.ReadAsStringAsync());
        var text = await res.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(text) ? new JsonObject() : JsonNode.Parse(text)!.AsObject();
    }

    private static async Task Problem(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal(status, res.StatusCode, body);
        Assert.Contains(code, body);
    }

    private Task<T> InUowAsync<T>(Func<IUnitOfWork, IInventory, Task<T>> action) =>
        _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            var r = await action(uow, _host.Service<IInventory>());
            await uow.CommitAsync(CancellationToken.None);
            return r;
        }, CancellationToken.None);

    private static CharacterId Id(JsonObject c) => new(Guid.Parse(c.Str("id")));

    private Task<long> BalanceAsync(JsonObject c) => InUowAsync((uow, inv) => inv.BalanceAsync(uow, Id(c), CancellationToken.None));

    private async Task BefriendAsync((Player P, JsonObject C, string Name) a, (Player P, JsonObject C, string Name) b)
    {
        await Ok(await a.P.PostAsync($"/api/v1/characters/{a.C.Str("id")}/social/friends", new { name = b.Name }));
        await Ok(await b.P.PostAsync($"/api/v1/characters/{b.C.Str("id")}/social/friends/{a.C.Str("id")}/accept"));
    }

    [Fact]
    public async Task Friends_message_and_send_gifts_by_mail_with_a_courier_fee_claimed_once()
    {
        var a = await NewAsync();
        var b = await NewAsync();
        var aId = a.C.Str("id");
        var bId = b.C.Str("id");

        // only friends may write
        await Problem(await a.P.PostAsync($"/api/v1/characters/{aId}/social/messages/{bId}", new { text = "hej" }, Player.NewKey()), HttpStatusCode.Forbidden, "not_friends");
        var request = await Ok(await a.P.PostAsync($"/api/v1/characters/{aId}/social/friends", new { name = b.Name.ToUpperInvariant() }));
        Assert.Equal(1, request["outgoing"]!.AsArray().Count);
        var incoming = await Ok(await b.P.GetAsync($"/api/v1/characters/{bId}/social/friends"));
        Assert.Equal(aId, incoming["incoming"]![0]!.Str("characterId"));
        await Ok(await b.P.PostAsync($"/api/v1/characters/{bId}/social/friends/{aId}/accept"));

        await Ok(await a.P.PostAsync($"/api/v1/characters/{aId}/social/messages/{bId}", new { text = "Witaj, bracie" }, Player.NewKey()));
        var unread = await Ok(await b.P.GetAsync($"/api/v1/characters/{bId}/social/friends"));
        Assert.Equal(1, unread["friends"]![0]!.Int("unread"));
        var convo = await Ok(await b.P.GetAsync($"/api/v1/characters/{bId}/social/messages/{aId}"));
        Assert.Equal("Witaj, bracie", convo["items"]![0]!.Str("text"));
        Assert.Equal(0, (await Ok(await b.P.GetAsync($"/api/v1/characters/{bId}/social/friends")))["friends"]![0]!.Int("unread"));

        // a letter with 50 stones (+3 fee, rounded up) and 2 spirit herbs
        await InUowAsync((uow, inv) => inv.AddItemsAsync(uow, Id(a.C), "spirit_herb", 2, "test", Guid.NewGuid().ToString(), CancellationToken.None));
        var before = await BalanceAsync(a.C);
        var mail = await Ok(await a.P.PostAsync($"/api/v1/characters/{aId}/social/mail",
            new { toCharacterId = bId, text = "Na drogę", stones = 50, itemId = "spirit_herb", quantity = 2 }, Player.NewKey()));
        Assert.Equal(before - 53, await BalanceAsync(a.C));
        await Problem(await a.P.PostAsync($"/api/v1/characters/{aId}/social/mail", new { toCharacterId = bId, text = "", stones = 0, itemId = (string?)null, quantity = 0 }, Player.NewKey()),
            HttpStatusCode.BadRequest, "mail_empty");

        var bBefore = await BalanceAsync(b.C);
        var claim = await Ok(await b.P.PostAsync($"/api/v1/characters/{bId}/social/mail/{mail.Str("id")}/claim"));
        Assert.True(claim["claimed"]!.GetValue<bool>());
        Assert.Equal(bBefore + 50, await BalanceAsync(b.C));
        Assert.Equal(2L, await InUowAsync((uow, inv) => inv.CountAsync(uow, Id(b.C), "spirit_herb", CancellationToken.None)));
        await Problem(await b.P.PostAsync($"/api/v1/characters/{bId}/social/mail/{mail.Str("id")}/claim"), HttpStatusCode.Conflict, "mail_claimed");
        await Problem(await a.P.PostAsync($"/api/v1/characters/{aId}/social/mail/{mail.Str("id")}/claim"), HttpStatusCode.NotFound, "mail_not_found");

        // a block ends the friendship and silences the blocked one
        await Ok(await b.P.SendAsync(HttpMethod.Put, $"/api/v1/characters/{bId}/social/blocks/{aId}"));
        await Problem(await a.P.PostAsync($"/api/v1/characters/{aId}/social/friends", new { name = b.Name }), HttpStatusCode.Forbidden, "blocked");
        await Ok(await b.P.PostAsync($"/api/v1/characters/{bId}/social/reports", new { reportedCharacterId = aId, reason = "spam", excerpt = "Witaj, bracie" }), HttpStatusCode.NoContent);
        await Problem(await b.P.PostAsync($"/api/v1/characters/{bId}/social/reports", new { reportedCharacterId = aId, reason = "nope" }), HttpStatusCode.BadRequest, "reason_invalid");
    }

    [Fact]
    public async Task A_duel_victory_collects_every_open_bounty_on_the_loser_exactly_once()
    {
        var poster = await NewAsync();
        var target = await NewAsync();
        var hunter = await NewAsync();
        var posted = await Ok(await poster.P.PostAsync("/api/v1/bounties", new { characterId = poster.C.Str("id"), targetName = target.Name, stones = 100 }, Player.NewKey()));
        Assert.Equal(target.C.Str("id"), posted.Str("targetCharacterId"));
        await Problem(await poster.P.PostAsync("/api/v1/bounties", new { characterId = poster.C.Str("id"), targetName = target.Name, stones = 5 }, Player.NewKey()),
            HttpStatusCode.BadRequest, "bounty_invalid");

        var before = await BalanceAsync(hunter.C);
        var outbox = _host.Service<IOutbox>();
        var directory = _host.Service<JianghuGo.Modules.Characters.Contracts.ICharacterDirectory>();
        Task<Guid> AccountOf(JsonObject c) => _host.Service<IUnitOfWorkFactory>().RunAsync(async uow => (await directory.FindAsync(uow, Guid.Parse(c.Str("id")), default))!.AccountId.Value, default);
        var hunterAccount = await AccountOf(hunter.C);
        var targetAccount = await AccountOf(target.C);
        var message = OutboxMessage.Create(CombatEvents.BattleFinished, new CombatEvents.BattleFinishedV1(Guid.NewGuid(), "pvp", "a_won", null,
        [
            new(hunterAccount, Guid.Parse(hunter.C.Str("id")), "a", "won", 0),
            new(targetAccount, Guid.Parse(target.C.Str("id")), "b", "lost", 0),
        ]), _host.Clock.GetUtcNow(), null);
        await _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            await outbox.EnqueueAsync(uow, message, default);
            await uow.CommitAsync(default);
        }, default);
        await _host.DrainOutboxAsync();
        Assert.Equal(before + 100, await BalanceAsync(hunter.C));
        var list = await Ok(await hunter.P.GetAsync("/api/v1/bounties?characterId=" + hunter.C.Str("id")));
        Assert.Equal(0, list["items"]!.AsArray().Count);
        await Problem(await poster.P.PostAsync($"/api/v1/bounties/{posted.Str("id")}/withdraw", new { characterId = poster.C.Str("id") }), HttpStatusCode.Conflict, "bounty_closed");
    }

    [Fact]
    public async Task Craft_orders_move_items_and_reward_atomically()
    {
        var orderer = await NewAsync();
        var crafter = await NewAsync();
        var order = await Ok(await orderer.P.PostAsync("/api/v1/market/orders", new { characterId = orderer.C.Str("id"), itemId = "Duchowe Zioło", quantity = 3, reward = 60 }, Player.NewKey()));
        Assert.Equal("spirit_herb", order.Str("itemId"));
        await Problem(await crafter.P.PostAsync($"/api/v1/market/orders/{order.Str("id")}/fulfil", new { characterId = crafter.C.Str("id") }), HttpStatusCode.UnprocessableEntity, "insufficient");

        await InUowAsync((uow, inv) => inv.AddItemsAsync(uow, Id(crafter.C), "spirit_herb", 3, "test", Guid.NewGuid().ToString(), CancellationToken.None));
        var before = await BalanceAsync(crafter.C);
        var done = await Ok(await crafter.P.PostAsync($"/api/v1/market/orders/{order.Str("id")}/fulfil", new { characterId = crafter.C.Str("id") }));
        Assert.Equal(57, done.Int("reward"));                                  // 60 − 5 % tax
        Assert.Equal(before + 57, await BalanceAsync(crafter.C));
        Assert.Equal(3L, await InUowAsync((uow, inv) => inv.CountAsync(uow, Id(orderer.C), "spirit_herb", CancellationToken.None)));
        await Problem(await crafter.P.PostAsync($"/api/v1/market/orders/{order.Str("id")}/fulfil", new { characterId = crafter.C.Str("id") }), HttpStatusCode.NotFound, "order_not_found");
    }

    [Fact]
    public async Task The_board_shows_the_weekly_event_season_pass_and_birth_sign()
    {
        var a = await NewAsync();
        var board = await Ok(await a.P.GetAsync($"/api/v1/characters/{a.C.Str("id")}/quests"));
        Assert.NotNull(board["weekly"]?["personal"]);
        Assert.Equal(40, board["pass"]!.Int("levels"));
        Assert.Equal(0, board["pass"]!.Int("level"));
        Assert.NotNull(board["zodiac"]?["namePl"]);
        await Problem(await a.P.PostAsync($"/api/v1/characters/{a.C.Str("id")}/quests/pass/claim"), HttpStatusCode.Conflict, "quest_not_completed");
        var road = await Ok(await a.P.GetAsync($"/api/v1/characters/{a.C.Str("id")}/road"));
        Assert.Equal(66, road.Int("realms"));
    }

    [Fact]
    public async Task Deleting_the_account_anonymises_characters_and_blocks_sign_in()
    {
        var a = await NewAsync();
        await Problem(await a.P.PostAsync("/api/v1/auth/delete-account", new { password = "wrong-password-123" }), HttpStatusCode.Unauthorized, "invalid_credentials");
        var export = await a.P.GetAsync("/api/v1/auth/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains(a.Name, await export.Content.ReadAsStringAsync());

        await Ok(await a.P.PostAsync("/api/v1/auth/delete-account", new { password = ApiHost.Password }), HttpStatusCode.NoContent);
        await _host.DrainOutboxAsync();
        var login = await _host.Client.PostAsJsonAsync("/api/v1/auth/login", new { email = a.P.Email, password = ApiHost.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        var name = await _host.Service<IUnitOfWorkFactory>().RunAsync(
            async uow => (await _host.Service<JianghuGo.Modules.Characters.Contracts.ICharacterDirectory>().FindAsync(uow, Guid.Parse(a.C.Str("id")), default))!.Name, default);
        Assert.True(name.StartsWith("Zapomniany ", StringComparison.Ordinal), name);
    }
}
