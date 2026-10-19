namespace JianghuGo.Modules.Combat.Contracts;

/// <summary>Integration events published by Combat (consumed via the outbox by Cultivation and Notifications).</summary>
public static class CombatEvents
{
    public const string BattleFinished = "combat.battle_finished";
    public const string ChallengeReceived = "combat.challenge_received";
    public const string ItemsRewarded = "combat.items_rewarded";
    public const string ArenaLeague = "combat.arena_league";

    /// <summary>A weekly arena reward was collected in a league (titles for the high leagues).</summary>
    public sealed record ArenaLeagueV1(Guid AccountId, Guid CharacterId, string LeagueId, int Rating);

    public sealed record RewardLine(string ItemId, int Quantity);

    /// <summary>Spirit stones and items earned in the Tower of Trials or from the world boss (granted by Inventory, once per event).</summary>
    public sealed record ItemsRewardedV1(Guid AccountId, Guid CharacterId, string Source, string ReferenceId, long Stones, IReadOnlyList<RewardLine> Items);

    /// <summary>
/// StonesPermille scales loot stones with the realm the opponent was raised to; LuckPermille (from Fate) raises drop chances;
/// MaxHp lets damage-based progress (tempering) be measured relative to the fighter's health.
/// </summary>
    public sealed record Participant(Guid AccountId, Guid CharacterId, string Side, string Result, long RewardQi, int DamageTaken = 0,
        int StonesPermille = 1000, int LuckPermille = 0, int MaxHp = 0);

    /// <summary>Result per participant: won / lost / draw. NPC sides are not listed.</summary>
    public sealed record BattleFinishedV1(Guid BattleId, string Kind, string Outcome, string? NpcId, IReadOnlyList<Participant> Participants, bool Boss = false,
        string? Context = null);

    public sealed record ChallengeReceivedV1(Guid BattleId, Guid ChallengerAccountId, Guid ChallengerCharacterId, string ChallengerName, Guid OpponentAccountId, Guid OpponentCharacterId);
}
