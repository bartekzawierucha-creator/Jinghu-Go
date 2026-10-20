using System.Net;
using System.Text.Json.Nodes;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.World.Application;
using JianghuGo.Modules.World.Domain;
using JianghuGo.TestKit;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.IntegrationTests;

[Category("Integration")]
public class WorldTests : IAsyncLifetime
{
    private ApiHost _host = null!;
    private static int _names;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static readonly GeoPoint Warsaw = new(52.2297, 21.0122);
    private static readonly GeoPoint Wawel = new(50.0533, 19.9332);

    private sealed class Explorer(ApiHost host, Player player, JsonObject character)
    {
        public Player P { get; } = player;
        public JsonObject C { get; } = character;
        public string Id => C.Str("id");
        public GeoPoint At { get; private set; }

        public async Task<JsonObject> StartAsync()
        {
            var res = await P.PostAsync($"/api/v1/characters/{Id}/exploration/start");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode, await res.Content.ReadAsStringAsync());
            return await ApiHost.Json(res);
        }

        public async Task<JsonObject> SendAsync(GeoPoint p, double accuracy = 8, HttpStatusCode expected = HttpStatusCode.OK)
        {
            var res = await P.PostAsync($"/api/v1/characters/{Id}/exploration/samples",
                new { samples = new[] { new { lat = p.Lat, lon = p.Lon, accuracyM = accuracy, capturedAt = host.Clock.GetUtcNow() } } });
            Assert.Equal(expected, res.StatusCode, await res.Content.ReadAsStringAsync());
            At = p;
            return await ApiHost.Json(res);
        }

        /// <summary>Walks at ~4 m/s in 40 m steps, one sample every 10 s of server time.</summary>
        public async Task WalkToAsync(GeoPoint target)
        {
            var distance = GeoMath.DistanceM(At, target);
            var steps = Math.Max(1, (int)Math.Ceiling(distance / 40));
            var from = At;
            for (var i = 1; i <= steps; i++)
            {
                host.Clock.Advance(TimeSpan.FromSeconds(10));
                if (i % 60 == 0) await P.RefreshAsync();
                await SendAsync(new GeoPoint(from.Lat + ((target.Lat - from.Lat) * i / steps), from.Lon + ((target.Lon - from.Lon) * i / steps)));
            }
        }

        public async Task<JsonObject> NearbyAsync() => await ApiHost.Json(await P.GetAsync($"/api/v1/characters/{Id}/world/nearby"));

        public Task<HttpResponseMessage> InteractAsync(string nodeKey, string? key = null) =>
            P.PostAsync("/api/v1/world/interactions", new { characterId = Id, nodeKey }, key ?? Player.NewKey());

