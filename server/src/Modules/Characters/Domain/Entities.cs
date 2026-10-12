using System.Globalization;
using System.Text;
using JianghuGo.Domain;

namespace JianghuGo.Modules.Characters.Domain;

public enum DraftStatus
{
    Active,
    Finalized,
}

/// <summary>Character being rolled. <see cref="Version"/> is an optimistic-concurrency token.</summary>
public sealed record CharacterDraft(
    DraftId Id,
    AccountId AccountId,
    int Version,
    DraftStatus Status,
    string RulesVersion,
    ulong LastSeed,
    RolledTraits Traits,
    IReadOnlyList<KeyValuePair<string, int>> Stats,
    int RerollCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record Character(
    CharacterId Id,
    AccountId AccountId,
    string Name,
    string NormalizedName,
    string Path,
    string RulesVersion,
    RolledTraits Traits,
    IReadOnlyList<KeyValuePair<string, int>> Stats,
    DraftId SourceDraftId,
    DateTimeOffset CreatedAt);

/// <summary>Immutable ledger line. Balance must always equal the sum of deltas (verified by tests and SQL).</summary>
public sealed record DestinyTokenEntry(
    Guid Id,
    AccountId AccountId,
    int Delta,
    int BalanceAfter,
    string Reason,
    string ReferenceId,
    DateTimeOffset CreatedAt);

public static class DestinyTokenReasons
{
    public const string InitialGrant = "initial_grant";
    public const string Reroll = "reroll";
}

public static class CharacterNames
{
    /// <summary>Letters (any script), single spaces, hyphens and apostrophes; trimmed; NFC-normalised.</summary>
    public static Result<(string Name, string Normalized)> Validate(string? raw, NameRules rules)
    {
        var invalid = Error.Validation("name_invalid",
            $"Name must be {rules.MinLength}-{rules.MaxLength} characters: letters, single spaces, hyphens or apostrophes.");
        if (raw is null)
        {
            return invalid;
        }

        var name = raw.Trim().Normalize(NormalizationForm.FormC);
        var length = new StringInfo(name).LengthInTextElements;
        if (length < rules.MinLength || length > rules.MaxLength)
        {
            return invalid;
        }

        var prev = ' ';
        foreach (var ch in name)
        {
            var ok = char.IsLetter(ch) || ch is '-' or '\'' || (ch == ' ' && prev != ' ') || CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark;
            if (!ok)
            {
                return invalid;
            }

            prev = ch;
        }

        if (!char.IsLetter(name[0]))
        {
            return invalid;
        }

        return (name, name.ToUpperInvariant());
    }
}
