using JianghuGo.Application;
using JianghuGo.Domain;

namespace JianghuGo.Modules.Combat.Contracts;

/// <summary>Lets other modules (World: spirit beasts) start a PvE battle inside THEIR unit of work.</summary>
public interface ICombatEncounters
{
    /// <summary>Returns the new battle id, or an error (character_not_found, npc_not_found, already_in_battle).</summary>
    Task<Result<Guid>> StartEncounterAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, string npcId, CancellationToken ct);
}
