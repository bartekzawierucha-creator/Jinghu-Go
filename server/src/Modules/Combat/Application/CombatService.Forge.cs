using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Domain;

namespace JianghuGo.Modules.Combat.Application;

public sealed record ForgedScrollView(Guid InstanceId, string TechniqueId, string NamePl, string Rarity, string RarityPl, int Era, string? Path, string DescriptionPl);

public sealed record TransmuteCommand(string? Path);

/// <summary>
/// The technique forge in play (1.8): scrolls of generated techniques drop from won battles (rarer from bosses), guard tower
/// floors and the world boss; once a world day a cultivator may meditate on the Dao for one; Dao essence (from broken scrolls)
/// transmutes into a new scroll. A forged technique is stored like a Dao design (frozen) and learned from its scroll.
/// </summary>
public sealed partial class CombatService
{
    public const string ForgedRules = TechniqueForge.RulesVersion;
    public const string MeditationRules = TechniqueForge.RulesVersion + "m";

    /// <summary>Creates a forged technique and puts its scroll in the bag (null when the forge is off or the bag is full).</summary>
    internal async Task<ForgedScrollView?> ForgeScrollAsync(IUnitOfWork uow, CharacterSummary finder, int era, string? path, int rarityBonus, string rulesVersion,
        string reason, string reference, DateTimeOffset at, CancellationToken ct)
    {
        if (config.Forge is not { } f) return null;
        var rng = new DeterministicRandom(seeds.NextSeed());
        var rarity = TechniqueForge.RollRarity(f, rng, rarityBonus);
        // a path scroll most of the time (the finder's), sometimes a free one anyone can learn
        var scrollPath = path ?? (rng.NextInt(0, 100) < 70 ? finder.Path : null);
        var id = TechniqueForge.NewId(era, rarity, scrollPath, rng.NextUInt64());
        if (await repo.FindCustomTechniqueAsync(uow, id, ct) is null)
        {
            var definition = TechniqueForge.Generate(config, id);
            await repo.AddCustomTechniqueAsync(uow, new CustomTechnique(id, finder.Id, finder.AccountId, "Niebo", definition, 0, 0, rulesVersion, at), ct);
        }

        var technique = (await repo.FindCustomTechniqueAsync(uow, id, ct))!.Definition;
        var scroll = await inventory.CreateInstanceAsync(uow, finder.Id, finder.AccountId, f.ScrollItem, reason, reference, ct, id);
        if (!scroll.IsSuccess) return null;
        var r = f.Rarity(rarity);
        return new ForgedScrollView(scroll.Value.Id, id, technique.NamePl, r.Id, r.NamePl, era, scrollPath, TechniqueText.Describe(config, technique));
    }

    /// <summary>A won, rewarded PvE battle may drop a scroll (chance by threat tier; bosses ×3).</summary>
    private async Task MaybeDropScrollAsync(IUnitOfWork uow, Battle battle, NpcDefinition npc, DateTimeOffset at, CancellationToken ct)
    {
        if (config.Forge is not { } f || f.DropPermille(npc) <= 0) return;
        var roll = new DeterministicRandom(battle.Seed ^ 0xF0F0F0F0F0F0F0F0UL).NextInt(0, 1000);
        if (roll >= f.DropPermille(npc)) return;
        if (await characters.FindAsync(uow, battle.CharacterA.Value, ct) is not { } c) return;
        var realm = await RealmOfAsync(uow, c.Id, ct);
        await ForgeScrollAsync(uow, c, TechniqueForge.EraOf(realm), null, npc.Boss ? 1 : 0, ForgedRules, "forge_drop", battle.Id.ToString("N"), at, ct);
    }

    /// <summary>Once a world day: meditate on the Dao for a scroll of your era (costs stones that grow with the era).</summary>
    public Task<Result<ForgedScrollView>> MeditateOnDaoAsync(AccountId accountId, Guid characterId, string? key, CancellationToken ct) =>
        uowFactory.RunAsync<Result<ForgedScrollView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "combat.dao_meditation", key, new { characterId }, async Task<Result<ForgedScrollView>> () =>
            {
                if (config.Forge is not { } f) return Error.NotFound("forge_disabled", "The forge is not available.");
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                var now = clock.GetUtcNow();
                var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
                if (await repo.CountForgedSinceAsync(uow, c.Id, MeditationRules, dayStart, ct) > 0)
                    return Error.LimitReached("dao_meditation_done", "You already meditated on the Dao today.");
                var era = TechniqueForge.EraOf(await RealmOfAsync(uow, c.Id, ct));
                var reference = Uuid7.New().ToString("N");
                var paid = await inventory.DebitAsync(uow, c.Id, f.MeditationStonesPerEra * (era + 1), "dao_meditation", reference, ct);
                if (!paid.IsSuccess) return paid.Error!;
                var scroll = await ForgeScrollAsync(uow, c, era, null, 0, MeditationRules, "dao_meditation", reference, now, ct);
                if (scroll is null) return Error.Conflict("bag_full", "There is no room in the bag.");
                await AuditAsync(uow, accountId, "combat.dao_meditation", Guid.Empty, new { characterId, scroll.TechniqueId, scroll.Rarity }, now, ct);
                return scroll;
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>Dao essence (from broken scrolls) becomes a new scroll of your era, one rarity luckier; a path may be chosen.</summary>
    public Task<Result<ForgedScrollView>> TransmuteAsync(AccountId accountId, Guid characterId, string? key, TransmuteCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<ForgedScrollView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "combat.dao_transmute", key, new { characterId, cmd }, async Task<Result<ForgedScrollView>> () =>
            {
                if (config.Forge is not { } f) return Error.NotFound("forge_disabled", "The forge is not available.");
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                if (cmd.Path is not null && !TechniqueForge.Paths.Contains(cmd.Path)) return Error.Validation("path_unknown", "Unknown path.");
                var now = clock.GetUtcNow();
                var reference = Uuid7.New().ToString("N");
                var used = await inventory.RemoveItemsAsync(uow, c.Id, f.EssenceItem, f.TransmuteEssence, "dao_transmute", reference, ct);
                if (!used.IsSuccess) return used.Error!;
                var era = TechniqueForge.EraOf(await RealmOfAsync(uow, c.Id, ct));
                var scroll = await ForgeScrollAsync(uow, c, era, string.IsNullOrEmpty(cmd.Path) ? null : cmd.Path, 1, ForgedRules, "dao_transmute", reference, now, ct);
                if (scroll is null) return Error.Conflict("bag_full", "There is no room in the bag.");
                return scroll;
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>What a scroll teaches (for the bag): name, rarity, description.</summary>
    public Task<Result<ForgedScrollView>> ScrollInfoAsync(string techniqueId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<ForgedScrollView>>(async uow =>
        {
            if (await repo.FindCustomTechniqueAsync(uow, techniqueId, ct) is not { } t) return Error.NotFound("technique_not_found", "Unknown technique.");
            var forged = t.RulesVersion.StartsWith(TechniqueForge.RulesVersion, StringComparison.Ordinal);
            var (era, rarity, path) = forged ? TechniqueForge.Header(t.Id) : (0, 0, t.Definition.Path);
            var r = config.Forge?.Rarity(rarity);
            return new ForgedScrollView(Guid.Empty, t.Id, t.Definition.NamePl, forged ? r?.Id ?? "common" : "dao", forged ? r?.NamePl ?? string.Empty : "Projekt Dao", era, path,
                TechniqueText.Describe(config, t.Definition));
        }, ct);
}
