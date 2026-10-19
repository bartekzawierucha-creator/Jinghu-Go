using System.Net;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Cultivation.Application;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

/// <summary>MVP 1.1 progression: quests and check-ins, meridians, rankings, nearby cultivators, spirit pets.</summary>
[Category("Integration")]
public class ProgressionTests : IAsyncLifetime
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
        return (p, await p.CreateCharacterAsync("Adept " + new string([.. n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(d => (char)('a' + (d - '0')))]), path));
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

    /// <summary>Puts a character at a realm/stage with some Qi (test setup through the module's own repository).</summary>
    private async Task SetCultivationAsync(Player p, JsonObject c, int realm, int stage, long qi)
    {
        var id = c.Str("id");
        await Ok(await p.PostAsync($"/api/v1/characters/{id}/cultivation/meditation/start"));
        var repo = _host.Service<ICultivationRepository>();
        await _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            var s = Assert.NotNull(await repo.FindAsync(uow, new CharacterId(Guid.Parse(id)), default));
            Assert.True(await repo.TrySaveAsync(uow, s with { RealmIndex = realm, Stage = stage, Qi = qi, MeditationStartedAt = null, Version = s.Version + 1 }, s.Version, default));
            await uow.CommitAsync(default);
        }, default);
    }

    private static JsonObject Quest(JsonObject board, string list, string id) =>
        board[list]!.AsArray().Select(q => q!.AsObject()).Single(q => q.Str("id") == id);

    [Fact]
    public async Task Checkin_pays_once_per_day_and_builds_a_streak()
    {
        var (p, c) = await NewAsync();
        var url = $"/api/v1/characters/{c.Str("id")}/quests";
        var board = await Ok(await p.GetAsync(url));
        Assert.Equal(3, board["daily"]!.AsArray().Count);
        Assert.Equal(0, board["checkin"].Int("streak"));

        var first = await Ok(await p.PostAsync(url + "/checkin"));
        Assert.Equal(1, first.Int("streak"));
        Assert.Equal(20, first.Int("stones"));
        await Problem(await p.PostAsync(url + "/checkin"), HttpStatusCode.Conflict, "already_checked_in");

        _host.Clock.Advance(TimeSpan.FromDays(1));
        await p.RefreshAsync();
        var second = await Ok(await p.PostAsync(url + "/checkin"));
        Assert.Equal(2, second.Int("streak"));
        Assert.Equal(30, second.Int("stones"));
        Assert.Equal(first.Int("balance") + 30, second.Int("balance"));

        _host.Clock.Advance(TimeSpan.FromDays(2));
        await p.RefreshAsync();
        Assert.Equal(1, (await Ok(await p.PostAsync(url + "/checkin"))).Int("streak"));
    }

    [Fact]
    public async Task Events_count_once_and_completed_quests_pay_once()
    {
        var (p, c) = await NewAsync();
        var id = c.Str("id");
        var url = $"/api/v1/characters/{id}/quests";
        await Problem(await p.PostAsync(url + "/a_win_10/claim"), HttpStatusCode.Conflict, "quest_not_completed");
        await Problem(await p.PostAsync(url + "/nope/claim"), HttpStatusCode.NotFound, "quest_not_found");

        // meditation is counted from cultivation.meditation_finished
        await Ok(await p.PostAsync($"/api/v1/characters/{id}/cultivation/meditation/start"));
        _host.Clock.Advance(TimeSpan.FromMinutes(31));
        await p.RefreshAsync();
        await Ok(await p.PostAsync($"/api/v1/characters/{id}/cultivation/meditation/finish", null, Player.NewKey()));

        // a won battle is counted from combat.battle_finished
        var battle = await Ok(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = id, npcId = "training_dummy" }, Player.NewKey()), HttpStatusCode.Created);
        for (var i = 0; i < 80 && battle.Str("status") == "active"; i++)
        {
            var slots = battle["mySlots"]!.AsArray().Select(s => s!.AsObject()).Where(s => s["unavailable"] is null).ToList();
            var attack = slots.Where(s => s.Str("kind") == "attack").OrderByDescending(s => s.Int("qiCost")).FirstOrDefault();
            var move = slots.FirstOrDefault(s => s.Str("kind") == "movement");
            object body = attack is not null ? new { turn = battle.Int("turn"), slot = attack.Int("slot") }
                : move is not null && battle.Int("distance") > 1 ? new { turn = battle.Int("turn"), slot = move.Int("slot"), direction = "advance" }
                : new { turn = battle.Int("turn") };
            battle = await Ok(await p.PostAsync($"/api/v1/combat/battles/{battle.Str("id")}/actions", body, Player.NewKey()));
        }

        var won = battle["result"]?.ToString() == "won";
        Assert.True(await _host.DrainOutboxAsync() > 0);
        Assert.Equal(0, await _host.DrainOutboxAsync());

        var board = await Ok(await p.GetAsync(url));
        Assert.Equal(31, Quest(board, "achievements", "a_meditate_600").Int("progress"));
        var wins = Quest(board, "achievements", "a_win_10").Int("progress");
        Assert.Equal(won ? 1 : 0, wins, battle.Compact());

        // a completed achievement pays once
        var repo = _host.Service<JianghuGo.Modules.Quests.Application.IQuestRepository>();
        await _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            await repo.AddProgressAsync(uow, new CharacterId(Guid.Parse(id)), "life", "m_bosses_won", 1, default);
            await uow.CommitAsync(default);
        }, default);
        var claim = await Ok(await p.PostAsync(url + "/a_boss_1/claim"));
        Assert.Equal(200, claim.Int("stones"));
        Assert.True(Quest(claim["board"]!.AsObject(), "achievements", "a_boss_1")["claimed"]!.GetValue<bool>());
        Assert.Contains("Pogromca Bestii", claim["board"]!["titles"]!.ToJsonString());
        Assert.Equal(3, claim["board"]!["titleBonuses"].Int("constitution"));        // the title's stat bonus
        var fromTitles = await _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            var total = 0;
            foreach (var provider in _host.Service<IEnumerable<ICombatStatBonuses>>())
                total += (await provider.BonusesAsync(uow, Guid.Parse(id), default)).GetValueOrDefault("constitution");
            return total;
        }, default);
        Assert.Equal(3, fromTitles);                                                   // no realm/meridians yet: only the title
        await Problem(await p.PostAsync(url + "/a_boss_1/claim"), HttpStatusCode.Conflict, "quest_already_claimed");

        var leaders = await Ok(await p.GetAsync(url + "/leaders/meditation_minutes"));
        Assert.Contains(id, leaders["items"]!.ToJsonString());
        await Problem(await p.GetAsync(url + "/leaders/hacking"), HttpStatusCode.BadRequest, "unknown_metric");
    }

    [Fact]
    public async Task Meridians_open_with_climbed_stages_and_feed_combat()
    {
        var (p, c) = await NewAsync();
        var id = c.Str("id");
        var url = $"/api/v1/characters/{id}/cultivation/meridians";
        var view = await Ok(await p.GetAsync(url));
        Assert.Equal(0, view.Int("pointsEarned"));
        await Problem(await p.PostAsync(url, new { stat = "strength", expectedSpent = 0 }), HttpStatusCode.Conflict, "no_meridian_points");

        await SetCultivationAsync(p, c, 1, 1, 0); // second realm: 12 stages climbed
        view = await Ok(await p.GetAsync(url));
        Assert.Equal(12, view.Int("pointsEarned"));
        Assert.Equal(2, view.Int("realmBonus"));
        view = await Ok(await p.PostAsync(url, new { stat = "strength", expectedSpent = 0 }));
        view = await Ok(await p.PostAsync(url, new { stat = "strength", expectedSpent = 1 }));
        Assert.Equal(2, view["allocated"].Int("strength"));
        await Problem(await p.PostAsync(url, new { stat = "strength", expectedSpent = 1 }), HttpStatusCode.Conflict, "meridians_changed");
        await Problem(await p.PostAsync(url, new { stat = "luck", expectedSpent = 2 }), HttpStatusCode.BadRequest, "unknown_stat");

        var bonuses = new Dictionary<string, int>();
        await _host.Service<IUnitOfWorkFactory>().RunAsync(async uow =>
        {
            foreach (var provider in _host.Service<IEnumerable<ICombatStatBonuses>>())
                foreach (var (k, v) in await provider.BonusesAsync(uow, Guid.Parse(id), default))
                    bonuses[k] = bonuses.GetValueOrDefault(k) + v;
        }, default);
        Assert.Equal(4, bonuses["strength"]);  // realm 2 + meridians 2
        Assert.Equal(2, bonuses["spirit"]);

        await Problem(await p.PostAsync(url + "/reset"), HttpStatusCode.Conflict, "not_enough_qi");
        await SetCultivationAsync(p, c, 1, 1, 100);
        view = await Ok(await p.PostAsync(url + "/reset"));
        Assert.Equal(0, view.Int("pointsSpent"));
        Assert.Equal(90, (await Ok(await p.GetAsync($"/api/v1/characters/{id}/cultivation"))).Int("qi"));

        var (thief, _) = await NewAsync();
        await Problem(await thief.GetAsync(url), HttpStatusCode.NotFound, "character_not_found");
    }

    [Fact]
    public async Task Ranking_orders_by_realm_then_stage()
    {
        var (a, ca) = await NewAsync();
        var (b, cb) = await NewAsync();
        await SetCultivationAsync(a, ca, 2, 3, 0);
        await SetCultivationAsync(b, cb, 2, 1, 5000);
        var view = await Ok(await b.GetAsync($"/api/v1/characters/{cb.Str("id")}/cultivation/ranking?limit=10"));
        var top = view["top"]!.AsArray().Select(e => e!.AsObject()).ToList();
        var ia = top.FindIndex(e => e.Str("characterId") == ca.Str("id"));
        var ib = top.FindIndex(e => e.Str("characterId") == cb.Str("id"));
        Assert.True(ia >= 0 && ib > ia, view.Compact());
        Assert.Equal(top[ib].Int("rank"), view["me"].Int("rank"));
    }

    [Fact]
    public async Task Nearby_shows_names_and_distance_bands_but_never_coordinates()
    {
        var (a, ca) = await NewAsync();
        var (b, cb) = await NewAsync();
        var (far, cf) = await NewAsync();
        await Problem(await a.GetAsync($"/api/v1/characters/{ca.Str("id")}/nearby"), HttpStatusCode.Conflict, "no_session");

        async Task At(Player p, JsonObject c, double lat, double lon)
        {
            await Ok(await p.PostAsync($"/api/v1/characters/{c.Str("id")}/exploration/start"));
            await Ok(await p.PostAsync($"/api/v1/characters/{c.Str("id")}/exploration/samples",
                new { samples = new[] { new { lat, lon, accuracyM = 8, capturedAt = _host.Clock.GetUtcNow() } } }));
        }

        await At(a, ca, 52.2297, 21.0122);
        await At(b, cb, 52.2310, 21.0122);   // ~145 m north
        await At(far, cf, 52.4000, 21.0122); // ~19 km
        var res = await a.GetAsync($"/api/v1/characters/{ca.Str("id")}/nearby");
        var json = await Ok(res);
        var items = json["items"]!.AsArray().Select(i => i!.AsObject()).ToList();
        Assert.Equal(1, items.Count, json.Compact());
        Assert.Equal(cb.Str("id"), items[0].Str("characterId"));
        Assert.Equal("<250m", items[0].Str("distanceBand"));
        Assert.False(json.Compact().Contains("lat", StringComparison.OrdinalIgnoreCase) || json.Compact().Contains("21.01", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Traits_show_their_stat_modifiers_and_paths_describe_their_perks()
    {
        var (p, c) = await NewAsync();
        var me = await Ok(await p.GetAsync($"/api/v1/characters/{c.Str("id")}"));
        Assert.True(me["traits"]!.AsArray().All(t => t!["modifiers"] is JsonObject), me.Compact());
        var catalog = await Ok(await p.GetAsync("/api/v1/combat/catalog"));
        foreach (var path in new[] { "qi", "body", "flame", "poison" })
            Assert.False(string.IsNullOrWhiteSpace(catalog["paths"]![path]!.Str("descriptionPl")));
        Assert.Equal("special", catalog["slots"]![6]!.GetValue<string>());
    }

    [Fact]
    public async Task Auto_battle_plays_turns_for_the_player_until_the_end()
    {
        var (p, c) = await NewAsync();
        var battle = await Ok(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = c.Str("id"), npcId = "training_dummy" }, Player.NewKey()), HttpStatusCode.Created);
        var url = $"/api/v1/combat/battles/{battle.Str("id")}/auto";
        var turns = 0;
        for (; turns < 40 && battle.Str("status") == "active"; turns++)
            battle = await Ok(await p.PostAsync(url, null, Player.NewKey()));
        Assert.Equal("finished", battle.Str("status"), battle.Compact());
        Assert.True(turns > 1);
        Assert.True(battle["result"]!.GetValue<string>() is "won" or "lost" or "draw");
        await Problem(await p.PostAsync(url, null, Player.NewKey()), HttpStatusCode.Conflict, "battle_not_active");
        var (stranger, _) = await NewAsync();
        await Problem(await stranger.PostAsync(url, null, Player.NewKey()), HttpStatusCode.NotFound, "battle_not_found");
    }

    [Fact]
    public async Task Idle_hunt_fights_one_battle_per_interval_up_to_the_time_limit()
    {
        var (p, c) = await NewAsync();
        var id = c.Str("id");
        var url = $"/api/v1/characters/{id}/combat/hunt";
        Assert.Null((await Ok(await p.GetAsync(url)))["hunt"]);
        await Problem(await p.PostAsync(url, new { npcId = "jiao_dragon" }), HttpStatusCode.BadRequest, "npc_world_only");
        await Problem(await p.PostAsync(url + "/claim"), HttpStatusCode.NotFound, "no_hunt");

        var hunt = (await Ok(await p.PostAsync(url, new { npcId = "training_dummy" })))["hunt"]!.AsObject();
        Assert.Equal(0, hunt.Int("battles"));
        Assert.Equal(5, hunt.Int("minutesPerBattle"));
        await Problem(await p.PostAsync(url, new { npcId = "grey_wolf" }), HttpStatusCode.Conflict, "hunt_in_progress");

        _host.Clock.Advance(TimeSpan.FromMinutes(23));
        await p.RefreshAsync();
        hunt = (await Ok(await p.GetAsync(url)))["hunt"]!.AsObject();
        Assert.Equal(4, hunt.Int("battles"));
        Assert.False(hunt["finished"]!.GetValue<bool>());
        // a manual battle is still possible during a hunt
        await Ok(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = id, npcId = "training_dummy" }, Player.NewKey()), HttpStatusCode.Created);

        _host.Clock.Advance(TimeSpan.FromHours(10));
        await p.RefreshAsync();
        hunt = (await Ok(await p.GetAsync(url)))["hunt"]!.AsObject();
        Assert.Equal(48, hunt.Int("battles"));               // 4 h / 5 min, nothing after the limit
        Assert.True(hunt["finished"]!.GetValue<bool>());
        var wins = hunt.Int("wins");
        Assert.True(wins > 0 && wins + hunt.Int("losses") <= 48, hunt.Compact());
        Assert.True(hunt.Int("qi") <= 20 * 5, "idle wins share the daily rewarded-wins cap");

        var final = (await Ok(await p.PostAsync(url + "/claim")))["hunt"]!.AsObject();
        Assert.Equal(48, final.Int("battles"));
        Assert.Null((await Ok(await p.GetAsync(url)))["hunt"]);
        await _host.DrainOutboxAsync();
        var board = await Ok(await p.GetAsync($"/api/v1/characters/{id}/quests"));
        Assert.True(Quest(board, "achievements", "a_win_10").Int("progress") >= Math.Min(10, wins), "idle wins count for quests");
    }

    [Fact]
    public async Task Forfeit_ends_the_battle_as_a_loss_and_frees_the_fighter()
    {
        var (p, c) = await NewAsync();
        var id = c.Str("id");
        var battle = await Ok(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = id, npcId = "grey_wolf" }, Player.NewKey()), HttpStatusCode.Created);
        await Problem(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = id, npcId = "training_dummy" }, Player.NewKey()), HttpStatusCode.Conflict, "already_in_battle");
        var (stranger, _) = await NewAsync();
        await Problem(await stranger.PostAsync($"/api/v1/combat/battles/{battle.Str("id")}/forfeit"), HttpStatusCode.NotFound, "battle_not_found");

        var done = await Ok(await p.PostAsync($"/api/v1/combat/battles/{battle.Str("id")}/forfeit"));
        Assert.Equal("finished", done.Str("status"));
        Assert.Equal("lost", done.Str("result"));
        Assert.Equal(0, done.Int("rewardQi"));
        await Problem(await p.PostAsync($"/api/v1/combat/battles/{battle.Str("id")}/forfeit"), HttpStatusCode.Conflict, "battle_not_active");
        await Ok(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = id, npcId = "training_dummy" }, Player.NewKey()), HttpStatusCode.Created);
        Assert.Null((await Ok(await p.GetAsync($"/api/v1/characters/{id}/combat/pet")))["pet"]);
    }

    [Fact]
    public async Task Spirit_pet_is_tamed_on_a_lucky_win_and_can_be_released()
    {
        var (p, c) = await NewAsync();
        var id = c.Str("id");
        var petUrl = $"/api/v1/characters/{id}/combat/pet";
        Assert.Null((await Ok(await p.GetAsync(petUrl)))["pet"]);

        // Seeds whose tame roll is < 150‰ (the minimum chance): (seed ^ 0x5EED9E7A) % 1000 < 150.
        JsonObject? pet = null;
        for (ulong k = 0; k < 40 && pet is null; k++)
        {
            _seeds.Enqueue(0x5EED_9E7AUL ^ (k * 1000));
            var battle = await Ok(await p.PostAsync("/api/v1/combat/battles/pve", new { characterId = id, npcId = "grey_wolf" }, Player.NewKey()), HttpStatusCode.Created);
            for (var i = 0; i < 80 && battle.Str("status") == "active"; i++)
            {
                var slots = battle["mySlots"]!.AsArray().Select(s => s!.AsObject()).Where(s => s["unavailable"] is null).ToList();
                var attack = slots.Where(s => s.Str("kind") == "attack").OrderByDescending(s => s.Int("qiCost")).FirstOrDefault();
                var move = slots.FirstOrDefault(s => s.Str("kind") == "movement");
                object body = attack is not null ? new { turn = battle.Int("turn"), slot = attack.Int("slot") }
                    : move is not null && battle.Int("distance") > 1 ? new { turn = battle.Int("turn"), slot = move.Int("slot"), direction = "advance" }
                    : new { turn = battle.Int("turn") };
                battle = await Ok(await p.PostAsync($"/api/v1/combat/battles/{battle.Str("id")}/actions", body, Player.NewKey()));
            }

            if (battle["tamedPetNpcId"] is { } tamed) Assert.Equal("grey_wolf", tamed.GetValue<string>());
            pet = (await Ok(await p.GetAsync(petUrl)))["pet"]?.AsObject();
            _host.Clock.Advance(TimeSpan.FromMinutes(5));
            await p.RefreshAsync();
        }

        Assert.NotNull(pet, "a lucky seed must tame the wolf within 40 wins");
        Assert.Equal("grey_wolf", pet!.Str("npcId"));
        await Ok(await p.PostAsync(petUrl + "/release"));
        Assert.Null((await Ok(await p.GetAsync(petUrl)))["pet"]);
        await Problem(await p.PostAsync(petUrl + "/release"), HttpStatusCode.NotFound, "pet_not_found");
    }
}
