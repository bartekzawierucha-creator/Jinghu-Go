namespace JianghuGo.Modules.World.Contracts;

public static class WorldEvents
{
    /// <summary>Qi earned in the world (springs, herbs, Flame sites). Consumed by Cultivation via the outbox.</summary>
    public const string QiReward = "world.qi_reward";

    public sealed record QiRewardV1(Guid AccountId, Guid CharacterId, long Qi, string Source);
}
