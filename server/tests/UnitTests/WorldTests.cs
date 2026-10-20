using JianghuGo.Domain;
using JianghuGo.Modules.World;
using JianghuGo.Modules.World.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

public class WorldTests
{
    private static readonly WorldConfig Config =
        WorldModule.LoadConfig(Path.Combine(AppContext.BaseDirectory, "game-config", "world.json"));

    private const ulong Seed = 424242;
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly GeoPoint Warsaw = new(52.2297, 21.0122);

    [Fact]
    public void Shipped_config_is_valid_with_twelve_flame_sites()
    {
        Assert.Equal(0, WorldConfigValidator.Validate(Config).Count);
        Assert.Equal(12, Config.Flame.Sites.Count);
        Assert.True(WorldConfigValidator.Validate(Config with { Flame = Config.Flame with { Sites = Config.Flame.Sites.Take(11).ToList() } }).Count > 0);
    }

    [Fact]
    public void Generation_is_deterministic_per_seed_epoch_and_cell()
    {
        var epoch = WorldGenerator.NodeEpoch(Config, Now);
        var a = WorldGenerator.NodesInCell(Config, Seed, epoch, 4202, 10445);
        var b = WorldGenerator.NodesInCell(Config, Seed, epoch, 4202, 10445);
        Assert.SequenceEqual(a, b);
        var cells = Enumerable.Range(0, 200).Select(i => (X: 4000L + i, Y: 10400L)).ToList();
        string Sig(ulong seed, long ep) => string.Join("|", cells.SelectMany(c => WorldGenerator.NodesInCell(Config, seed, ep, c.X, c.Y)).Select(n => n.Key + n.Kind));
        Assert.NotEqual(Sig(Seed, epoch), Sig(Seed + 1, epoch));
        Assert.NotEqual(Sig(Seed, epoch), Sig(Seed, epoch + 1));
    }

    [Fact]
    public void Nodes_stay_inside_their_cell_and_follow_kind_weights()
    {
        var counts = Config.NodeKinds.ToDictionary(k => k.Id, _ => 0);
        var total = 0;
        for (long x = 0; x < 100; x++)
        for (long y = 10000; y < 100 + 10000; y++)
        {
            var nodes = WorldGenerator.NodesInCell(Config, Seed, 7, x, y);
            Assert.InRange(nodes.Count, Config.NodesPerCell.Min, Config.NodesPerCell.Max);
            foreach (var n in nodes)
            {
                Assert.Equal((x, y), WorldGenerator.Cell(Config, n.Point));
                counts[n.Kind]++;
                total++;
            }
        }

        var weights = Config.NodeKinds.Sum(k => k.Weight);
        foreach (var k in Config.NodeKinds)
        {
            var share = 100.0 * counts[k.Id] / total;
            var expected = 100.0 * k.Weight / weights; // weights are relative
            Assert.True(Math.Abs(share - expected) < 2.5, $"{k.Id}: {share:0.0}% vs expected {expected:0.0}%");
        }
    }

    [Fact]
    public void Keys_resolve_only_in_the_current_epoch_and_cannot_be_forged()
    {
        var nearby = WorldGenerator.Nearby(Config, Seed, Warsaw, Now);
        Assert.True(nearby.Count > 0, "a 600 m radius around central Warsaw has nodes");
        var node = nearby[0];
        Assert.Equal(node, WorldGenerator.Resolve(Config, Seed, node.Key, Now));
        Assert.Null(WorldGenerator.Resolve(Config, Seed, node.Key, Now.AddHours(Config.NodeEpochHours)));
        Assert.NotEqual(node, WorldGenerator.Resolve(Config, Seed + 1, node.Key, Now)); // another world seed = another world
        Assert.True(WorldGenerator.TryParseKey(node.Key, out var e, out var cx, out var cy, out _));
        Assert.Null(WorldGenerator.Resolve(Config, Seed, WorldGenerator.Key(e, cx, cy, 17), Now));
        foreach (var junk in new[] { null, "", "1.2.3", "a.b.c.d", "1.2.3.-1", "1.2.3.4.5" })
            Assert.Null(WorldGenerator.Resolve(Config, Seed, junk, Now));
    }

