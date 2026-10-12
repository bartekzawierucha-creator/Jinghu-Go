using System.Diagnostics.CodeAnalysis;

namespace JianghuGo.Domain;

// Strongly typed identifiers: an AccountId can never be passed where a CharacterId is expected.
// Version 7 GUIDs are time-ordered, which keeps B-tree indexes in PostgreSQL compact.

public readonly record struct AccountId(Guid Value)
{
    public static AccountId New() => new(Uuid7.New());
    public override string ToString() => Value.ToString();
}

public readonly record struct SessionId(Guid Value)
{
    public static SessionId New() => new(Uuid7.New());
    public override string ToString() => Value.ToString();
}

public readonly record struct CharacterId(Guid Value)
{
    public static CharacterId New() => new(Uuid7.New());
    public override string ToString() => Value.ToString();
    public static bool TryParse([NotNullWhen(true)] string? s, out CharacterId id)
    {
        var ok = Guid.TryParse(s, out var g);
        id = new CharacterId(g);
        return ok;
    }
}

public readonly record struct DraftId(Guid Value)
{
    public static DraftId New() => new(Uuid7.New());
    public override string ToString() => Value.ToString();
}
