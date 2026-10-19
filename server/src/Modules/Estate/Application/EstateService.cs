using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Cultivation.Contracts;
using JianghuGo.Modules.Estate.Contracts;
using JianghuGo.Modules.Estate.Domain;
using JianghuGo.Modules.Inventory.Contracts;

namespace JianghuGo.Modules.Estate.Application;

public interface IEstateRepository
{
    Task<EstateProfile?> FindAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
    Task<bool> TrySaveAsync(IUnitOfWork uow, EstateProfile profile, int expectedVersion, CancellationToken ct);
}

public sealed record DropView(string Item, string NamePl, int ChancePermille, int Min, int Max);

public sealed record CropView(string Seed, string SeedNamePl, string NamePl, int Hours, int MinRealm, IReadOnlyList<DropView> Yields, long SeedsOwned, bool Available);

public sealed record PlotView(int Slot, string? Seed, string? CropNamePl, DateTimeOffset? PlantedAt, DateTimeOffset? ReadyAt, bool Ready);

public sealed record DiscipleView(Guid Id, string Name, string Talent, string TalentNamePl, int Level, int Xp, int XpNext, string? Expedition, string? ExpeditionNamePl,
    DateTimeOffset? Until, bool Returned);

public sealed record ExpeditionView(string Id, string NamePl, string DescriptionPl, int Hours, int MinRealm, int MinLevel, long StonesMin, long StonesMax, string Talent,
    string TalentNamePl, IReadOnlyList<DropView> Drops, bool Available);

public sealed record LootView(string Item, string NamePl, int Quantity);

public sealed record EstateView(
    Guid CharacterId,
    int Realm,
    IReadOnlyList<PlotView> Plots,
    int PlotsMax,
    IReadOnlyList<CropView> Crops,
    IReadOnlyList<DiscipleView> Disciples,
    int DiscipleSlots,
    int DiscipleSlotsMax,
    long? RecruitPrice,
    IReadOnlyList<ExpeditionView> Expeditions,
    IReadOnlyList<LootView> Gained,
    long StonesGained,
    int Version);

public sealed record PlantCommand(int Slot, string Seed);

public sealed record ExpeditionCommand(Guid DiscipleId, string ExpeditionId);

public sealed record DiscipleCommand(Guid DiscipleId);

