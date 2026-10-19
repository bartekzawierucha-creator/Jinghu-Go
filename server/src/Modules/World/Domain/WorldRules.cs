using System.Globalization;
using JianghuGo.Domain;

namespace JianghuGo.Modules.World.Domain;

public sealed record WorldConfig(
    string RulesVersion,
    double CellSizeDegrees,
    int NodeEpochHours,
    CountRange NodesPerCell,
    double NearbyRadiusM,
    double InteractionRadiusM,
    int InteractionsPerHour,
    IReadOnlyList<NodeKind> NodeKinds,
    FlameRules Flame,
    EventRules Events,
    RegionRules? Regions = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? EraYields = null)
{
    /// <summary>
    /// 1.9: a cultivator of a later era also finds the material's finer counterpart (spirit herb → lotus seed → heaven dew…),
    /// half as many; each entry lists the finer material per era 1, 2… (the last one stays for later eras).
    /// </summary>
    public string? FinerYield(string item, int era) =>
        era > 0 && EraYields is not null && EraYields.TryGetValue(item, out var tiers) && tiers.Count > 0 ? tiers[Math.Clamp(era - 1, 0, tiers.Count - 1)] : null;
}

public sealed record CountRange(int Min, int Max);

/// <param name="Time">"night" / "day": spawns only then (solar time at the node); null = always.</param>
/// <param name="Hidden">A hidden place: visible only from close by.</param>
/// <param name="NpcId">The beast fought here; "@region" = the region's boss.</param>
public sealed record NodeKind(string Id, string NamePl, string NameEn, int Weight, int RewardQi, string? NpcId, IReadOnlyList<NodeYield>? Yields = null,
    string? Time = null, bool Hidden = false);

/// <summary>Regions: named areas (a few km) with a terrain that shapes what spawns, a region boss and changing weather.</summary>
public sealed record RegionRules(double SizeDegrees, int WeatherEpochHours, double HiddenRevealM, IReadOnlyList<string> PrefixesPl, IReadOnlyList<string> NounsPl,
    IReadOnlyList<Terrain> Terrains, IReadOnlyList<Weather> Weathers);

/// <param name="NodeWeightPermille">Multiplier of node kind weights in this terrain (‰; missing = 1000).</param>
/// <param name="Bosses">NPCs that can rule a region of this terrain.</param>
public sealed record Terrain(string Id, string NamePl, int Weight, IReadOnlyDictionary<string, int> NodeWeightPermille, IReadOnlyList<string> Bosses);

/// <param name="RewardPermille">Qi reward multiplier by node kind ("*" = every kind).</param>
public sealed record Weather(string Id, string NamePl, int Weight, IReadOnlyDictionary<string, int> RewardPermille);

public sealed record Region(string Key, string NamePl, Terrain Terrain, Weather? Weather, string? BossNpcId, DateTimeOffset WeatherEndsAt);

/// <summary>Materials a node can give (item ids are validated against items.json by a cross-config test and by Inventory on receipt).</summary>
public sealed record NodeYield(string Item, int ChancePermille, int Min, int Max);

public sealed record FlameRules(double RadiusM, int CooldownHours, int RewardQiFlamePath, int RewardQiOtherPaths, IReadOnlyList<FlameSite> Sites);

public sealed record FlameSite(string Id, string NamePl, double Lat, double Lon)
{
    public GeoPoint Point => new(Lat, Lon);
}

public sealed record EventRules(double RegionSizeDegrees, int EpochHours, int ChancePermille, IReadOnlyList<EventKind> Kinds);

public sealed record EventKind(string Id, string NamePl, string NameEn, int Weight, int RewardMultiplierPermille);

/// <summary>A spawned point of interest. <see cref="Key"/> = "epoch.cellX.cellY.index" (regenerable, not stored).</summary>
public sealed record WorldNode(string Key, string Kind, GeoPoint Point, long Epoch, DateTimeOffset ExpiresAt);

public sealed record ActiveEvent(string Key, string Kind, string NamePl, int RewardMultiplierPermille, DateTimeOffset EndsAt);