    [Fact]
    public void Nearby_returns_nodes_within_radius_closest_first()
    {
        var nearby = WorldGenerator.Nearby(Config, Seed, Warsaw, Now);
        var distances = nearby.Select(n => GeoMath.DistanceM(Warsaw, n.Point)).ToList();
        Assert.True(distances.All(d => d <= Config.NearbyRadiusM));
        Assert.SequenceEqual(distances.Order(), distances);
    }

    [Fact]
    public void Events_occur_at_the_configured_rate_and_multiply_rewards()
    {
        var hits = 0;
        for (var i = 0; i < 4000; i++)
        {
            if (WorldGenerator.EventAt(Config, Seed, new GeoPoint(40 + (i % 80 * 0.05), i / 80 * 0.05), Now) is not null) hits++;
        }

        Assert.InRange(hits, 500, 700); // 15 % of 4000
        Assert.Equal(15, WorldGenerator.NodeReward(Config, "qi_spring", null));
        Assert.Equal(30, WorldGenerator.NodeReward(Config, "qi_spring", new ActiveEvent("k", "qi_storm", "x", 2000, Now)));
    }

    [Fact]
    public void Real_world_weather_places_and_keys_map_to_the_game()
    {
        Assert.Equal("rain", JianghuGo.Modules.World.Application.RealWorldRules.WeatherFor(61, 12));
        Assert.Equal("storm", JianghuGo.Modules.World.Application.RealWorldRules.WeatherFor(95, 20));
        Assert.Equal("snow", JianghuGo.Modules.World.Application.RealWorldRules.WeatherFor(73, -3));
        Assert.Equal("fog", JianghuGo.Modules.World.Application.RealWorldRules.WeatherFor(45, 8));
        Assert.Equal("heat", JianghuGo.Modules.World.Application.RealWorldRules.WeatherFor(0, 31));
        Assert.Equal("clear", JianghuGo.Modules.World.Application.RealWorldRules.WeatherFor(2, 18));

        var json = System.Text.Json.JsonDocument.Parse("""
            {"elements":[
              {"type":"node","id":42,"lat":50.0614,"lon":19.9372,"tags":{"amenity":"place_of_worship","name":"Kościół Mariacki"}},
              {"type":"node","id":7,"lat":50.05,"lon":19.93,"tags":{"natural":"peak","name:pl":"Kopiec"}},
              {"type":"node","id":9,"lat":50.05,"lon":19.93,"tags":{"shop":"bakery"}}
            ]}
            """).RootElement;
        var places = JianghuGo.Modules.World.Application.RealWorldRules.ParseOverpass(json);
        Assert.Equal(2, places.Count);
        Assert.Equal("sacred_peak", places[0].Kind);
        Assert.Equal("Kopiec", places[0].Name);
        Assert.Equal("sacred_shrine", places[1].Kind);
        Assert.True(places.All(p => Config.NodeKinds.Any(k => k.Id == p.Kind && k.RealOnly || k.Id == p.Kind)));

        var key = JianghuGo.Modules.World.Application.RealWorldRules.Key(20380, 665, -1669, 9_876_543_210);
        Assert.True(JianghuGo.Modules.World.Application.RealWorldRules.TryParseKey(key, out var day, out var rx, out var ry, out var id));
        Assert.Equal((20380L, 665L, -1669L, 9_876_543_210L), (day, rx, ry, id));
        Assert.False(WorldGenerator.TryParseKey(key, out var epoch, out _, out _, out _) && epoch >= 0); // never a procedural key
        Assert.False(JianghuGo.Modules.World.Application.RealWorldRules.TryParseKey("5.1.2.3", out _, out _, out _, out _));

        // real-only kinds never spawn procedurally
        foreach (var k in Config.NodeKinds.Where(k => k.RealOnly))
            for (long x = 0; x < 50; x++)
                Assert.True(WorldGenerator.NodesInCell(Config, Seed, 7, x, 10000).All(n => n.Kind != k.Id));
    }
}
