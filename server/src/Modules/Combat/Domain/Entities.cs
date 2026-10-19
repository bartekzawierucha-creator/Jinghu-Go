using JianghuGo.Domain;

namespace JianghuGo.Modules.Combat.Domain;

public sealed record TechniqueMastery(CharacterId CharacterId, string TechniqueId, int Level, int Xp);

/// <summary>A Dao Lab design. Immutable once created: battles and scrolls refer to exactly these rules.</summary>
public sealed record CustomTechnique(string Id, CharacterId AuthorCharacterId, AccountId AuthorAccountId, string AuthorName, TechniqueDefinition Definition,
    int Points, int Allowance, string RulesVersion, DateTimeOffset CreatedAt);

/// <summary>A Dao technique a character can equip: designed by them or learned from a scroll.</summary>
public sealed record KnownTechnique(CharacterId CharacterId, string TechniqueId, string Source, DateTimeOffset LearnedAt);

public static class KnownSources
{
    public const string Designed = "designed";
    public const string Scroll = "scroll";
}

public sealed record Loadout(CharacterId CharacterId, AccountId AccountId, IReadOnlyList<string?> Slots, int Version);

public static class BattleKinds
{
    public const string Pve = "pve";
    public const string Pvp = "pvp";
    public const string Tower = "tower";
    public const string Boss = "boss";
    public const string Tribulation = "tribulation";
    public const string Arena = "arena";
    public const string Spirit = "spirit";

    /// <summary>Side B is played by the server AI (PvE, tower floor, world boss, tribulation, arena copy of a player).</summary>
    public static bool AgainstNpc(string kind) => kind is Pve or Tower or Boss or Tribulation or Arena or Spirit;
}

public static class BattleStatuses
{
    public const string PendingAcceptance = "pending_acceptance";
    public const string Active = "active";
    public const string Finished = "finished";
    public const string Declined = "declined";
    public const string Expired = "expired";
}

/// <summary>Side A always belongs to the player who started the battle; side B is the NPC or the challenged player.</summary>
public sealed record Battle(
    Guid Id,
    string Kind,
    string Status,
    string RulesVersion,
    ulong Seed,
    string SeedHash,
    CharacterId CharacterA,
    AccountId AccountA,
    CharacterId? CharacterB,
    AccountId? AccountB,
    string? NpcId,
    Combatant? CombatantA,
    Combatant? CombatantB,
    BattleState? State,
    int Version,
    DateTimeOffset Deadline,
    int TimeoutsA,
    int TimeoutsB,
    long RewardQi,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt,
    string? Context = null)
{
    /// <summary>Tower floor fought in a tower battle (context "tower:n").</summary>
    public int? TowerFloor => Kind == BattleKinds.Tower && Context is { } ctx && ctx.StartsWith("tower:", StringComparison.Ordinal) && int.TryParse(ctx[6..], out var n) ? n : null;

    /// <summary>World boss cycle of a boss battle (context "boss:n").</summary>
    public int? BossCycle => Kind == BattleKinds.Boss && Context is { } ctx && ctx.StartsWith("boss:", StringComparison.Ordinal) && int.TryParse(ctx[5..], out var n) ? n : null;

    public char? SideOf(AccountId account) =>
        account == AccountA ? 'a' : AccountB is { } b && account == b ? 'b' : null;
}

public sealed record SubmittedIntent(Guid BattleId, int Turn, char Side, Intent Intent, DateTimeOffset SubmittedAt);

/// <summary>Immutable record of one resolved turn: with the battle seed it reproduces the battle exactly.</summary>
public sealed record TurnRecord(
    Guid BattleId,
    int Turn,
    Intent IntentA,
    Intent IntentB,
    bool ForcedA,
    bool ForcedB,
    IReadOnlyList<TurnEvent> Events,
    BattleState StateAfter,
    DateTimeOffset ResolvedAt);

/// <summary>Highest Tower of Trials floor a character has cleared.</summary>
public sealed record TowerProgress(CharacterId CharacterId, AccountId AccountId, string Name, int BestFloor, DateTimeOffset UpdatedAt, int Version);

/// <summary>The shared world boss of one cycle.</summary>
public sealed record WorldBossState(int Cycle, string NpcId, int PoolHp, int HpLeft, DateTimeOffset? DefeatedAt, int Version);

/// <summary>A character's contribution to one world boss cycle; <see cref="ClaimedAt"/> is set once the kill reward is taken.</summary>
public sealed record WorldBossDamage(int Cycle, CharacterId CharacterId, AccountId AccountId, string Name, long Damage, int Attempts, DateTimeOffset? ClaimedAt, int Version);

/// <summary>A player's place in the ranked arena: rating, record and the frozen copy others fight against.</summary>
public sealed record ArenaEntry(CharacterId CharacterId, AccountId AccountId, string Name, int Rating, int Wins, int Losses, Combatant Defense, DateTimeOffset UpdatedAt, int Version);
