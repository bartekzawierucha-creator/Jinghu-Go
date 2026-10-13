using JianghuGo.Application;

namespace JianghuGo.Modules.Combat.Contracts;

/// <summary>
/// Extra combat stats a character has earned outside combat (e.g. cultivation realm and meridians). Every registered
/// provider is summed into the battle snapshot when a battle starts, so bonuses are frozen for the battle.
/// </summary>
public interface ICombatStatBonuses
{
    Task<IReadOnlyDictionary<string, int>> BonusesAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct);
}