public static class WorldConfigValidator
{
    public static IReadOnlyList<string> Validate(WorldConfig? c)
    {
        if (c is null) return ["Config is missing."];
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(c.RulesVersion)) e.Add("rulesVersion is required.");
        if (c.CellSizeDegrees is < 0.001 or > 0.1) e.Add("cellSizeDegrees must be 0.001-0.1.");
        if (c.NodeEpochHours is < 1 or > 168) e.Add("nodeEpochHours must be 1-168.");
        if (c.NodesPerCell is null || c.NodesPerCell.Min < 0 || c.NodesPerCell.Max < c.NodesPerCell.Min || c.NodesPerCell.Max > 20) e.Add("nodesPerCell invalid.");
        if (c.NearbyRadiusM is < 50 or > 5000 || c.InteractionRadiusM is < 5 or > 500 || c.InteractionRadiusM > c.NearbyRadiusM) e.Add("radii invalid.");
        if (c.InteractionsPerHour is < 1 or > 1000) e.Add("interactionsPerHour must be 1-1000.");
        if (c.NodeKinds is null || c.NodeKinds.Count == 0 || c.NodeKinds.Any(k => k.Weight <= 0 || k.RewardQi < 0) || c.NodeKinds.Select(k => k.Id).Distinct().Count() != c.NodeKinds.Count)
            e.Add("nodeKinds invalid (unique ids, positive weights, rewards >= 0).");
        foreach (var k in c.NodeKinds ?? [])
        {
            if (k.Yields is null) continue;
            if (k.NpcId is not null && k.Yields.Count > 0) e.Add($"node {k.Id}: beast nodes give loot through combat, not yields.");
            if (k.Yields.Any(y => string.IsNullOrWhiteSpace(y.Item) || y.ChancePermille is < 1 or > 1000 || y.Min < 1 || y.Max < y.Min || y.Max > 20))
                e.Add($"node {k.Id}: yields invalid.");
        }

        if (c.Flame is null || c.Flame.Sites is null || c.Flame.Sites.Count != 12) e.Add("flame path needs exactly 12 fixed sites.");
        else
        {
            if (c.Flame.Sites.Any(s => !GeoPoint.IsValid(s.Lat, s.Lon))) e.Add("flame site with invalid coordinates.");
            if (c.Flame.Sites.Select(s => s.Id).Distinct().Count() != 12) e.Add("flame site ids must be unique.");
            if (c.Flame.RadiusM is < 10 or > 2000 || c.Flame.CooldownHours < 1 || c.Flame.RewardQiFlamePath < c.Flame.RewardQiOtherPaths || c.Flame.RewardQiOtherPaths < 0)
                e.Add("flame rules invalid (Flame path must earn at least as much as other paths).");
        }

        if (c.Regions is { } r)
        {
            if (r.SizeDegrees < c.CellSizeDegrees || r.WeatherEpochHours is < 1 or > 48 || r.HiddenRevealM is < 5 or > 1000) e.Add("regions: size/weather/reveal invalid.");
            if (r.PrefixesPl is null || r.PrefixesPl.Count == 0 || r.NounsPl is null || r.NounsPl.Count == 0) e.Add("regions: name parts required.");
            if (r.Terrains is null || r.Terrains.Count == 0 || r.Terrains.Any(t => t.Weight <= 0 || t.Bosses is null || t.Bosses.Count == 0 || t.NodeWeightPermille is null
                    || t.NodeWeightPermille.Any(kv => kv.Value is < 0 or > 10000 || c.NodeKinds?.All(k => k.Id != kv.Key) == true)))
                e.Add("regions: terrains invalid (weights, bosses, known node kinds).");
            if (r.Weathers is null || r.Weathers.Count == 0 || r.Weathers.Any(w => w.Weight <= 0 || w.RewardPermille is null || w.RewardPermille.Any(kv => kv.Value is < 0 or > 5000)))
                e.Add("regions: weathers invalid.");
        }

        foreach (var k in c.NodeKinds ?? [])
        {
            if (k.Time is not (null or "night" or "day")) e.Add($"node {k.Id}: time must be night/day.");
            if (k.NpcId == "@region" && c.Regions is null) e.Add($"node {k.Id}: region bosses need regions.");
        }

        if (c.Events is null || c.Events.RegionSizeDegrees < c.CellSizeDegrees || c.Events.EpochHours < 1 || c.Events.ChancePermille is < 0 or > 1000
            || c.Events.Kinds is null || c.Events.Kinds.Count == 0 || c.Events.Kinds.Any(k => k.Weight <= 0 || k.RewardMultiplierPermille is < 1000 or > 5000))
            e.Add("events invalid.");
        return e;
    }
}

/// <summary>
/// Procedural, deterministic world: nodes and events are a pure function of (secret world seed, time bucket, cell).
/// Nothing about spawns is stored; only claims are. The seed is a server secret so the map cannot be precomputed.
/// </summary>
public static class WorldGenerator
{
    public static long NodeEpoch(WorldConfig c, DateTimeOffset at) => at.ToUnixTimeSeconds() / (c.NodeEpochHours * 3600L);

    public static DateTimeOffset NodeEpochEnd(WorldConfig c, long epoch) => DateTimeOffset.FromUnixTimeSeconds((epoch + 1) * c.NodeEpochHours * 3600L);

