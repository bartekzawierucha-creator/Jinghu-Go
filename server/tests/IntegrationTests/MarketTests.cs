using System.Net;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Inventory.Application;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Integration")]
public class MarketTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static int _names;

    private async Task<(Player P, JsonObject C)> TraderAsync()
    {
        var n = Interlocked.Increment(ref _names);
        var p = await _host.RegisterAndLoginAsync();
        return (p, await p.CreateCharacterAsync("Handlarz " + new string([.. n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(d => (char)('a' + (d - '0')))])));
    }

    private static async Task<JsonObject> Ok(HttpResponseMessage res, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, res.StatusCode, await res.Content.ReadAsStringAsync());
        return await ApiHost.Json(res);
    }

    private static async Task Problem(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal(status, res.StatusCode, body);
        Assert.Equal(code, JsonNode.Parse(body)!["code"]!.GetValue<string>(), body);
    }

    private Task<T> InUowAsync<T>(Func<IUnitOfWork, IInventory, Task<T>> action) =>
        _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            var r = await action(uow, _host.Service<IInventory>());
            await uow.CommitAsync(CancellationToken.None);
            return r;
        }, CancellationToken.None);

    private static CharacterId Id(JsonObject c) => new(Guid.Parse(c.Str("id")));

    private Task GiveAsync(JsonObject c, string itemId, long qty) =>
        InUowAsync(async (uow, inv) => (await inv.AddItemsAsync(uow, Id(c), itemId, qty, "test", Guid.NewGuid().ToString(), CancellationToken.None)).Value);

    private Task FundAsync(JsonObject c, long amount) =>
        InUowAsync(async (uow, inv) => (await inv.CreditAsync(uow, Id(c), amount, "test", Guid.NewGuid().ToString(), CancellationToken.None)).Value);

    private Task<InstanceInfo> GiveInstanceAsync(JsonObject c, string itemId) =>
        InUowAsync(async (uow, inv) =>
        {
            var owner = (await _host.Service<ICharacterDirectory>().FindAsync(uow, Guid.Parse(c.Str("id")), CancellationToken.None))!;
            return (await inv.CreateInstanceAsync(uow, owner.Id, owner.AccountId, itemId, "test", Guid.NewGuid().ToString(), CancellationToken.None)).Value;
        });

    private async Task<(long Balance, Dictionary<string, long> Stacks, JsonArray Items)> StateAsync(Player p, JsonObject c)
    {
        var inv = await Ok(await p.GetAsync($"/api/v1/characters/{c.Str("id")}/inventory"));
        return (inv["balance"]!.GetValue<long>(),
            inv["stacks"]!.AsArray().ToDictionary(s => s!.Str("itemId"), s => s!["quantity"]!.GetValue<long>()),
            inv["items"]!.AsArray());
    }

    private static Task<HttpResponseMessage> ListStack(Player p, JsonObject c, string item, long qty, long price, string? key = null) =>
        p.PostAsync("/api/v1/market/listings", new { characterId = c.Str("id"), itemId = item, quantity = qty, price }, key ?? Player.NewKey());

    private static Task<HttpResponseMessage> ListInstance(Player p, JsonObject c, Guid instance, int version, long price) =>
        p.PostAsync("/api/v1/market/listings", new { characterId = c.Str("id"), instanceId = instance, expectedVersion = version, price }, Player.NewKey());

    private static Task<HttpResponseMessage> Buy(Player p, JsonObject c, string listingId, string? key = null) =>
        p.PostAsync($"/api/v1/market/listings/{listingId}/buy", new { characterId = c.Str("id") }, key ?? Player.NewKey());

    private Task<(int, int)> MismatchesAsync() =>
        _host.Service<IUnitOfWorkFactory>().RunAsync(uow => _host.Service<IInventoryRepository>().CountMismatchesAsync(uow, CancellationToken.None), CancellationToken.None);

    [Fact]
    public async Task Stack_sale_moves_goods_and_stones_once_with_fee_and_tax()
    {
        var (seller, s) = await TraderAsync();
        var (buyer, b) = await TraderAsync();
        await GiveAsync(s, "wolf_fang", 10);
        await _host.DrainOutboxAsync();

        var listing = await Ok(await ListStack(seller, s, "wolf_fang", 4, 60), HttpStatusCode.Created);
        var (sb, ss, _) = await StateAsync(seller, s);
        Assert.Equal(99, sb);                                    // fee max(1, 60 × 1 %) = 1 (rounded down 0.6 → min 1)
        Assert.Equal(6, ss["wolf_fang"]);                       // 4 in escrow

        var browse = await Ok(await buyer.GetAsync("/api/v1/market/listings?itemId=wolf_fang"));
        Assert.Equal(listing.Str("id"), browse["items"]![0]!.Str("id"));

        var key = Player.NewKey();
        var bought = await Ok(await Buy(buyer, b, listing.Str("id"), key));
        Assert.Equal(40, bought.Int("balance"));
        Assert.Equal(bought.Compact(), (await Ok(await Buy(buyer, b, listing.Str("id"), key))).Compact(), "retry replays, never charges twice");
        await Problem(await Buy(buyer, b, listing.Str("id")), HttpStatusCode.Conflict, "listing_unavailable");

        var (bb, bs, _) = await StateAsync(buyer, b);
        Assert.Equal(40, bb);
        Assert.Equal(4, bs["wolf_fang"]);
        Assert.Equal(99 + 60 - 3, (await StateAsync(seller, s)).Balance);   // tax 5 % of 60 = 3

        Assert.Equal(1, await _host.DrainOutboxAsync());
        var note = (await Ok(await seller.GetAsync("/api/v1/notifications")))["items"]!.AsArray().First(n => n!.Str("type") == "market_sold")!;
        Assert.Equal(57, note["payload"].Int("proceeds"));

        var mine = await Ok(await seller.GetAsync($"/api/v1/characters/{s.Str("id")}/market/listings"));
        Assert.Equal("sold", mine["items"]![0]!["listing"]!.Str("status"));
        Assert.Equal(57, mine["items"]![0]!.Int("proceeds"));
        Assert.Equal((0, 0), await MismatchesAsync());
    }

    /// <summary>Acceptance #3: two concurrent purchases of the same unique item — exactly one succeeds.</summary>
    [Fact]
    public async Task Concurrent_buyers_of_a_unique_item_cannot_both_win()
    {
        var (seller, s) = await TraderAsync();
        var buyers = new List<(Player P, JsonObject C)>();
        for (var i = 0; i < 4; i++)
        {
            var t = await TraderAsync();
            await FundAsync(t.C, 1000);
            buyers.Add(t);
        }

        for (var round = 0; round < 3; round++)
        {
            var feather = await GiveInstanceAsync(s, "phoenix_feather");
            var listing = await Ok(await ListInstance(seller, s, feather.Id, feather.Version, 300), HttpStatusCode.Created);
            var results = await Task.WhenAll(buyers.Select(x => Buy(x.P, x.C, listing.Str("id"))));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK), string.Join(", ", results.Select(r => r.StatusCode)));
            foreach (var r in results.Where(r => r.StatusCode != HttpStatusCode.OK))
                await Problem(r, HttpStatusCode.Conflict, "listing_unavailable");

            var owners = 0;
            foreach (var (p, c) in buyers)
                owners += (await StateAsync(p, c)).Items.Count(i => i!.Str("id") == feather.Id.ToString());
            Assert.Equal(1, owners, "the feather exists exactly once");
        }

        long spent = 0;
        foreach (var (p, c) in buyers) spent += 1100 - (await StateAsync(p, c)).Balance;
        Assert.Equal(3 * 300, spent, "every buyer paid only for what they got");
        Assert.Equal(100 - 3 * 3 + 3 * (300 - 15), (await StateAsync(seller, s)).Balance);
        Assert.Equal((0, 0), await MismatchesAsync());
    }

    [Fact]
    public async Task Invalid_listings_are_rejected_and_change_nothing()
    {
        var (seller, s) = await TraderAsync();
        var (other, o) = await TraderAsync();
        await GiveAsync(s, "jade", 3);
        foreach (var price in new long[] { 0, -5, 1_000_000_001 })
            await Problem(await ListStack(seller, s, "jade", 1, price), HttpStatusCode.BadRequest, "price_invalid");
        foreach (var qty in new long[] { 0, -1, 1000 })
            await Problem(await ListStack(seller, s, "jade", qty, 10), HttpStatusCode.BadRequest, "quantity_invalid");
        await Problem(await ListStack(seller, s, "jade", 4, 10), (HttpStatusCode)422, "insufficient_items");
        await Problem(await ListStack(seller, s, "no_such_item", 1, 10), HttpStatusCode.BadRequest, "item_unknown");
        await Problem(await ListStack(other, s, "jade", 1, 10), HttpStatusCode.NotFound, "character_not_found");
        await Problem(await seller.PostAsync("/api/v1/market/listings", new { characterId = s.Str("id"), price = 10 }, Player.NewKey()),
            HttpStatusCode.BadRequest, "listing_target_invalid");

        var sword = await GiveInstanceAsync(s, "iron_sword");
        await Problem(await ListInstance(other, o, sword.Id, 1, 10), HttpStatusCode.NotFound, "item_not_found");
        await Ok(await seller.PostAsync($"/api/v1/characters/{s.Str("id")}/inventory/equip", new { instanceId = sword.Id, expectedVersion = 1 }));
        await Problem(await ListInstance(seller, s, sword.Id, 2, 10), HttpStatusCode.Conflict, "item_not_in_bag");

        var (balance, stacks, _) = await StateAsync(seller, s);
        Assert.Equal(100, balance);
        Assert.Equal(3, stacks["jade"]);

        await InUowAsync((uow, inv) => inv.DebitAsync(uow, Id(s), 100, "test", "drain", CancellationToken.None));
        await Problem(await ListStack(seller, s, "jade", 1, 10), (HttpStatusCode)422, "insufficient_funds");
        Assert.Equal(3, (await StateAsync(seller, s)).Stacks["jade"], "fee failure rolls the escrow back");
    }

    [Fact]
    public async Task Buyers_need_funds_and_bag_space_and_cannot_buy_their_own_listing()
    {
        var (seller, s) = await TraderAsync();
        var (buyer, b) = await TraderAsync();
        await GiveAsync(s, "iron_ore", 5);
        var pricey = await Ok(await ListStack(seller, s, "iron_ore", 1, 150), HttpStatusCode.Created);
        await Problem(await Buy(buyer, b, pricey.Str("id")), (HttpStatusCode)422, "insufficient_funds");
        await Problem(await Buy(seller, s, pricey.Str("id")), HttpStatusCode.Conflict, "own_listing");

        var cheap = await Ok(await ListStack(seller, s, "iron_ore", 2, 5), HttpStatusCode.Created);
        for (var i = 0; i < 60; i++) await GiveInstanceAsync(b, "iron_sword");
        await Problem(await Buy(buyer, b, cheap.Str("id")), (HttpStatusCode)422, "bag_full");
        Assert.Equal(100, (await StateAsync(buyer, b)).Balance, "a failed purchase charges nothing");
        Assert.Equal("active", (await Ok(await buyer.GetAsync($"/api/v1/market/listings/{cheap.Str("id")}"))).Str("status"));
    }

    [Fact]
    public async Task Cancel_and_expiry_return_goods_but_keep_the_fee()
    {
        var (seller, s) = await TraderAsync();
        var (buyer, b) = await TraderAsync();
        var ring = await GiveInstanceAsync(s, "spatial_ring");
        var listing = await Ok(await ListInstance(seller, s, ring.Id, ring.Version, 500), HttpStatusCode.Created);
        Assert.Equal(95, (await StateAsync(seller, s)).Balance);
        await Problem(await buyer.PostAsync($"/api/v1/market/listings/{listing.Str("id")}/cancel", new { characterId = b.Str("id") }),
            HttpStatusCode.NotFound, "listing_not_found");
        await Problem(await seller.PostAsync($"/api/v1/characters/{s.Str("id")}/crafting/upgrade", new { instanceId = ring.Id, expectedVersion = 2 }, Player.NewKey()),
            HttpStatusCode.Conflict, "item_listed");

        var cancelled = await Ok(await seller.PostAsync($"/api/v1/market/listings/{listing.Str("id")}/cancel", new { characterId = s.Str("id") }));
        Assert.Equal("cancelled", cancelled.Str("status"));
        var (balance, _, items) = await StateAsync(seller, s);
        Assert.Equal(95, balance);
        Assert.Equal("bag", items.Single()!.Str("state"));
        await Problem(await seller.PostAsync($"/api/v1/market/listings/{listing.Str("id")}/cancel", new { characterId = s.Str("id") }),
            HttpStatusCode.Conflict, "listing_unavailable");

        await GiveAsync(s, "spirit_herb", 10);
        var herbs = await Ok(await ListStack(seller, s, "spirit_herb", 10, 30), HttpStatusCode.Created);
        _host.Clock.Advance(TimeSpan.FromHours(49));
        await seller.RefreshAsync();
        await buyer.RefreshAsync();
        Assert.Equal(0, (await Ok(await buyer.GetAsync("/api/v1/market/listings?itemId=spirit_herb")))["items"]!.AsArray().Count, "expired listings are hidden");
        await Problem(await Buy(buyer, b, herbs.Str("id")), HttpStatusCode.Conflict, "listing_unavailable");
        Assert.True((await Ok(await seller.GetAsync($"/api/v1/market/listings/{herbs.Str("id")}")))["expired"]!.GetValue<bool>());
        await Ok(await seller.PostAsync($"/api/v1/market/listings/{herbs.Str("id")}/cancel", new { characterId = s.Str("id") }));
        Assert.Equal(10, (await StateAsync(seller, s)).Stacks["spirit_herb"]);
        Assert.Equal((0, 0), await MismatchesAsync());
    }
}
