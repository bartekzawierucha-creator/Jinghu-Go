using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using JianghuGo.Domain;
using JianghuGo.Modules.World.Domain;
using Microsoft.Extensions.Logging;

namespace JianghuGo.Modules.World.Application;

// 2.1 — the real world: real weather (Open-Meteo) and real places (OpenStreetMap via Overpass) around the player.
//
// Privacy: the services are asked about the REGION (a grid cell of ~3 km), never about the player's position, and nothing
// about the player is sent. Latency: answers are cached per region (weather 30 min, places 24 h); a cache miss starts
// a background refresh and the world falls back to its procedural weather and places until the answer arrives.
// Any failure simply keeps the procedural world.

/// <summary>Current weather of a region as the game understands it.</summary>
public sealed record RealWeather(string WeatherId, double TemperatureC, int WmoCode, DateTimeOffset FetchedAt);

/// <summary>A real place mapped to a world node kind. <paramref name="OsmId"/> is the OpenStreetMap node id.</summary>
public sealed record RealPlace(long OsmId, string Kind, string? Name, GeoPoint Point);

public interface IRealWorldSource
{
    bool Enabled { get; }

    /// <summary>Cached weather of the region, or null (and a refresh is started) when not known yet.</summary>
    RealWeather? Weather(long rx, long ry, double sizeDegrees);

    /// <summary>Cached real places of the region, or empty (and a refresh is started) when not known yet.</summary>
    IReadOnlyList<RealPlace> Places(long rx, long ry, double sizeDegrees);
}

/// <summary>Used when the real world is switched off (tests, servers without internet).</summary>
public sealed class NoRealWorld : IRealWorldSource
{
    public bool Enabled => false;

    public RealWeather? Weather(long rx, long ry, double sizeDegrees) => null;

    public IReadOnlyList<RealPlace> Places(long rx, long ry, double sizeDegrees) => [];
}

public static class RealWorldRules
{
    /// <summary>WMO weather code (Open-Meteo) → game weather id; hot clear days are "heat".</summary>
    public static string WeatherFor(int wmo, double temperatureC) => wmo switch
    {
        45 or 48 => "fog",
        >= 51 and <= 67 or >= 80 and <= 82 => "rain",
        >= 71 and <= 77 or 85 or 86 => "snow",
        >= 95 => "storm",
        _ => temperatureC >= 28 ? "heat" : "clear",
    };

    /// <summary>OpenStreetMap tags → node kind (null = not a place the game uses).</summary>
    public static string? KindFor(IReadOnlyDictionary<string, string> tags)
    {
        string? T(string k) => tags.TryGetValue(k, out var v) ? v : null;
        if (T("amenity") == "place_of_worship" || T("historic") == "wayside_shrine") return "sacred_shrine";
        if (T("historic") is "ruins" or "castle" or "archaeological_site" or "fort") return "ancient_ruins";
        if (T("historic") is "monument" or "memorial") return "sword_memorial";
        if (T("natural") == "peak") return "sacred_peak";
        if (T("natural") == "spring") return "qi_spring";
        if (T("waterway") == "waterfall") return "sacred_waterfall";
        if (T("tourism") == "viewpoint") return "moon_well";
        return null;
    }

    public const int MaxPlacesPerRegion = 60;

    /// <summary>Region center sent to the services (never the player's position).</summary>
    public static GeoPoint Center(long rx, long ry, double size) => new((ry + 0.5) * size, (rx + 0.5) * size);

    public static string OverpassQuery(long rx, long ry, double size)
    {
        var s = (ry * size).ToString("0.#####", CultureInfo.InvariantCulture);
        var w = (rx * size).ToString("0.#####", CultureInfo.InvariantCulture);
        var n = ((ry + 1) * size).ToString("0.#####", CultureInfo.InvariantCulture);
        var e = ((rx + 1) * size).ToString("0.#####", CultureInfo.InvariantCulture);
        var bbox = $"({s},{w},{n},{e})";
        return "[out:json][timeout:10];(" +
               $"node[\"amenity\"=\"place_of_worship\"]{bbox};" +
               $"node[\"historic\"~\"^(wayside_shrine|ruins|castle|archaeological_site|fort|monument|memorial)$\"]{bbox};" +
               $"node[\"natural\"~\"^(peak|spring)$\"]{bbox};" +
               $"node[\"waterway\"=\"waterfall\"]{bbox};" +
               $"node[\"tourism\"=\"viewpoint\"]{bbox};" +
               $");out {MaxPlacesPerRegion * 2};";
    }

    public static IReadOnlyList<RealPlace> ParseOverpass(JsonElement root)
    {
        var list = new List<RealPlace>();
        if (!root.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array) return list;
        foreach (var el in elements.EnumerateArray())
        {
            if (!el.TryGetProperty("id", out var id) || !el.TryGetProperty("lat", out var lat) || !el.TryGetProperty("lon", out var lon)) continue;
            var tags = new Dictionary<string, string>(StringComparer.Ordinal);
            if (el.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Object)
                foreach (var p in t.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) tags[p.Name] = p.Value.GetString()!;
            if (KindFor(tags) is not { } kind || !GeoPoint.IsValid(lat.GetDouble(), lon.GetDouble())) continue;
            var name = tags.TryGetValue("name:pl", out var pl) ? pl : tags.GetValueOrDefault("name");
            if (name is { Length: > 60 }) name = name[..60];
            list.Add(new RealPlace(id.GetInt64(), kind, name, new GeoPoint(lat.GetDouble(), lon.GetDouble())));
        }

        return [.. list.OrderBy(p => p.OsmId).Take(MaxPlacesPerRegion)];
    }

