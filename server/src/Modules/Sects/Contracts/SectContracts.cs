using JianghuGo.Application;
using JianghuGo.Domain;

namespace JianghuGo.Modules.Sects.Contracts;

public sealed record SectMembership(Guid SectId, string SectName, string Role);

public sealed record SectInfo(Guid Id, string Name, Guid LeaderCharacterId, Guid LeaderAccountId, int MemberCount, bool Disbanded);

/// <summary>What other modules (Territory) may ask about sects. Runs in the caller's unit of work.</summary>
public interface ISectDirectory
{
    Task<SectMembership?> MembershipAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
    Task<SectInfo?> FindAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct);

    /// <summary>Whether the role may perform a sect action (see SectActions in the module; e.g. "wage_war").</summary>
    bool Can(string role, string action);

    Task<Result<long>> DebitTreasuryAsync(IUnitOfWork uow, Guid sectId, long amount, string reason, string referenceId, CancellationToken ct);
    Task<Result<long>> CreditTreasuryAsync(IUnitOfWork uow, Guid sectId, long amount, string reason, string referenceId, CancellationToken ct);
}

public static class SectEvents
{
    public const string ApplicationDecided = "sects.application_decided";

    public sealed record ApplicationDecidedV1(Guid ApplicationId, Guid SectId, string SectName, Guid AccountId, Guid CharacterId, bool Accepted);
}

public static class SectActionNames
{
    public const string WageWar = "wage_war";
    public const string CollectTribute = "collect_tribute";
}
