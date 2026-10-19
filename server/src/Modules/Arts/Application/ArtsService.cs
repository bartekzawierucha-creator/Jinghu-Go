using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Arts.Contracts;
using JianghuGo.Modules.Arts.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Cultivation.Contracts;
using JianghuGo.Modules.Inventory.Contracts;

namespace JianghuGo.Modules.Arts.Application;

public interface IArtsRepository
{
    Task<ArtsProfile?> FindAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
    Task<bool> TrySaveAsync(IUnitOfWork uow, ArtsProfile profile, int expectedVersion, CancellationToken ct);

    /// <summary>False when this (event, purpose, character) was already applied (outbox redelivery).</summary>
    Task<bool> TryMarkHandledAsync(IUnitOfWork uow, Guid guardId, CharacterId characterId, DateTimeOffset at, CancellationToken ct);
}

// ---------------------------------------------------------------- views

public sealed record ItemLineView(string Item, string NamePl, int Quantity, long Owned);

public sealed record FlameView(
    string Id, int Rank, string NamePl, string SourcePl, string State, int Level, int Xp, int XpNext, bool Slotted, int MinRealm, int AbsorbHours,
    IReadOnlyDictionary<string, int> Stats, PerkBonus Perks, int AlchemyPermille, int ForgePermille, string? FusedWith);

public sealed record FlamesView(
    IReadOnlyList<FlameView> All, int Slots, int SlotsMax, IReadOnlyList<int> SlotRealms, IReadOnlyList<string> Slotted, string? Subdued, Guid? SpiritBattleId,
    string? AbsorbingId, DateTimeOffset? AbsorbUntil, int? AbsorbChancePermille, DateTimeOffset? BurnedUntil, long Traces, int TracesPerUse, long FeedItems,
    int XpPerFeed, int MaxLevel, IReadOnlyList<string> Fusions, int Owned, int FusionBonusPermille);

public sealed record VenomView(string Item, string NamePl, string Rarity, string Element, int Xp, int Absorbed, long Owned);

public sealed record ToxinPerkView(int Level, string DescriptionPl, bool Unlocked);

public sealed record CoatingView(string Id, string NamePl, IReadOnlyList<ItemLineView> Venoms, int Battles, PerkBonus Perks, bool Craftable);

public sealed record ToxinView(
    int Level, int Xp, int? NextXp, int MaxLevel, IReadOnlyList<VenomView> Venoms, IReadOnlyList<ToxinPerkView> Perks, DateTimeOffset? PoisonedUntil, long Antidotes,
    string? ActiveCoating, int CoatingCharges, IReadOnlyList<CoatingView> Coatings, int FailPermille);

public sealed record GuWormView(int Slot, string Type, string NamePl, int Level, int Rank, DateTimeOffset? GrowingUntil);

public sealed record GuTypeView(string Id, string NamePl, string DescriptionPl, PerkBonus PerLevel);

public sealed record GuView(int Slots, IReadOnlyList<GuWormView> Worms, IReadOnlyList<GuTypeView> Types, long Herbs, int HatchHerbs, int FeedHerbs, int HoursPerLevel, int MaxLevel, int MaxRank);

public sealed record BathView(string Id, string NamePl, IReadOnlyList<ItemLineView> Items, int Hours, int Xp, bool Affordable);

public sealed record TemperView(
    int Level, int MaxLevel, int Xp, int XpNeeded, int ChancePermille, bool NeedsPill, long Pills, string? StageNamePl, string? NextStageNamePl, int? NextStageAt,
    string? Bath, DateTimeOffset? BathUntil, IReadOnlyList<BathView> Baths, int Attempts, string? Badge, int StepsPerXp);

public sealed record SwordView(int Level, int MaxLevel, int Xp, int? NextXp, long Echoes, int XpPerEcho, string? Badge);

public sealed record BranchOptionView(string Id, string NamePl, string DescriptionPl, PerkBonus PerTier);

public sealed record BranchView(string? Chosen, int Tier, IReadOnlyList<int> TierRealms, IReadOnlyList<BranchOptionView> Options, long RespecStones,
    int CapstoneRealm = 65, string? Capstone = null, IReadOnlyList<BranchOptionView>? Capstones = null);

public sealed record NoticeView(string Kind, string TextPl, DateTimeOffset At);

public sealed record ArtsTitleView(string Id, string NamePl, string Rarity);

public sealed record ArtsView(
    Guid CharacterId,
    string Path,
    int Realm,
    string RulesVersion,
    FlamesView Flames,
    ToxinView Toxin,
    GuView Gu,
    TemperView Temper,
    SwordView Sword,
    BranchView Branch,
    IReadOnlyList<NoticeView> Notices,
    IReadOnlyList<ArtsTitleView> Titles,
    PerkBonus TotalPerks,
    IReadOnlyDictionary<string, int> Stats,
    int AlchemyPermille,
    int ForgePermille,
    int Version);

