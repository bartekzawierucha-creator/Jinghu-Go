using System.Globalization;
using JianghuGo.Domain;

namespace JianghuGo.Modules.Territory.Domain;

public sealed record TerritoryConfig(
    string RulesVersion,
    double RegionSizeDegrees,
    long ClaimStones,
    long ChallengeStones,
    int PreparationHours,
    int WarHours,
    int BaseTerritories,
    int MembersPerExtraTerritory,
    long TributeStones,
    int TributeIntervalHours);

/// <summary>A region of the real map (a fixed grid cell). Created on first claim; ownership changes are versioned.</summary>
public sealed record Territory(string Key, Guid? OwnerSectId, DateTimeOffset? ClaimedAt, DateTimeOffset? LastTributeAt, int Version);

public static class WarStatuses
{
    public const string Declared = "declared";   // preparation: no scoring yet
    public const string Resolved = "resolved";
}

/// <summary>A GvG war for one territory. Scored by PvP duels between members of the two sects inside the war window.</summary>
public sealed record War(
    Guid Id,
    string TerritoryKey,
    Guid AttackerSectId,
    Guid DefenderSectId,
    string Status,
    DateTimeOffset DeclaredAt,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int AttackerScore,
    int DefenderScore,
    Guid? WinnerSectId,
    DateTimeOffset? ResolvedAt,
    int Version)
{
    public bool InWindow(DateTimeOffset t) => Status == WarStatuses.Declared && t >= StartsAt && t < EndsAt;
    public bool Due(DateTimeOffset now) => Status == WarStatuses.Declared && now >= EndsAt;
}

/// <summary>Append-only ownership history of a territory.</summary>
public sealed record OwnershipChange(Guid Id, string TerritoryKey, Guid? FromSectId, Guid? ToSectId, string Reason, Guid? WarId, DateTimeOffset At);

public static class OwnershipReasons
{
    public const string Claim = "claim";
    public const string War = "war";
}

public static class TerritoryRules
{
    /// <summary>"rx.ry": grid indices of the cell containing the point. Keys are derived on the server from a verified position only.</summary>
    public static string KeyOf(TerritoryConfig c, GeoPoint p) =>
        string.Create(CultureInfo.InvariantCulture, $"{(long)Math.Floor(p.Lon / c.RegionSizeDegrees)}.{(long)Math.Floor(p.Lat / c.RegionSizeDegrees)}");

    public static bool IsKey(string? key)
    {
        var parts = key?.Split('.');
        return parts is { Length: 2 } && parts.All(x => long.TryParse(x, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) && x.Length <= 8);
    }

    /// <summary>South-west corner and size of the cell, for maps.</summary>
    public static (double Lat, double Lon, double Size) Bounds(TerritoryConfig c, string key)
    {
        var parts = key.Split('.');
        var rx = long.Parse(parts[0], CultureInfo.InvariantCulture);
        var ry = long.Parse(parts[1], CultureInfo.InvariantCulture);
        return (ry * c.RegionSizeDegrees, rx * c.RegionSizeDegrees, c.RegionSizeDegrees);
    }

    public static int MaxTerritories(TerritoryConfig c, int memberCount) => c.BaseTerritories + memberCount / c.MembersPerExtraTerritory;

    /// <summary>Ties keep the territory with the defender.</summary>
    public static Guid Winner(War w) => w.AttackerScore > w.DefenderScore ? w.AttackerSectId : w.DefenderSectId;
}

public static class TerritoryConfigValidator
{
    public static IReadOnlyList<string> Validate(TerritoryConfig? c)
    {
        if (c is null) return ["Config is missing."];
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(c.RulesVersion)) e.Add("rulesVersion is required.");
        if (c.RegionSizeDegrees is < 0.005 or > 1) e.Add("regionSizeDegrees must be 0.005-1.");
        if (c.ClaimStones < 0 || c.ChallengeStones < 0 || c.TributeStones < 0) e.Add("costs must be >= 0.");
        if (c.PreparationHours is < 0 or > 168 || c.WarHours is < 1 or > 168) e.Add("war timing invalid.");
        if (c.BaseTerritories < 1 || c.MembersPerExtraTerritory < 1) e.Add("territory limits invalid.");
        if (c.TributeIntervalHours < 1) e.Add("tributeIntervalHours >= 1.");
        return e;
    }
}