    /// <summary>Real node keys: a negative "epoch" (the UTC day) so they never collide with procedural keys; one claim a day.</summary>
    public static string Key(long day, long rx, long ry, long osmId) => string.Create(CultureInfo.InvariantCulture, $"-{day}.{rx}.{ry}.{osmId}");

    public static bool TryParseKey(string? key, out long day, out long rx, out long ry, out long osmId)
    {
        day = rx = ry = osmId = 0;
        var parts = key?.Split('.');
        return parts is { Length: 4 } && parts[0].StartsWith('-')
               && long.TryParse(parts[0][1..], NumberStyles.None, CultureInfo.InvariantCulture, out day)
               && long.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out rx)
               && long.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out ry)
               && long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out osmId);
    }

    public static long Day(DateTimeOffset at) => at.ToUnixTimeSeconds() / 86_400;
}

/// <summary>Open-Meteo and Overpass with per-region caches and background refresh (never blocks a request).</summary>
public sealed class OpenDataWorldSource(IHttpClientFactory http, TimeProvider clock, ILogger<OpenDataWorldSource> log) : IRealWorldSource, IDisposable
{
    public const string ClientName = "real-world";
    private static readonly TimeSpan WeatherTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PlacesTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(10);
    private const int MaxCachedRegions = 5000;

    private sealed record Entry<T>(T Value, DateTimeOffset Expires);

    private readonly ConcurrentDictionary<(long, long), Entry<RealWeather?>> _weather = new();
    private readonly ConcurrentDictionary<(long, long), Entry<IReadOnlyList<RealPlace>>> _places = new();
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();
    private readonly SemaphoreSlim _overpass = new(2); // be gentle with the public Overpass servers

    public bool Enabled => true;

    public void Dispose() => _overpass.Dispose();

    public RealWeather? Weather(long rx, long ry, double sizeDegrees)
    {
        var size = sizeDegrees;
        var now = clock.GetUtcNow();
        if (_weather.TryGetValue((rx, ry), out var e) && e.Expires > now) return e.Value;
        Refresh("w:" + rx + ":" + ry, () => FetchWeatherAsync(rx, ry, size));
        return e?.Value; // a stale value is better than none while refreshing
    }

    public IReadOnlyList<RealPlace> Places(long rx, long ry, double sizeDegrees)
    {
        var size = sizeDegrees;
        var now = clock.GetUtcNow();
        if (_places.TryGetValue((rx, ry), out var e) && e.Expires > now) return e.Value;
        Refresh("p:" + rx + ":" + ry, () => FetchPlacesAsync(rx, ry, size));
        return e?.Value ?? [];
    }

    private void Refresh(string key, Func<Task> fetch)
    {
        if (!_inFlight.TryAdd(key, 0)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await fetch();
            }
            catch (Exception ex)
            {
                // Region cells only, never positions.
                log.LogWarning("Real-world refresh {Key} failed: {Error}", key, ex.GetType().Name);
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
            }
        });
    }

    private async Task FetchWeatherAsync(long rx, long ry, double size)
    {
        Trim(_weather);
        var c = RealWorldRules.Center(rx, ry, size);
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={c.Lat:0.###}&longitude={c.Lon:0.###}&current=temperature_2m,weather_code");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var doc = await http.CreateClient(ClientName).GetFromJsonAsync<JsonElement>(url, cts.Token);
            var current = doc.GetProperty("current");
            var temp = current.GetProperty("temperature_2m").GetDouble();
            var code = current.GetProperty("weather_code").GetInt32();
            var now = clock.GetUtcNow();
            _weather[(rx, ry)] = new Entry<RealWeather?>(new RealWeather(RealWorldRules.WeatherFor(code, temp), temp, code, now), now + WeatherTtl);
        }
        catch (Exception) when (!_weather.ContainsKey((rx, ry)))
        {
            _weather[(rx, ry)] = new Entry<RealWeather?>(null, clock.GetUtcNow() + FailureTtl);
            throw;
        }
    }

    private async Task FetchPlacesAsync(long rx, long ry, double size)
    {
        Trim(_places);
        await _overpass.WaitAsync();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", RealWorldRules.OverpassQuery(rx, ry, size))]);
            using var res = await http.CreateClient(ClientName).PostAsync("https://overpass-api.de/api/interpreter", content, cts.Token);
            res.EnsureSuccessStatusCode();
            var doc = await res.Content.ReadFromJsonAsync<JsonElement>(cts.Token);
            _places[(rx, ry)] = new Entry<IReadOnlyList<RealPlace>>(RealWorldRules.ParseOverpass(doc), clock.GetUtcNow() + PlacesTtl);
        }
        catch (Exception)
        {
            if (!_places.ContainsKey((rx, ry))) _places[(rx, ry)] = new Entry<IReadOnlyList<RealPlace>>([], clock.GetUtcNow() + FailureTtl);
            throw;
        }
        finally
        {
            _overpass.Release();
        }
    }

    private static void Trim<T>(ConcurrentDictionary<(long, long), T> cache)
    {
        if (cache.Count < MaxCachedRegions) return;
        foreach (var k in cache.Keys.Take(cache.Count / 4)) cache.TryRemove(k, out _);
    }
}
