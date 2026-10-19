using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Cultivation.Domain;

namespace JianghuGo.Modules.Cultivation.Application;

/// <summary>
/// Meridian tree: every stage climbed opens one meridian point the player assigns to a stat; every realm also gives a
/// flat bonus to all stats. Both feed the combat snapshot through <see cref="ICombatStatBonuses"/>.
/// </summary>
public static class MeridianRules
{
    public static readonly string[] Stats = ["strength", "spirit", "agility", "constitution", "insight", "willpower", "focus", "elementalAffinity", "fate"];

    public const int RealmBonusPerRealm = 2;
    public const int ResetQiCostPerPoint = 5;

    public static int PointsEarned(CultivationConfig c, CultivationState? s) => s is null ? 0 : CultivationRules.GlobalIndex(c, s.RealmIndex, s.Stage);

    public static int RealmBonus(CultivationState? s) => (s?.RealmIndex ?? 0) * RealmBonusPerRealm;

    public static IReadOnlyDictionary<string, int> Bonuses(CultivationState? s, IReadOnlyDictionary<string, int> allocated)
    {
        var realm = RealmBonus(s);
        return Stats.Select(st => (st, v: realm + allocated.GetValueOrDefault(st))).Where(x => x.v > 0).ToDictionary(x => x.st, x => x.v);
    }
}

public sealed record MeridianView(int PointsEarned, int PointsSpent, int RealmBonus, IReadOnlyDictionary<string, int> Allocated, long ResetQiCost);

public sealed record MeridianCommand(string Stat, int ExpectedSpent);

public sealed class MeridianService(
    IUnitOfWorkFactory uowFactory,
    ICultivationRepository repo,
    ICharacterDirectory characters,
    IAuditLog audit,
    ICorrelationContext correlation,
    CultivationConfig config,
    TimeProvider clock)
{
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");

    public Task<Result<MeridianView>> GetAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<MeridianView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            return await ViewAsync(uow, c.Id, ct);
        }, ct);

    /// <summary>Opens one point in a stat. ExpectedSpent makes a retried request harmless (it fails with a conflict).</summary>
    public Task<Result<MeridianView>> OpenAsync(AccountId accountId, Guid characterId, MeridianCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<MeridianView>>(async uow =>
        {
            if (!MeridianRules.Stats.Contains(cmd.Stat)) return Error.Validation("unknown_stat", "Unknown stat.");
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var view = await ViewAsync(uow, c.Id, ct);
            if (view.PointsSpent != cmd.ExpectedSpent) return Error.Conflict("meridians_changed", "Meridians changed; reload and retry.");
            if (view.PointsSpent >= view.PointsEarned) return Error.Conflict("no_meridian_points", "No free meridian points; climb a stage first.");
            await repo.SaveMeridianAsync(uow, c.Id, cmd.Stat, view.Allocated.GetValueOrDefault(cmd.Stat) + 1, ct);
            await AuditAsync(uow, accountId, "cultivation.meridian_opened", characterId, $"{{\"stat\":\"{cmd.Stat}\"}}", ct);
            var after = await ViewAsync(uow, c.Id, ct);
            await uow.CommitAsync(ct);
            return after;
        }, ct);

    /// <summary>Closes every meridian for a Qi price (points × cost). Fails if the character lacks the Qi.</summary>
    public Task<Result<MeridianView>> ResetAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<MeridianView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var view = await ViewAsync(uow, c.Id, ct);
            if (view.PointsSpent == 0) return view;
            var state = await repo.FindAsync(uow, c.Id, ct);
            if (state is null || state.Qi < view.ResetQiCost) return Error.Conflict("not_enough_qi", "Not enough Qi to reset meridians.");
            var now = clock.GetUtcNow();
            if (!await repo.TrySaveAsync(uow, state with { Qi = state.Qi - view.ResetQiCost, Version = state.Version + 1, UpdatedAt = now }, state.Version, ct))
            {
                return Error.Conflict("version_conflict", "Cultivation state changed concurrently; reload and retry.");
            }

            await repo.ClearMeridiansAsync(uow, c.Id, ct);
            await AuditAsync(uow, accountId, "cultivation.meridians_reset", characterId, $"{{\"qi\":{view.ResetQiCost}}}", ct);
            var after = await ViewAsync(uow, c.Id, ct);
            await uow.CommitAsync(ct);
            return after;
        }, ct);

    private async Task<MeridianView> ViewAsync(IUnitOfWork uow, CharacterId id, CancellationToken ct)
    {
        var state = await repo.FindAsync(uow, id, ct);
        var allocated = await repo.ListMeridiansAsync(uow, id, ct);
        var spent = allocated.Values.Sum();
        return new MeridianView(MeridianRules.PointsEarned(config, state), spent, MeridianRules.RealmBonus(state), allocated,
            (long)spent * MeridianRules.ResetQiCostPerPoint);
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, Guid characterId, string json, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "character", characterId.ToString(), json, correlation.CorrelationId, clock.GetUtcNow()), ct);
}

