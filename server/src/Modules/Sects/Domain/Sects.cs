using System.Globalization;
using System.Text;
using JianghuGo.Domain;

namespace JianghuGo.Modules.Sects.Domain;

public sealed record SectsConfig(
    string RulesVersion,
    long FoundingFeeStones,
    int MaxMembers,
    int MaxPendingApplicationsPerCharacter,
    int ApplicationMessageMaxLength,
    long MinDonation,
    long MaxDonation,
    SeatRules Seat);

public sealed record SeatRules(long CostStones, int MoveCooldownDays, int PrecisionDecimals);

public static class SectRoles
{
    public const string Leader = "leader";
    public const string Elder = "elder";
    public const string Member = "member";
    public static readonly IReadOnlyList<string> All = [Leader, Elder, Member];

    public static int Rank(string role) => role switch { Leader => 3, Elder => 2, Member => 1, _ => 0 };
}

public static class SectActions
{
    public const string DecideApplication = "decide_application";
    public const string Kick = "kick";
    public const string SetRole = "set_role";
    public const string SetSeat = "set_seat";
    public const string WageWar = "wage_war";          // claim and challenge territories, spend the treasury on them
    public const string CollectTribute = "collect_tribute";
}

/// <summary>
/// Who may do what in a sect. Deliberately code, not configuration: a mistake in a permission table is a security bug,
/// so changes go through review and the permission tests. Every check happens on the server for every request.
/// </summary>
public static class SectPolicy
{
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        [SectActions.DecideApplication] = [SectRoles.Leader, SectRoles.Elder],
        [SectActions.Kick] = [SectRoles.Leader, SectRoles.Elder],
        [SectActions.SetRole] = [SectRoles.Leader],
        [SectActions.SetSeat] = [SectRoles.Leader],
        [SectActions.WageWar] = [SectRoles.Leader, SectRoles.Elder],
        [SectActions.CollectTribute] = [SectRoles.Leader, SectRoles.Elder],
    };

    public static bool Can(string role, string action) => Allowed.TryGetValue(action, out var roles) && roles.Contains(role);

    /// <summary>Kicking also needs a strictly higher rank than the target (elders cannot kick elders, nobody kicks the leader).</summary>
    public static bool CanKick(string actorRole, string targetRole) => Can(actorRole, SectActions.Kick) && SectRoles.Rank(actorRole) > SectRoles.Rank(targetRole);
}

public sealed record Sect(
    Guid Id,
    string Name,
    string NormalizedName,
    CharacterId LeaderCharacterId,
    AccountId LeaderAccountId,
    int MemberCount,
    GeoPoint? Seat,
    DateTimeOffset? SeatSetAt,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DisbandedAt);

public sealed record Member(CharacterId CharacterId, Guid SectId, AccountId AccountId, string Name, string Role, long Contribution, DateTimeOffset JoinedAt);

public static class ApplicationStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Withdrawn = "withdrawn";
}

public sealed record SectApplication(
    Guid Id,
    Guid SectId,
    CharacterId CharacterId,
    AccountId AccountId,
    string CharacterName,
    string Message,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    CharacterId? DecidedBy);

/// <summary>Append-only sect history: who joined, left, was kicked or promoted, who donated, seat changes.</summary>
public sealed record SectEvent(Guid Id, Guid SectId, string Kind, CharacterId? Actor, CharacterId? Subject, string DataJson, DateTimeOffset At);

public static class SectNames
{
    /// <summary>3-24 letters/digits with single inner spaces, hyphens or apostrophes. Normalised (NFKC, upper case) for uniqueness.</summary>
    public static Result<(string Name, string Normalized)> Validate(string? raw)
    {
        var invalid = Error.Validation("sect_name_invalid", "Sect name: 3-24 letters or digits, single spaces, hyphens or apostrophes.");
        if (raw is null) return invalid;
        var name = raw.Trim().Normalize(NormalizationForm.FormC);
        var length = new StringInfo(name).LengthInTextElements;
        if (length is < 3 or > 24 || !char.IsLetter(name[0])) return invalid;
        var prev = ' ';
        foreach (var ch in name)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is '-' or '\'' || (ch == ' ' && prev != ' ') || CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark))
                return invalid;
            prev = ch;
        }

        return (name, name.Normalize(NormalizationForm.FormKC).ToUpperInvariant());
    }
}

public static class SectsConfigValidator
{
    public static IReadOnlyList<string> Validate(SectsConfig? c)
    {
        if (c is null) return ["Config is missing."];
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(c.RulesVersion)) e.Add("rulesVersion is required.");
        if (c.FoundingFeeStones < 0) e.Add("foundingFeeStones >= 0.");
        if (c.MaxMembers is < 2 or > 500) e.Add("maxMembers must be 2-500.");
        if (c.MaxPendingApplicationsPerCharacter is < 1 or > 20) e.Add("maxPendingApplicationsPerCharacter must be 1-20.");
        if (c.ApplicationMessageMaxLength is < 0 or > 1000) e.Add("applicationMessageMaxLength must be 0-1000.");
        if (c.MinDonation < 1 || c.MaxDonation < c.MinDonation) e.Add("donation range invalid.");
        if (c.Seat is null || c.Seat.CostStones < 0 || c.Seat.MoveCooldownDays < 0 || c.Seat.PrecisionDecimals is < 2 or > 4) e.Add("seat rules invalid (precision 2-4 decimals).");
        return e;
    }
}