        public Task<HttpResponseMessage> AttuneAsync(string siteId) =>
            P.PostAsync("/api/v1/world/flame-attunements", new { characterId = Id, siteId }, Player.NewKey());
    }

    private async Task<Explorer> ExplorerAsync(string path = "qi", GeoPoint? at = null)
    {
        var p = await _host.RegisterAndLoginAsync();
        var n = Interlocked.Increment(ref _names);
        var c = await p.CreateCharacterAsync("Wedrowiec " + new string([.. n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(d => (char)('a' + (d - '0')))]), path);
        var e = new Explorer(_host, p, c);
        await e.StartAsync();
        await e.SendAsync(at ?? Warsaw);
        return e;
    }

    /// <summary>A node of the given kind near Warsaw, using the server's own generator and secret seed.</summary>
    private WorldNode FindNode(string kind)
    {
        var config = _host.Service<WorldConfig>();
        var seed = _host.Service<WorldSecret>().Seed;
        for (var i = 0; i < 400; i++)
        {
            var probe = new GeoPoint(Warsaw.Lat + ((i % 20) * 0.01), Warsaw.Lon + ((i / 20) * 0.01));
            var node = WorldGenerator.Nearby(config, seed, probe, _host.Clock.GetUtcNow()).FirstOrDefault(x => x.Kind == kind);
            if (node is not null) return node;
        }

        throw new AssertionException($"no {kind} found near Warsaw for this seed");
    }

    private static GeoPoint SouthOf(GeoPoint p, double meters) => new(p.Lat - GeoMath.MetersToLatDegrees(meters), p.Lon);

    private async Task<long> QiAsync(Explorer e) =>
        (await ApiHost.Json(await e.P.GetAsync($"/api/v1/characters/{e.Id}/cultivation")))["qi"]!.GetValue<long>();

    [Fact]
    public async Task Exploration_session_lifecycle_and_sample_outcomes()
    {
        var e = await ExplorerAsync();
        var again = await e.StartAsync();
        Assert.Equal(1, again.Int("acceptedSamples"), "starting twice keeps the same session");

        var batch = await e.P.PostAsync($"/api/v1/characters/{e.Id}/exploration/samples", new
        {
            samples = new object[]
            {
                new { lat = 95.0, lon = 0.0, accuracyM = 5, capturedAt = _host.Clock.GetUtcNow() },
                new { lat = Warsaw.Lat, lon = Warsaw.Lon, accuracyM = 5, capturedAt = _host.Clock.GetUtcNow().AddHours(1) },
            },
        });
        var outcomes = (await ApiHost.Json(batch))["outcomes"]!.AsArray().Select(o => o!.GetValue<string>()).ToList();
        Assert.SequenceEqual(["invalid", "future"], outcomes);

        var tooMany = await e.P.PostAsync($"/api/v1/characters/{e.Id}/exploration/samples",
            new { samples = Enumerable.Range(0, 21).Select(i => new { lat = Warsaw.Lat, lon = Warsaw.Lon, accuracyM = 5, capturedAt = _host.Clock.GetUtcNow() }) });
        Assert.Equal("samples_invalid", (await ApiHost.Json(tooMany)).Str("code"));

        var stopped = await ApiHost.Json(await e.P.PostAsync($"/api/v1/characters/{e.Id}/exploration/stop"));
        Assert.Equal("ended", stopped.Str("status"));
        Assert.Equal("no_session", (await e.SendAsync(Warsaw, expected: HttpStatusCode.Conflict)).Str("code"));
        Assert.Equal("no_position", (await e.NearbyAsync()).Str("code"));
    }

    [Fact]
    public async Task Idle_sessions_expire_on_their_own()
    {
        var e = await ExplorerAsync();
        _host.Clock.Advance(TimeSpan.FromMinutes(11));
        await e.P.RefreshAsync();
        var res = await e.P.GetAsync($"/api/v1/characters/{e.Id}/exploration");
        Assert.Equal("no_session", (await ApiHost.Json(res)).Str("code"));
    }

    [Fact]
    public async Task Harvesting_a_qi_node_pays_once_through_the_outbox()
    {
        var node = FindNode("qi_spring");
        var e = await ExplorerAsync(at: SouthOf(node.Point, 150));
        await e.WalkToAsync(node.Point);
        var view = (await e.NearbyAsync())["nodes"]!.AsArray().Single(n => n!.Str("key") == node.Key)!;
        Assert.True(view["inRange"]!.GetValue<bool>());
        var expected = view["rewardQi"]!.GetValue<long>();

        var key = Player.NewKey();
        var first = await ApiHost.Json(await e.InteractAsync(node.Key, key));
        Assert.Equal(expected, first["rewardQi"]!.GetValue<long>());
        var replay = await ApiHost.Json(await e.InteractAsync(node.Key, key));
        Assert.Equal(first.Compact(), replay.Compact());
        var twice = await e.InteractAsync(node.Key);
        Assert.Equal("already_harvested", (await ApiHost.Json(twice)).Str("code"));

        var before = await QiAsync(e);                              // the idle flow may have added a little already
        await _host.DrainOutboxAsync();
        Assert.Equal(before + expected, await QiAsync(e));
        Assert.True((await e.NearbyAsync())["nodes"]!.AsArray().Single(n => n!.Str("key") == node.Key)!["harvested"]!.GetValue<bool>());
    }

    // Acceptance #9: a GPS sample with an unrealistic jump does not automatically grant a reward.
    [Fact]
    public async Task Teleporting_next_to_a_node_does_not_earn_its_reward()
    {
        var node = FindNode("qi_spring");
        var e = await ExplorerAsync(at: Wawel);                    // ~250 km away
        _host.Clock.Advance(TimeSpan.FromSeconds(30));
        var jump = await e.SendAsync(node.Point);                   // "arrives" 30 s later
        Assert.Equal("relocated", jump["outcomes"]![0]!.GetValue<string>());
        Assert.False(jump["session"]!["positionVerified"]!.GetValue<bool>());

        var denied = await e.InteractAsync(node.Key);
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Equal("relocation_cooldown", (await ApiHost.Json(denied)).Str("code"));
        await _host.DrainOutboxAsync();
        Assert.True(await QiAsync(e) < 10, "only the idle Qi flow of a few seconds, not the spring's reward");

        // Not a punishment: a genuine traveller who stays put is trusted again after the cooldown.
        for (var i = 0; i < 7; i++)
        {
            _host.Clock.Advance(TimeSpan.FromSeconds(50));
            await e.SendAsync(node.Point);
        }

        Assert.Equal(HttpStatusCode.OK, (await e.InteractAsync(node.Key)).StatusCode);
    }

    [Fact]
    public async Task Weak_gps_and_distance_block_rewards_without_penalties()
    {
        var node = FindNode("qi_spring");
        var e = await ExplorerAsync(at: SouthOf(node.Point, 100));
        var far = await e.InteractAsync(node.Key);
        Assert.Equal("too_far", (await ApiHost.Json(far)).Str("code"));

        await e.WalkToAsync(node.Point);
        _host.Clock.Advance(TimeSpan.FromSeconds(5));
        await e.SendAsync(node.Point, accuracy: 200);
        var weak = await e.InteractAsync(node.Key);
        Assert.Equal("low_accuracy", (await ApiHost.Json(weak)).Str("code"));

        _host.Clock.Advance(TimeSpan.FromSeconds(5));
        await e.SendAsync(node.Point, accuracy: 6);
        Assert.Equal(HttpStatusCode.OK, (await e.InteractAsync(node.Key)).StatusCode);
    }

    [Fact]
    public async Task Forged_and_expired_node_keys_are_rejected()
    {
        var node = FindNode("spirit_herb");
        var e = await ExplorerAsync(at: SouthOf(node.Point, 60));
        await e.WalkToAsync(node.Point);
        var parts = node.Key.Split('.');
        foreach (var forged in new[] { $"{parts[0]}.{parts[1]}.{parts[2]}.9", "1.2.3.4", "garbage", $"{long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture) - 1}.{parts[1]}.{parts[2]}.{parts[3]}" })
        {
            Assert.Equal("node_not_found", (await ApiHost.Json(await e.InteractAsync(forged))).Str("code"), forged);
        }

        _host.Clock.Advance(TimeSpan.FromHours(6));                // next spawn epoch: the node has faded
        await e.P.RefreshAsync();
        await e.StartAsync();                                       // the idle session expired meanwhile
        await e.SendAsync(node.Point);
        Assert.Equal("node_not_found", (await ApiHost.Json(await e.InteractAsync(node.Key))).Str("code"));
    }

    [Fact]
    public async Task Herbs_land_in_the_bag_once_through_the_outbox()
    {
        var node = FindNode("spirit_herb");
        var e = await ExplorerAsync(at: SouthOf(node.Point, 60));
        await e.WalkToAsync(node.Point);
        await _host.DrainOutboxAsync();
        var res = await ApiHost.Json(await e.InteractAsync(node.Key));
        var gathered = res["items"]!.AsArray().Single()!;
        Assert.Equal("spirit_herb", gathered.Str("itemId"));
        var qty = gathered.Int("quantity");
        Assert.True(qty is >= 1 and <= 3, $"quantity {qty}");

        var inventory = $"/api/v1/characters/{e.Id}/inventory";
        Assert.Equal(0, (await ApiHost.Json(await e.P.GetAsync(inventory)))["stacks"]!.AsArray().Count, "materials arrive via the worker");
        Assert.Equal(2, await _host.DrainOutboxAsync(), "Qi reward + items gathered");
        Assert.Equal(0, await _host.DrainOutboxAsync());
        var stack = (await ApiHost.Json(await e.P.GetAsync(inventory)))["stacks"]!.AsArray().Single()!;
        Assert.Equal("spirit_herb", stack.Str("itemId"));
        Assert.Equal(qty, stack.Int("quantity"));
    }

    [Fact]
    public async Task Spirit_beast_starts_a_battle_and_blocks_a_second_one()
    {
        var beast = FindNode("spirit_beast");
        var e = await ExplorerAsync(at: SouthOf(beast.Point, 80));
        await e.WalkToAsync(beast.Point);
        var res = await ApiHost.Json(await e.InteractAsync(beast.Key));
        Assert.Equal("spirit_beast", res.Str("kind"));
        var battle = await ApiHost.Json(await e.P.GetAsync($"/api/v1/combat/battles/{res.Str("battleId")}"));
        Assert.Equal("active", battle.Str("status"));
        Assert.Equal("grey_wolf", battle.Str("npcId"));

        // Already fighting: no second battle can start.
        var other = await e.P.PostAsync("/api/v1/combat/battles/pve", new { characterId = e.Id, npcId = "training_dummy" }, Player.NewKey());
        Assert.Equal("already_in_battle", (await ApiHost.Json(other)).Str("code"));
    }

    [Fact]
    public async Task Flame_sites_reward_the_flame_path_most_with_a_cooldown()
    {
        var flame = await ExplorerAsync("flame", Wawel);
        var near = (await flame.NearbyAsync())["nearestFlameSite"]!;
        Assert.Equal("wawel", near.Str("id"));
        Assert.True(near["inRange"]!.GetValue<bool>());

        var first = await ApiHost.Json(await flame.AttuneAsync("wawel"));
        Assert.Equal(250, first["rewardQi"]!.GetValue<long>());
        Assert.Equal("attunement_cooldown", (await ApiHost.Json(await flame.AttuneAsync("wawel"))).Str("code"));
        Assert.Equal("too_far", (await ApiHost.Json(await flame.AttuneAsync("etna"))).Str("code"));
        Assert.Equal("site_not_found", (await ApiHost.Json(await flame.AttuneAsync("atlantis"))).Str("code"));

        var other = await ExplorerAsync("body", Wawel);
        Assert.Equal(40, (await ApiHost.Json(await other.AttuneAsync("wawel")))["rewardQi"]!.GetValue<long>());

        await _host.DrainOutboxAsync();
        Assert.Equal(250, await QiAsync(flame));
        Assert.Equal(40, await QiAsync(other));

        _host.Clock.Advance(TimeSpan.FromHours(21));
        await flame.P.RefreshAsync();
        await flame.StartAsync();
        await flame.SendAsync(Wawel);
        Assert.Equal(250, (await ApiHost.Json(await flame.AttuneAsync("wawel")))["rewardQi"]!.GetValue<long>());
    }

    [Fact]
    public async Task Repeated_teleports_flag_the_session_and_leave_an_audit_trail_without_coordinates()
    {
        var e = await ExplorerAsync(at: Warsaw);
        for (var i = 0; i < 5; i++)
        {
            _host.Clock.Advance(TimeSpan.FromSeconds(20));
            await e.SendAsync(i % 2 == 0 ? Wawel : Warsaw);
        }

        var session = await ApiHost.Json(await e.P.GetAsync($"/api/v1/characters/{e.Id}/exploration"));
        Assert.True(session["flagged"]!.GetValue<bool>());
        Assert.Equal("session_flagged", session.Str("positionProblem"));

        var audit = await _host.Service<IUnitOfWorkFactory>().RunAsync(uow => _host.Service<IAuditLog>().ListForEntityAsync(uow, "exploration_session", session.Str("id"), default), default);
        var entry = audit.Single(a => a.Action == "geolocation.session_flagged");
        Assert.False(entry.DataJson.Contains("lat", StringComparison.OrdinalIgnoreCase) || entry.DataJson.Contains("52.2", StringComparison.Ordinal), entry.DataJson);
    }

    [Fact]
    public async Task Cannot_use_someone_elses_character_in_the_world()
    {
        var owner = await ExplorerAsync();
        var thief = await _host.RegisterAndLoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await thief.PostAsync($"/api/v1/characters/{owner.Id}/exploration/start")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await thief.GetAsync($"/api/v1/characters/{owner.Id}/world/nearby")).StatusCode);
        var res = await thief.PostAsync("/api/v1/world/interactions", new { characterId = owner.Id, nodeKey = "1.2.3.0" }, Player.NewKey());
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Hourly_interaction_limit_applies()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "game-config", "world.json"))
            .Replace("\"interactionsPerHour\": 60", "\"interactionsPerHour\": 1", StringComparison.Ordinal);
        var path = Path.Combine(Path.GetTempPath(), $"world-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        await _host.DisposeAsync();
        _host = await ApiHost.StartAsync(new() { ["GameConfig:WorldPath"] = path });

        var spring = FindNode("qi_spring");
        var herb = FindNode("spirit_herb");
        var e = await ExplorerAsync(at: SouthOf(spring.Point, 60));
        await e.WalkToAsync(spring.Point);
        Assert.Equal(HttpStatusCode.OK, (await e.InteractAsync(spring.Key)).StatusCode);
        Assert.Equal("interaction_limit", (await ApiHost.Json(await e.InteractAsync(herb.Key))).Str("code"));
    }

    private sealed class FakeRealWorld(GeoPoint place) : IRealWorldSource
    {
        public bool Enabled => true;

        public RealWeather? Weather(long rx, long ry, double sizeDegrees) => new("storm", 17.4, 95, DateTimeOffset.UnixEpoch.AddYears(56));

        public IReadOnlyList<RealPlace> Places(long rx, long ry, double sizeDegrees) =>
            (rx, ry) == ((long)Math.Floor(place.Lon / sizeDegrees), (long)Math.Floor(place.Lat / sizeDegrees)) ? [new RealPlace(123456789012, "sacred_shrine", "Testowa Kaplica", place)] : [];
    }

    [Fact]
    public async Task Real_places_and_weather_appear_and_a_place_pays_once_a_day()
    {
        await _host.DisposeAsync();
        var shrine = SouthOf(Warsaw, 20);
        _host = await ApiHost.StartAsync(services: s => s.AddSingleton<IRealWorldSource>(new FakeRealWorld(shrine)));
        var e = await ExplorerAsync();
        var near = await e.NearbyAsync();
        Assert.Equal("storm", near["region"]!.Str("weather"));
        Assert.True(near["region"]!["realWeather"]!.GetValue<bool>());
        Assert.Equal(17.4, near["region"]!["temperatureC"]!.GetValue<double>());
        var node = near["nodes"]!.AsArray().Select(n => n!.AsObject()).Single(n => n.Str("kind") == "sacred_shrine");
        Assert.Equal("Święte Miejsce: Testowa Kaplica", node.Str("namePl"));
        Assert.True(node["inRange"]!.GetValue<bool>());

        var first = await e.InteractAsync(node.Str("key"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync());
        Assert.True((await ApiHost.Json(first)).Int("rewardQi") > 0);
        Assert.Equal(HttpStatusCode.Conflict, (await e.InteractAsync(node.Str("key"))).StatusCode);

        // a forged key (unknown place, or another day) is rejected
        Assert.Equal(HttpStatusCode.NotFound, (await e.InteractAsync(node.Str("key").Replace("123456789012", "999", StringComparison.Ordinal))).StatusCode);
        _host.Clock.Advance(TimeSpan.FromDays(1));
        await e.P.RefreshAsync();
        await e.StartAsync();
        await e.SendAsync(Warsaw);
        var stale = await e.InteractAsync(node.Str("key"));
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode, await stale.Content.ReadAsStringAsync()); // yesterday's key
        var today = (await e.NearbyAsync())["nodes"]!.AsArray().Select(n => n!.AsObject()).Single(n => n.Str("kind") == "sacred_shrine");
        Assert.NotEqual(node.Str("key"), today.Str("key"));
        Assert.Equal(HttpStatusCode.OK, (await e.InteractAsync(today.Str("key"))).StatusCode);         // the place pays again the next day
    }
}
