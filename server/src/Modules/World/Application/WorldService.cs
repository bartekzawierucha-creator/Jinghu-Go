using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Geolocation.Contracts;
using JianghuGo.Modules.World.Contracts;
using JianghuGo.Modules.World.Domain;

namespace JianghuGo.Modules.World.Application;

/// <summary>Server secret: knowing it would let anyone precompute every spawn on Earth.</summary>
public sealed record WorldSecret(ulong Seed);

public sealed record FlameAttunement(CharacterId CharacterId, string SiteId, DateTimeOffset LastAttunedAt, int Count);

public interface IWorldRepository
{
    /// <summary>False if this character already harvested the node.</summary>
    Task<bool> TryClaimAsync(IUnitOfWork uow, string nodeKey, CharacterId characterId, AccountId accountId, string kind, DateTimeOffset at, CancellationToken ct);
    Task<int> CountClaimsSinceAsync(IUnitOfWork uow, CharacterId characterId, DateTimeOffset since, CancellationToken ct);
    Task<IReadOnlySet<string>> ClaimedAmongAsync(IUnitOfWork uow, CharacterId characterId, IReadOnlyCollection<string> nodeKeys, CancellationToken ct);
    Task<FlameAttunement?> FindAttunementAsync(IUnitOfWork uow, CharacterId characterId, string siteId, CancellationToken ct);
    Task SaveAttunementAsync(IUnitOfWork uow, FlameAttunement attunement, CancellationToken ct);
}

public sealed record NodeView(string Key, string Kind, string NamePl, double Lat, double Lon, int DistanceM, bool InRange, bool Harvested, long RewardQi, DateTimeOffset ExpiresAt,
    string? NpcId = null, bool Hidden = false);

public sealed record FlameSiteView(string Id, string NamePl, double Lat, double Lon, long DistanceM, bool InRange, DateTimeOffset? AvailableAt);

public sealed record EventView(string Kind, string NamePl, int RewardMultiplierPermille, DateTimeOffset EndsAt);

public sealed record RegionView(string NamePl, string Terrain, string TerrainNamePl, string? Weather, string? WeatherNamePl, DateTimeOffset WeatherEndsAt, bool Night,
    string? BossNpcId, string? BossNamePl, bool RealWeather = false, double? TemperatureC = null);

public sealed record NearbyView(bool PositionVerified, string? PositionProblem, EventView? Event, IReadOnlyList<NodeView> Nodes, FlameSiteView NearestFlameSite,
    RegionView? Region = null);

public sealed record InteractCommand(Guid CharacterId, string? NodeKey);

public sealed record AttuneCommand(Guid CharacterId, string? SiteId);

public sealed record InteractionResult(string Kind, long RewardQi, Guid? BattleId, IReadOnlyList<WorldEvents.GatheredItem>? Items = null);

