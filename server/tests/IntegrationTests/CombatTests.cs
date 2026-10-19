using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.Modules.Combat.Application;
using JianghuGo.Modules.Combat.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Integration")]
public class CombatTests : IAsyncLifetime
{
    private readonly QueuedSeedSource _seeds = new();
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync(seeds: _seeds);
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static int _names;

    private static string NewName()
    {
        var n = Interlocked.Increment(ref _names);
        var letters = new string([.. n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(d => (char)('a' + (d - '0')))]);
        return "Wojownik " + letters;
    }

    private async Task<(Player P, JsonObject C)> FighterAsync(string path = "qi")
    {
        var p = await _host.RegisterAndLoginAsync();
        return (p, await p.CreateCharacterAsync(NewName(), path));
    }

    private static async Task<JsonObject> Ok(HttpResponseMessage res, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, res.StatusCode, await res.Content.ReadAsStringAsync());
        return await ApiHost.Json(res);
    }

    private static Task<HttpResponseMessage> StartPve(Player p, JsonObject c, string npc = "training_dummy", string? key = null) =>
        p.PostAsync("/api/v1/combat/battles/pve", new { characterId = c.Str("id"), npcId = npc }, key ?? Player.NewKey());

    private static Task<HttpResponseMessage> Act(Player p, JsonObject battle, int? slot, string? direction = null, int? turn = null, string? key = null) =>
        p.PostAsync($"/api/v1/combat/battles/{battle.Str("id")}/actions", new { turn = turn ?? battle.Int("turn"), slot, direction }, key ?? Player.NewKey());

    /// <summary>Simple player strategy: strongest usable attack, else step closer, else rest.</summary>
    private static (int? Slot, string? Direction) Choose(JsonObject battle)
    {
        var slots = battle["mySlots"]!.AsArray().Select(s => s!.AsObject()).ToList();
        var usable = slots.Where(s => s["unavailable"] is null).ToList();
        var attack = usable.Where(s => s.Str("kind") == "attack").OrderByDescending(s => s.Int("qiCost")).FirstOrDefault();
        if (attack is not null) return (attack.Int("slot"), null);
        var move = usable.FirstOrDefault(s => s.Str("kind") == "movement");
        return move is not null && battle.Int("distance") > 1 ? (move.Int("slot"), "advance") : (null, null);
    }

    private async Task<JsonObject> FightToTheEndAsync(Player p, JsonObject battle)
    {
        for (var i = 0; i < 60 && battle.Str("status") == "active"; i++)
        {
            var (slot, dir) = Choose(battle);
            battle = await Ok(await Act(p, battle, slot, dir));
        }

        Assert.Equal("finished", battle.Str("status"));
        return battle;
    }

    // ------------------------------------------------------------------ loadout

    [Fact]
    public async Task Loadout_defaults_to_path_starter_and_validates_changes()
    {
        var (p, c) = await FighterAsync("qi");
        var url = $"/api/v1/characters/{c.Str("id")}/combat/loadout";
        var loadout = await Ok(await p.GetAsync(url));
        Assert.Equal(0, loadout.Int("version"));
        Assert.Equal("sword_qi_wave", loadout["slots"]![1].Str("techniqueId"));
        Assert.Equal(8, loadout["slots"]!.AsArray().Count);

        string?[] swapped = ["palm_strike", "sword_thrust", "qi_blast", "flying_daggers", "flowing_water", "iron_shirt", "heavenly_seal", "mirror_palm"];
        var saved = await Ok(await p.SendAsync(HttpMethod.Put, url, new { expectedVersion = 0, slots = swapped }));
        Assert.Equal(1, saved.Int("version"));
        var stale = await p.SendAsync(HttpMethod.Put, url, new { expectedVersion = 0, slots = swapped });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        string?[] foreign = ["palm_strike", "iron_fist", null, null, "flowing_water", null, null, null];
        var bad = await p.SendAsync(HttpMethod.Put, url, new { expectedVersion = 1, slots = foreign });
        Assert.Equal("loadout_invalid", (await ApiHost.Json(bad)).Str("code"));

        // 1.6: techniques open realm by realm; locked ones are listed so the player sees what is coming
        string?[] early = ["palm_strike", "sword_thrust", "qi_blast", "flying_daggers", "flowing_water", "iron_shirt", "swallow_glide", "mirror_palm"];
        var locked = await p.SendAsync(HttpMethod.Put, url, new { expectedVersion = 1, slots = early });
        Assert.Equal("technique_locked", (await ApiHost.Json(locked)).Str("code"));
        var known = (await Ok(await p.GetAsync(url)))["known"]!.AsArray();
        Assert.True(known.Single(k => k!.Str("id") == "swallow_glide")!["locked"]!.GetValue<bool>());
        Assert.False(known.Single(k => k!.Str("id") == "palm_strike")!["locked"]!.GetValue<bool>());

        var thief = await _host.RegisterAndLoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await thief.GetAsync(url)).StatusCode);
    }

    // ------------------------------------------------------------------ PvE

    [Fact]
    public async Task Pve_battle_starts_once_per_key_and_one_active_battle_per_character()
    {
        var (p, c) = await FighterAsync();
        var key = Player.NewKey();
        var first = await Ok(await StartPve(p, c, key: key), HttpStatusCode.Created);
        var replay = await Ok(await StartPve(p, c, key: key), HttpStatusCode.Created);
        Assert.Equal(first.Str("id"), replay.Str("id"));
        Assert.Equal("active", first.Str("status"));
        Assert.Equal(1, first.Int("turn"));
        Assert.Equal(64, first.Str("seedHash").Length);
        Assert.Null(first["seed"]);

        var second = await StartPve(p, c);
        Assert.Equal("already_in_battle", (await ApiHost.Json(second)).Str("code"));
        var unknown = await StartPve(p, c, "dragon_emperor");
        Assert.Equal("npc_not_found", (await ApiHost.Json(unknown)).Str("code"));
    }

    [Fact]
    public async Task Pve_battle_runs_to_completion_reveals_seed_and_replays_exactly()
    {
        var (p, c) = await FighterAsync();
        var battle = await Ok(await StartPve(p, c, "grey_wolf"), HttpStatusCode.Created);
        var end = await FightToTheEndAsync(p, battle);
        Assert.True(end.Str("result") is "won" or "lost" or "draw");
        var seed = end.Str("seed");
        Assert.Equal(end.Str("seedHash"), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed))), "commit-reveal: published hash matches revealed seed");

        var turns = (await Ok(await p.GetAsync($"/api/v1/combat/battles/{end.Str("id")}/turns")))["items"]!.AsArray();
        Assert.Equal(end.Int("turn"), turns.Count);

        // Audit replay: recorded intents + revealed seed + frozen combatants reproduce every stored state bit-for-bit.
        var config = _host.Service<CombatConfig>();
        var stored = await _host.Service<IUnitOfWorkFactory>().RunAsync(uow => _host.Service<ICombatRepository>().FindBattleAsync(uow, Guid.Parse(end.Str("id")), default), default);
        var records = await _host.Service<IUnitOfWorkFactory>().RunAsync(uow => _host.Service<ICombatRepository>().ListTurnsAsync(uow, stored!.Id, default), default);
        var state = CombatEngine.Start(config, stored!.CombatantA!, stored.CombatantB!);
        foreach (var r in records)
        {
            state = CombatEngine.ResolveTurn(config, stored.CombatantA!, stored.CombatantB!, state, r.IntentA, r.IntentB, CombatEngine.TurnRng(ulong.Parse(seed, System.Globalization.CultureInfo.InvariantCulture), r.Turn)).State;
            Assert.Equal(JsonSerializer.Serialize(r.StateAfter), JsonSerializer.Serialize(state), $"turn {r.Turn} replays identically");
        }

        Assert.NotNull(state.Outcome);
        var after = await Act(p, end, 0);
        Assert.Equal("battle_not_active", (await ApiHost.Json(after)).Str("code"));
    }

    // Acceptance #6: the client sends an intent, never damage or results.
    [Fact]
    public async Task Client_cannot_send_damage_or_outcome()
    {
        var (p, c) = await FighterAsync();
        var battle = await Ok(await StartPve(p, c), HttpStatusCode.Created);
        var res = await p.PostAsync($"/api/v1/combat/battles/{battle.Str("id")}/actions",
            new { turn = 1, slot = 2, damage = 9999 }, Player.NewKey());
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var res2 = await p.PostAsync($"/api/v1/combat/battles/{battle.Str("id")}/actions",
            new { turn = 1, slot = 2, outcome = "a_won" }, Player.NewKey());
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);
        Assert.Equal(1, (await Ok(await p.GetAsync($"/api/v1/combat/battles/{battle.Str("id")}"))).Int("turn"));
    }

    // Acceptance #7: wrong turn order is rejected.
    [Fact]
    public async Task Wrong_turn_number_and_foreign_battles_are_rejected()
    {
        var (p, c) = await FighterAsync();
        var battle = await Ok(await StartPve(p, c), HttpStatusCode.Created);
        foreach (var turn in new[] { 0, 2, 7 })
        {
            var res = await Act(p, battle, null, turn: turn);
            Assert.Equal("turn_mismatch", (await ApiHost.Json(res)).Str("code"), turn.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var next = await Ok(await Act(p, battle, null));          // rest in turn 1
        Assert.Equal(2, next.Int("turn"));
        var late = await Act(p, battle, null, turn: 1);           // a second action for the old turn
        Assert.Equal("turn_mismatch", (await ApiHost.Json(late)).Str("code"));

        var thief = await _host.RegisterAndLoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Act(thief, next, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await thief.GetAsync($"/api/v1/combat/battles/{battle.Str("id")}")).StatusCode);
    }

    // Acceptance #8: a repeated action is not executed twice.
    [Fact]
    public async Task Repeated_action_with_same_key_replays_and_resolves_the_turn_once()
    {
        var (p, c) = await FighterAsync();
        var battle = await Ok(await StartPve(p, c), HttpStatusCode.Created);
        var key = Player.NewKey();
        var r1 = await Ok(await Act(p, battle, null, key: key));
        var r2 = await Ok(await Act(p, battle, null, key: key));
        Assert.Equal(r1.Compact(), r2.Compact());
        Assert.Equal(2, (await Ok(await p.GetAsync($"/api/v1/combat/battles/{battle.Str("id")}"))).Int("turn"));
        Assert.Equal(1, (await Ok(await p.GetAsync($"/api/v1/combat/battles/{battle.Str("id")}/turns")))["items"]!.AsArray().Count);
    }

    [Fact]
    public async Task Concurrent_actions_for_the_same_turn_resolve_it_once()
    {
        var (p, c) = await FighterAsync();
        var battle = await Ok(await StartPve(p, c), HttpStatusCode.Created);
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Act(p, battle, null)));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, (await Ok(await p.GetAsync($"/api/v1/combat/battles/{battle.Str("id")}/turns")))["items"]!.AsArray().Count);
    }

    [Fact]
    public async Task Illegal_actions_report_engine_codes()
    {
        var (p, c) = await FighterAsync("qi");
        var battle = await Ok(await StartPve(p, c), HttpStatusCode.Created);
        Assert.Equal("special", battle["mySlots"]![6].Str("kind"));                                     // the old movement slot
        Assert.Equal("slot_invalid", (await ApiHost.Json(await Act(p, battle, 9))).Str("code"));
        var used = await Ok(await Act(p, battle, 2));                                                    // qi blast, cooldown 2
        Assert.Equal("on_cooldown", used["mySlots"]![2].Str("unavailable"));
        Assert.Equal("on_cooldown", (await ApiHost.Json(await Act(p, used, 2))).Str("code"));
    }

    [Fact]
    public async Task Missed_deadlines_rest_for_the_absent_player_and_three_in_a_row_forfeit()
    {
        var (p, c) = await FighterAsync();
        var battle = await Ok(await StartPve(p, c, "grey_wolf"), HttpStatusCode.Created);
        _host.Clock.Advance(TimeSpan.FromSeconds(100));                         // one deadline (90 s) missed
        await p.RefreshAsync();
        var after = await Ok(await p.GetAsync($"/api/v1/combat/battles/{battle.Str("id")}"));
        Assert.Equal("active", after.Str("status"), "disconnecting does not void the battle");
        Assert.Equal(2, after.Int("turn"));
        Assert.Equal("rest", after["lastTurnEvents"]!.AsArray().First(e => e!.Str("actor") == "a")!.Str("action"));

        _host.Clock.Advance(TimeSpan.FromMinutes(10));
        await p.RefreshAsync();
        var forfeit = await Ok(await p.GetAsync($"/api/v1/combat/battles/{battle.Str("id")}"));
        Assert.Equal("finished", forfeit.Str("status"));
        Assert.Equal("lost", forfeit.Str("result"));
        Assert.Equal(3, forfeit.Int("turn"), "two forced turns resolved, the third missed deadline forfeits");
    }

    [Fact]
    public async Task Pve_victory_pays_qi_once_through_the_outbox_and_trains_techniques()
    {
        var (p, c) = await FighterAsync("qi");
        var battle = await Ok(await StartPve(p, c, "training_dummy"), HttpStatusCode.Created);
        var end = await FightToTheEndAsync(p, battle);
        Assert.Equal("won", end.Str("result"));
        Assert.Equal(5, end.Int("rewardQi"));

        var cultivation = $"/api/v1/characters/{c.Str("id")}/cultivation";
        Assert.Equal(0, (await Ok(await p.GetAsync(cultivation))).Int("qi"), "reward arrives via the worker, not inline");
        await _host.DrainOutboxAsync();
        Assert.Equal(5, (await Ok(await p.GetAsync(cultivation))).Int("qi"));
        Assert.Equal(0, await _host.DrainOutboxAsync());
        var notifications = (await Ok(await p.GetAsync("/api/v1/notifications")))["items"]!.AsArray();
        Assert.True(notifications.Any(n => n is not null && n.Str("type") == "battle_won" && n["payload"].Int("rewardQi") == 5));

        var loadout = await Ok(await p.GetAsync($"/api/v1/characters/{c.Str("id")}/combat/loadout"));
        var trained = loadout["known"]!.AsArray().Where(k => k!.Int("xp") > 0 || k.Int("level") > 1).ToList();
        Assert.True(trained.Count > 0, "used techniques gained mastery XP");
    }

    // ------------------------------------------------------------------ PvP

    [Fact]
    public async Task Pvp_challenge_accept_simultaneous_turns_and_notifications()
    {
        var (a, ca) = await FighterAsync("body");
        var (b, cb) = await FighterAsync("poison");
        var challenge = await Ok(await a.PostAsync("/api/v1/combat/battles/pvp",
            new { characterId = ca.Str("id"), opponentCharacterId = cb.Str("id") }, Player.NewKey()), HttpStatusCode.Created);
        Assert.Equal("pending_acceptance", challenge.Str("status"));
        await _host.DrainOutboxAsync();
        var invite = (await Ok(await b.GetAsync("/api/v1/notifications")))["items"]!.AsArray().Single(n => n!.Str("type") == "pvp_challenge");
        Assert.Equal(challenge.Str("id"), invite!["payload"].Str("battleId"));

        Assert.Equal("battle_not_pending", (await ApiHost.Json(await a.PostAsync($"/api/v1/combat/battles/{challenge.Str("id")}/accept"))).Str("code"));
        var started = await Ok(await b.PostAsync($"/api/v1/combat/battles/{challenge.Str("id")}/accept"));
        Assert.Equal("active", started.Str("status"));
        Assert.Equal("b", started.Str("mySide"));

        // A acts; the turn waits for B. B's view shows that A acted, not what A did.
        var waiting = await Ok(await Act(a, started, null));
        Assert.Equal(1, waiting.Int("turn"));
        Assert.True(waiting["myActionSubmitted"]!.GetValue<bool>());
        var bView = await Ok(await b.GetAsync($"/api/v1/combat/battles/{started.Str("id")}"));
        Assert.True(bView["opponentActionSubmitted"]!.GetValue<bool>());
        Assert.Equal("action_already_submitted", (await ApiHost.Json(await Act(a, started, null))).Str("code"));

        var resolved = await Ok(await Act(b, bView, null));
        Assert.Equal(2, resolved.Int("turn"));
        Assert.Equal(2, resolved["lastTurnEvents"]!.AsArray().Count(e => e!.Str("action") == "rest"));

        // Fight it out: both use the simple strategy, B occasionally late (deadline resolves its turn as rest).
        var aView = await Ok(await a.GetAsync($"/api/v1/combat/battles/{started.Str("id")}"));
        for (var i = 0; i < 80 && aView.Str("status") == "active"; i++)
        {
            var (sa, da) = Choose(aView);
            await Ok(await Act(a, aView, sa, da));
            if (i % 5 == 4)
            {
                _host.Clock.Advance(TimeSpan.FromSeconds(91));
                await a.RefreshAsync();
                await b.RefreshAsync();
            }
            else
            {
                bView = await Ok(await b.GetAsync($"/api/v1/combat/battles/{started.Str("id")}"));
                if (bView.Str("status") == "active" && !bView["myActionSubmitted"]!.GetValue<bool>())
                {
                    var (sb, db) = Choose(bView);
                    await Ok(await Act(b, bView, sb, db));
                }
            }

            aView = await Ok(await a.GetAsync($"/api/v1/combat/battles/{started.Str("id")}"));
        }

        Assert.Equal("finished", aView.Str("status"));
        await _host.DrainOutboxAsync();
        foreach (var player in new[] { a, b })
        {
            var types = (await Ok(await player.GetAsync("/api/v1/notifications")))["items"]!.AsArray().Select(n => n!.Str("type")).ToList();
            Assert.True(types.Any(t => t is "battle_won" or "battle_lost" or "battle_draw"));
        }

        Assert.Equal(0, aView.Int("rewardQi"), "PvP never pays Qi (no win-trading farm)");
    }

    [Fact]
    public async Task Pvp_rules_self_challenge_decline_and_expiry()
    {
        var (a, ca) = await FighterAsync();
        var own = await a.CreateCharacterAsync(NewName(), "flame");
        var self = await a.PostAsync("/api/v1/combat/battles/pvp", new { characterId = ca.Str("id"), opponentCharacterId = own.Str("id") }, Player.NewKey());
        Assert.Equal("cannot_challenge_self", (await ApiHost.Json(self)).Str("code"));

        var (b, cb) = await FighterAsync();
        var c1 = await Ok(await a.PostAsync("/api/v1/combat/battles/pvp", new { characterId = ca.Str("id"), opponentCharacterId = cb.Str("id") }, Player.NewKey()), HttpStatusCode.Created);
        var declined = await Ok(await b.PostAsync($"/api/v1/combat/battles/{c1.Str("id")}/decline"));
        Assert.Equal("declined", declined.Str("status"));

        var c2 = await Ok(await a.PostAsync("/api/v1/combat/battles/pvp", new { characterId = ca.Str("id"), opponentCharacterId = cb.Str("id") }, Player.NewKey()), HttpStatusCode.Created);
        _host.Clock.Advance(TimeSpan.FromMinutes(11));
        await b.RefreshAsync();
        Assert.Equal("battle_not_pending", (await ApiHost.Json(await b.PostAsync($"/api/v1/combat/battles/{c2.Str("id")}/accept"))).Str("code"));
        Assert.Equal("expired", (await Ok(await b.GetAsync($"/api/v1/combat/battles/{c2.Str("id")}"))).Str("status"));

        var outsider = await _host.RegisterAndLoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsync($"/api/v1/combat/battles/{c2.Str("id")}/accept")).StatusCode);
    }

    [Fact]
    public async Task Pve_rewards_stop_after_the_daily_limit()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "game-config", "combat.json"))
            .Replace("\"dailyRewardedPveWins\": 20", "\"dailyRewardedPveWins\": 1", StringComparison.Ordinal);
        var path = Path.Combine(Path.GetTempPath(), $"combat-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        await _host.DisposeAsync();
        _host = await ApiHost.StartAsync(new() { ["GameConfig:CombatPath"] = path }, seeds: _seeds);

        var (p, c) = await FighterAsync("qi");
        var first = await FightToTheEndAsync(p, await Ok(await StartPve(p, c), HttpStatusCode.Created));
        var second = await FightToTheEndAsync(p, await Ok(await StartPve(p, c), HttpStatusCode.Created));
        Assert.Equal("won", first.Str("result"));
        Assert.Equal("won", second.Str("result"));
        Assert.Equal(5, first.Int("rewardQi"));
        Assert.Equal(0, second.Int("rewardQi"), "second win in 24 h exceeds the configured limit of 1");

        _host.Clock.Advance(TimeSpan.FromHours(25));
        await p.RefreshAsync();
        var third = await FightToTheEndAsync(p, await Ok(await StartPve(p, c), HttpStatusCode.Created));
        Assert.Equal(5, third.Int("rewardQi"));
    }
}
