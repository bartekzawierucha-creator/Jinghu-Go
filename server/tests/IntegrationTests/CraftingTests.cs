using System.Net;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Crafting.Domain;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Integration")]
public class CraftingTests : IAsyncLifetime
{
    private readonly QueuedSeedSource _seeds = new();
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync(seeds: _seeds);
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static int _names;

    private async Task<(Player P, JsonObject C)> CharacterAsync()
    {
        var n = Interlocked.Increment(ref _names);
        var p = await _host.RegisterAndLoginAsync();
        return (p, await p.CreateCharacterAsync("Kowal " + new string([.. n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(d => (char)('a' + (d - '0')))])));
    }

    private static async Task<JsonObject> Ok(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.OK, res.StatusCode, await res.Content.ReadAsStringAsync());
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

    private static Task<HttpResponseMessage> Craft(Player p, JsonObject c, string recipe, int times = 1, string? key = null) =>
        p.PostAsync($"/api/v1/characters/{c.Str("id")}/crafting/craft", new { recipeId = recipe, times }, key ?? Player.NewKey());

    private static Task<HttpResponseMessage> Upgrade(Player p, JsonObject c, Guid instance, int version, string? key = null) =>
        p.PostAsync($"/api/v1/characters/{c.Str("id")}/crafting/upgrade", new { instanceId = instance, expectedVersion = version }, key ?? Player.NewKey());

    /// <summary>First seed whose rolls produce exactly the wanted success pattern at this chance.</summary>
    private static ulong SeedFor(int chance, params bool[] pattern)
    {
        for (ulong s = 1; ; s++)
        {
            var rolls = CraftingRules.Rolls(s, pattern.Length);
            if (pattern.Select((want, i) => (rolls[i] < chance) == want).All(x => x)) return s;
        }
    }

    [Fact]
    public async Task Forging_takes_materials_and_stones_atomically_and_once()
    {
        var (p, c) = await CharacterAsync();
        await GiveAsync(c, "iron_ore", 5);
        var key = Player.NewKey();
        var first = await Ok(await Craft(p, c, "forge_iron_sword", key: key));
        Assert.Equal(1, first.Int("successes"));
        Assert.Equal(1, first["createdItems"]!.AsArray().Count);
        Assert.Equal(80, first.Int("balance"));
        Assert.Equal(first.Compact(), (await Ok(await Craft(p, c, "forge_iron_sword", key: key))).Compact());

        var (balance, stacks, items) = await StateAsync(p, c);
        Assert.Equal(80, balance);
        Assert.False(stacks.ContainsKey("iron_ore"));
        Assert.Equal("iron_sword", items.Single()!.Str("itemId"));

        await Problem(await Craft(p, c, "forge_iron_sword"), (HttpStatusCode)422, "insufficient_items");
        Assert.Equal(80, (await StateAsync(p, c)).Balance);
    }

    [Fact]
    public async Task Missing_stones_or_bag_space_consume_nothing()
    {
        var (p, c) = await CharacterAsync();
        await GiveAsync(c, "iron_ore", 10);
        await GiveAsync(c, "wolf_fang", 2);
        await Problem(await Craft(p, c, "forge_spatial_ring"), (HttpStatusCode)422, "insufficient_funds");   // 200 > 100
        var (balance, stacks, _) = await StateAsync(p, c);
        Assert.Equal(100, balance);
        Assert.Equal(10, stacks["iron_ore"]);
        Assert.Equal(2, stacks["wolf_fang"]);

        for (var i = 0; i < 58; i++) await GiveInstanceAsync(c, "iron_sword");  // 58 + 2 stacks = 60 slots
        await Problem(await Craft(p, c, "forge_iron_sword"), (HttpStatusCode)422, "bag_full");
        (balance, stacks, _) = await StateAsync(p, c);
        Assert.Equal(100, balance);
        Assert.Equal(10, stacks["iron_ore"]);
    }

    [Fact]
    public async Task Invalid_requests_are_rejected()
    {
        var (p, c) = await CharacterAsync();
        var (other, _) = await CharacterAsync();
        await GiveAsync(c, "spirit_herb", 50);
        await Problem(await Craft(p, c, "brew_qi_pill", 0), HttpStatusCode.BadRequest, "times_invalid");
        await Problem(await Craft(p, c, "brew_qi_pill", 11), HttpStatusCode.BadRequest, "times_invalid");
        await Problem(await Craft(p, c, "brew_qi_pill", -3), HttpStatusCode.BadRequest, "times_invalid");
        await Problem(await Craft(p, c, "forge_iron_sword", 2), HttpStatusCode.BadRequest, "times_invalid");
        await Problem(await Craft(p, c, "turn_lead_into_gold"), HttpStatusCode.BadRequest, "recipe_unknown");
        await Problem(await Craft(other, c, "brew_qi_pill"), HttpStatusCode.NotFound, "character_not_found");
        Assert.Equal(50, (await StateAsync(p, c)).Stacks["spirit_herb"]);
    }

