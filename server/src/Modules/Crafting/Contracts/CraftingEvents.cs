using JianghuGo.Application;

namespace JianghuGo.Modules.Crafting.Contracts;

public static class CraftingEvents
{
    /// <summary>A craft or upgrade finished (consumed by Quests for daily/lifetime counters).</summary>
    public const string Crafted = "crafting.crafted";

    public sealed record CraftedV1(Guid AccountId, Guid CharacterId, string Operation, string RecipeOrItem, int Attempts, int Successes);
}

/// <summary>Extra success chance (‰) for alchemy and smithing earned elsewhere (e.g. a Heavenly Flame used as the cauldron fire).</summary>
public interface ICraftingBonuses
{
    Task<(int AlchemyPermille, int ForgePermille)> BonusAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct);
}
