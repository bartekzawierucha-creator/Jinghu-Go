namespace JianghuGo.Modules.Combat.Contracts;

/// <summary>Integration events published by Combat (consumed via the outbox by Cultivation and Notifications).</summary>
public static class CombatEvents
{
    public const string BattleFinished = "combat.battle_finished";
    public const string ChallengeReceived = "combat.challenge_received";

    public sealed record Participant(Guid AccountId, Guid CharacterId, string Side, string Result, long RewardQi);

    /// <summary>Result per participant: won / lost / draw. NPC sides are not listed.</summary>
    public sealed record BattleFinishedV1(Guid BattleId, string Kind, string Outcome, string? NpcId, IReadOnlyList<Participant> Participants, bool Boss = false);

    public sealed record ChallengeReceivedV1(Guid BattleId, Guid ChallengerAccountId, Guid ChallengerCharacterId, string ChallengerName, Guid OpponentAccountId, Guid OpponentCharacterId);
}