/// <summary>
/// The estate works while the player is away: plots grow crops, disciples walk expeditions. Results are rolled
/// server-side when collected; items go into the bag in the same transaction.
/// </summary>
public sealed class EstateService(
    IUnitOfWorkFactory uowFactory,
    IEstateRepository repo,
    ICharacterDirectory characters,
    ICultivationStatus cultivation,
    IInventory inventory,
    IItemCatalog catalog,
    IAuditLog audit,
    IOutbox outbox,
    ICorrelationContext correlation,
    IRandomSeedSource seeds,
    EstateConfig config,
    TimeProvider clock)
{
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");

    private sealed record Ctx(CharacterSummary Character, int Realm, EstateProfile Profile, int StoredVersion);

    private sealed record Change(EstateProfile Profile, IReadOnlyList<(string Item, int Quantity)> Gained, long Stones);

    public Task<Result<EstateView>> GetAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, null, (_, x, _) => Task.FromResult<Result<Change>>(new Change(x.Profile, [], 0)), ct);

    public Task<Result<EstateView>> PlantAsync(AccountId accountId, Guid characterId, PlantCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "estate.planted", async (uow, x, now) =>
        {
            var p = x.Profile;
            if (config.Crop(cmd.Seed) is not { } crop) return Error.NotFound("crop_unknown", "Nothing grows from this.");
            if (x.Realm < crop.MinRealm) return Error.Conflict("realm_too_low", "Your realm is too low to tend this crop.");
            if (cmd.Slot < 0 || cmd.Slot >= EstateRules.Plots(config, x.Realm)) return Error.Validation("plot_invalid", "No such plot.");
            if (p.Plots.Any(pl => pl.Slot == cmd.Slot)) return Error.Conflict("plot_busy", "This plot is already planted.");
            var taken = await inventory.RemoveItemsAsync(uow, x.Character.Id, crop.Seed, 1, "estate_plant", Ref(), ct);
            if (!taken.IsSuccess) return taken.Error!;
            var hours = x.Character.Path == "poison" ? Math.Max(1, crop.Hours * 3 / 4) : crop.Hours; // poisoners are gardeners of venomous herbs
            return new Change(p with { Plots = [.. p.Plots, new Plot(cmd.Slot, crop.Seed, now, now.AddHours(hours))] }, [], 0);
        }, ct);

    /// <summary>Harvests every ripe plot.</summary>
    public Task<Result<EstateView>> HarvestAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, "estate.harvested", async (uow, x, now) =>
        {
            var p = x.Profile;
            var ripe = p.Plots.Where(pl => pl.ReadyAt <= now).ToList();
            if (ripe.Count == 0) return Error.Conflict("nothing_ripe", "Nothing is ripe yet.");
            var rnd = new DeterministicRandom(seeds.NextSeed());
            var gained = new List<(string Item, int Quantity)>();
            foreach (var pl in ripe)
                if (config.Crop(pl.Seed) is { } crop) gained.AddRange(EstateRules.Roll(crop.Yields, rnd, 0, 0));
            var reference = Ref();
            foreach (var g in Merge(gained))
            {
                var added = await inventory.AddItemsAsync(uow, x.Character.Id, g.Item, g.Quantity, "estate_harvest", reference, ct);
                if (!added.IsSuccess) return added.Error!;
            }

            await outbox.EnqueueAsync(uow, OutboxMessage.Create(EstateEvents.Harvested,
                new EstateEvents.HarvestedV1(x.Character.AccountId.Value, x.Character.Id.Value, ripe.Count), now, correlation.CorrelationId), ct);
            return new Change(p with { Plots = [.. p.Plots.Where(pl => pl.ReadyAt > now)] }, Merge(gained), 0);
        }, ct);

    public Task<Result<EstateView>> RecruitAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, "estate.recruited", async (uow, x, now) =>
        {
            var p = x.Profile;
            if (p.Disciples.Count >= EstateRules.DiscipleSlots(config, x.Realm)) return Error.LimitReached("disciples_full", "No room for another disciple at your realm.");
            var price = EstateRules.RecruitPrice(config, p.Recruited) ?? 0;
            if (price > 0)
            {
                var paid = await inventory.DebitAsync(uow, x.Character.Id, price, "estate_recruit", Ref(), ct);
                if (!paid.IsSuccess) return paid.Error!;
            }

            var rnd = new DeterministicRandom(seeds.NextSeed());
            var used = p.Disciples.Select(d => d.Name).ToHashSet();
            var free = config.Disciples.Names.Where(n => !used.Contains(n)).ToList();
            var name = free.Count > 0 ? free[rnd.NextInt(0, free.Count)] : config.Disciples.Names[rnd.NextInt(0, config.Disciples.Names.Count)] + " " + (p.Recruited + 1);
            var talent = config.Disciples.Talents[rnd.NextInt(0, config.Disciples.Talents.Count)].Id;
            return new Change(p with { Disciples = [.. p.Disciples, new Disciple(Guid.NewGuid(), name, talent, 1, 0, null, null, now)], Recruited = p.Recruited + 1 }, [], 0);
        }, ct);

    public Task<Result<EstateView>> DismissAsync(AccountId accountId, Guid characterId, DiscipleCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "estate.dismissed", (_, x, _) =>
        {
            var d = x.Profile.Disciples.FirstOrDefault(d => d.Id == cmd.DiscipleId);
            if (d is null) return Task.FromResult<Result<Change>>(Error.NotFound("disciple_not_found", "No such disciple."));
            if (d.Expedition is not null) return Task.FromResult<Result<Change>>(Error.Conflict("disciple_away", "The disciple is away on an expedition."));
            return Task.FromResult<Result<Change>>(new Change(x.Profile with { Disciples = [.. x.Profile.Disciples.Where(o => o.Id != d.Id)] }, [], 0));
        }, ct);

    public Task<Result<EstateView>> StartExpeditionAsync(AccountId accountId, Guid characterId, ExpeditionCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "estate.expedition_started", (_, x, now) =>
        {
            Result<Change> Fail(Error e) => e;
            var p = x.Profile;
            var d = p.Disciples.FirstOrDefault(d => d.Id == cmd.DiscipleId);
            if (d is null) return Task.FromResult(Fail(Error.NotFound("disciple_not_found", "No such disciple.")));
            if (d.Expedition is not null) return Task.FromResult(Fail(Error.Conflict("disciple_away", "The disciple is already away.")));
            if (config.Expedition(cmd.ExpeditionId) is not { } e) return Task.FromResult(Fail(Error.NotFound("expedition_unknown", "Unknown expedition.")));
            if (x.Realm < e.MinRealm || d.Level < e.MinLevel) return Task.FromResult(Fail(Error.Conflict("expedition_locked", "Too dangerous for now.")));
            var next = d with { Expedition = e.Id, Until = now.AddHours(e.Hours) };
            return Task.FromResult<Result<Change>>(new Change(p with { Disciples = [.. p.Disciples.Select(o => o.Id == d.Id ? next : o)] }, [], 0));
        }, ct);

    /// <summary>Collects every disciple who has come back: stones, loot (talent and level help) and experience.</summary>
    public Task<Result<EstateView>> CollectExpeditionsAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, "estate.expeditions_collected", async (uow, x, now) =>
        {
            var p = x.Profile;
            var back = p.Disciples.Where(d => d.Expedition is not null && d.Until <= now).ToList();
            if (back.Count == 0) return Error.Conflict("nobody_back", "No disciple has returned yet.");
            var rnd = new DeterministicRandom(seeds.NextSeed());
            var gained = new List<(string Item, int Quantity)>();
            long stones = 0;
            var disciples = p.Disciples.ToList();
            foreach (var d in back)
            {
                var e = config.Expedition(d.Expedition);
                var i = disciples.FindIndex(o => o.Id == d.Id);
                if (e is null)
                {
                    disciples[i] = d with { Expedition = null, Until = null };
                    continue;
                }

                var talented = d.Talent == e.Talent;
                var chanceBonus = (d.Level - 1) * config.Disciples.LevelChancePermille + (talented ? config.Disciples.TalentBonusPermille / 2 : 0);
                var qtyBonus = talented ? config.Disciples.TalentBonusPermille : 0;
                gained.AddRange(EstateRules.Roll(e.Drops, rnd, chanceBonus, qtyBonus));
                var s = e.Stones[0] + (long)(rnd.NextInt(0, 1001) * (e.Stones[1] - e.Stones[0]) / 1000);
                stones += talented && e.Talent == "blade" ? s * 3 / 2 : s;
                disciples[i] = EstateRules.AddXp(config, d with { Expedition = null, Until = null }, e.Xp);
            }

            var reference = Ref();
            foreach (var g in Merge(gained))
            {
                var added = await inventory.AddItemsAsync(uow, x.Character.Id, g.Item, g.Quantity, "estate_expedition", reference, ct);
                if (!added.IsSuccess) return added.Error!;
            }

            if (stones > 0)
            {
                var credited = await inventory.CreditAsync(uow, x.Character.Id, stones, "estate_expedition", reference, ct);
                if (!credited.IsSuccess) return credited.Error!;
            }

            foreach (var group in back.GroupBy(d => d.Expedition!))
                await outbox.EnqueueAsync(uow, OutboxMessage.Create(EstateEvents.ExpeditionFinished,
                    new EstateEvents.ExpeditionFinishedV1(x.Character.AccountId.Value, x.Character.Id.Value, group.Key, group.Count()), now, correlation.CorrelationId), ct);
            return new Change(p with { Disciples = disciples }, Merge(gained), stones);
        }, ct);

    // ---------------------------------------------------------------- plumbing

    private static string Ref() => Uuid7.New().ToString("N");

    private static IReadOnlyList<(string Item, int Quantity)> Merge(IEnumerable<(string Item, int Quantity)> list) =>
        [.. list.GroupBy(g => g.Item).Select(g => (g.Key, g.Sum(x => x.Quantity)))];

    private Task<Result<EstateView>> Mutate(AccountId accountId, Guid characterId, string? action, Func<IUnitOfWork, Ctx, DateTimeOffset, Task<Result<Change>>> change, CancellationToken ct) =>
        uowFactory.RunAsync<Result<EstateView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var stored = await repo.FindAsync(uow, c.Id, ct);
            var status = await cultivation.StatusAsync(uow, c.Id.Value, ct);
            var x = new Ctx(c, status.Realm, stored ?? EstateProfile.New(c.Id), stored?.Version ?? 0);
            var changed = await change(uow, x, now);
            if (!changed.IsSuccess) return changed.Error!;
            var next = changed.Value.Profile;
            if (action is not null || stored is null)
            {
                next = next with { Version = x.StoredVersion + 1 };
                if (!await repo.TrySaveAsync(uow, next, x.StoredVersion, ct)) return Error.Conflict("version_conflict", "The estate changed concurrently; reload and retry.");
            }

            if (action is not null)
                await audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "character", characterId.ToString(), "{}", correlation.CorrelationId, now), ct);
            var view = await ViewAsync(uow, x with { Profile = next }, changed.Value, now, ct);
            await uow.CommitAsync(ct);
            return view;
        }, ct);

    private string Name(string item) => catalog.Find(item)?.NamePl ?? item;

    private async Task<EstateView> ViewAsync(IUnitOfWork uow, Ctx x, Change change, DateTimeOffset now, CancellationToken ct)
    {
        var p = x.Profile;
        var plots = Enumerable.Range(0, EstateRules.Plots(config, x.Realm)).Select(slot =>
        {
            var pl = p.Plots.FirstOrDefault(o => o.Slot == slot);
            return new PlotView(slot, pl?.Seed, config.Crop(pl?.Seed)?.NamePl, pl?.PlantedAt, pl?.ReadyAt, pl is not null && pl.ReadyAt <= now);
        }).ToList();
        var crops = new List<CropView>();
        foreach (var crop in config.Garden.Crops)
            crops.Add(new CropView(crop.Seed, Name(crop.Seed), crop.NamePl, crop.Hours, crop.MinRealm, [.. crop.Yields.Select(y => new DropView(y.Item, Name(y.Item), y.ChancePermille, y.Min, y.Max))],
                await inventory.CountAsync(uow, x.Character.Id, crop.Seed, ct), x.Realm >= crop.MinRealm));
        var talents = config.Disciples.Talents.ToDictionary(t => t.Id, t => t.NamePl);
        var disciples = p.Disciples.Select(d => new DiscipleView(d.Id, d.Name, d.Talent, talents.GetValueOrDefault(d.Talent, d.Talent), d.Level, d.Xp,
            d.Level >= config.Disciples.MaxLevel ? 0 : config.Disciples.LevelXp * d.Level, d.Expedition, config.Expedition(d.Expedition)?.NamePl, d.Until,
            d.Expedition is not null && d.Until <= now)).ToList();
        var expeditions = config.Expeditions.Select(e => new ExpeditionView(e.Id, e.NamePl, e.DescriptionPl, e.Hours, e.MinRealm, e.MinLevel, e.Stones[0], e.Stones[1], e.Talent,
            talents.GetValueOrDefault(e.Talent, e.Talent), [.. e.Drops.Select(y => new DropView(y.Item, Name(y.Item), y.ChancePermille, y.Min, y.Max))], x.Realm >= e.MinRealm)).ToList();
        var slots = EstateRules.DiscipleSlots(config, x.Realm);
        return new EstateView(x.Character.Id.Value, x.Realm, plots, config.Garden.MaxPlots, crops, disciples, slots, config.Disciples.MaxSlots,
            p.Disciples.Count < slots ? EstateRules.RecruitPrice(config, p.Recruited) : null, expeditions,
            [.. change.Gained.Select(g => new LootView(g.Item, Name(g.Item), g.Quantity))], change.Stones, p.Version);
    }
}
