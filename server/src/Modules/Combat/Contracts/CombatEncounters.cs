using JianghuGo.Application;
using JianghuGo.Domain;

namespace JianghuGo.Modules.Combat.Contracts;

/// <summary>Lets other modules (World: spirit beasts) start a PvE battle inside THEIR unit of work.</summary>
public interface ICombatEncounters
{
    /// <summary>Returns the new battle id, or an error (character_not_found, npc_not_found, already_in_battle).</summary>
    Task<Result<Guid>> StartEncounterAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, string npcId, CancellationToken ct);

    /// <summary>
    /// A heavenly tribulation (Cultivation): a battle against the sky at the strength of <paramref name="realm"/>
    /// (‰ <paramref name="powerPermille"/>); the result is published in combat.battle_finished with kind "tribulation".
    /// </summary>
    Task<Result<Guid>> StartTribulationAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, int realm, int powerPermille, string namePl, CancellationToken ct);

    /// <summary>
    /// A spirit to subdue (Arts: a flame spirit guarding a heavenly flame): the NPC template scaled by realm and power;
    /// the result is published in combat.battle_finished with kind "spirit" and context "spirit:&lt;reference&gt;".
    /// </summary>
    Task<Result<Guid>> StartSpiritAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, string npcId, int realm, int powerPermille, Guid reference, string namePl, CancellationToken ct);
}

/// <summary>Display names of combat NPCs (World shows its region bosses by name).</summary>
public interface INpcNames
{
    string? NamePl(string npcId);
}
