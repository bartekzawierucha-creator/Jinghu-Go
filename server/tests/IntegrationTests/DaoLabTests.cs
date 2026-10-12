using System.Net;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Integration")]
public class DaoLabTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static int _names;

    private async Task<(Player P, JsonObject C)> CultivatorAsync()
    {
        var n = Interlocked.Increment(ref _names);
        var p = await _host.RegisterAndLoginAsync();
        return (p, await p.CreateCharacterAsync("Mistrz " + new string([.. n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(d => (char)('a' + (d - '0')))])));
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

    private Task FundAsync(JsonObject c, long stones) =>
        InUowAsync(async (uow, inv) => (await inv.CreditAsync(uow, Id(c), stones, "test", Guid.NewGuid().ToString(), CancellationToken.None)).Value);

    private Task GiveAsync(JsonObject c, string item, long qty) =>
        InUowAsync(async (uow, inv) => (await inv.AddItemsAsync(uow, Id(c), item, qty, "test", Guid.NewGuid().ToString(), CancellationToken.None)).Value);

    private static object Design(string name = "Dłoń Burzy", int power = 25, int qiCost = 6, int cooldown = 2) => new
    {
        name, slot = "attack", qiCost, cooldown, speed = 0,
        attack = new { power, scaling = "strength", rangeMin = 1, rangeMax = 1, accuracyPermille = 0, effect = (object?)null },
        defense = (object?)null, movement = (object?)null, counter = (object?)null,
    };

    private static string Dao(JsonObject c) => $"/api/v1/characters/{c.Str("id")}/dao-lab";

    private async Task<JsonObject> InventoryAsync(Player p, JsonObject c) => await Ok(await p.GetAsync($"/api/v1/characters/{c.Str("id")}/inventory"));

    [Fact]
    public async Task Designs_are_validated_paid_for_once_and_playable_in_battle()
    {
        var (p, c) = await CultivatorAsync();
        var preview = await Ok(await p.PostAsync("/api/v1/dao-lab/preview", Design(power: 25, qiCost: 0, cooldown: 0)));
        Assert.False(preview["valid"]!.GetValue<bool>());
        await Problem(await p.PostAsync(Dao(c) + "/designs", Design(power: 25, qiCost: 0, cooldown: 0), Player.NewKey()), HttpStatusCode.BadRequest, "design_invalid");

        await Problem(await p.PostAsync(Dao(c) + "/designs", Design(), Player.NewKey()), (HttpStatusCode)422, "insufficient_funds"); // fee 150 > 100
        Assert.Equal(0, (await Ok(await p.GetAsync(Dao(c) + "/techniques")))["items"]!.AsArray().Count, "a refused design stores nothing");

        await FundAsync(c, 100);
        var key = Player.NewKey();
        var created = await Ok(await p.PostAsync(Dao(c) + "/designs", Design(), key), HttpStatusCode.Created);
        var techniqueId = created["technique"]!.Str("id");
        Assert.Equal((await Ok(await p.PostAsync(Dao(c) + "/designs", Design(), key), HttpStatusCode.Created)).Compact(), created.Compact());
        Assert.Equal(50, (await InventoryAsync(p, c)).Int("balance"), "fee charged once");
        Assert.Equal(250, created["technique"]!.Int("points"));

        var loadout = await Ok(await p.GetAsync($"/api/v1/characters/{c.Str("id")}/combat/loadout"));
        Assert.True(loadout["known"]!.AsArray().Any(k => k!.Str("id") == techniqueId));
        var slots = loadout["slots"]!.AsArray().Select(s => s!["techniqueId"]?.GetValue<string>()).ToList();
        slots[0] = techniqueId;
        await Ok(await p.SendAsync(HttpMethod.Put, $"/api/v1/characters/{c.Str("id")}/combat/loadout", new { expectedVersion = loadout.Int("version"), slots }));

        var battle = await Ok(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = c.Str("id"), npcId = "training_dummy" }, Player.NewKey()), HttpStatusCode.Created);
        Assert.Equal(techniqueId, battle["mySlots"]![0]!.Str("techniqueId"));
        Assert.Equal("Dłoń Burzy", battle["mySlots"]![0]!.Str("namePl"));
        if (battle.Int("distance") > 1)
        {
            var move = battle["mySlots"]!.AsArray().First(s => s!.Str("kind") == "movement")!;
            battle = await Ok(await p.PostAsync($"/api/v1/combat/battles/{battle.Str("id")}/actions", new { turn = battle.Int("turn"), slot = move.Int("slot"), direction = "advance" }, Player.NewKey()));
        }

        var after = await Ok(await p.PostAsync($"/api/v1/combat/battles/{battle.Str("id")}/actions", new { turn = battle.Int("turn"), slot = 0, direction = (string?)null }, Player.NewKey()));
        Assert.True(after["lastTurnEvents"]!.AsArray().Any(e => e!["techniqueId"]?.GetValue<string>() == techniqueId), after.Compact());

        var (other, oc) = await CultivatorAsync();
        var foreign = await Ok(await other.GetAsync($"/api/v1/characters/{oc.Str("id")}/combat/loadout"));
        var stolen = foreign["slots"]!.AsArray().Select(s => s!["techniqueId"]?.GetValue<string>()).ToList();
        stolen[0] = techniqueId;
        await Problem(await other.SendAsync(HttpMethod.Put, $"/api/v1/characters/{oc.Str("id")}/combat/loadout", new { expectedVersion = foreign.Int("version"), slots = stolen }),
            HttpStatusCode.BadRequest, "loadout_invalid");
        var publicView = await Ok(await other.GetAsync($"/api/v1/dao-lab/techniques/{techniqueId}"));
        Assert.Equal(c.Str("name"), publicView.Str("authorName"));
        await Problem(await other.GetAsync("/api/v1/dao-lab/techniques/palm_strike"), HttpStatusCode.NotFound, "technique_not_found");
    }

    [Fact]
    public async Task Scrolls_are_inscribed_by_the_author_traded_and_learned_once()
    {
        var (author, a) = await CultivatorAsync();
        var (pupil, b) = await CultivatorAsync();
        await FundAsync(a, 1000);
        await FundAsync(b, 1000);
        var technique = (await Ok(await author.PostAsync(Dao(a) + "/designs", Design("Krok Smoka"), Player.NewKey()), HttpStatusCode.Created))["technique"]!.Str("id");
        var scrolls = $"/api/v1/dao-lab/techniques/{technique}/scrolls";

        await Problem(await pupil.PostAsync(scrolls, new { characterId = b.Str("id") }, Player.NewKey()), HttpStatusCode.Conflict, "not_author");
        await Problem(await author.PostAsync(scrolls, new { characterId = a.Str("id") }, Player.NewKey()), (HttpStatusCode)422, "insufficient_items");  // no jade
        await GiveAsync(a, "jade", 2);
        var first = await Ok(await author.PostAsync(scrolls, new { characterId = a.Str("id") }, Player.NewKey()), HttpStatusCode.Created);
        var second = await Ok(await author.PostAsync(scrolls, new { characterId = a.Str("id") }, Player.NewKey()), HttpStatusCode.Created);
        var bag = (await InventoryAsync(author, a))["items"]!.AsArray();
        Assert.Equal(2, bag.Count(i => i!.Str("itemId") == "technique_scroll" && i!["boundRef"]!.GetValue<string>() == technique));
        await Problem(await author.PostAsync($"/api/v1/characters/{a.Str("id")}/inventory/equip", new { instanceId = first.Str("instanceId"), expectedVersion = 1 }),
            HttpStatusCode.BadRequest, "item_not_equippable");
        await GiveAsync(a, "refining_stone", 1);
        await Problem(await author.PostAsync($"/api/v1/characters/{a.Str("id")}/crafting/upgrade", new { instanceId = first.Str("instanceId"), expectedVersion = 1 }, Player.NewKey()),
            HttpStatusCode.BadRequest, "item_not_upgradable");

        foreach (var s in new[] { first, second })
        {
            var listing = await Ok(await author.PostAsync("/api/v1/market/listings",
                new { characterId = a.Str("id"), instanceId = s.Str("instanceId"), expectedVersion = s.Int("version"), price = 100 }, Player.NewKey()), HttpStatusCode.Created);
            Assert.Equal(technique, listing.Str("instanceRef"), "buyers see which technique a scroll teaches");
            await Ok(await pupil.PostAsync($"/api/v1/market/listings/{listing.Str("id")}/buy", new { characterId = b.Str("id") }, Player.NewKey()));
        }

        var owned = (await InventoryAsync(pupil, b))["items"]!.AsArray().Where(i => i!.Str("itemId") == "technique_scroll").ToList();
        Assert.Equal(2, owned.Count);
        var learnKey = Player.NewKey();
        var learnUrl = $"{Dao(b)}/scrolls/{owned[0]!.Str("id")}/learn";
        var learned = await Ok(await pupil.PostAsync(learnUrl, new { expectedVersion = owned[0]!.Int("version") }, learnKey));
        Assert.Equal("scroll", learned.Str("source"));
        Assert.Equal(learned.Compact(), (await Ok(await pupil.PostAsync(learnUrl, new { expectedVersion = owned[0]!.Int("version") }, learnKey))).Compact());

        await Problem(await pupil.PostAsync($"{Dao(b)}/scrolls/{owned[1]!.Str("id")}/learn", new { expectedVersion = owned[1]!.Int("version") }, Player.NewKey()),
            HttpStatusCode.Conflict, "technique_already_known");
        var remaining = (await InventoryAsync(pupil, b))["items"]!.AsArray().Where(i => i!.Str("itemId") == "technique_scroll").ToList();
        Assert.Equal(owned[1]!.Str("id"), remaining.Single()!.Str("id"), "the used scroll is gone, the spare one is kept");

        var known = (await Ok(await pupil.GetAsync(Dao(b) + "/techniques")))["items"]!.AsArray();
        Assert.Equal(technique, known.Single()!["technique"]!.Str("id"));
        await Problem(await author.PostAsync($"{Dao(a)}/scrolls/{owned[1]!.Str("id")}/learn", new { expectedVersion = 2 }, Player.NewKey()),
            HttpStatusCode.NotFound, "scroll_not_found");
    }

    [Fact]
    public async Task A_cultivator_authors_at_most_six_techniques()
    {
        var (p, c) = await CultivatorAsync();
        await FundAsync(c, 5000);
        for (var i = 0; i < 6; i++)
            await Ok(await p.PostAsync(Dao(c) + "/designs", Design("Technika " + (char)('a' + i)), Player.NewKey()), HttpStatusCode.Created);
        await Problem(await p.PostAsync(Dao(c) + "/designs", Design("Technika siódma"), Player.NewKey()), (HttpStatusCode)422, "design_limit");
        Assert.Equal(6, (await Ok(await p.GetAsync(Dao(c) + "/techniques")))["items"]!.AsArray().Count);
    }
}