public sealed class WorldService(
    IUnitOfWorkFactory uowFactory,
    IWorldRepository repo,
    ICharacterDirectory characters,
    IPositionVerifier positions,
    ICombatEncounters encounters,
    IIdempotencyStore idempotency,
    IAuditLog audit,
    IOutbox outbox,
    ICorrelationContext correlation,
    WorldConfig config,
    WorldSecret secret,
    IRandomSeedSource seeds,
    TimeProvider clock,
    INpcNames npcNames,
    IEnumerable<ICombatRealm> realms,
    IRealWorldSource real)
{
    /// <summary>The region with the real weather when it is known (2.1); otherwise the procedural weather.</summary>
    private (Region Region, RealWeather? Real) RegionWithWeather(GeoPoint at, DateTimeOffset now)
    {
        var region = WorldGenerator.RegionAt(config, secret.Seed, at, now);
        if (!real.Enabled || config.Regions is null) return (region, null);
        var (rx, ry) = WorldGenerator.RegionCell(config, at);
        if (real.Weather(rx, ry, config.Regions.SizeDegrees) is not { } w || config.Regions.Weathers.FirstOrDefault(x => x.Id == w.WeatherId) is not { } weather)
            return (region, null);
        return (region with { Weather = weather, WeatherEndsAt = w.FetchedAt.AddMinutes(30) }, w);
    }

    /// <summary>Real places around a point as world nodes (2.1): they stay where they are; each can be visited once a day.</summary>
    private IEnumerable<WorldNode> RealNodes(GeoPoint at, DateTimeOffset now)
    {
        if (!real.Enabled || config.Regions is not { } r) yield break;
        var day = RealWorldRules.Day(now);
        var (rx, ry) = WorldGenerator.RegionCell(config, at);
        var dayEnd = DateTimeOffset.FromUnixTimeSeconds((day + 1) * 86_400);
        for (var x = rx - 1; x <= rx + 1; x++)
        for (var y = ry - 1; y <= ry + 1; y++)
            foreach (var p in real.Places(x, y, r.SizeDegrees))
                if (config.NodeKinds.Any(k => k.Id == p.Kind) && GeoMath.DistanceM(at, p.Point) <= config.NearbyRadiusM)
                    yield return new WorldNode(RealWorldRules.Key(day, x, y, p.OsmId), p.Kind, p.Point, -day, dayEnd);
    }

    private RealPlace? RealPlaceOf(string? key, DateTimeOffset now)
    {
        if (!real.Enabled || config.Regions is not { } r || !RealWorldRules.TryParseKey(key, out var day, out var rx, out var ry, out var osmId)) return null;
        if (day != RealWorldRules.Day(now)) return null;
        return real.Places(rx, ry, r.SizeDegrees).FirstOrDefault(p => p.OsmId == osmId);
    }
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");

    public Task<Result<NearbyView>> NearbyAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<NearbyView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var check = await positions.CheckAsync(uow, accountId, c.Id, now, ct);
            if (check.LastKnown is not { } at) return Error.Conflict("no_position", "Start exploring and send a location first.");

            var ev = WorldGenerator.EventAt(config, secret.Seed, at, now);
            var (region, realWeather) = config.Regions is null ? (null, null) : RegionWithWeather(at, now);
            var reveal = config.Regions?.HiddenRevealM ?? 0;
            var nodes = WorldGenerator.Nearby(config, secret.Seed, at, now)
                .Where(n => !config.NodeKinds.First(k => k.Id == n.Kind).Hidden || GeoMath.DistanceM(at, n.Point) <= reveal)
                .Concat(RealNodes(at, now)).OrderBy(n => GeoMath.DistanceM(at, n.Point)).ToList();
            var names = real.Enabled && config.Regions is not null
                ? nodes.Where(n => n.Epoch < 0).Select(n => (n.Key, Place: RealPlaceOf(n.Key, now))).Where(x => x.Place?.Name is not null)
                    .ToDictionary(x => x.Key, x => x.Place!.Name!)
                : new Dictionary<string, string>();
            var claimed = await repo.ClaimedAmongAsync(uow, c.Id, [.. nodes.Select(n => n.Key)], ct);
            var views = nodes.Select(n =>
            {
                var d = GeoMath.DistanceM(at, n.Point);
                var kind = config.NodeKinds.First(k => k.Id == n.Kind);
                return new NodeView(n.Key, n.Kind, names.TryGetValue(n.Key, out var placeName) ? kind.NamePl + ": " + placeName : kind.NamePl, n.Point.Lat, n.Point.Lon, (int)Math.Round(d), d <= config.InteractionRadiusM,
                    claimed.Contains(n.Key), WorldGenerator.NodeReward(config, n.Kind, ev, region?.Weather), n.ExpiresAt,
                    kind.NpcId == "@region" ? region?.BossNpcId : kind.NpcId, kind.Hidden);
            }).ToList();

            var site = config.Flame.Sites.MinBy(s => GeoMath.DistanceM(at, s.Point))!;
            var siteDistance = GeoMath.DistanceM(at, site.Point);
            var attunement = await repo.FindAttunementAsync(uow, c.Id, site.Id, ct);
            var available = attunement?.LastAttunedAt.AddHours(config.Flame.CooldownHours);
            return new NearbyView(check.Verified is not null, check.Problem,
                ev is null ? null : new EventView(ev.Kind, ev.NamePl, ev.RewardMultiplierPermille, ev.EndsAt), views,
                new FlameSiteView(site.Id, site.NamePl, site.Lat, site.Lon, (long)Math.Round(siteDistance), siteDistance <= config.Flame.RadiusM,
                    available > now ? available : null),
                region is null ? null : new RegionView(region.NamePl, region.Terrain.Id, region.Terrain.NamePl, region.Weather?.Id, region.Weather?.NamePl, region.WeatherEndsAt,
                    WorldGenerator.IsNight(now, at.Lon), region.BossNpcId, region.BossNpcId is null ? null : npcNames.NamePl(region.BossNpcId) ?? region.BossNpcId,
                    realWeather is not null, realWeather is null ? null : Math.Round(realWeather.TemperatureC, 1)));
        }, ct);

    /// <summary>
    /// Harvest a node or engage a spirit beast. Requires a VERIFIED position within range (acceptance #9: a spoofed
    /// jump never earns rewards), the node to exist in the current epoch (regenerated server-side: keys cannot be
    /// forged), one claim per node per character, and the hourly interaction limit.
    /// </summary>
    public Task<Result<InteractionResult>> InteractAsync(AccountId accountId, string? key, InteractCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<InteractionResult>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "world.interact", key, cmd, async Task<Result<InteractionResult>> () =>
            {
                var now = clock.GetUtcNow();
                var c = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
                if (c is null) return CharacterNotFound;
                var check = await positions.CheckAsync(uow, accountId, c.Id, now, ct);
                if (check.Verified is not { } position) return Error.Conflict(check.Problem!, $"Your position cannot be used for rewards right now ({check.Problem}).");
                var node = WorldGenerator.Resolve(config, secret.Seed, cmd.NodeKey, now);
                if (node is null && RealPlaceOf(cmd.NodeKey, now) is { } place)
                    node = new WorldNode(cmd.NodeKey!, place.Kind, place.Point, -RealWorldRules.Day(now), DateTimeOffset.FromUnixTimeSeconds((RealWorldRules.Day(now) + 1) * 86_400));
                if (node is null) return Error.NotFound("node_not_found", "This node does not exist or has already faded.");
                if (await repo.CountClaimsSinceAsync(uow, c.Id, now.AddHours(-1), ct) >= config.InteractionsPerHour)
                    return Error.LimitReached("interaction_limit", "Too many interactions this hour; rest a little.");
                if (GeoMath.DistanceM(position.Point, node.Point) > config.InteractionRadiusM) return Error.Validation("too_far", "Move closer to interact.");
                if (!await repo.TryClaimAsync(uow, node.Key, c.Id, accountId, node.Kind, now, ct))
                    return Error.Conflict("already_harvested", "You already gathered from this node.");

                var kind = config.NodeKinds.First(k => k.Id == node.Kind);
                Guid? battleId = null;
                long reward = 0;
                List<WorldEvents.GatheredItem> gathered = [];
                var npcId = kind.NpcId == "@region" ? WorldGenerator.RegionAt(config, secret.Seed, node.Point, now).BossNpcId : kind.NpcId;
                if (npcId is { } npc)
                {
                    var battle = await encounters.StartEncounterAsync(uow, accountId, c.Id.Value, npc, ct);
                    if (!battle.IsSuccess) return battle.Error!; // rolls back the claim too
                    battleId = battle.Value;
                }
                else
                {
                    reward = WorldGenerator.NodeReward(config, node.Kind, WorldGenerator.EventAt(config, secret.Seed, node.Point, now),
                        config.Regions is null ? null : RegionWithWeather(node.Point, now).Region.Weather);
                    await PayAsync(uow, accountId, c.Id, reward, "node:" + node.Kind, now, ct);
                    var realm = 0;
                    foreach (var r in realms) { realm = await r.RealmAsync(uow, c.Id.Value, ct); break; }
                    var era = Math.Clamp(realm / 11, 0, 5);
                    var rolled = WorldGenerator.RollYields(kind, seeds.NextSeed()).ToList();
                    gathered = [.. rolled.Select(y => new WorldEvents.GatheredItem(y.Item, y.Quantity))];
                    foreach (var y in rolled)
                        if (config.FinerYield(y.Item, era) is { } finer) gathered.Add(new WorldEvents.GatheredItem(finer, Math.Max(1, y.Quantity / 2)));
                    if (gathered.Count > 0)
                    {
                        await outbox.EnqueueAsync(uow, OutboxMessage.Create(WorldEvents.ItemsGathered,
                            new WorldEvents.ItemsGatheredV1(accountId.Value, c.Id.Value, "node:" + node.Kind, gathered), now, correlation.CorrelationId), ct);
                    }
                }

                await AuditAsync(uow, accountId, "world.node_used", c.Id, new { kind = node.Kind, reward, battleId, items = gathered }, now, ct);
                return new InteractionResult(node.Kind, reward, battleId, gathered);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>Flame path cultivation at one of the 12 fixed sites (other paths earn a smaller share).</summary>
    public Task<Result<InteractionResult>> AttuneAsync(AccountId accountId, string? key, AttuneCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<InteractionResult>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "world.flame.attune", key, cmd, async Task<Result<InteractionResult>> () =>
            {
                var now = clock.GetUtcNow();
                var c = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
                if (c is null) return CharacterNotFound;
                var site = config.Flame.Sites.FirstOrDefault(s => s.Id == cmd.SiteId);
                if (site is null) return Error.NotFound("site_not_found", "Unknown Flame site.");
                var check = await positions.CheckAsync(uow, accountId, c.Id, now, ct);
                if (check.Verified is not { } position) return Error.Conflict(check.Problem!, $"Your position cannot be used for rewards right now ({check.Problem}).");
                if (GeoMath.DistanceM(position.Point, site.Point) > config.Flame.RadiusM) return Error.Validation("too_far", "You must stand at the Flame site.");
                var previous = await repo.FindAttunementAsync(uow, c.Id, site.Id, ct);
                if (previous is not null && previous.LastAttunedAt.AddHours(config.Flame.CooldownHours) > now)
                    return Error.Conflict("attunement_cooldown", "The flame here has not recovered yet.");

                var reward = (long)(c.Path == "flame" ? config.Flame.RewardQiFlamePath : config.Flame.RewardQiOtherPaths);
                await repo.SaveAttunementAsync(uow, new FlameAttunement(c.Id, site.Id, now, (previous?.Count ?? 0) + 1), ct);
                await PayAsync(uow, accountId, c.Id, reward, "flame:" + site.Id, now, ct);
                await AuditAsync(uow, accountId, "world.flame_attuned", c.Id, new { site = site.Id, reward }, now, ct);
                return new InteractionResult("flame_site", reward, null);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    private Task PayAsync(IUnitOfWork uow, AccountId accountId, CharacterId characterId, long qi, string source, DateTimeOffset now, CancellationToken ct) =>
        qi <= 0
            ? Task.CompletedTask
            : outbox.EnqueueAsync(uow, OutboxMessage.Create(WorldEvents.QiReward, new WorldEvents.QiRewardV1(accountId.Value, characterId.Value, qi, source), now, correlation.CorrelationId), ct);

    // Audit entries carry what happened, never where (no coordinates, no node keys that encode the cell).
    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, CharacterId characterId, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "character", characterId.ToString(),
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}
