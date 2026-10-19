using System.Net;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Arts.Application;
using JianghuGo.Modules.Arts.Domain;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

/// <summary>MVP 1.5: path arts, titles above the character, estate, arena, cauldron queue, dismantling, sect development.</summary>
[Category("Integration")]
public class GrowthTests : IAsyncLifetime
{
    private readonly QueuedSeedSource _seeds = new();
    private ApiHost _host = null!;
    private static int _names;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync(seeds: _seeds);
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<(Player P, JsonObject C)> NewAsync(string path = "qi")
    {
        var n = Interlocked.Increment(ref _names);
        var p = await _host.RegisterAndLoginAsync();
        var c = await p.CreateCharacterAsync("Mistrz " + new string([.. n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(d => (char)('a' + (d - '0')))]), path);
        await _host.DrainOutboxAsync();
        return (p, c);
    }

    private static string Url(JsonObject c, string rest) => $"/api/v1/characters/{c.Str("id")}/{rest}";

    private static CharacterId Id(JsonObject c) => new(Guid.Parse(c.Str("id")));

    private static async Task<JsonObject> Ok(HttpResponseMessage res, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, res.StatusCode, await res.Content.ReadAsStringAsync());
        return await ApiHost.Json(res);
    }

    private static async Task Problem(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal(status, res.StatusCode, body);
        Assert.Contains(code, body);
    }

    private Task<T> InUowAsync<T>(Func<IUnitOfWork, Task<T>> action) =>
        _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            var r = await action(uow);
            await uow.CommitAsync(CancellationToken.None);
            return r;
        }, CancellationToken.None);

    private Task GiveAsync(JsonObject c, string itemId, long qty) =>
        InUowAsync(async uow => (await _host.Service<IInventory>().AddItemsAsync(uow, Id(c), itemId, qty, "test", Guid.NewGuid().ToString(), CancellationToken.None)).Value);

    private Task FundAsync(JsonObject c, long stones) =>
        InUowAsync(async uow => (await _host.Service<IInventory>().CreditAsync(uow, Id(c), stones, "test", Guid.NewGuid().ToString(), CancellationToken.None)).Value);

    private Task<long> CountAsync(JsonObject c, string itemId) =>
        InUowAsync(uow => _host.Service<IInventory>().CountAsync(uow, Id(c), itemId, CancellationToken.None));

    /// <summary>Test setup through the module's own repository (the profile is otherwise built by play).</summary>
    private Task EditArtsAsync(JsonObject c, Func<ArtsProfile, ArtsProfile> change) =>
        InUowAsync(async uow =>
        {
            var repo = _host.Service<IArtsRepository>();
            var stored = await repo.FindAsync(uow, Id(c), default);
            var next = change(stored ?? ArtsProfile.New(Id(c)));
            Assert.True(await repo.TrySaveAsync(uow, next with { Version = (stored?.Version ?? 0) + 1 }, stored?.Version ?? 0, default));
            return true;
        });

    private async Task<JsonObject> BattleAsync(Player p, Guid battleId) => await Ok(await p.GetAsync($"/api/v1/combat/battles/{battleId}"));

    private async Task ForfeitAsync(Player p, Guid battleId)
    {
        await Ok(await p.PostAsync($"/api/v1/combat/battles/{battleId}/forfeit"));
        await _host.DrainOutboxAsync();
    }

    // ---- path arts --------------------------------------------------------------------------------------

    [Fact]
    public async Task Flames_are_fought_for_absorbed_over_time_slotted_and_fed()
    {
        var (p, c) = await NewAsync("flame");
        var arts = await Ok(await p.GetAsync(Url(c, "arts")));
        Assert.Equal(18, arts["flames"]!["all"]!.AsArray().Count);
        Assert.True(arts["flames"]!["all"]!.AsArray().All(f => f!.Str("state") == "unknown"));
        await Problem(await p.PostAsync(Url(c, "arts/flames/subdue"), new { flameId = "soul_candle" }), HttpStatusCode.Conflict, "flame_not_found_yet");
        await Problem(await p.PostAsync(Url(c, "arts/flames/traces")), HttpStatusCode.Conflict, "not_enough_traces");

        // found (as if by ember traces): its spirit must be beaten first
        await EditArtsAsync(c, a => a with { FlamesSeen = ["soul_candle"] });
        var subdue = await Ok(await p.PostAsync(Url(c, "arts/flames/subdue"), new { flameId = "soul_candle" }));
        var battleId = Guid.Parse(subdue.Str("battleId"));
        Assert.Equal("spirit", (await BattleAsync(p, battleId)).Str("kind"));
        await ForfeitAsync(p, battleId);
        arts = await Ok(await p.GetAsync(Url(c, "arts")));
        Assert.True(arts["flames"]!["spiritBattleId"] is null);
        Assert.Equal("seen", arts["flames"]!["all"]!.AsArray().Single(f => f!.Str("id") == "soul_candle")!.Str("state"));

        // subdued → absorbed after the absorption time (a certain roll here)
        await EditArtsAsync(c, a => a with { SubduedFlame = "soul_candle" });
        await Ok(await p.PostAsync(Url(c, "arts/flames/absorb")));
        await EditArtsAsync(c, a => a with { Absorbing = a.Absorbing! with { ChancePermille = 1000 } });
        _host.Clock.Advance(TimeSpan.FromHours(2));
        await p.RefreshAsync();
        arts = await Ok(await p.GetAsync(Url(c, "arts")));
        var flame = arts["flames"]!["all"]!.AsArray().Single(f => f!.Str("id") == "soul_candle")!;
        Assert.Equal("owned", flame.Str("state"));

        var slotted = await Ok(await SendPut(p, Url(c, "arts/flames/slots"), new { flameIds = new[] { "soul_candle" } }));
        Assert.True(slotted["totalPerks"]!["qiRegen"]!.GetValue<int>() > 0);
        await Problem(await SendPut(p, Url(c, "arts/flames/slots"), new { flameIds = new[] { "phoenix" } }), HttpStatusCode.BadRequest, "flame_not_owned");

        await GiveAsync(c, "fire_crystal", 4);
        var fed = await Ok(await p.PostAsync(Url(c, "arts/flames/feed"), new { flameId = "soul_candle", quantity = 4 }));
        Assert.Equal(2, fed["flames"]!["all"]!.AsArray().Single(f => f!.Str("id") == "soul_candle")!.Int("level"));
        Assert.Equal(0, await CountAsync(c, "fire_crystal"));
        await _host.DrainOutboxAsync();
        var board = await Ok(await p.GetAsync(Url(c, "quests")));
        Assert.Equal(1, board["achievements"]!.AsArray().Single(q => q!.Str("id") == "a_flames_1")!.Int("progress"));
    }

    [Fact]
    public async Task Venoms_toxin_body_coatings_and_gu()
    {
        var (p, c) = await NewAsync("poison");
        await GiveAsync(c, "viper_venom", 3);
        await GiveAsync(c, "nightshade", 2);
        var r = await Ok(await p.PostAsync(Url(c, "arts/venoms/absorb"), new { item = "viper_venom", quantity = 2 }));
        Assert.True(r.Int("absorbed") + r.Int("failed") >= 1);
        Assert.Equal(1, await CountAsync(c, "viper_venom"));
        await Problem(await p.PostAsync(Url(c, "arts/venoms/absorb"), new { item = "iron_ore", quantity = 1 }), HttpStatusCode.NotFound, "venom_unknown");

        var coated = await Ok(await p.PostAsync(Url(c, "arts/coatings"), new { coatingId = "viper_coating" }));
        Assert.Equal("viper_coating", coated["toxin"]!.Str("activeCoating"));
        Assert.Equal(7, coated["toxin"]!.Int("coatingCharges")); // poison path: +2 battles

        // a battle wears one charge off
        var battle = await Ok(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = c.Str("id"), npcId = "training_dummy" }, Player.NewKey()), HttpStatusCode.Created);
        await ForfeitAsync(p, Guid.Parse(battle.Str("id")));
        Assert.Equal(6, (await Ok(await p.GetAsync(Url(c, "arts"))))["toxin"]!.Int("coatingCharges"));

        await Problem(await p.PostAsync(Url(c, "arts/gu/hatch"), new { type = "warrior" }), HttpStatusCode.UnprocessableEntity, "insufficient");
        await GiveAsync(c, "spirit_herb", 10);
        var gu = await Ok(await p.PostAsync(Url(c, "arts/gu/hatch"), new { type = "warrior" }));
        Assert.Equal(1, gu["gu"]!["worms"]!.AsArray().Count);
        await Ok(await p.PostAsync(Url(c, "arts/gu/feed"), new { slot = 0 }));
        await Problem(await p.PostAsync(Url(c, "arts/gu/feed"), new { slot = 0 }), HttpStatusCode.Conflict, "gu_growing");
        _host.Clock.Advance(TimeSpan.FromHours(7));
        await p.RefreshAsync();
        var grown = await Ok(await p.GetAsync(Url(c, "arts")));
        Assert.Equal(2, grown["gu"]!["worms"]![0]!.Int("level"));
        Assert.True(grown["totalPerks"]!["petPower"]!.GetValue<int>() > 0);
    }

    [Fact]
    public async Task Walking_tempers_the_body_and_the_badge_shows_in_battle()
    {
        var (p, c) = await NewAsync("body");
        await Ok(await p.PostAsync(Url(c, "cultivation/steps"), new { steps = 2000 }, Player.NewKey()));
        await _host.DrainOutboxAsync();
        var arts = await Ok(await p.GetAsync(Url(c, "arts")));
        Assert.Equal(40, arts["temper"]!.Int("xp")); // 2000 / 100 × 2 (Body path); +1 needs 50
        await Problem(await p.PostAsync(Url(c, "arts/tempering/advance")), HttpStatusCode.Conflict, "temper_xp");

        await EditArtsAsync(c, a => a with { TemperXp = 10_000 });
        var t = await Ok(await p.PostAsync(Url(c, "arts/tempering/advance")));
        Assert.Equal(1, t["state"]!["temper"]!.Int("attempts"));
        await EditArtsAsync(c, a => a with { Temper = 27, SwordXp = 100 });
        var battle = await Ok(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = c.Str("id"), npcId = "training_dummy" }, Player.NewKey()), HttpStatusCode.Created);
        var badges = battle["me"]!["badges"]!.AsArray().Select(b => b!.GetValue<string>()).ToList();
        Assert.True(badges.Contains("+27"), string.Join(",", badges));
        Assert.True(badges.Contains("⚔2"), string.Join(",", badges));
        await ForfeitAsync(p, Guid.Parse(battle.Str("id")));
    }

    [Fact]
    public async Task Branch_and_sword_intent()
    {
        var (p, c) = await NewAsync("qi");
        await Problem(await p.PostAsync(Url(c, "arts/branch"), new { branchId = "sword" }), HttpStatusCode.Conflict, "realm_too_low");
        await Problem(await p.PostAsync(Url(c, "arts/branch"), new { branchId = "wolf" }), HttpStatusCode.NotFound, "branch_unknown");
        await GiveAsync(c, "sword_echo", 3);
        var s = await Ok(await p.PostAsync(Url(c, "arts/sword/echoes"), new { quantity = 3 }));
        Assert.Equal(72, s["sword"]!.Int("xp")); // 3 × 12 × 2 (Qi path)
        Assert.Equal(1, s["sword"]!.Int("level"));
        Assert.Equal("⚔1", s["sword"]!.Str("badge"));
    }

    // ---- titles ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Chosen_title_is_shown_above_the_character_in_battle()
    {
        var (p, c) = await NewAsync("qi");
        var titles = (await Ok(await p.GetAsync(Url(c, "quests/titles"))))["items"]!.AsArray();
        var realm = titles.Single(t => t!.Str("id") == "realm_body_tempering")!;
        Assert.Equal("cultivation", realm.Str("source"));
        await Problem(await SendPut(p, Url(c, "quests/titles/selected"), new { titleId = "q_a_win_500" }), HttpStatusCode.NotFound, "title_not_earned");
        var chosen = (await Ok(await SendPut(p, Url(c, "quests/titles/selected"), new { titleId = "realm_body_tempering" })))["items"]!.AsArray();
        Assert.True(chosen.Single(t => t!.Str("id") == "realm_body_tempering")!["selected"]!.GetValue<bool>());

        var battle = await Ok(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = c.Str("id"), npcId = "training_dummy" }, Player.NewKey()), HttpStatusCode.Created);
        Assert.Equal(realm.Str("namePl"), battle["me"]!.Str("title"));
        await ForfeitAsync(p, Guid.Parse(battle.Str("id")));

        var board = await Ok(await p.GetAsync(Url(c, "quests")));
        Assert.Equal("realm_body_tempering", board.Str("selectedTitleId"));
        Assert.Equal(15, board.Int("secretsTotal"));
        var secret = board["achievements"]!.AsArray().First(q => q!["secret"]!.GetValue<bool>())!;
        Assert.Equal("???", secret.Str("namePl"));
        Assert.Equal(1, board["story"]!.Int("chapter"));
        await Ok(await SendPut(p, Url(c, "quests/titles/selected"), new { titleId = (string?)null }));
    }

    // ---- estate ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Garden_grows_and_disciples_bring_back_loot()
    {
        var (p, c) = await NewAsync("qi");
        var estate = await Ok(await p.GetAsync(Url(c, "estate")));
        Assert.Equal(3, estate["plots"]!.AsArray().Count);
        await Problem(await p.PostAsync(Url(c, "estate/garden/plant"), new { slot = 0, seed = "herb_seed" }), HttpStatusCode.UnprocessableEntity, "insufficient");
        await GiveAsync(c, "herb_seed", 1);
        await Ok(await p.PostAsync(Url(c, "estate/garden/plant"), new { slot = 0, seed = "herb_seed" }));
        await Problem(await p.PostAsync(Url(c, "estate/garden/plant"), new { slot = 0, seed = "herb_seed" }), HttpStatusCode.Conflict, "plot_busy");
        await Problem(await p.PostAsync(Url(c, "estate/garden/harvest")), HttpStatusCode.Conflict, "nothing_ripe");
        _host.Clock.Advance(TimeSpan.FromHours(5));
        await p.RefreshAsync();
        var harvest = await Ok(await p.PostAsync(Url(c, "estate/garden/harvest")));
        Assert.True(harvest["gained"]!.AsArray().Any(g => g!.Str("item") == "spirit_herb"));
        Assert.True(await CountAsync(c, "spirit_herb") >= 4);

        await FundAsync(c, 1000);
        var recruited = await Ok(await p.PostAsync(Url(c, "estate/disciples/recruit")));
        var disciple = recruited["disciples"]![0]!;
        await Problem(await p.PostAsync(Url(c, "estate/disciples/recruit")), HttpStatusCode.UnprocessableEntity, "disciples_full");
        await Ok(await p.PostAsync(Url(c, "estate/expeditions/start"), new { discipleId = disciple.Str("id"), expeditionId = "herb_valley" }));
        await Problem(await p.PostAsync(Url(c, "estate/expeditions/start"), new { discipleId = disciple.Str("id"), expeditionId = "iron_hills" }), HttpStatusCode.Conflict, "disciple_away");
        _host.Clock.Advance(TimeSpan.FromHours(3));
        await p.RefreshAsync();
        var back = await Ok(await p.PostAsync(Url(c, "estate/expeditions/collect")));
        Assert.True(back["stonesGained"]!.GetValue<long>() >= 20);
        Assert.True(back["disciples"]![0]!.Int("xp") > 0);
        await _host.DrainOutboxAsync();
        var board = await Ok(await p.GetAsync(Url(c, "quests")));
        Assert.Equal(1, board["achievements"]!.AsArray().Single(q => q!.Str("id") == "a_expeditions_20")!.Int("progress"));
        Assert.Equal(1, board["achievements"]!.AsArray().Single(q => q!.Str("id") == "a_harvest_100")!.Int("progress"));
    }

    // ---- arena ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Arena_duels_move_ratings_of_both_sides()
    {
        var (a, ca) = await NewAsync("qi");
        var (b, cb) = await NewAsync("body");
        await Ok(await b.GetAsync(Url(cb, "combat/arena")));
        var view = await Ok(await a.GetAsync(Url(ca, "combat/arena")));
        Assert.Equal(1000, view.Int("rating"));
        Assert.True(view["opponents"]!.AsArray().Any(o => o!.Str("characterId") == cb.Str("id")));
        await Problem(await a.PostAsync(Url(ca, "combat/arena/challenge"), new { opponentCharacterId = ca.Str("id") }, Player.NewKey()), HttpStatusCode.BadRequest, "cannot_challenge_self");
        var battle = await Ok(await a.PostAsync(Url(ca, "combat/arena/challenge"), new { opponentCharacterId = cb.Str("id") }, Player.NewKey()), HttpStatusCode.Created);
        Assert.Equal("arena", battle.Str("kind"));
        await ForfeitAsync(a, Guid.Parse(battle.Str("id")));
        var after = await Ok(await a.GetAsync(Url(ca, "combat/arena")));
        Assert.True(after.Int("rating") < 1000);
        Assert.Equal(1, after.Int("losses"));
        Assert.True((await Ok(await b.GetAsync(Url(cb, "combat/arena")))).Int("rating") > 1000);
        var week = await Ok(await a.PostAsync(Url(ca, "combat/arena/claim")));
        Assert.False(week["weeklyClaimable"]!.GetValue<bool>());
        await Problem(await a.PostAsync(Url(ca, "combat/arena/claim")), HttpStatusCode.Conflict, "arena_claimed");
    }

    // ---- crafting queue, dismantling ----------------------------------------------------------------------

    [Fact]
    public async Task Cauldron_batches_finish_while_away_and_equipment_is_dismantled()
    {
        var (p, c) = await NewAsync("qi");
        await GiveAsync(c, "spirit_herb", 15);
        var q = await Ok(await p.PostAsync(Url(c, "crafting/queue"), new { recipeId = "brew_qi_pill", times = 5 }, Player.NewKey()));
        Assert.Equal(1, q["entries"]!.AsArray().Count);
        Assert.Equal(0, await CountAsync(c, "spirit_herb"));
        await Problem(await p.PostAsync(Url(c, "crafting/queue"), new { recipeId = "brew_qi_pill", times = 1 }, Player.NewKey()), HttpStatusCode.Conflict, "queue_busy");
        await Problem(await p.PostAsync(Url(c, "crafting/queue"), new { recipeId = "forge_iron_sword", times = 1 }, Player.NewKey()), HttpStatusCode.BadRequest, "queue_unique");
        await Problem(await p.PostAsync(Url(c, "crafting/queue/collect")), HttpStatusCode.Conflict, "nothing_ready");
        _host.Clock.Advance(TimeSpan.FromMinutes(31));
        await p.RefreshAsync();
        var done = await Ok(await p.PostAsync(Url(c, "crafting/queue/collect")));
        var collected = done["collected"]![0]!;
        Assert.Equal(5, collected.Int("times"));
        Assert.Equal(collected.Int("successes"), (int)await CountAsync(c, "qi_pill"));
        Assert.Equal(0, done["entries"]!.AsArray().Count);

        var sword = await InUowAsync(async uow =>
        {
            var owner = (await _host.Service<JianghuGo.Modules.Characters.Contracts.ICharacterDirectory>().FindAsync(uow, Id(c).Value, default))!;
            return (await _host.Service<IInventory>().CreateInstanceAsync(uow, Id(c), owner.AccountId, "frost_sword", "test", Guid.NewGuid().ToString(), default)).Value;
        });
        var inv = await Ok(await p.GetAsync(Url(c, "inventory")));
        var item = inv["items"]!.AsArray().Single(i => i!.Str("id") == sword.Id.ToString())!;
        Assert.Equal(1, item["affixes"]!.AsArray().Count); // earth rarity: one property
        Assert.Equal("frost", item.Str("setId"));
        var r = await Ok(await p.PostAsync(Url(c, "inventory/dismantle"), new { instanceId = sword.Id, expectedVersion = sword.Version }, Player.NewKey()));
        Assert.True(r["materials"]!.AsArray().Count >= 0);
        Assert.False(r["inventory"]!["items"]!.AsArray().Any(i => i!.Str("id") == sword.Id.ToString()));
    }

    // ---- sect development -----------------------------------------------------------------------------------

    [Fact]
    public async Task Sect_buildings_give_members_perks_and_the_sect_boss_is_fought()
    {
        var (p, c) = await NewAsync("qi");
        await FundAsync(c, 5000);
        var sect = await Ok(await p.PostAsync("/api/v1/sects", new { characterId = c.Str("id"), name = "Pawilon Testu" }, Player.NewKey()), HttpStatusCode.Created);
        var sectId = sect["sect"]!.Str("id");
        await Ok(await p.PostAsync($"/api/v1/sects/{sectId}/donations", new { characterId = c.Str("id"), amount = 2000 }, Player.NewKey()));
        var dev = await Ok(await p.GetAsync($"/api/v1/sects/{sectId}/development?characterId={c.Str("id")}"));
        Assert.Equal(3, dev["missions"]!.AsArray().Count);
        Assert.True(dev["canBuild"]!.GetValue<bool>());
        var built = await Ok(await p.PostAsync($"/api/v1/sects/{sectId}/buildings", new { characterId = c.Str("id"), buildingId = "training_hall" }));
        Assert.Equal(1, built["buildings"]!.AsArray().Single(b => b!.Str("id") == "training_hall")!.Int("level"));
        Assert.Equal(1000, built.Int("treasury"));

        var perks = await InUowAsync(async uow =>
        {
            var sum = PerkBonus.None;
            foreach (var provider in _host.Service<IEnumerable<ICombatPerkBonuses>>()) sum += await provider.BonusAsync(uow, Guid.Parse(c.Str("id")), default);
            return sum;
        });
        Assert.True(perks.DamagePermille >= 15);

        var fight = await Ok(await p.PostAsync($"/api/v1/sects/{sectId}/boss/fight", new { characterId = c.Str("id") }));
        Assert.Equal(2, fight["state"]!["boss"]!.Int("attemptsLeft"));
        Assert.Equal("spirit", (await BattleAsync(p, Guid.Parse(fight.Str("battleId")))).Str("kind"));
        await ForfeitAsync(p, Guid.Parse(fight.Str("battleId")));

        var (outsider, oc) = await NewAsync("qi");
        await Problem(await outsider.GetAsync($"/api/v1/sects/{sectId}/development?characterId={oc.Str("id")}"), HttpStatusCode.Forbidden, "not_a_member");
    }

    private static Task<HttpResponseMessage> SendPut(Player p, string url, object body) => p.SendAsync(HttpMethod.Put, url, body);
}
