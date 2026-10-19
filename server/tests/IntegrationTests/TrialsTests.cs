using System.Net;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Combat.Application;
using JianghuGo.Modules.Combat.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

/// <summary>MVP 1.4: telegraphed NPC intents, the Tower of Trials and the shared world boss.</summary>
[Category("Integration")]
public class TrialsTests : IAsyncLifetime
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
        return (p, await p.CreateCharacterAsync("Trial " + new string([.. n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(d => (char)('a' + (d - '0')))]), path));
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
        Assert.Contains(code, body);
    }

    private static async Task<JsonObject> AutoToEndAsync(Player p, JsonObject battle)
    {
        for (var i = 0; i < 60 && battle.Str("status") == "active"; i++)
            battle = await Ok(await p.PostAsync($"/api/v1/combat/battles/{battle.Str("id")}/auto", null, Player.NewKey()));
        Assert.Equal("finished", battle.Str("status"), battle.Compact());
        return battle;
    }

    private async Task<(long Balance, Dictionary<string, long> Stacks)> InventoryAsync(Player p, JsonObject c)
    {
        await _host.DrainOutboxAsync();
        var inv = await Ok(await p.GetAsync($"/api/v1/characters/{c.Str("id")}/inventory"));
        return (inv["balance"]!.GetValue<long>(), inv["stacks"]!.AsArray().ToDictionary(s => s!.Str("itemId"), s => s!["quantity"]!.GetValue<long>()));
    }

    private Task InUowAsync(Func<IUnitOfWork, ICombatRepository, Task> work) =>
        _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            await work(uow, _host.Service<ICombatRepository>());
            await uow.CommitAsync(default);
        }, default);

    [Fact]
    public async Task Npc_intent_is_telegraphed_and_is_exactly_what_the_npc_does()
    {
        var (p, c) = await NewAsync("body");
        var battle = await Ok(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = c.Str("id"), npcId = "mountain_bandit" }, Player.NewKey()), HttpStatusCode.Created);
        var seen = new HashSet<string>();
        for (var turn = 0; turn < 30 && battle.Str("status") == "active"; turn++)
        {
            var preview = battle["opponentIntent"]!.AsObject();
            var action = preview.Str("action");
            seen.Add(action);
            var again = await Ok(await p.GetAsync($"/api/v1/combat/battles/{battle.Str("id")}"));
            Assert.Equal(preview.Compact(), again["opponentIntent"]!.AsObject().Compact()); // stable until the turn resolves

            battle = await Ok(await p.PostAsync($"/api/v1/combat/battles/{battle.Str("id")}/actions", new { turn = battle.Int("turn"), slot = (int?)null }, Player.NewKey()));
            var npc = battle["lastTurnEvents"]!.AsArray().Select(e => e!.AsObject()).Where(e => e.Str("actor") == "b" && e.Str("action") is not ("bleed" or "time_limit")).ToList();
            if (npc.Count == 0) continue; // the fight ended before the NPC acted
            var expected = action switch { "counter" => "counter_stance", "defend" => "defend", _ => action };
            Assert.Equal(expected, npc[0].Str("action"), battle["lastTurnEvents"]!.ToJsonString());
            if (preview["techniqueId"] is { } tech) Assert.Equal(tech.GetValue<string>(), npc[0].Str("techniqueId"));
        }

        Assert.True(seen.Count >= 2, "the bandit varies its moves: " + string.Join(",", seen));
        if (battle.Str("status") == "finished") Assert.Null(battle["opponentIntent"]);
    }

    [Fact]
    public async Task Tower_is_climbed_floor_by_floor_with_first_clear_rewards_and_a_ranking()
    {
        var (p, c) = await NewAsync();
        var id = c.Str("id");
        var tower = await Ok(await p.GetAsync($"/api/v1/characters/{id}/combat/tower"));
        Assert.Equal(0, tower.Int("bestFloor"));
        Assert.Equal(330, tower.Int("floors"));
        Assert.Equal(1, tower["next"]!.AsObject().Int("floor"));
        Assert.Equal("training_dummy", tower["next"]!.AsObject().Str("npcId"));
        Assert.Equal(5, tower["nextMilestone"]!.AsObject().Int("floor"));
        Assert.Null(tower["me"]);

        var battle = await Ok(await p.PostAsync($"/api/v1/characters/{id}/combat/tower/start", null, Player.NewKey()), HttpStatusCode.Created);
        Assert.Equal("tower", battle.Str("kind"));
        Assert.Equal(1, battle.Int("towerFloor"));
        await Problem(await p.PostAsync($"/api/v1/characters/{id}/combat/tower/start", null, Player.NewKey()), HttpStatusCode.Conflict, "already_in_battle");
        battle = await AutoToEndAsync(p, battle);
        Assert.Equal("won", battle.Str("result"), battle.Compact());
        Assert.Equal(20, battle.Int("rewardQi"));

        tower = await Ok(await p.GetAsync($"/api/v1/characters/{id}/combat/tower"));
        Assert.Equal(1, tower.Int("bestFloor"));
        Assert.Equal(2, tower["next"]!.AsObject().Int("floor"));
        Assert.Equal(1, tower["me"]!.AsObject().Int("value"));
        Assert.True(tower["top"]!.AsArray().Any(r => r!.Str("characterId") == id));

        // jump to floor 4, clear 5 (a milestone): Qi 5 × 20, 100 stones and 2 refining stones, exactly once
        await InUowAsync(async (uow, repo) =>
        {
            var cid = new CharacterId(Guid.Parse(id));
            var mine = (await repo.FindTowerAsync(uow, cid, default))!;
            Assert.True(await repo.TrySaveTowerAsync(uow, mine with { BestFloor = 4, Version = mine.Version + 1 }, mine.Version, default));
        });
        var (stonesBefore, stacksBefore) = await InventoryAsync(p, c);
        battle = await AutoToEndAsync(p, await Ok(await p.PostAsync($"/api/v1/characters/{id}/combat/tower/start", null, Player.NewKey()), HttpStatusCode.Created));
        Assert.Equal(5, battle.Int("towerFloor"));
        Assert.Equal("won", battle.Str("result"), battle.Compact());
        Assert.Equal(100, battle.Int("rewardQi"));
        var (stonesAfter, stacksAfter) = await InventoryAsync(p, c);
        Assert.Equal(stonesBefore + 100, stonesAfter);
        Assert.Equal(stacksBefore.GetValueOrDefault("refining_stone") + 2, stacksAfter.GetValueOrDefault("refining_stone"));
        Assert.Equal(6, (await Ok(await p.GetAsync($"/api/v1/characters/{id}/combat/tower")))["next"]!.AsObject().Int("floor"));

        // a cleared tower cannot be entered again
        await InUowAsync(async (uow, repo) =>
        {
            var cid = new CharacterId(Guid.Parse(id));
            var mine = (await repo.FindTowerAsync(uow, cid, default))!;
            Assert.True(await repo.TrySaveTowerAsync(uow, mine with { BestFloor = 330, Version = mine.Version + 1 }, mine.Version, default));
        });
        await Problem(await p.PostAsync($"/api/v1/characters/{id}/combat/tower/start", null, Player.NewKey()), HttpStatusCode.Conflict, "tower_completed");
        Assert.Null((await Ok(await p.GetAsync($"/api/v1/characters/{id}/combat/tower")))["next"]);
    }

    [Fact]
    public async Task World_boss_shares_one_pool_limits_attempts_and_pays_the_kill_reward_once()
    {
        var (p, c) = await NewAsync();
        var id = c.Str("id");
        var url = $"/api/v1/characters/{id}/combat/world-boss";
        var boss = await Ok(await p.GetAsync(url));
        var pool = boss.Int("poolHp");
        Assert.Equal(pool, boss.Int("hpLeft"));
        Assert.Equal(3, boss.Int("attemptsLeft"));
        Assert.Equal("storm_qilin", boss.Str("npcId"));
        await Problem(await p.PostAsync(url + "/claim", new { }), HttpStatusCode.Conflict, "world_boss_alive");
        await Problem(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = id, npcId = "storm_qilin" }, Player.NewKey()), HttpStatusCode.BadRequest, "npc_world_only");

        var battle = await Ok(await p.PostAsync(url + "/start", null, Player.NewKey()), HttpStatusCode.Created);
        Assert.Equal("boss", battle.Str("kind"));
        Assert.Equal(2500 * 10 / 40, battle["opponent"]!.AsObject().Int("maxHp"));   // a fresh cultivator fights the boss at realm 0 (budget 10 vs 40 at home)
        battle = await AutoToEndAsync(p, battle);
        Assert.True(battle.Int("damageDealt") > 0, battle.Compact());
        // damage counts on the pool scale: a realm-0 cultivator's phase had a quarter of the HP, so each point is worth four
        var dealt = (await Ok(await p.GetAsync(url))).Int("myDamage");
        Assert.Equal(Math.Min(2500, battle.Int("damageDealt") * 40 / 10), dealt);
        Assert.Equal(Math.Min(200, dealt * 8 / 100), battle.Int("rewardQi"));

        // a second player hits the same pool
        var (q, d) = await NewAsync("flame");
        var other = await AutoToEndAsync(q, await Ok(await q.PostAsync($"/api/v1/characters/{d.Str("id")}/combat/world-boss/start", null, Player.NewKey()), HttpStatusCode.Created));
        boss = await Ok(await p.GetAsync(url));
        Assert.Equal(pool - dealt - Math.Min(2500, other.Int("damageDealt") * 40 / 10), boss.Int("hpLeft"));
        Assert.Equal(dealt, boss.Int("myDamage"));
        Assert.Equal(2, boss.Int("attemptsLeft"));
        Assert.Equal(2, boss["top"]!.AsArray().Count);

        await Ok(await p.PostAsync($"/api/v1/combat/battles/{(await Ok(await p.PostAsync(url + "/start", null, Player.NewKey()), HttpStatusCode.Created)).Str("id")}/forfeit"));
        await Ok(await p.PostAsync($"/api/v1/combat/battles/{(await Ok(await p.PostAsync(url + "/start", null, Player.NewKey()), HttpStatusCode.Created)).Str("id")}/forfeit"));
        await Problem(await p.PostAsync(url + "/start", null, Player.NewKey()), HttpStatusCode.UnprocessableEntity, "world_boss_attempts");
        Assert.Equal(0, (await Ok(await p.GetAsync(url))).Int("attemptsLeft"));

        // next day: attempts are back; leave the boss 1 HP and finish it
        _host.Clock.Advance(TimeSpan.FromDays(1));
        await p.RefreshAsync();
        await q.RefreshAsync();
        var cycle = (await Ok(await p.GetAsync(url))).Int("cycle");
        await InUowAsync(async (uow, repo) =>
        {
            var b = (await repo.FindBossAsync(uow, cycle, default))!;
            Assert.True(await repo.TrySaveBossAsync(uow, b with { HpLeft = 1, Version = b.Version + 1 }, b.Version, default));
            // a fair share of this cycle's damage (the clock may also have crossed into a new cycle)
            var mine = await repo.FindBossDamageAsync(uow, cycle, new CharacterId(Guid.Parse(id)), default);
            var owner = (await _host.Service<JianghuGo.Modules.Characters.Contracts.ICharacterDirectory>().FindAsync(uow, Guid.Parse(id), default))!;
            Assert.True(await repo.TrySaveBossDamageAsync(uow,
                mine is null ? new WorldBossDamage(cycle, owner.Id, owner.AccountId, owner.Name, 1000, 1, null, 1) : mine with { Damage = 1000, Version = mine.Version + 1 },
                mine?.Version ?? 0, default));
        });
        battle = await Ok(await p.PostAsync(url + "/start", null, Player.NewKey()), HttpStatusCode.Created);
        Assert.Equal(1, battle["opponent"]!.AsObject().Int("maxHp"));
        battle = await AutoToEndAsync(p, battle);
        Assert.Equal("won", battle.Str("result"), battle.Compact());
        boss = await Ok(await p.GetAsync(url));
        Assert.True(boss["defeated"]!.GetValue<bool>());
        Assert.Equal(0, boss.Int("attemptsLeft"));
        Assert.True(boss["canClaim"]!.GetValue<bool>(), boss.Compact());
        await Problem(await p.PostAsync(url + "/start", null, Player.NewKey()), HttpStatusCode.Conflict, "world_boss_defeated");

        var (stones, stacks) = await InventoryAsync(p, c);
        var claimed = await Ok(await p.PostAsync(url + "/claim", new { }));
        Assert.True(claimed["claimed"]!.GetValue<bool>());
        Assert.False(claimed["canClaim"]!.GetValue<bool>());
        await Problem(await p.PostAsync(url + "/claim", new { }), HttpStatusCode.Conflict, "world_boss_claimed");
        var (stones2, stacks2) = await InventoryAsync(p, c);
        Assert.Equal(stones + 1500, stones2);
        Assert.Equal(stacks.GetValueOrDefault("core_pill") + 1, stacks2.GetValueOrDefault("core_pill"));

        var (r, e) = await NewAsync();
        await Problem(await r.PostAsync($"/api/v1/characters/{e.Str("id")}/combat/world-boss/claim", new { }), HttpStatusCode.Conflict, "world_boss_no_share");
    }
}
