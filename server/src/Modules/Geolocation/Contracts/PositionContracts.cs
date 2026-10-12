using JianghuGo.Application;
using JianghuGo.Domain;

namespace JianghuGo.Modules.Geolocation.Contracts;

/// <summary>A position the server trusts for rewards right now (fresh, accurate, no cooldown, not flagged).</summary>
public sealed record VerifiedPosition(GeoPoint Point, double AccuracyM, Guid SessionId);

public sealed record PositionCheck(VerifiedPosition? Verified, GeoPoint? LastKnown, string? Problem);

/// <summary>The only way other modules learn where a player is. Raw samples never leave Geolocation.</summary>
public interface IPositionVerifier
{
    Task<PositionCheck> CheckAsync(IUnitOfWork uow, AccountId accountId, CharacterId characterId, DateTimeOffset now, CancellationToken ct);
}