    public static (long X, long Y) Cell(WorldConfig c, GeoPoint p) =>
        ((long)Math.Floor(p.Lon / c.CellSizeDegrees), (long)Math.Floor(p.Lat / c.CellSizeDegrees));

    /// <summary>Solar hour (0-24) at a longitude: UTC shifted by 15° per hour. Good enough for day and night.</summary>
    public static double SolarHour(DateTimeOffset at, double lon)
    {
        var h = (at.UtcDateTime.TimeOfDay.TotalHours + (lon / 15.0)) % 24.0;
        return h < 0 ? h + 24 : h;
    }

    public static bool IsNight(DateTimeOffset at, double lon)
    {
        var h = SolarHour(at, lon);
        return h >= 20 || h < 5;
    }

    /// <summary>Kind weights for a cell: the region's terrain and the time of day (night kinds only at night).</summary>
    private static IReadOnlyList<(NodeKind Kind, int Weight)> Weights(WorldConfig c, ulong worldSeed, long epoch, long cx, long cy)
    {
        var lat = (cy + 0.5) * c.CellSizeDegrees;
        var lon = (cx + 0.5) * c.CellSizeDegrees;
        var mid = DateTimeOffset.FromUnixTimeSeconds((epoch * c.NodeEpochHours * 3600L) + (c.NodeEpochHours * 1800L));
        var night = IsNight(mid, lon);
        var terrain = c.Regions is null || !GeoPoint.IsValid(lat, lon) ? null : RegionAt(c, worldSeed, new GeoPoint(lat, lon), mid).Terrain;
        var list = new List<(NodeKind, int)>();
        foreach (var k in c.NodeKinds)
        {
            if (k.Time == "night" && !night || k.Time == "day" && night) continue;
            var w = terrain is null ? k.Weight : (int)((long)k.Weight * terrain.NodeWeightPermille.GetValueOrDefault(k.Id, 1000) / 1000);
            if (w > 0) list.Add((k, w));
        }

        return list;
    }

    public static IReadOnlyList<WorldNode> NodesInCell(WorldConfig c, ulong worldSeed, long epoch, long cx, long cy)
    {
        var rng = new DeterministicRandom(SeedMix.Mix(worldSeed, 1, epoch, cx, cy));
        var count = rng.NextInt(c.NodesPerCell.Min, c.NodesPerCell.Max + 1);
        var weights = Weights(c, worldSeed, epoch, cx, cy);
        var totalWeight = weights.Sum(k => k.Weight);
        var nodes = new List<WorldNode>(count);
        for (var i = 0; i < count && totalWeight > 0; i++)
        {
            var pick = rng.NextInt(0, totalWeight);
            var kind = weights[^1].Kind;
            foreach (var (k, w) in weights)
            {
                pick -= w;
                if (pick < 0)
                {
                    kind = k;
                    break;
                }
            }

            var lat = (cy + (rng.NextInt(0, 1_000_000) / 1_000_000.0)) * c.CellSizeDegrees;
            var lon = (cx + (rng.NextInt(0, 1_000_000) / 1_000_000.0)) * c.CellSizeDegrees;
            if (!GeoPoint.IsValid(lat, lon)) continue; // cells straddling the poles / antimeridian
            nodes.Add(new WorldNode(Key(epoch, cx, cy, i), kind.Id, new GeoPoint(lat, lon), epoch, NodeEpochEnd(c, epoch)));
        }

        return nodes;
    }

