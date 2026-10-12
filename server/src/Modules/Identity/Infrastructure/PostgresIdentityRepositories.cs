using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Identity.Application;
using JianghuGo.Modules.Identity.Domain;
using Npgsql;

namespace JianghuGo.Modules.Identity.Infrastructure;

internal sealed class PostgresAccountRepository : IAccountRepository
{
    private const string Columns = "id, email, password_hash, created_at, failed_login_count, locked_until";

    private static Account Map(NpgsqlDataReader r) => new(
        new AccountId(r.GetGuid(0)), r.GetString(1), r.GetString(2), r.GetFieldValue<DateTimeOffset>(3), r.GetInt32(4),
        r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5));

    public Task<Account?> FindByIdAsync(IUnitOfWork uow, AccountId id, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync($"SELECT {Columns} FROM identity.accounts WHERE id = $1", Map, ct, id.Value);

    public Task<Account?> FindByEmailAsync(IUnitOfWork uow, string normalizedEmail, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync($"SELECT {Columns} FROM identity.accounts WHERE email = $1", Map, ct, normalizedEmail);

    public Task AddAsync(IUnitOfWork uow, Account a, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            $"INSERT INTO identity.accounts ({Columns}) VALUES ($1, $2, $3, $4, $5, $6)",
            ct, a.Id.Value, a.Email, a.PasswordHash, a.CreatedAt, a.FailedLoginCount, a.LockedUntil);

    public async Task UpdateAsync(IUnitOfWork uow, Account a, CancellationToken ct)
    {
        var rows = await PostgresUnitOfWork.From(uow).ExecuteAsync(
            "UPDATE identity.accounts SET password_hash = $2, failed_login_count = $3, locked_until = $4 WHERE id = $1 AND email = $5",
            ct, a.Id.Value, a.PasswordHash, a.FailedLoginCount, a.LockedUntil, a.Email);
        if (rows != 1)
        {
            throw new InvalidOperationException("Account not found (or e-mail changed) during update.");
        }
    }
}

internal sealed class PostgresSessionRepository : ISessionRepository
{
    private const string Columns =
        "id, account_id, family_id, access_token_hash, access_expires_at, refresh_token_hash, refresh_expires_at, created_at, rotated_at, revoked_at, revoke_reason";

    private static Session Map(NpgsqlDataReader r) => new(
        new SessionId(r.GetGuid(0)), new AccountId(r.GetGuid(1)), r.GetGuid(2),
        r.GetString(3), r.GetFieldValue<DateTimeOffset>(4), r.GetString(5), r.GetFieldValue<DateTimeOffset>(6), r.GetFieldValue<DateTimeOffset>(7),
        r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8),
        r.IsDBNull(9) ? null : r.GetFieldValue<DateTimeOffset>(9),
        r.IsDBNull(10) ? null : ParseReason(r.GetString(10)));

    private static SessionRevokeReason ParseReason(string s) => s switch
    {
        "logout" => SessionRevokeReason.Logout,
        "refresh_token_reuse" => SessionRevokeReason.RefreshTokenReuse,
        _ => throw new InvalidOperationException($"Unknown revoke reason '{s}'."),
    };

    private static string? Reason(SessionRevokeReason? r) => r switch
    {
        null => null,
        SessionRevokeReason.Logout => "logout",
        SessionRevokeReason.RefreshTokenReuse => "refresh_token_reuse",
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    public Task<Session?> FindByAccessHashAsync(IUnitOfWork uow, string accessTokenHash, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync($"SELECT {Columns} FROM identity.sessions WHERE access_token_hash = $1", Map, ct, accessTokenHash);

    public Task<Session?> FindByRefreshHashAsync(IUnitOfWork uow, string refreshTokenHash, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync($"SELECT {Columns} FROM identity.sessions WHERE refresh_token_hash = $1", Map, ct, refreshTokenHash);

    public Task<Session?> FindByIdAsync(IUnitOfWork uow, SessionId id, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).QuerySingleOrDefaultAsync($"SELECT {Columns} FROM identity.sessions WHERE id = $1", Map, ct, id.Value);

    public async Task<IReadOnlyList<Session>> ListFamilyAsync(IUnitOfWork uow, Guid familyId, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).QueryAsync($"SELECT {Columns} FROM identity.sessions WHERE family_id = $1 ORDER BY id", Map, ct, familyId);

    public Task AddAsync(IUnitOfWork uow, Session s, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            $"INSERT INTO identity.sessions ({Columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)",
            ct, s.Id.Value, s.AccountId.Value, s.FamilyId, s.AccessTokenHash, s.AccessExpiresAt, s.RefreshTokenHash, s.RefreshExpiresAt,
            s.CreatedAt, s.RotatedAt, s.RevokedAt, Reason(s.RevokeReason));

    public async Task UpdateAsync(IUnitOfWork uow, Session s, CancellationToken ct)
    {
        // Token hashes and ownership are immutable (also enforced by a trigger).
        var rows = await PostgresUnitOfWork.From(uow).ExecuteAsync(
            "UPDATE identity.sessions SET rotated_at = $2, revoked_at = $3, revoke_reason = $4 WHERE id = $1",
            ct, s.Id.Value, s.RotatedAt, s.RevokedAt, Reason(s.RevokeReason));
        if (rows != 1)
        {
            throw new InvalidOperationException("Session not found.");
        }
    }
}
