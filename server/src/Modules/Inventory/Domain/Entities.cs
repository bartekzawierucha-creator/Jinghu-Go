using JianghuGo.Domain;

namespace JianghuGo.Modules.Inventory.Domain;

/// <summary>Immutable ledger line for a stackable item. Stack quantity always equals the sum of deltas.</summary>
public sealed record StackEntry(Guid Id, CharacterId CharacterId, string ItemId, long Delta, long QuantityAfter, string Reason, string ReferenceId, DateTimeOffset At);

/// <summary>Immutable ledger line for Spirit Stones. Balance always equals the sum of deltas.</summary>
public sealed record WalletEntry(Guid Id, CharacterId CharacterId, long Delta, long BalanceAfter, string Reason, string ReferenceId, DateTimeOffset At);

public static class InstanceStates
{
    public const string Bag = "bag";
    public const string Equipped = "equipped";
    public const string Listed = "listed";   // in market escrow: cannot be equipped, upgraded or listed again
    public const string Destroyed = "destroyed"; // discarded by the owner; kept for provenance, never usable again
}

/// <summary>A unique (non-stackable) item. Ownership changes only through versioned updates; provenance in item events.</summary>
public sealed record ItemInstance(
    Guid Id,
    CharacterId CharacterId,
    AccountId AccountId,
    string ItemId,
    int UpgradeLevel,
    string State,
    int Version,
    DateTimeOffset CreatedAt,
    string? BoundRef = null); // immutable reference fixed at creation (a scroll's technique id)

/// <summary>Append-only provenance of a unique item: created, upgraded, equipped, listed, transferred …</summary>
public sealed record ItemEvent(Guid Id, Guid ItemInstanceId, CharacterId CharacterId, string Kind, string DataJson, DateTimeOffset At);
