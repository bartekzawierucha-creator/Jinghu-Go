using System.Net;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Inventory.Application;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Integration")]
public class InventoryTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static int _names;

    private static string NewName()
    {
        var n = Interlocked.Increment(ref _names);
        return "Kupiec " + new string([.. n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(d => (char)('a' + (d - '0')))]);
    }

    private async Task<(Player P, JsonObject C)> CharacterAsync(string path = "qi")
    {
        var p = await _host.RegisterAndLoginAsync();
        return (p, await p.CreateCharacterAsync(NewName(), path));
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

    private static string Inv(JsonObject c) => $"/api/v1/characters/{c.Str("id")}/inventory";

    /// <summary>Test arrangement through the module's own contract (what Crafting/Market will call).</summary>
    private Task<T> InUowAsync<T>(Func<IUnitOfWork, IInventory, Task<T>> action) =>
        _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            var r = await action(uow, _host.Service<IInventory>());
            await uow.CommitAsync(CancellationToken.None);
            return r;
        }, CancellationToken.None);

    private Task GiveAsync(JsonObject c, string itemId, long qty) =>
        InUowAsync(async (uow, inv) =>
        {
            var r = await inv.AddItemsAsync(uow, new CharacterId(Guid.Parse(c.Str("id"))), itemId, qty, "test", Guid.NewGuid().ToString(), CancellationToken.None);
            Assert.True(r.IsSuccess, r.Error?.Code);
            return r.Value;
        });

    private Task<InstanceInfo> GiveInstanceAsync(Player p, JsonObject c, string itemId) =>
        InUowAsync(async (uow, inv) =>
        {
            var owner = await _host.Service<JianghuGo.Modules.Characters.Contracts.ICharacterDirectory>().FindAsync(uow, Guid.Parse(c.Str("id")), CancellationToken.None);
            var r = await inv.CreateInstanceAsync(uow, owner!.Id, owner.AccountId, itemId,
                "test", Guid.NewGuid().ToString(), CancellationToken.None);
            Assert.True(r.IsSuccess, r.Error?.Code);
            return r.Value;
        });

    private Task<(int, int)> MismatchesAsync() =>
        _host.Service<IUnitOfWorkFactory>().RunAsync(uow => _host.Service<IInventoryRepository>().CountMismatchesAsync(uow, CancellationToken.None), CancellationToken.None);

    private static async Task<JsonObject> FightAsync(Player p, JsonObject battle)
    {
        for (var i = 0; i < 60 && battle.Str("status") == "active"; i++)
        {
            var attack = battle["mySlots"]!.AsArray().Select(s => s!.AsObject())
                .Where(s => s["unavailable"] is null && s.Str("kind") == "attack").OrderByDescending(s => s.Int("qiCost")).FirstOrDefault();
            battle = await Ok(await p.PostAsync($"/api/v1/combat/battles/{battle.Str("id")}/actions",
                new { turn = battle.Int("turn"), slot = attack?.Int("slot"), direction = (string?)null }, Player.NewKey()));
        }

        Assert.Equal("finished", battle.Str("status"));
        return battle;
    }

    [Fact]
    public async Task New_character_starts_with_an_empty_bag_and_the_initial_purse()
    {
        var (p, c) = await CharacterAsync();
        var inv = await Ok(await p.GetAsync(Inv(c)));
        Assert.Equal(100, inv.Int("balance"));
        Assert.Equal(60, inv.Int("capacity"));
        Assert.Equal(0, inv.Int("slotsUsed"));
        Assert.Equal(0, inv["stacks"]!.AsArray().Count);

        var catalog = await Ok(await p.GetAsync("/api/v1/items"));
        Assert.Equal("items-1", catalog.Str("rulesVersion"));
        Assert.True(catalog["items"]!.AsArray().Any(i => i!.Str("id") == "spatial_ring" && i!["bagBonus"]!.GetValue<int>() == 20));
    }

    [Fact]
    public async Task Other_players_inventory_is_not_reachable()
    {
        var (_, c) = await CharacterAsync();
        var (intruder, _) = await CharacterAsync();
        await Problem(await intruder.GetAsync(Inv(c)), HttpStatusCode.NotFound, "character_not_found");
        await Problem(await intruder.GetAsync(Inv(c) + "/wallet"), HttpStatusCode.NotFound, "character_not_found");
        await Problem(await intruder.PostAsync(Inv(c) + "/use", new { itemId = "qi_pill", quantity = 1 }, Player.NewKey()), HttpStatusCode.NotFound, "character_not_found");
    }

    [Fact]
    public async Task Pills_grant_qi_exactly_once_and_invalid_quantities_are_rejected()
    {
        var (p, c) = await CharacterAsync();
        await GiveAsync(c, "qi_pill", 5);
        await _host.DrainOutboxAsync(); // welcome notification etc.
        var use = Inv(c) + "/use";

        foreach (var q in new[] { 0, -1, 11 })
            await Problem(await p.PostAsync(use, new { itemId = "qi_pill", quantity = q }, Player.NewKey()), HttpStatusCode.BadRequest, "quantity_invalid");
        await Problem(await p.PostAsync(use, new { itemId = "iron_ore", quantity = 1 }, Player.NewKey()), HttpStatusCode.BadRequest, "item_not_consumable");
        await Problem(await p.PostAsync(use, new { itemId = "qi_pill", quantity = 1 }), HttpStatusCode.BadRequest, "idempotency_key_invalid");

        var key = Player.NewKey();
        var first = await Ok(await p.PostAsync(use, new { itemId = "qi_pill", quantity = 2 }, key));
        Assert.Equal(120, first.Int("qiGranted"));
        Assert.Equal(3, first.Int("remaining"));
        var replay = await Ok(await p.PostAsync(use, new { itemId = "qi_pill", quantity = 2 }, key));
        Assert.Equal(first.Compact(), replay.Compact());

        Assert.Equal(1, await _host.DrainOutboxAsync());
        Assert.Equal(0, await _host.DrainOutboxAsync());
        var cultivation = await Ok(await p.GetAsync($"/api/v1/characters/{c.Str("id")}/cultivation"));
        Assert.Equal(120, cultivation.Int("qi"));
        var inv = await Ok(await p.GetAsync(Inv(c)));
        Assert.Equal(3, inv["stacks"]!.AsArray().Single(s => s!.Str("itemId") == "qi_pill").Int("quantity"));
    }

    [Fact]
    public async Task Failed_use_changes_nothing()
    {
        var (p, c) = await CharacterAsync();
        await GiveAsync(c, "qi_pill", 1);
        await _host.DrainOutboxAsync(); // welcome notification etc.
        await Problem(await p.PostAsync(Inv(c) + "/use", new { itemId = "qi_pill", quantity = 2 }, Player.NewKey()), (HttpStatusCode)422, "insufficient_items");
        Assert.Equal(0, await _host.DrainOutboxAsync(), "no event for a failed use");
        var inv = await Ok(await p.GetAsync(Inv(c)));
        Assert.Equal(1, inv["stacks"]!.AsArray().Single()!.Int("quantity"));
    }

    [Fact]
    public async Task Equipment_swaps_per_slot_adds_bonuses_and_the_ring_enlarges_the_bag()
    {
        var (p, c) = await CharacterAsync();
        var sword = await GiveInstanceAsync(p, c, "iron_sword");
        var dagger = await GiveInstanceAsync(p, c, "wolf_fang_dagger");
        var ring = await GiveInstanceAsync(p, c, "spatial_ring");

        await Problem(await p.PostAsync(Inv(c) + "/equip", new { instanceId = sword.Id, expectedVersion = 7 }), HttpStatusCode.Conflict, "version_conflict");
        var inv = await Ok(await p.PostAsync(Inv(c) + "/equip", new { instanceId = sword.Id, expectedVersion = 1 }));
        Assert.Equal(2, inv["bonuses"]!.Int("strength"));
        Assert.Equal(sword.Id.ToString(), inv["equipped"]!.Str("weapon"));
        Assert.Equal(2, inv.Int("slotsUsed"));

        inv = await Ok(await p.PostAsync(Inv(c) + "/equip", new { instanceId = dagger.Id, expectedVersion = 1 }));
        Assert.Equal(dagger.Id.ToString(), inv["equipped"]!.Str("weapon"));
        Assert.Equal(1, inv["bonuses"]!.Int("strength"));
        Assert.Equal(3, inv["bonuses"]!.Int("agility"));
        Assert.Equal("bag", inv["items"]!.AsArray().Single(i => i!.Str("id") == sword.Id.ToString())!.Str("state"));

        inv = await Ok(await p.PostAsync(Inv(c) + "/equip", new { instanceId = ring.Id, expectedVersion = 1 }));
        Assert.Equal(80, inv.Int("capacity"));
        inv = await Ok(await p.PostAsync(Inv(c) + "/unequip", new { instanceId = ring.Id, expectedVersion = 2 }));
        Assert.Equal(60, inv.Int("capacity"));

        var history = await Ok(await p.GetAsync($"{Inv(c)}/items/{dagger.Id}/history"));
        Assert.Equal("created,equipped", string.Join(",", history["items"]!.AsArray().Select(e => e!.Str("kind"))));

        var (other, oc) = await CharacterAsync();
        await Problem(await other.PostAsync(Inv(oc) + "/equip", new { instanceId = sword.Id, expectedVersion = 2 }), HttpStatusCode.NotFound, "item_not_found");
        await Problem(await other.GetAsync($"{Inv(oc)}/items/{sword.Id}/history"), HttpStatusCode.NotFound, "item_not_found");
    }

    [Fact]
    public async Task Equipped_gear_is_frozen_into_the_battle_snapshot()
    {
        var (p, c) = await CharacterAsync();
        var start = () => p.PostAsync("/api/v1/combat/battles/pve", new { characterId = c.Str("id"), npcId = "training_dummy" }, Player.NewKey());
        var before = await Ok(await start(), HttpStatusCode.Created);
        var hp = before["me"]!.Int("maxHp");
        await FightAsync(p, before);

        var robe = await GiveInstanceAsync(p, c, "herb_silk_robe");
        await Ok(await p.PostAsync(Inv(c) + "/equip", new { instanceId = robe.Id, expectedVersion = 1 }));
        var after = await Ok(await start(), HttpStatusCode.Created);
        Assert.True(after["me"]!.Int("maxHp") > hp, $"constitution +2 from the robe raises max HP ({hp} → {after["me"]!.Int("maxHp")})");
    }

    [Fact]
    public async Task Bag_capacity_blocks_new_slots_but_not_existing_stacks()
    {
        var (p, c) = await CharacterAsync();
        for (var i = 0; i < 59; i++) await GiveInstanceAsync(p, c, "iron_sword");
        await GiveAsync(c, "iron_ore", 3);                         // slot 60
        await GiveAsync(c, "iron_ore", 3);                         // same stack: fine
        var full = await InUowAsync((uow, inv) => inv.AddItemsAsync(uow, new CharacterId(Guid.Parse(c.Str("id"))), "jade", 1, "test", "x", CancellationToken.None));
        Assert.Equal("bag_full", full.Error?.Code);
        var tooMany = await InUowAsync((uow, inv) => inv.AddItemsAsync(uow, new CharacterId(Guid.Parse(c.Str("id"))), "iron_ore", 994, "test", "y", CancellationToken.None));
        Assert.Equal("stack_full", tooMany.Error?.Code);
        var negative = await InUowAsync((uow, inv) => inv.AddItemsAsync(uow, new CharacterId(Guid.Parse(c.Str("id"))), "iron_ore", -5, "test", "z", CancellationToken.None));
        Assert.Equal("quantity_invalid", negative.Error?.Code);

        var inv = await Ok(await p.PostAsync(Inv(c) + "/discard", new { itemId = "iron_ore", quantity = 6 }, Player.NewKey()));
        Assert.Equal(59, inv.Int("slotsUsed"));
        var sword = inv["items"]!.AsArray()[0]!;
        await Ok(await p.PostAsync(Inv(c) + "/equip", new { instanceId = sword.Str("id"), expectedVersion = 1 }));
        await Problem(await p.PostAsync(Inv(c) + "/discard", new { instanceId = sword.Str("id"), quantity = 1 }, Player.NewKey()), HttpStatusCode.Conflict, "item_not_in_bag");
        var other = inv["items"]!.AsArray()[1]!;
        inv = await Ok(await p.PostAsync(Inv(c) + "/discard", new { instanceId = other.Str("id"), quantity = 1 }, Player.NewKey()));
        Assert.Equal(57, inv.Int("slotsUsed"));
    }

    [Fact]
    public async Task Pve_loot_arrives_once_through_the_outbox_and_the_ledger_reconciles()
    {
        var (p, c) = await CharacterAsync();
        var battle = await FightAsync(p, await Ok(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = c.Str("id"), npcId = "training_dummy" }, Player.NewKey()), HttpStatusCode.Created));

        Assert.Equal("won", battle.Str("result"));
        Assert.Equal(100, (await Ok(await p.GetAsync(Inv(c)))).Int("balance"));
        await _host.DrainOutboxAsync();
        Assert.Equal(0, await _host.DrainOutboxAsync());
        Assert.Equal(102, (await Ok(await p.GetAsync(Inv(c)))).Int("balance"));

        var wallet = await Ok(await p.GetAsync(Inv(c) + "/wallet"));
        Assert.Equal("loot,initial_grant", string.Join(",", wallet["items"]!.AsArray().Select(e => e!.Str("reason"))));
        var page2 = await Ok(await p.GetAsync($"{Inv(c)}/wallet?limit=1&before={wallet["items"]![0]!.Str("id")}"));
        Assert.Equal("initial_grant", page2["items"]![0]!.Str("reason"));

        Assert.Equal((0, 0), await MismatchesAsync());
    }

    [Fact]
    public async Task Ledger_lines_cannot_be_applied_twice()
    {
        var (_, c) = await CharacterAsync();
        var id = new CharacterId(Guid.Parse(c.Str("id")));
        await InUowAsync((uow, inv) => inv.CreditAsync(uow, id, 10, "test", "same-ref", CancellationToken.None));
        await Assert.ThrowsAsync<DuplicateKeyException>(() =>
            InUowAsync((uow, inv) => inv.CreditAsync(uow, id, 10, "test", "same-ref", CancellationToken.None)));
        Assert.Equal(110L, await InUowAsync((uow, inv) => inv.BalanceAsync(uow, id, CancellationToken.None)));
        var overdraw = await InUowAsync((uow, inv) => inv.DebitAsync(uow, id, 111, "test", "big", CancellationToken.None));
        Assert.Equal("insufficient_funds", overdraw.Error?.Code);
        var zero = await InUowAsync((uow, inv) => inv.DebitAsync(uow, id, 0, "test", "zero", CancellationToken.None));
        Assert.Equal("amount_invalid", zero.Error?.Code);
        Assert.Equal((0, 0), await MismatchesAsync());
    }
}