    public static string Key(long epoch, long cx, long cy, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{epoch}.{cx}.{cy}.{index}");

    public static bool TryParseKey(string? key, out long epoch, out long cx, out long cy, out int index)
    {
        epoch = cx = cy = index = 0;
        var parts = key?.Split('.');
        return parts is { Length: 4 }
               && long.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out epoch)
               && long.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out cx)
               && long.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out cy)
               && int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    /// <summary>The node for a key if it exists in the CURRENT epoch; regenerating it proves it is real.</summary>
    public static WorldNode? Resolve(WorldConfig c, ulong worldSeed, string? key, DateTimeOffset now)
    {
        if (!TryParseKey(key, out var epoch, out var cx, out var cy, out var index) || epoch != NodeEpoch(c, now)) return null;
        var nodes = NodesInCell(c, worldSeed, epoch, cx, cy);
        return nodes.FirstOrDefault(n => n.Key == key);
    }

    public static IReadOnlyList<WorldNode> Nearby(WorldConfig c, ulong worldSeed, GeoPoint at, DateTimeOffset now)
    {
        var epoch = NodeEpoch(c, now);
        var dLat = GeoMath.MetersToLatDegrees(c.NearbyRadiusM);
        var dLon = GeoMath.MetersToLonDegrees(c.NearbyRadiusM, at.Lat);
        var (minX, minY) = Cell(c, new GeoPoint(Math.Max(-90, at.Lat - dLat), Math.Max(-180, at.Lon - dLon)));
        var (maxX, maxY) = Cell(c, new GeoPoint(Math.Min(90, at.Lat + dLat), Math.Min(180, at.Lon + dLon)));
        var result = new List<WorldNode>();
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            result.AddRange(NodesInCell(c, worldSeed, epoch, x, y).Where(n => GeoMath.DistanceM(at, n.Point) <= c.NearbyRadiusM));
        }

        return [.. result.OrderBy(n => GeoMath.DistanceM(at, n.Point))];
    }

    public static ActiveEvent? EventAt(WorldConfig c, ulong worldSeed, GeoPoint at, DateTimeOffset now)
    {
        var e = c.Events;
        var epoch = now.ToUnixTimeSeconds() / (e.EpochHours * 3600L);
        var rx = (long)Math.Floor(at.Lon / e.RegionSizeDegrees);
        var ry = (long)Math.Floor(at.Lat / e.RegionSizeDegrees);
        var rng = new DeterministicRandom(SeedMix.Mix(worldSeed, 2, epoch, rx, ry));
        if (rng.NextInt(0, 1000) >= e.ChancePermille) return null;
        var pick = rng.NextInt(0, e.Kinds.Sum(k => k.Weight));
        foreach (var k in e.Kinds)
        {
            pick -= k.Weight;
            if (pick < 0)
                return new ActiveEvent(string.Create(CultureInfo.InvariantCulture, $"{epoch}.{rx}.{ry}"), k.Id, k.NamePl, k.RewardMultiplierPermille,
                    DateTimeOffset.FromUnixTimeSeconds((epoch + 1) * e.EpochHours * 3600L));
        }

        return null;
    }

    /// <summary>Materials from one harvest. <paramref name="seed"/> comes from the server CSPRNG (one claim per node and character).</summary>
    public static IReadOnlyList<(string Item, int Quantity)> RollYields(NodeKind kind, ulong seed)
    {
        var rng = new DeterministicRandom(seed);
        var list = new List<(string, int)>();
        foreach (var y in kind.Yields ?? [])
        {
            if (rng.NextInt(0, 1000) < y.ChancePermille) list.Add((y.Item, rng.NextInt(y.Min, y.Max + 1)));
        }

        return list;
    }

    public static long NodeReward(WorldConfig c, string kind, ActiveEvent? ev, Weather? weather = null)
    {
        var w = weather is null ? 1000 : weather.RewardPermille.GetValueOrDefault(kind, weather.RewardPermille.GetValueOrDefault("*", 1000));
        return (long)c.NodeKinds.First(k => k.Id == kind).RewardQi * (ev?.RewardMultiplierPermille ?? 1000) / 1000 * w / 1000;
    }

    private static T Pick<T>(IReadOnlyList<T> items, Func<T, int> weight, DeterministicRandom rng)
    {
        var pick = rng.NextInt(0, items.Sum(weight));
        foreach (var x in items)
        {
            pick -= weight(x);
            if (pick < 0) return x;
        }

        return items[^1];
    }

    /// <summary>The region at a point: a stable name, terrain and boss (per region), and the weather of the current weather epoch.</summary>
    public static Region RegionAt(WorldConfig c, ulong worldSeed, GeoPoint at, DateTimeOffset now)
    {
        var r = c.Regions ?? throw new InvalidOperationException("Regions are not configured.");
        var rx = (long)Math.Floor(at.Lon / r.SizeDegrees);
        var ry = (long)Math.Floor(at.Lat / r.SizeDegrees);
        var rng = new DeterministicRandom(SeedMix.Mix(worldSeed, 3, 0, rx, ry));
        var name = r.PrefixesPl[rng.NextInt(0, r.PrefixesPl.Count)] + " " + r.NounsPl[rng.NextInt(0, r.NounsPl.Count)];
        var terrain = Pick(r.Terrains, t => t.Weight, rng);
        var boss = terrain.Bosses[rng.NextInt(0, terrain.Bosses.Count)];
        var wEpoch = now.ToUnixTimeSeconds() / (r.WeatherEpochHours * 3600L);
        var weather = Pick(r.Weathers, w => w.Weight, new DeterministicRandom(SeedMix.Mix(worldSeed, 4, wEpoch, rx, ry)));
        return new Region(string.Create(CultureInfo.InvariantCulture, $"{rx}.{ry}"), name, terrain, weather, boss,
            DateTimeOffset.FromUnixTimeSeconds((wEpoch + 1) * r.WeatherEpochHours * 3600L));
    }
}
