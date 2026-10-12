using System.Text.Json;
using JianghuGo.Client.Application;
using JianghuGo.Client.Infrastructure;
using JianghuGo.Domain;
using JianghuGo.Modules.World.Application;
using JianghuGo.Modules.World.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

[Category("Client")]
public class ClientWorldTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private sealed class StjSerializer : IJsonSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public string Serialize(object value) => JsonSerializer.Serialize(value, Options);
        public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
    }

    /// <summary>Scripted device GPS driven by the test clock.</summary>
    private sealed class FakeLocation(ManualTimeProvider clock) : ILocationSource
    {
        private readonly List<LocationFix> _fixes = [];
        public LocationStatus Status { get; set; } = LocationStatus.Running;

        public void Fix(GeoPoint p, double accuracy = 8) => _fixes.Add(new LocationFix(p.Lat, p.Lon, accuracy, clock.GetUtcNow()));

        public IReadOnlyList<LocationFix> FixesSince(DateTimeOffset since) => [.. _fixes.Where(f => f.CapturedAt > since)];
    }

    private async Task<(ApiClient Api, CharacterDto Character)> PlayerAsync(string email)
    {
        var api = new ApiClient(new HttpClientTransport(_host.NewClient()), new StjSerializer(), new InMemoryTokenStore());
        await api.RegisterAsync(email, ApiHost.Password, default);
        await api.LoginAsync(email, ApiHost.Password, default);
        var creation = new CharacterCreationFlow(api);
        await creation.StartAsync(default);
        return (api, await creation.FinalizeAsync("Podroznik", "qi", default));
    }

    [Fact]
    public async Task Denied_permission_is_reported_without_calling_the_server()
    {
        var (api, c) = await PlayerAsync("brak@gps.dev");
        var gps = new FakeLocation(_host.Clock) { Status = LocationStatus.Denied };
        var flow = new ExplorationFlow(api, gps, c.Id);
        await flow.StartAsync(default);
        Assert.Equal("location_denied", flow.Problem);
        Assert.Null(flow.Nearby);
        gps.Status = LocationStatus.Unavailable;
        await flow.TickAsync(default);
        Assert.Equal("location_unavailable", flow.Problem);
    }

    [Fact]
    public async Task Client_walks_to_a_node_and_harvests_it()
    {
        var config = _host.Service<WorldConfig>();
        var seed = _host.Service<WorldSecret>().Seed;
        var start = new GeoPoint(52.2297, 21.0122);
        var node = WorldGenerator.Nearby(config, seed, start, _host.Clock.GetUtcNow()).First(n => n.Kind != "spirit_beast");

        var (api, c) = await PlayerAsync("spacer@gps.dev");
        var gps = new FakeLocation(_host.Clock);
        gps.Fix(start);
        var flow = new ExplorationFlow(api, gps, c.Id);
        await flow.StartAsync(default);
        Assert.True(flow.Nearby!.Nodes.Any(n => n.Key == node.Key));

        var steps = (int)Math.Ceiling(GeoMath.DistanceM(start, node.Point) / 40);
        for (var i = 1; i <= steps; i++)
        {
            _host.Clock.Advance(TimeSpan.FromSeconds(10));
            gps.Fix(new GeoPoint(start.Lat + ((node.Point.Lat - start.Lat) * i / steps), start.Lon + ((node.Point.Lon - start.Lon) * i / steps)));
            await flow.TickAsync(default);
        }

        Assert.Null(flow.Problem);
        Assert.Equal(TimeSpan.FromSeconds(5), flow.RecommendedInterval);
        var result = await flow.InteractAsync(node.Key, default);
        Assert.True(result.RewardQi > 0);
        Assert.True(flow.Nearby!.Nodes.Single(n => n.Key == node.Key).Harvested);

        _host.Clock.Advance(TimeSpan.FromSeconds(5));
        gps.Fix(node.Point, accuracy: 400);
        await flow.TickAsync(default);
        Assert.Equal("low_accuracy", flow.Problem);
    }

    [Fact]
    public void Web_mercator_tiles_and_pixels_round_trip()
    {
        var tile = WebMercator.TileFor(52.2297, 21.0122, 16);
        Assert.Equal(16, tile.Zoom);
        Assert.Equal(36593, tile.X);   // reference values from the standard OSM slippy-map formula (independent Python implementation)
        Assert.Equal(21579, tile.Y);
        var (x, y) = WebMercator.ToPixel(52.2297, 21.0122, 16);
        var (lat, lon) = WebMercator.FromPixel(x, y, 16);
        Assert.True(Math.Abs(lat - 52.2297) < 1e-9 && Math.Abs(lon - 21.0122) < 1e-9);
        Assert.Equal(new TileId(0, 0, 0), WebMercator.TileFor(89, 179.99, 0));
    }
}
