using JianghuGo.Domain;

namespace JianghuGo.Modules.Identity.Domain;

public sealed record Account(
    AccountId Id,
    string Email,
    string PasswordHash,
    DateTimeOffset CreatedAt,
    int FailedLoginCount,
    DateTimeOffset? LockedUntil)
{
    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;
}

public enum SessionRevokeReason
{
    Logout,
    RefreshTokenReuse,
}

/// <summary>
/// One issued access/refresh token pair. Tokens are stored only as SHA-256 hashes. Refreshing rotates
/// the pair: the old row is marked rotated (both its tokens die) and a new row joins the same family.
/// Presenting a rotated refresh token again is treated as theft and revokes the entire family.
/// </summary>
public sealed record Session(
    SessionId Id,
    AccountId AccountId,
    Guid FamilyId,
    string AccessTokenHash,
    DateTimeOffset AccessExpiresAt,
    string RefreshTokenHash,
    DateTimeOffset RefreshExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? RevokedAt,
    SessionRevokeReason? RevokeReason)
{
    public bool IsAccessValid(DateTimeOffset now) => RevokedAt is null && RotatedAt is null && AccessExpiresAt > now;
}