    [Fact]
    public async Task Alchemy_rolls_on_the_server_with_insight_and_failures_still_cost_inputs()
    {
        var (p, c) = await CharacterAsync();
        await GiveAsync(c, "spirit_herb", 9);
        var insight = c["stats"]!.AsArray().Single(s => s!.Str("id") == "insight")!.Int("value");
        var chance = Math.Min(1000, 850 + insight * 10);
        _seeds.Enqueue(SeedFor(chance, true, false, true));
        var res = await Ok(await Craft(p, c, "brew_qi_pill", 3));
        Assert.Equal(chance, res.Int("chancePermille"));
        Assert.Equal(2, res.Int("successes"));
        Assert.Equal(2, res.Int("outputQuantity"));
        var (balance, stacks, _) = await StateAsync(p, c);
        Assert.Equal(85, balance);
        Assert.Equal(2, stacks["qi_pill"]);
        Assert.False(stacks.ContainsKey("spirit_herb"));

        var log = await Ok(await p.GetAsync($"/api/v1/characters/{c.Str("id")}/crafting/log"));
        var entry = log["items"]!.AsArray().Single()!;
        var replayed = CraftingRules.Rolls(ulong.Parse(entry.Str("seed"), System.Globalization.CultureInfo.InvariantCulture), 3).Count(r => r < chance);
        Assert.Equal(entry.Int("successes"), replayed, "the logged seed reproduces the outcome");
    }

    [Fact]
    public async Task Upgrades_keep_the_level_on_failure_and_stop_at_the_cap()
    {
        var (p, c) = await CharacterAsync();
        await GiveAsync(c, "refining_stone", 4);
        await InUowAsync((uow, inv) => inv.CreditAsync(uow, Id(c), 1000, "test", "funds", CancellationToken.None));
        var sword = await GiveInstanceAsync(c, "iron_sword");

        var up = await Ok(await Upgrade(p, c, sword.Id, 1));               // +1: 100 %
        Assert.True(up["success"]!.GetValue<bool>());
        Assert.Equal(1, up.Int("level"));
        await Problem(await Upgrade(p, c, sword.Id, 1), HttpStatusCode.Conflict, "version_conflict");

        var at5 = await InUowAsync(async (uow, inv) => (await inv.SetUpgradeLevelAsync(uow, sword.Id, 2, 5, "test", CancellationToken.None)).Value);
        _seeds.Enqueue(SeedFor(900, false));
        var fail = await Ok(await Upgrade(p, c, sword.Id, at5.Version));
        Assert.False(fail["success"]!.GetValue<bool>());
        Assert.Equal(5, fail.Int("level"));
        Assert.Equal(at5.Version, fail.Int("version"));
        Assert.Equal(120, fail.Int("stonesSpent"));

        _seeds.Enqueue(SeedFor(900, true));
        var ok = await Ok(await Upgrade(p, c, sword.Id, at5.Version));
        Assert.Equal(6, ok.Int("level"));
        var (balance, stacks, items) = await StateAsync(p, c);
        Assert.Equal(1100 - 20 - 120 - 120, balance);
        Assert.Equal(1, stacks["refining_stone"]);
        Assert.Equal(6, items.Single()!.Int("upgradeLevel"));
        Assert.Equal(3, items.Single()!["stats"]!.Int("strength"), "2 × (1 + 6 × 10 %) = 3.2 → 3");

        var at15 = await InUowAsync(async (uow, inv) => (await inv.SetUpgradeLevelAsync(uow, sword.Id, ok.Int("version"), 15, "test", CancellationToken.None)).Value);
        await Problem(await Upgrade(p, c, sword.Id, at15.Version), (HttpStatusCode)422, "max_upgrade_level");

        var listed = await InUowAsync(async (uow, inv) => (await inv.SetUpgradeLevelAsync(uow, sword.Id, at15.Version, 3, "test", CancellationToken.None)).Value);
        listed = await InUowAsync(async (uow, inv) => (await inv.SetListedAsync(uow, sword.Id, listed.Version, true, "test", CancellationToken.None)).Value);
        await Problem(await Upgrade(p, c, sword.Id, listed.Version), HttpStatusCode.Conflict, "item_listed");
        Assert.Equal(1, (await StateAsync(p, c)).Stacks["refining_stone"], "a refused upgrade consumes nothing");
    }

    [Fact]
    public async Task Smithing_levels_come_from_crafting_and_unlock_recipes_with_a_discount()
    {
        var (p, c) = await CharacterAsync();
        var url = $"/api/v1/characters/{c.Str("id")}/crafting/professions";
        var profs = (await Ok(await p.GetAsync(url)))["items"]!.AsArray().Select(x => x!.AsObject()).ToDictionary(x => x.Str("id"));
        Assert.Equal(1, profs["smithing"].Int("level"));
        Assert.Equal(1, profs["alchemy"].Int("level"));
        await InUowAsync(async (uow, inv) => (await inv.CreditAsync(uow, Id(c), 1000, "test", Guid.NewGuid().ToString(), CancellationToken.None)).Value);
        await GiveAsync(c, "fire_crystal", 2);
        await GiveAsync(c, "iron_ore", 30);
        await Problem(await Craft(p, c, "forge_fire_ring", 1), HttpStatusCode.Conflict, "profession_level_too_low");

        await Ok(await Craft(p, c, "refine_stone", 5));                            // 5 × 3 XP = 15 = level 2
        profs = (await Ok(await p.GetAsync(url)))["items"]!.AsArray().Select(x => x!.AsObject()).ToDictionary(x => x.Str("id"));
        Assert.Equal(2, profs["smithing"].Int("level"));
        Assert.Equal(15, profs["smithing"].Int("xp"));
        Assert.Equal(20, profs["smithing"].Int("stoneDiscountPermille"));
        Assert.Equal(1, profs["alchemy"].Int("level"));

        var ring = await Ok(await Craft(p, c, "forge_fire_ring", 1));
        Assert.Equal(117, ring.Int("stonesSpent"));                                 // 120 − 2 %
    }
}
