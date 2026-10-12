namespace JianghuGo.Modules.Territory.Contracts;

public static class TerritoryEvents
{
    public const string WarDeclared = "territory.war_declared";
    public const string WarResolved = "territory.war_resolved";

    public sealed record WarDeclaredV1(Guid WarId, string TerritoryKey, Guid AttackerSectId, string AttackerName, Guid DefenderSectId, Guid DefenderLeaderAccountId,
        DateTimeOffset StartsAt, DateTimeOffset EndsAt);

    public sealed record WarResolvedV1(Guid WarId, string TerritoryKey, Guid AttackerSectId, Guid DefenderSectId, Guid WinnerSectId,
        int AttackerScore, int DefenderScore, IReadOnlyList<Guid> LeaderAccountIds);
}
