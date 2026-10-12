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
    DateTimeOffset? FinishedAt)
{
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
