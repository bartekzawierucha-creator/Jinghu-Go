namespace JianghuGo.Modules.World.Contracts;

public static class WorldEvents
{
    /// <summary>Qi earned in the world (springs, herbs, Flame sites). Consumed by Cultivation via the outbox.</summary>
    public const string QiReward = "world.qi_reward";

    public sealed record QiRewardV1(Guid AccountId, Guid CharacterId, long Qi, string Source);

    /// <summary>Materials gathered from a node. Consumed by Inventory (through its payload mirror).</summary>
    public const string ItemsGathered = "world.items_gathered";

    public sealed record GatheredItem(string ItemId, int Quantity);

    public sealed record ItemsGatheredV1(Guid AccountId, Guid CharacterId, string Source, IReadOnlyList<GatheredItem> Items);
}