/// <summary>Realm bonus + opened meridians, summed into the battle snapshot by Combat.</summary>
internal sealed class CultivationCombatBonuses(ICultivationRepository repo) : ICombatStatBonuses
{
    public async Task<IReadOnlyDictionary<string, int>> BonusesAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct)
    {
        var id = new CharacterId(characterId);
        var state = await repo.FindAsync(uow, id, ct);
        var bonuses = new Dictionary<string, int>(MeridianRules.Bonuses(state, await repo.ListMeridiansAsync(uow, id, ct)));
        if (await repo.FindProfileAsync(uow, id, ct) is { } profile)
            foreach (var (stat, v) in CultivationRules.CoreBonuses(profile)) bonuses[stat] = bonuses.GetValueOrDefault(stat) + v;
        return bonuses;
    }
}

/// <summary>Realm and stage for other modules (old-ladder rows are reported on the new ladder).</summary>
internal sealed class CultivationStatusProvider(ICultivationRepository repo, CultivationConfig config) : JianghuGo.Modules.Cultivation.Contracts.ICultivationStatus
{
    public async Task<JianghuGo.Modules.Cultivation.Contracts.CultivationStatus> StatusAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct)
    {
        var id = new CharacterId(characterId);
        var s = await repo.FindAsync(uow, id, ct);
        var realm = s?.RealmIndex ?? 0;
        if (s is not null && await repo.FindProfileAsync(uow, id, ct) is null) realm = Math.Min(config.Realms.Count - 1, realm + 2);
        var r = config.Realms[realm];
        return new JianghuGo.Modules.Cultivation.Contracts.CultivationStatus(realm, s?.Stage ?? 1, r.Id, r.NamePl, r.Era);
    }
}

/// <summary>Every realm reached grants its title (the era sets the plate colour).</summary>
internal sealed class CultivationTitles(JianghuGo.Modules.Cultivation.Contracts.ICultivationStatus status, CultivationConfig config) : JianghuGo.Modules.Characters.Contracts.ITitleSource
{
    private static readonly string[] EraRarity = ["common", "common", "rare", "epic", "legendary", "mythic", "mythic"];

    public async Task<IReadOnlyList<JianghuGo.Modules.Characters.Contracts.TitleGrant>> TitlesAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct)
    {
        var s = await status.StatusAsync(uow, characterId, ct);
        return [.. config.Realms.Take(s.Realm + 1).Select(r => new JianghuGo.Modules.Characters.Contracts.TitleGrant("realm_" + r.Id, r.TitlePl, EraRarity[Math.Clamp(r.Era, 0, 6)], "cultivation"))];
    }
}

public sealed record RankingEntry(int Rank, Guid CharacterId, string Name, string Path, string RealmId, string RealmNamePl, int Stage);

public sealed record RankingView(IReadOnlyList<RankingEntry> Top, RankingEntry? Me);

/// <summary>Public leaderboard of cultivators (realm → stage → Qi). Names and builds are public in the game.</summary>
public sealed class RankingService(IUnitOfWorkFactory uowFactory, ICultivationRepository repo, ICharacterDirectory characters, CultivationConfig config)
{
    public Task<Result<RankingView>> GetAsync(AccountId accountId, Guid characterId, int limit, CancellationToken ct) =>
        uowFactory.RunAsync<Result<RankingView>>(async uow =>
        {
            var me = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (me is null) return Error.NotFound("character_not_found", "Character not found.");
            var top = new List<RankingEntry>();
            var rank = 0;
            foreach (var s in await repo.ListTopAsync(uow, Math.Clamp(limit, 1, 100), ct))
            {
                rank++;
                var c = await characters.FindAsync(uow, s.CharacterId.Value, ct);
                if (c is not null) top.Add(Entry(rank, c, s));
            }

            var mine = await repo.FindAsync(uow, me.Id, ct);
            RankingEntry? meEntry = mine is null ? null : Entry(await repo.CountAheadAsync(uow, mine, ct) + 1, me, mine);
            return new RankingView(top, meEntry);
        }, ct);

    private RankingEntry Entry(int rank, CharacterSummary c, CultivationState s) =>
        new(rank, c.Id.Value, c.Name, c.Path, config.Realms[s.RealmIndex].Id, config.Realms[s.RealmIndex].NamePl, s.Stage);
}
