using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;

namespace JianghuGo.Modules.Quests;

public sealed record RoadRealmView(int Index, string NamePl, string Hanzi, int Era, string EraPl, string TribulationPl, string UnlockPl, bool Reached, bool Current,
    IReadOnlyList<string> Unlocks);

public sealed record RoadView(int Realm, int Realms, IReadOnlyList<RoadRealmView> Steps);

/// <summary>
/// The Road of 66 Realms (1.9): every realm with its era, tribulation and everything it opens for this cultivator's path —
/// techniques, flames, methods, expeditions, crops, branch tiers and the ultimate — so the whole climb is visible at once.
/// Each module contributes what it opens (IRoadContributor); the ladder comes from Cultivation (IRoadRealms).
/// </summary>
public sealed class RoadService(
    IUnitOfWorkFactory uowFactory,
    ICharacterDirectory characters,
    JianghuGo.Modules.Cultivation.Contracts.ICultivationStatus cultivation,
    IRoadRealms ladder,
    IEnumerable<IRoadContributor> contributors)
{
    public Task<Result<RoadView>> GetAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<RoadView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return Error.NotFound("character_not_found", "Character not found.");
            var realm = (await cultivation.StatusAsync(uow, c.Id.Value, ct)).Realm;
            var unlocks = contributors.SelectMany(x => x.Unlocks(c.Path)).ToLookup(u => u.Realm, u => u.TextPl);
            var steps = ladder.Realms.Select(r => new RoadRealmView(r.Index, r.NamePl, r.Hanzi, r.Era, r.EraPl, r.TribulationPl, r.UnlockPl, r.Index <= realm, r.Index == realm,
                [.. unlocks[r.Index], .. (r.Index > 0 && r.Index % 11 == 0 ? new[] { "Nowa era: mocniejsze zwoje technik i materiały w świecie" } : [])])).ToList();
            return new RoadView(realm, steps.Count, steps);
        }, ct);
}
