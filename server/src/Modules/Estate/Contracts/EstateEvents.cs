namespace JianghuGo.Modules.Estate.Contracts;

/// <summary>Integration events published by the Estate (Quests counts harvests and expeditions).</summary>
public static class EstateEvents
{
    public const string Harvested = "estate.harvested";
    public const string ExpeditionFinished = "estate.expedition_finished";

    public sealed record HarvestedV1(Guid AccountId, Guid CharacterId, int Plots);

    public sealed record ExpeditionFinishedV1(Guid AccountId, Guid CharacterId, string ExpeditionId, int Count);
}
