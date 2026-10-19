using JianghuGo.Application;

namespace JianghuGo.Modules.Cultivation.Contracts;

/// <summary>Where a character stands on the cultivation ladder (for modules whose features unlock with the realm).</summary>
public sealed record CultivationStatus(int Realm, int Stage, string RealmId, string RealmNamePl, int Era);

public interface ICultivationStatus
{
    Task<CultivationStatus> StatusAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct);
}

/// <summary>Body path steps accepted by Cultivation (tempering grows from walking).</summary>
public static class CultivationStepEvents
{
    public const string StepsAccepted = "cultivation.steps_accepted";

    public sealed record StepsAcceptedV1(Guid AccountId, Guid CharacterId, int Steps);
}