// ---------------------------------------------------------------- commands

public sealed record FlameCommand(string FlameId);

public sealed record FeedFlameCommand(string FlameId, int Quantity);

public sealed record SlotsCommand(IReadOnlyList<string> FlameIds);

public sealed record FuseCommand(string FirstFlameId, string SecondFlameId);

public sealed record VenomCommand(string Item, int Quantity);

public sealed record CoatingCommand(string CoatingId);

public sealed record GuHatchCommand(string Type);

public sealed record GuSlotCommand(int Slot);

public sealed record BathCommand(string BathId);

public sealed record EchoCommand(int Quantity);

public sealed record BranchCommand(string BranchId);

public sealed record TraceResult(string? FoundFlameId, string? FoundNamePl, ArtsView State);

public sealed record VenomResult(int Absorbed, int Failed, int XpGained, bool Poisoned, ArtsView State);

public sealed record TemperResult(bool Success, int ChancePermille, ArtsView State);

public sealed record SpiritBattleView(Guid BattleId, ArtsView State);

/// <summary>
/// Path arts (rules arts-1). Every read settles the timers first (absorption, baths, Gu growth). Materials are taken
/// from the bag inside the same transaction; every roll is server-side.
/// </summary>
public sealed class ArtsService(
    IUnitOfWorkFactory uowFactory,
    IArtsRepository repo,
    ICharacterDirectory characters,
    ICultivationStatus cultivation,
    IInventory inventory,
    IItemCatalog catalog,
    ICombatEncounters encounters,
    IAuditLog audit,
    IOutbox outbox,
    ICorrelationContext correlation,
    IRandomSeedSource seeds,
    ArtsConfig config,
    TimeProvider clock)
{
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");
    private static readonly Error Concurrent = Error.Conflict("version_conflict", "Arts changed concurrently; reload and retry.");

    internal sealed record Ctx(CharacterSummary Character, CultivationStatus Status, ArtsProfile Profile, int StoredVersion, ArtsProfile? Stored = null);

    // ---------------------------------------------------------------- reading

    public Task<Result<ArtsView>> GetAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, "get", (_, x, _) => Task.FromResult<Result<ArtsProfile>>(x.Profile), ct);

    public Task<Result<ArtsView>> DismissNoticesAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, "notices", (_, x, _) => Task.FromResult<Result<ArtsProfile>>(x.Profile with { Notices = [] }), ct);

    // ---------------------------------------------------------------- flames

    /// <summary>Follows ember traces: what they lead to depends on the hour (night, noon) and the moon.</summary>
    public async Task<Result<TraceResult>> FollowTracesAsync(AccountId accountId, Guid characterId, CancellationToken ct)
    {
        string? found = null;
        var r = await Mutate(accountId, characterId, "traces", async (uow, x, now) =>
        {
            var p = x.Profile;
            var taken = await inventory.RemoveItemsAsync(uow, x.Character.Id, config.FlameRules.TraceItem, config.FlameRules.TracesPerUse, "arts_trace", Ref(), ct);
            if (!taken.IsSuccess) return Error.Conflict("not_enough_traces", $"You need {config.FlameRules.TracesPerUse} ember traces.");
            var rnd = new DeterministicRandom(seeds.NextSeed());
            var rare = rnd.NextInt(0, 1000) < 50;
            var candidates = ArtsRules.TraceCandidates(config, now, rare).Where(f => !p.FlamesSeen.Contains(f.Id) && p.Owned(f.Id) is null).ToList();
            if (candidates.Count == 0 || rnd.NextInt(0, 1000) >= 600)
                return p.Notify("trace_cold", "Ślady żaru wystygły, zanim do czegoś doprowadziły.", now);
            // weaker flames are easier to find
            var weights = candidates.Select(f => f.Rank * f.Rank).ToList();
            var pick = rnd.NextInt(0, weights.Sum());
            var chosen = candidates[^1];
            for (var i = 0; i < candidates.Count; i++)
            {
                if (pick < weights[i])
                {
                    chosen = candidates[i];
                    break;
                }

                pick -= weights[i];
            }

            found = chosen.Id;
            return ArtsRules.Discover(p, chosen, now);
        }, ct);
        return r.IsSuccess ? new TraceResult(found, config.Flame(found)?.NamePl, r.Value) : r.Error!;
    }

    /// <summary>Fights the flame's spirit (a real battle); winning it subdues the flame, ready to absorb.</summary>
    public async Task<Result<SpiritBattleView>> SubdueAsync(AccountId accountId, Guid characterId, FlameCommand cmd, CancellationToken ct)
    {
        Guid battleId = Guid.Empty;
        var r = await Mutate(accountId, characterId, "subdue", async (uow, x, now) =>
        {
            var p = x.Profile;
            if (config.Flame(cmd.FlameId) is not { } flame) return Error.NotFound("flame_not_found", "Unknown flame.");
            if (!p.FlamesSeen.Contains(flame.Id)) return Error.Conflict("flame_not_found_yet", "You have not found this flame yet.");
            if (p.SubduedFlame is not null || p.Absorbing is not null) return Error.Conflict("flame_busy", "Absorb the flame you already hold first.");
            if (p.BurnedUntil is { } b && b > now) return Error.Conflict("meridians_burned", "Your meridians are still burned.");
            if (x.Status.Realm < flame.MinRealm) return Error.Conflict("realm_too_low", "Your realm is too low to face this flame.");
            var reference = Guid.NewGuid();
            var started = await encounters.StartSpiritAsync(uow, x.Character.AccountId, x.Character.Id.Value, flame.Spirit, Math.Max(x.Status.Realm, flame.MinRealm),
                ArtsRules.SpiritPower(flame), reference, "Duch: " + flame.NamePl, ct);
            if (!started.IsSuccess) return started.Error!;
            battleId = started.Value;
            return p with { Spirit = new PendingSpirit(flame.Id, reference, started.Value, now) };
        }, ct);
        return r.IsSuccess ? new SpiritBattleView(battleId, r.Value) : r.Error!;
    }

    /// <summary>Starts absorbing the subdued flame (hours; the outcome is rolled when the time is up).</summary>
    public Task<Result<ArtsView>> AbsorbAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, "absorb", (_, x, now) =>
        {
            var p = x.Profile;
            if (config.Flame(p.SubduedFlame) is not { } flame) return Task.FromResult<Result<ArtsProfile>>(Error.Conflict("no_subdued_flame", "Subdue a flame's spirit first."));
            if (p.Absorbing is not null) return Task.FromResult<Result<ArtsProfile>>(Error.Conflict("already_absorbing", "Already absorbing a flame."));
            var chance = ArtsRules.AbsorbChance(config, flame, x.Status.Realm);
            return Task.FromResult<Result<ArtsProfile>>(p with { SubduedFlame = null, Absorbing = new Absorption(flame.Id, now, now.AddHours(flame.AbsorbHours), chance) });
        }, ct);

    public Task<Result<ArtsView>> FeedFlameAsync(AccountId accountId, Guid characterId, FeedFlameCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "feed", async (uow, x, _) =>
        {
            var p = x.Profile;
            if (cmd.Quantity is < 1 or > 999) return Error.Validation("quantity_invalid", "Quantity must be 1-999.");
            if (p.Owned(cmd.FlameId) is not { } owned) return Error.NotFound("flame_not_owned", "You do not own this flame.");
            if (owned.Level >= config.FlameRules.MaxLevel) return Error.Conflict("flame_max_level", "This flame is already at its peak.");
            var taken = await inventory.RemoveItemsAsync(uow, x.Character.Id, config.FlameRules.FeedItem, cmd.Quantity, "arts_feed", Ref(), ct);
            if (!taken.IsSuccess) return taken.Error!;
            var fed = ArtsRules.Feed(config, owned, cmd.Quantity * config.FlameRules.XpPerFeed);
            return p with { Flames = [.. p.Flames.Select(f => f.Id == fed.Id ? fed : f)] };
        }, ct);

    public Task<Result<ArtsView>> SetSlotsAsync(AccountId accountId, Guid characterId, SlotsCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "slots", (_, x, _) =>
        {
            var p = x.Profile;
            var ids = cmd.FlameIds ?? [];
            if (ids.Count > ArtsRules.FlameSlots(config, x.Status.Realm)) return Task.FromResult<Result<ArtsProfile>>(Error.Validation("too_many_flames", "Not enough flame slots for your realm."));
            if (ids.Distinct().Count() != ids.Count || ids.Any(id => p.Owned(id) is null))
                return Task.FromResult<Result<ArtsProfile>>(Error.Validation("flame_not_owned", "Slot only flames you own, each once."));
            return Task.FromResult<Result<ArtsProfile>>(p with { Slots = [.. ids] });
        }, ct);

    /// <summary>Fuses two mature flames: while both are slotted, both burn stronger.</summary>
    public Task<Result<ArtsView>> FuseAsync(AccountId accountId, Guid characterId, FuseCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "fuse", (_, x, _) =>
        {
            var p = x.Profile;
            Result<ArtsProfile> Fail(Error e) => e;
            if (cmd.FirstFlameId == cmd.SecondFlameId) return Task.FromResult(Fail(Error.Validation("fusion_invalid", "Choose two different flames.")));
            var a = p.Owned(cmd.FirstFlameId);
            var b = p.Owned(cmd.SecondFlameId);
            if (a is null || b is null) return Task.FromResult(Fail(Error.NotFound("flame_not_owned", "You do not own both flames.")));
            var half = Math.Max(1, config.FlameRules.MaxLevel / 2);
            if (a.Level < half || b.Level < half) return Task.FromResult(Fail(Error.Conflict("flames_too_weak", $"Both flames need level {half}.")));
            if (p.Fusions.Any(f => f.Split('+').Intersect([a.Id, b.Id]).Any())) return Task.FromResult(Fail(Error.Conflict("already_fused", "A flame can be fused only once.")));
            var key = string.CompareOrdinal(a.Id, b.Id) < 0 ? a.Id + "+" + b.Id : b.Id + "+" + a.Id;
            return Task.FromResult<Result<ArtsProfile>>(p with { Fusions = [.. p.Fusions, key] });
        }, ct);

    // ---------------------------------------------------------------- venoms, coatings

    /// <summary>Absorbs venoms into the toxin body: each dose may fail (poisoned for a while, no XP for that dose).</summary>
    public async Task<Result<VenomResult>> AbsorbVenomAsync(AccountId accountId, Guid characterId, VenomCommand cmd, CancellationToken ct)
    {
        int absorbed = 0, failed = 0, xp = 0;
        var r = await Mutate(accountId, characterId, "venom", async (uow, x, now) =>
        {
            var p = x.Profile;
            if (cmd.Quantity is < 1 or > 100) return Error.Validation("quantity_invalid", "Quantity must be 1-100.");
            if (config.Venom(cmd.Item) is not { } venom) return Error.NotFound("venom_unknown", "This is not a venom.");
            if (p.PoisonedUntil is { } po && po > now) return Error.Conflict("poisoned", "You are poisoned; cure yourself or wait.");
            var taken = await inventory.RemoveItemsAsync(uow, x.Character.Id, venom.Item, cmd.Quantity, "arts_venom", Ref(), ct);
            if (!taken.IsSuccess) return taken.Error!;
            var rnd = new DeterministicRandom(seeds.NextSeed());
            var fail = x.Character.Path == ArtsRules.PoisonPath ? config.Toxin.FailPermille / 2 : config.Toxin.FailPermille;
            for (var i = 0; i < cmd.Quantity; i++)
            {
                if (rnd.NextInt(0, 1000) < fail)
                {
                    failed++;
                    break; // a failed dose poisons: the rest of the batch is lost with it
                }

                absorbed++;
                xp += venom.Xp;
            }

            var venoms = new Dictionary<string, int>(p.Venoms) { [venom.Item] = p.Venoms.GetValueOrDefault(venom.Item) + absorbed };
            var before = ArtsRules.ToxinLevel(config, p.ToxinXp);
            p = p with { Venoms = venoms, ToxinXp = p.ToxinXp + xp, PoisonedUntil = failed > 0 ? now.AddHours(config.Toxin.PoisonedHours) : p.PoisonedUntil };
            var after = ArtsRules.ToxinLevel(config, p.ToxinXp);
            if (after > before) p = p.Notify("toxin_level", $"Ciało Toksyn osiąga poziom {after}.", now);
            if (failed > 0) p = p.Notify("poisoned", "Jad przełamał twoją obronę — jesteś zatruty.", now);
            return p;
        }, ct);
        return r.IsSuccess ? new VenomResult(absorbed, failed, xp, failed > 0, r.Value) : r.Error!;
    }

    public Task<Result<ArtsView>> CureAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, "cure", async (uow, x, now) =>
        {
            if (x.Profile.PoisonedUntil is not { } po || po <= now) return Error.Conflict("not_poisoned", "You are not poisoned.");
            var taken = await inventory.RemoveItemsAsync(uow, x.Character.Id, config.Toxin.AntidoteItem, 1, "arts_antidote", Ref(), ct);
            if (!taken.IsSuccess) return taken.Error!;
            return x.Profile with { PoisonedUntil = null };
        }, ct);

    /// <summary>Coats the weapon: one dose of each listed venom, lasts a number of battles.</summary>
    public Task<Result<ArtsView>> ApplyCoatingAsync(AccountId accountId, Guid characterId, CoatingCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "coating", async (uow, x, _) =>
        {
            if (config.Coating(cmd.CoatingId) is not { } coating) return Error.NotFound("coating_unknown", "Unknown coating.");
            var reference = Ref();
            foreach (var venom in coating.Venoms)
            {
                var taken = await inventory.RemoveItemsAsync(uow, x.Character.Id, venom, 1, "arts_coating", reference, ct);
                if (!taken.IsSuccess) return Error.Conflict("missing_venoms", "You lack the venoms for this coating.");
            }

            var charges = x.Character.Path == ArtsRules.PoisonPath ? coating.Battles + 2 : coating.Battles;
            return x.Profile with { Coating = coating.Id, CoatingCharges = charges };
        }, ct);

    // ---------------------------------------------------------------- Gu

    public Task<Result<ArtsView>> HatchGuAsync(AccountId accountId, Guid characterId, GuHatchCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "gu_hatch", async (uow, x, now) =>
        {
            var p = x.Profile;
            if (config.GuType(cmd.Type) is not { } type) return Error.NotFound("gu_unknown", "Unknown Gu.");
            var slots = ArtsRules.GuSlots(config, x.Status.Realm);
            var free = Enumerable.Range(0, slots).FirstOrDefault(s => p.Gu.All(w => w.Slot != s), -1);
            if (free < 0) return Error.LimitReached("gu_nest_full", "Your Gu nest is full.");
            var taken = await inventory.RemoveItemsAsync(uow, x.Character.Id, config.Gu.HerbItem, config.Gu.HatchHerbs, "arts_gu", Ref(), ct);
            if (!taken.IsSuccess) return taken.Error!;
            return p with { Gu = [.. p.Gu, new GuWorm(free, type.Id, 1, 1, now, null)] };
        }, ct);

    public Task<Result<ArtsView>> FeedGuAsync(AccountId accountId, Guid characterId, GuSlotCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "gu_feed", async (uow, x, now) =>
        {
            var p = x.Profile;
            if (p.Gu.FirstOrDefault(w => w.Slot == cmd.Slot) is not { } worm) return Error.NotFound("gu_not_found", "No Gu in this slot.");
            if (worm.GrowingUntil is not null) return Error.Conflict("gu_growing", "This Gu is still digesting.");
            if (worm.Level >= config.Gu.MaxLevel && worm.Rank >= config.Gu.MaxRank) return Error.Conflict("gu_max", "This Gu cannot grow further.");
            var taken = await inventory.RemoveItemsAsync(uow, x.Character.Id, config.Gu.HerbItem, config.Gu.FeedHerbs, "arts_gu", Ref(), ct);
            if (!taken.IsSuccess) return taken.Error!;
            var hours = config.Gu.HoursPerLevel * worm.Rank;
            return p with { Gu = [.. p.Gu.Select(w => w.Slot == cmd.Slot ? w with { GrowingUntil = now.AddHours(hours) } : w)] };
        }, ct);

    public Task<Result<ArtsView>> ReleaseGuAsync(AccountId accountId, Guid characterId, GuSlotCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "gu_release", (_, x, _) => Task.FromResult<Result<ArtsProfile>>(
            x.Profile.Gu.Any(w => w.Slot == cmd.Slot)
                ? x.Profile with { Gu = [.. x.Profile.Gu.Where(w => w.Slot != cmd.Slot)] }
                : Error.NotFound("gu_not_found", "No Gu in this slot.")), ct);

    // ---------------------------------------------------------------- tempering

    public Task<Result<ArtsView>> StartBathAsync(AccountId accountId, Guid characterId, BathCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "bath", async (uow, x, now) =>
        {
            var p = x.Profile;
            if (config.Bath(cmd.BathId) is not { } bath) return Error.NotFound("bath_unknown", "Unknown bath.");
            if (p.Bath is not null) return Error.Conflict("bath_running", "You are already soaking in a bath.");
            if (p.Temper >= config.Tempering.MaxLevel) return Error.Conflict("temper_max", "Your body is fully tempered.");
            var reference = Ref();
            foreach (var item in bath.Items)
            {
                var taken = await inventory.RemoveItemsAsync(uow, x.Character.Id, item.Item, item.Quantity, "arts_bath", reference, ct);
                if (!taken.IsSuccess) return Error.Conflict("missing_ingredients", "You lack the ingredients for this bath.");
            }

            var hours = x.Character.Path == ArtsRules.BodyPath ? Math.Max(1, bath.Hours * 3 / 4) : bath.Hours;
            return p with { Bath = bath.Id, BathUntil = now.AddHours(hours) };
        }, ct);

    /// <summary>Tries to raise the body's tempering by one (+N). Every <c>pillEvery</c>-th level needs a pill; a failure costs half the XP.</summary>
    public async Task<Result<TemperResult>> AdvanceTemperAsync(AccountId accountId, Guid characterId, CancellationToken ct)
    {
        var success = false;
        var chance = 0;
        var r = await Mutate(accountId, characterId, "temper", async (uow, x, now) =>
        {
            var p = x.Profile;
            if (p.Temper >= config.Tempering.MaxLevel) return Error.Conflict("temper_max", "Your body is fully tempered.");
            var need = ArtsRules.TemperXpNeeded(config, p.Temper);
            if (p.TemperXp < need) return Error.Conflict("temper_xp", "Not enough tempering yet.");
            if (ArtsRules.TemperNeedsPill(config, p.Temper))
            {
                var taken = await inventory.RemoveItemsAsync(uow, x.Character.Id, config.Tempering.PillItem, 1, "arts_pill", Ref(), ct);
                if (!taken.IsSuccess) return Error.Conflict("pill_required", "This threshold needs a Body Transcendence Pill.");
            }

            chance = ArtsRules.TemperChance(config, p.Temper) + (x.Character.Path == ArtsRules.BodyPath ? 50 : 0);
            success = new DeterministicRandom(seeds.NextSeed()).NextInt(0, 1000) < chance;
            if (!success) return p with { TemperXp = p.TemperXp - (need / 2), TemperAttempts = p.TemperAttempts + 1 };
            var next = p with { Temper = p.Temper + 1, TemperXp = p.TemperXp - need, TemperAttempts = p.TemperAttempts + 1 };
            if (ArtsRules.TemperStageOf(config, next.Temper) is { } stage && stage.From == next.Temper)
                next = next.Notify("temper_stage", $"Hartowanie +{next.Temper}: {stage.NamePl}!", now);
            return next;
        }, ct);
        return r.IsSuccess ? new TemperResult(success, chance, r.Value) : r.Error!;
    }

    // ---------------------------------------------------------------- sword intent, branches

    public Task<Result<ArtsView>> OfferEchoesAsync(AccountId accountId, Guid characterId, EchoCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "echo", async (uow, x, now) =>
        {
            if (cmd.Quantity is < 1 or > 999) return Error.Validation("quantity_invalid", "Quantity must be 1-999.");
            var taken = await inventory.RemoveItemsAsync(uow, x.Character.Id, config.SwordIntent.EchoItem, cmd.Quantity, "arts_echo", Ref(), ct);
            if (!taken.IsSuccess) return taken.Error!;
            var p = x.Profile;
            var mult = x.Character.Path == ArtsRules.QiPath ? 2 : 1;
            var before = ArtsRules.SwordLevel(config, p.SwordXp);
            p = p with { SwordXp = p.SwordXp + (cmd.Quantity * config.SwordIntent.XpPerEcho * mult) };
            var after = ArtsRules.SwordLevel(config, p.SwordXp);
            return after > before ? p.Notify("sword_level", $"Intencja Miecza: ⚔{after}.", now) : p;
        }, ct);

    public Task<Result<ArtsView>> ChooseBranchAsync(AccountId accountId, Guid characterId, BranchCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "branch", async (uow, x, _) =>
        {
            var p = x.Profile;
            if (config.BranchesOf(x.Character.Path).FirstOrDefault(b => b.Id == cmd.BranchId) is not { } branch) return Error.NotFound("branch_unknown", "Unknown branch for your path.");
            if (ArtsRules.BranchTier(config, x.Status.Realm) < 1) return Error.Conflict("realm_too_low", $"Branches open at realm {config.Branches.TierRealms[0]}.");
            if (p.Branch == branch.Id) return Error.Conflict("branch_chosen", "Already on this branch.");
            if (p.Branch is not null)
            {
                var paid = await inventory.DebitAsync(uow, x.Character.Id, config.Branches.RespecStones, "arts_branch", Ref(), ct);
                if (!paid.IsSuccess) return paid.Error!;
            }

            return p with { Branch = branch.Id };
        }, ct);

    /// <summary>Chooses the path's ultimate at the capstone realm (1.7). The first choice is free, a change costs 10× the branch respec.</summary>
    public Task<Result<ArtsView>> ChooseCapstoneAsync(AccountId accountId, Guid characterId, BranchCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, "capstone", async (uow, x, _) =>
        {
            var p = x.Profile;
            if (config.Branches.CapstonesOf(x.Character.Path).FirstOrDefault(b => b.Id == cmd.BranchId) is not { } cap) return Error.NotFound("capstone_unknown", "Unknown ultimate for your path.");
            if (x.Status.Realm < config.Branches.CapstoneRealm) return Error.Conflict("realm_too_low", $"The ultimate opens at realm {config.Branches.CapstoneRealm}.");
            if (p.Capstone == cap.Id) return Error.Conflict("capstone_chosen", "Already chosen.");
            if (p.Capstone is not null)
            {
                var paid = await inventory.DebitAsync(uow, x.Character.Id, config.Branches.RespecStones * 10, "arts_capstone", Ref(), ct);
                if (!paid.IsSuccess) return paid.Error!;
            }

            return p with { Capstone = cap.Id };
        }, ct);

    // ---------------------------------------------------------------- plumbing

    private static string Ref() => Uuid7.New().ToString("N");

    private Task<Result<ArtsView>> Mutate(AccountId accountId, Guid characterId, string action, Func<IUnitOfWork, Ctx, DateTimeOffset, Task<Result<ArtsProfile>>> change, CancellationToken ct) =>
        uowFactory.RunAsync<Result<ArtsView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var x = await LoadAsync(uow, c, now, ct);
            var changed = await change(uow, x, now);
            if (!changed.IsSuccess) return changed.Error!;
            var next = changed.Value with { Version = x.StoredVersion + 1 };
            if (!await repo.TrySaveAsync(uow, next, x.StoredVersion, ct)) return Concurrent;
            await PublishProgressAsync(uow, x.Character, x.Stored ?? ArtsProfile.New(c.Id), next, now, ct);
            if (action != "get")
                await audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, "arts." + action, "character", characterId.ToString(), "{}", correlation.CorrelationId, now), ct);
            var view = await ViewAsync(uow, x with { Profile = next }, now, ct);
            await uow.CommitAsync(ct);
            return view;
        }, ct);

    /// <summary>Publishes the milestones when one of them moved (Quests achievements).</summary>
    internal async Task PublishProgressAsync(IUnitOfWork uow, CharacterSummary c, ArtsProfile before, ArtsProfile after, DateTimeOffset now, CancellationToken ct)
    {
        var a = Milestones(config, before);
        var b = Milestones(config, after);
        if (a == b) return;
        await outbox.EnqueueAsync(uow, OutboxMessage.Create(ArtsEvents.Progress,
            new ArtsEvents.ProgressV1(c.AccountId.Value, c.Id.Value, b.Flames, b.Toxin, b.Temper, b.Swords), now, correlation.CorrelationId), ct);
    }

    internal static (int Flames, int Toxin, int Temper, int Swords) Milestones(ArtsConfig c, ArtsProfile p) =>
        (p.Flames.Count, ArtsRules.ToxinLevel(c, p.ToxinXp), p.Temper, ArtsRules.SwordLevel(c, p.SwordXp));

    internal async Task<Ctx> LoadAsync(IUnitOfWork uow, CharacterSummary c, DateTimeOffset now, CancellationToken ct)
    {
        var stored = await repo.FindAsync(uow, c.Id, ct);
        var status = await cultivation.StatusAsync(uow, c.Id.Value, ct);
        var profile = ArtsRules.Settle(config, stored ?? ArtsProfile.New(c.Id), now);
        return new Ctx(c, status, profile, stored?.Version ?? 0, stored);
    }

    private async Task<long> CountAsync(IUnitOfWork uow, CharacterId id, string item, Dictionary<string, long> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(item, out var n)) return n;
        n = catalog.Find(item) is null ? 0 : await inventory.CountAsync(uow, id, item, ct);
        cache[item] = n;
        return n;
    }

    private string Name(string item) => catalog.Find(item)?.NamePl ?? item;

    internal async Task<ArtsView> ViewAsync(IUnitOfWork uow, Ctx x, DateTimeOffset now, CancellationToken ct)
    {
        var p = x.Profile;
        var c = x.Character;
        var realm = x.Status.Realm;
        var bag = new Dictionary<string, long>();
        async Task<ItemLineView> Line(string item, int qty) => new(item, Name(item), qty, await CountAsync(uow, c.Id, item, bag, ct));

        // flames
        var slotted = ArtsRules.SlottedFlames(config, p, realm).Select(s => s.Def.Id).ToHashSet();
        var flames = config.Flames.OrderByDescending(f => f.Rank).Select(f =>
        {
            var owned = p.Owned(f.Id);
            var state = owned is not null ? "owned"
                : p.Absorbing?.FlameId == f.Id ? "absorbing"
                : p.SubduedFlame == f.Id ? "subdued"
                : p.FlamesSeen.Contains(f.Id) ? "seen" : "unknown";
            var fused = p.Fusions.Select(z => z.Split('+')).FirstOrDefault(z => z.Contains(f.Id))?.FirstOrDefault(id => id != f.Id);
            return new FlameView(f.Id, f.Rank, f.NamePl, f.SourcePl, state, owned?.Level ?? 0, owned?.Xp ?? 0,
                owned is null || owned.Level >= config.FlameRules.MaxLevel ? 0 : ArtsRules.FlameLevelXp(config, owned.Level), slotted.Contains(f.Id), f.MinRealm, f.AbsorbHours,
                f.Stats, f.Perks, f.AlchemyPermille, f.ForgePermille, fused);
        }).ToList();
        var flameView = new FlamesView(flames, ArtsRules.FlameSlots(config, realm), config.FlameRules.SlotRealms.Count, config.FlameRules.SlotRealms, [.. p.Slots], p.SubduedFlame,
            p.Spirit?.BattleId, p.Absorbing?.FlameId, p.Absorbing?.Until, p.Absorbing?.ChancePermille, p.BurnedUntil,
            await CountAsync(uow, c.Id, config.FlameRules.TraceItem, bag, ct), config.FlameRules.TracesPerUse, await CountAsync(uow, c.Id, config.FlameRules.FeedItem, bag, ct),
            config.FlameRules.XpPerFeed, config.FlameRules.MaxLevel, [.. p.Fusions], p.Flames.Count, config.FlameRules.FusionBonusPermille);

        // toxin
        var venoms = new List<VenomView>();
        foreach (var v in config.Venoms)
            venoms.Add(new VenomView(v.Item, Name(v.Item), v.Rarity, v.Element, v.Xp, p.Venoms.GetValueOrDefault(v.Item), await CountAsync(uow, c.Id, v.Item, bag, ct)));
        var level = ArtsRules.ToxinLevel(config, p.ToxinXp);
        var coatings = new List<CoatingView>();
        foreach (var co in config.Coatings)
        {
            var lines = new List<ItemLineView>();
            foreach (var v in co.Venoms) lines.Add(await Line(v, 1));
            coatings.Add(new CoatingView(co.Id, co.NamePl, lines, co.Battles, co.Perks, lines.All(l => l.Owned >= 1)));
        }

        var toxin = new ToxinView(level, p.ToxinXp, level < config.Toxin.Levels.Count ? ArtsRules.ToxinNext(config, p.ToxinXp) : null, config.Toxin.Levels.Count, venoms,
            [.. config.Toxin.Perks.Select(t => new ToxinPerkView(t.Level, t.DescriptionPl, t.Level <= level))], p.PoisonedUntil,
            await CountAsync(uow, c.Id, config.Toxin.AntidoteItem, bag, ct), p.CoatingCharges > 0 ? p.Coating : null, p.CoatingCharges, coatings,
            c.Path == ArtsRules.PoisonPath ? config.Toxin.FailPermille / 2 : config.Toxin.FailPermille);

        // Gu
        var gu = new GuView(ArtsRules.GuSlots(config, realm),
            [.. p.Gu.OrderBy(w => w.Slot).Select(w => new GuWormView(w.Slot, w.Type, config.GuType(w.Type)?.NamePl ?? w.Type, w.Level, w.Rank, w.GrowingUntil))],
            [.. config.Gu.Types.Select(t => new GuTypeView(t.Id, t.NamePl, t.DescriptionPl, t.PerLevel))], await CountAsync(uow, c.Id, config.Gu.HerbItem, bag, ct),
            config.Gu.HatchHerbs, config.Gu.FeedHerbs, config.Gu.HoursPerLevel, config.Gu.MaxLevel, config.Gu.MaxRank);

        // tempering
        var baths = new List<BathView>();
        foreach (var b in config.Tempering.Baths)
        {
            var lines = new List<ItemLineView>();
            foreach (var i in b.Items) lines.Add(await Line(i.Item, i.Quantity));
            baths.Add(new BathView(b.Id, b.NamePl, lines, c.Path == ArtsRules.BodyPath ? Math.Max(1, b.Hours * 3 / 4) : b.Hours, b.Xp, lines.All(l => l.Owned >= l.Quantity)));
        }

        var stage = ArtsRules.TemperStageOf(config, p.Temper);
        var nextStage = config.Tempering.Stages.FirstOrDefault(s => s.From > p.Temper);
        var temper = new TemperView(p.Temper, config.Tempering.MaxLevel, p.TemperXp, ArtsRules.TemperXpNeeded(config, p.Temper),
            ArtsRules.TemperChance(config, p.Temper) + (c.Path == ArtsRules.BodyPath ? 50 : 0), ArtsRules.TemperNeedsPill(config, p.Temper),
            await CountAsync(uow, c.Id, config.Tempering.PillItem, bag, ct), stage?.NamePl, nextStage?.NamePl, nextStage?.From, p.Bath, p.BathUntil, baths, p.TemperAttempts,
            p.Temper > 0 ? "+" + p.Temper : null, config.Tempering.StepsPerXp);

        // sword
        var swords = ArtsRules.SwordLevel(config, p.SwordXp);
        var sword = new SwordView(swords, config.SwordIntent.Levels.Count, p.SwordXp, swords < config.SwordIntent.Levels.Count ? config.SwordIntent.Levels[swords] : null,
            await CountAsync(uow, c.Id, config.SwordIntent.EchoItem, bag, ct), config.SwordIntent.XpPerEcho * (c.Path == ArtsRules.QiPath ? 2 : 1), swords > 0 ? "⚔" + swords : null);

        var branch = new BranchView(p.Branch, ArtsRules.BranchTier(config, realm), config.Branches.TierRealms,
            [.. config.BranchesOf(c.Path).Select(b => new BranchOptionView(b.Id, b.NamePl, b.DescriptionPl, b.PerTier))], config.Branches.RespecStones,
            config.Branches.CapstoneRealm, p.Capstone, [.. config.Branches.CapstonesOf(c.Path).Select(b => new BranchOptionView(b.Id, b.NamePl, b.DescriptionPl, b.PerTier))]);

        var (alchemy, forge) = ArtsRules.Crafting(config, p, c.Path, realm);
        return new ArtsView(c.Id.Value, c.Path, realm, config.RulesVersion, flameView, toxin, gu, temper, sword, branch,
            [.. p.Notices.Select(n => new NoticeView(n.Kind, n.TextPl, n.At))], [.. ArtsRules.Titles(config, p).Select(t => new ArtsTitleView(t.Id, t.NamePl, t.Rarity))],
            ArtsRules.Perks(config, p, c.Path, realm), ArtsRules.Stats(config, p, c.Path, realm, now), alchemy, forge, p.Version);
    }
}
