using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Arts.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Crafting.Contracts;
using JianghuGo.Modules.Cultivation.Contracts;

namespace JianghuGo.Modules.Arts.Application;

/// <summary>Reads a character's arts profile with its path and realm (timers settled at <paramref name="now"/>).</summary>
internal sealed class ArtsReader(IArtsRepository repo, ICharacterDirectory characters, ICultivationStatus cultivation, ArtsConfig config, TimeProvider clock)
{
    public async Task<(ArtsProfile Profile, string Path, int Realm, DateTimeOffset Now)?> ReadAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct)
    {
        var c = await characters.FindAsync(uow, characterId, ct);
        if (c is null) return null;
        var stored = await repo.FindAsync(uow, c.Id, ct);
        if (stored is null) return null;
        var now = clock.GetUtcNow();
        var status = await cultivation.StatusAsync(uow, characterId, ct);
        return (ArtsRules.Settle(config, stored, now), c.Path, status.Realm, now);
    }
}

internal sealed class ArtsCombatPerks(ArtsReader reader, ArtsConfig config) : ICombatPerkBonuses
{
    public async Task<PerkBonus> BonusAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct) =>
        await reader.ReadAsync(uow, characterId, ct) is { } r ? ArtsRules.Perks(config, r.Profile, r.Path, r.Realm) : PerkBonus.None;
}

internal sealed class ArtsCombatStats(ArtsReader reader, ArtsConfig config) : ICombatStatBonuses
{
    public async Task<IReadOnlyDictionary<string, int>> BonusesAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct) =>
        await reader.ReadAsync(uow, characterId, ct) is { } r ? ArtsRules.Stats(config, r.Profile, r.Path, r.Realm, r.Now) : new Dictionary<string, int>();
}

/// <summary>Heavenly flames in the slots refine pills and temper blades.</summary>
internal sealed class ArtsCraftingBonuses(ArtsReader reader, ArtsConfig config) : ICraftingBonuses
{
    public async Task<(int AlchemyPermille, int ForgePermille)> BonusAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct) =>
        await reader.ReadAsync(uow, characterId, ct) is { } r ? ArtsRules.Crafting(config, r.Profile, r.Path, r.Realm) : (0, 0);
}

/// <summary>Badges next to the name: body tempering "+27", sword intent "⚔4".</summary>
internal sealed class ArtsDecorations(ArtsReader reader, ArtsConfig config) : ICharacterDecorationProvider
{
    public async Task<CharacterDecoration> DecorateAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct)
    {
        if (await reader.ReadAsync(uow, characterId, ct) is not { } r) return CharacterDecoration.None;
        var badges = new List<string>();
        if (r.Profile.Temper > 0) badges.Add("+" + r.Profile.Temper);
        var swords = ArtsRules.SwordLevel(config, r.Profile.SwordXp);
        if (swords > 0) badges.Add("⚔" + swords);
        return new CharacterDecoration(null, null, badges);
    }
}

internal sealed class ArtsTitles(ArtsReader reader, ArtsConfig config) : ITitleSource
{
    public async Task<IReadOnlyList<TitleGrant>> TitlesAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct) =>
        await reader.ReadAsync(uow, characterId, ct) is { } r
            ? [.. ArtsRules.Titles(config, r.Profile).Select(t => new TitleGrant("arts_" + t.Id, t.NamePl, t.Rarity, "arts"))]
            : [];
}
