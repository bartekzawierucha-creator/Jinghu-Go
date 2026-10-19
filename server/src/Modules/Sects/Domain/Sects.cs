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
    SeatRules Seat,
    DevelopmentRules? Development = null);

/// <summary>Sect buildings (bonuses for every member), weekly missions (shared goals) and the weekly sect boss.</summary>
public sealed record DevelopmentRules(
    IReadOnlyList<BuildingDefinition> Buildings,
    IReadOnlyList<MissionDefinition> Missions,
    int MissionsPerWeek,
    SectBossRules Boss);

/// <param name="Costs">Treasury cost of each level (index 0 = level 1).</param>
/// <param name="PerLevel">Combat perks per level (Combat.Contracts.PerkBonus, ‰).</param>
public sealed record BuildingDefinition(string Id, string NamePl, string DescriptionPl, IReadOnlyList<long> Costs,
    JianghuGo.Modules.Combat.Contracts.PerkBonus? PerLevel = null, int AlchemyPermillePerLevel = 0, int ForgePermillePerLevel = 0, int MissionRewardPermillePerLevel = 0);

/// <param name="Metric">battles_won / items_gathered / crafts / bosses_won.</param>
public sealed record MissionDefinition(string Id, string NamePl, string DescriptionPl, string Metric, long Target, long TreasuryReward, long ContributionReward);

public sealed record SectBossRules(string NpcId, string NamePl, int Seals, int PowerPermille, int RealmBonus, long TreasuryReward, long MemberStones, int AttemptsPerDay);

public static class MissionMetrics
{
    public const string BattlesWon = "battles_won";
    public const string ItemsGathered = "items_gathered";
    public const string Crafts = "crafts";
    public const string BossesWon = "bosses_won";
    public static readonly string[] All = [BattlesWon, ItemsGathered, Crafts, BossesWon];
}

/// <summary>A sect's development: building levels and this week's missions and boss.</summary>
public sealed record SectDevelopment(
    Guid SectId,
    IReadOnlyDictionary<string, int> Buildings,
    string Week,
    IReadOnlyDictionary<string, long> Missions,
    IReadOnlyList<string> MissionsClaimed,
    Guid BossReference,
    int SealsBroken,
    IReadOnlyList<Guid> BossFighters,
    IReadOnlyDictionary<string, int> BossAttempts,
    bool BossRewarded,
    int Version)
{
    public static SectDevelopment New(Guid sectId, string week) =>
        new(sectId, new Dictionary<string, int>(), week, new Dictionary<string, long>(), [], Guid.NewGuid(), 0, [], new Dictionary<string, int>(), false, 0);

    /// <summary>A new week resets missions and summons a new boss (buildings stay).</summary>
    public SectDevelopment ForWeek(string week) =>
        Week == week ? this : this with { Week = week, Missions = new Dictionary<string, long>(), MissionsClaimed = [], BossReference = Guid.NewGuid(), SealsBroken = 0, BossFighters = [], BossAttempts = new Dictionary<string, int>(), BossRewarded = false };

    public static string AttemptKey(Guid characterId, DateTimeOffset at) => characterId.ToString("N") + ":" + at.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    public int Level(string building) => Buildings.GetValueOrDefault(building);
}

public static class SectDevelopmentRules
{
    public static string WeekOf(DateTimeOffset at) =>
        ISOWeek.GetYear(at.UtcDateTime).ToString("0000", CultureInfo.InvariantCulture) + "-W" + ISOWeek.GetWeekOfYear(at.UtcDateTime).ToString("00", CultureInfo.InvariantCulture);

    /// <summary>This week's missions for a sect (deterministic pick).</summary>
    public static IReadOnlyList<MissionDefinition> MissionsFor(DevelopmentRules r, Guid sectId, string week)
    {
        var h = 14695981039346656037UL;
        foreach (var b in sectId.ToByteArray()) h = (h ^ b) * 1099511628211UL;
        foreach (var ch in week) h = (h ^ ch) * 1099511628211UL;
        var rnd = new DeterministicRandom(h);
        var pool = r.Missions.ToList();
        var list = new List<MissionDefinition>();
        while (list.Count < r.MissionsPerWeek && pool.Count > 0)
        {
            var i = rnd.NextInt(0, pool.Count);
            list.Add(pool[i]);
            pool.RemoveAt(i);
        }

        return list;
    }
}

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
/// <summary>A sect chat line. Members only; plain text, no location, kept for a limited time.</summary>
public sealed record ChatMessage(Guid Id, Guid SectId, CharacterId CharacterId, string Name, string Text, DateTimeOffset At);

public static class SectChat
{
    public const int MaxLength = 300;
    public const int BurstMessages = 5;
    public static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(30);

    /// <summary>Trimmed text, or null when empty, too long or containing control characters (newlines included).</summary>
    public static string? Clean(string? text)
    {
        var t = (text ?? string.Empty).Trim();
        return t.Length is 0 or > MaxLength || t.Any(char.IsControl) ? null : t;
    }
}

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
        if (c.Development is { } d)
        {
            if (d.Buildings is null || d.Buildings.Count == 0 || d.Buildings.Select(b => b.Id).Distinct().Count() != d.Buildings.Count) e.Add("development.buildings invalid.");
            foreach (var b in d.Buildings ?? [])
                if (string.IsNullOrWhiteSpace(b.Id) || string.IsNullOrWhiteSpace(b.NamePl) || b.Costs is null || b.Costs.Count is < 1 or > 20 || b.Costs.Any(x => x < 0)
                    || b.AlchemyPermillePerLevel is < 0 or > 100 || b.ForgePermillePerLevel is < 0 or > 100 || b.MissionRewardPermillePerLevel is < 0 or > 500)
                    e.Add($"building '{b.Id}' invalid.");
            if (d.Missions is null || d.Missions.Count < d.MissionsPerWeek || d.MissionsPerWeek is < 1 or > 6
                || d.Missions.Any(m => !MissionMetrics.All.Contains(m.Metric) || m.Target < 1 || m.TreasuryReward < 0 || m.ContributionReward < 0))
                e.Add("development.missions invalid.");
            if (d.Boss is null || string.IsNullOrWhiteSpace(d.Boss.NpcId) || d.Boss.Seals is < 1 or > 1000 || d.Boss.PowerPermille is < 100 or > 10000 || d.Boss.AttemptsPerDay < 1
                || d.Boss.TreasuryReward < 0 || d.Boss.MemberStones < 0)
                e.Add("development.boss invalid.");
        }

        if (c.Seat is null || c.Seat.CostStones < 0 || c.Seat.MoveCooldownDays < 0 || c.Seat.PrecisionDecimals is < 2 or > 4) e.Add("seat rules invalid (precision 2-4 decimals).");
        return e;
    }
}
