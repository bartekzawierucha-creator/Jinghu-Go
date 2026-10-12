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
    EventRules Events);

public sealed record CountRange(int Min, int Max);

public sealed record NodeKind(string Id, string NamePl, string NameEn, int Weight, int RewardQi, string? NpcId);

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
        if (c.Flame is null || c.Flame.Sites is null || c.Flame.Sites.Count != 12) e.Add("flame path needs exactly 12 fixed sites.");
        else
        {
            if (c.Flame.Sites.Any(s => !GeoPoint.IsValid(s.Lat, s.Lon))) e.Add("flame site with invalid coordinates.");
            if (c.Flame.Sites.Select(s => s.Id).Distinct().Count() != 12) e.Add("flame site ids must be unique.");
            if (c.Flame.RadiusM is < 10 or > 2000 || c.Flame.CooldownHours < 1 || c.Flame.RewardQiFlamePath < c.Flame.RewardQiOtherPaths || c.Flame.RewardQiOtherPaths < 0)
                e.Add("flame rules invalid (Flame path must earn at least as much as other paths).");
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

    public static IReadOnlyList<WorldNode> NodesInCell(WorldConfig c, ulong worldSeed, long epoch, long cx, long cy)
    {
        var rng = new DeterministicRandom(SeedMix.Mix(worldSeed, 1, epoch, cx, cy));
        var count = rng.NextInt(c.NodesPerCell.Min, c.NodesPerCell.Max + 1);
        var totalWeight = c.NodeKinds.Sum(k => k.Weight);
        var nodes = new List<WorldNode>(count);
        for (var i = 0; i < count; i++)
        {
            var pick = rng.NextInt(0, totalWeight);
            var kind = c.NodeKinds[^1];
            foreach (var k in c.NodeKinds)
            {
                pick -= k.Weight;
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

    public static long NodeReward(WorldConfig c, string kind, ActiveEvent? ev) =>
        (long)c.NodeKinds.First(k => k.Id == kind).RewardQi * (ev?.RewardMultiplierPermille ?? 1000) / 1000;
}
