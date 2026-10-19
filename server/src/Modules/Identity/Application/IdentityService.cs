using System.Net.Mail;
using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace JianghuGo.Modules.Identity.Application;

public sealed record TokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record AuthenticatedSession(AccountId AccountId, SessionId SessionId);

public sealed class IdentityService(
    IUnitOfWorkFactory uowFactory,
    IAccountRepository accounts,
    ISessionRepository sessions,
    IAuditLog audit,
    ICorrelationContext correlation,
    IPasswordHasher<Account> hasher,
    TimeProvider clock,
    IOptions<IdentityModuleOptions> options)
{
    private static readonly Error InvalidCredentials = Error.Unauthorized("invalid_credentials", "Invalid e-mail or password.");
    private static readonly Error InvalidRefreshToken = Error.Unauthorized("invalid_refresh_token", "Refresh token is invalid, expired or revoked.");

    // Verifying against a real hash when the account does not exist keeps response timing uniform,
    // so login timing does not reveal which e-mails are registered.
    private readonly Lazy<string> _dummyHash = new(() =>
        hasher.HashPassword(new Account(default, "", "", default, 0, null), "timing-equaliser-not-a-password"));

    private IdentityModuleOptions Opt => options.Value;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public async Task<Result<AccountId>> RegisterAsync(string? email, string? password, CancellationToken ct)
    {
        if (ValidateEmail(email) is { } emailError)
        {
            return emailError;
        }

        if (ValidatePassword(password) is { } passwordError)
        {
            return passwordError;
        }

        var normalized = NormalizeEmail(email!);
        var now = clock.GetUtcNow();
        var account = new Account(AccountId.New(), normalized, "", now, 0, null);
        account = account with { PasswordHash = hasher.HashPassword(account, password!) };

        return await uowFactory.RunAsync<Result<AccountId>>(async uow =>
        {
            if (await accounts.FindByEmailAsync(uow, normalized, ct) is not null)
            {
                return Error.Conflict("email_taken", "An account with this e-mail already exists.");
            }

            try
            {
                await accounts.AddAsync(uow, account, ct);
            }
            catch (DuplicateKeyException)
            {
                return Error.Conflict("email_taken", "An account with this e-mail already exists.");
            }

            await AuditAsync(uow, account.Id, "account.registered", "account", account.Id.ToString(), new { }, now, ct);
            await uow.CommitAsync(ct);
            return account.Id;
        }, ct);
    }

    public async Task<Result<TokenPair>> LoginAsync(string? email, string? password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password) || password.Length > Opt.PasswordMaxLength)
        {
            return InvalidCredentials;
        }

        var now = clock.GetUtcNow();
        return await uowFactory.RunAsync<Result<TokenPair>>(async uow =>
        {
            var account = await accounts.FindByEmailAsync(uow, NormalizeEmail(email), ct);
            if (account is null)
            {
                hasher.VerifyHashedPassword(new Account(default, "", "", default, 0, null), _dummyHash.Value, password);
                return InvalidCredentials;
            }

            var verification = hasher.VerifyHashedPassword(account, account.PasswordHash, password);
            if (account.IsLocked(now))
            {
                // Same generic error while locked: no oracle for password correctness during lockout.
                return InvalidCredentials;
            }

            if (verification == PasswordVerificationResult.Failed)
            {
                var failures = account.FailedLoginCount + 1;
                if (failures >= Opt.MaxFailedLogins)
                {
                    account = account with { FailedLoginCount = 0, LockedUntil = now + Opt.LockoutDuration };
                    await AuditAsync(uow, account.Id, "account.locked", "account", account.Id.ToString(), new { until = account.LockedUntil }, now, ct);
                }
                else
                {
                    account = account with { FailedLoginCount = failures };
                }

                await accounts.UpdateAsync(uow, account, ct);
                await uow.CommitAsync(ct); // failed-attempt counter must persist even though login fails
                return InvalidCredentials;
            }

            account = account with { FailedLoginCount = 0, LockedUntil = null };
            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            {
                account = account with { PasswordHash = hasher.HashPassword(account, password) };
            }

            await accounts.UpdateAsync(uow, account, ct);
            var pair = await IssueAsync(uow, account.Id, Uuid7.New(), now, ct);
            await uow.CommitAsync(ct);
            return pair;
        }, ct);
    }

    public async Task<Result<TokenPair>> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken) || !refreshToken.StartsWith(Tokens.RefreshPrefix, StringComparison.Ordinal) || refreshToken.Length > 128)
        {
            return InvalidRefreshToken;
        }

        var now = clock.GetUtcNow();
        return await uowFactory.RunAsync<Result<TokenPair>>(async uow =>
        {
            var session = await sessions.FindByRefreshHashAsync(uow, Tokens.Hash(refreshToken), ct);
            if (session is null || session.RevokedAt is not null || session.RefreshExpiresAt <= now)
            {
                return InvalidRefreshToken;
            }

            if (session.RotatedAt is not null)
            {
                await RevokeFamilyAsync(uow, session.FamilyId, SessionRevokeReason.RefreshTokenReuse, now, ct);
                await AuditAsync(uow, session.AccountId, "session.refresh_reuse_detected", "session_family", session.FamilyId.ToString(), new { sessionId = session.Id.Value }, now, ct);
                await uow.CommitAsync(ct);
                return InvalidRefreshToken;
            }

            await sessions.UpdateAsync(uow, session with { RotatedAt = now }, ct);
            var pair = await IssueAsync(uow, session.AccountId, session.FamilyId, now, ct);
            await uow.CommitAsync(ct);
            return pair;
        }, ct);
    }

    /// <summary>
    /// A new one-time recovery code (2.0), shown once: with it and the e-mail the password can be reset without e-mail
    /// delivery. Only its hash is stored; a new code replaces the old one.
    /// </summary>
    public async Task<Result<string>> NewRecoveryCodeAsync(AccountId accountId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return await uowFactory.RunAsync<Result<string>>(async uow =>
        {
            var account = await accounts.FindByIdAsync(uow, accountId, ct);
            if (account is null) return Error.NotFound("account_not_found", "Account not found.");
            var code = RecoveryCodes.New();
            await accounts.SetRecoveryHashAsync(uow, accountId, hasher.HashPassword(account, RecoveryCodes.Normalize(code)), now, ct);
            await AuditAsync(uow, accountId, "account.recovery_code_created", "account", accountId.ToString(), new { }, now, ct);
            await uow.CommitAsync(ct);
            return code;
        }, ct);
    }

    /// <summary>Resets the password with the e-mail and the recovery code; the code is used up and every session ends.</summary>
    public async Task<Result<bool>> ResetPasswordAsync(string? email, string? recoveryCode, string? newPassword, CancellationToken ct)
    {
        var invalid = Error.Unauthorized("invalid_recovery", "Invalid e-mail or recovery code.");
        if (ValidatePassword(newPassword) is { } passwordError) return passwordError;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(recoveryCode) || recoveryCode.Length > 64) return invalid;
        var now = clock.GetUtcNow();
        return await uowFactory.RunAsync<Result<bool>>(async uow =>
        {
            var account = await accounts.FindByEmailAsync(uow, NormalizeEmail(email), ct);
            var hash = account is null ? null : await accounts.FindRecoveryHashAsync(uow, account.Id, ct);
            if (account is null || hash is null)
            {
                hasher.VerifyHashedPassword(new Account(default, "", "", default, 0, null), _dummyHash.Value, recoveryCode);
                return invalid;
            }

            if (account.IsLocked(now)) return invalid;
            if (hasher.VerifyHashedPassword(account, hash, RecoveryCodes.Normalize(recoveryCode)) == PasswordVerificationResult.Failed)
            {
                var failures = account.FailedLoginCount + 1;
                account = failures >= Opt.MaxFailedLogins
                    ? account with { FailedLoginCount = 0, LockedUntil = now + Opt.LockoutDuration }
                    : account with { FailedLoginCount = failures };
                await accounts.UpdateAsync(uow, account, ct);
                await uow.CommitAsync(ct);
                return invalid;
            }

            account = account with { PasswordHash = hasher.HashPassword(account, newPassword!), FailedLoginCount = 0, LockedUntil = null };
            await accounts.UpdateAsync(uow, account, ct);
            await accounts.SetRecoveryHashAsync(uow, account.Id, null, now, ct);
            await RevokeAllAsync(uow, account.Id, now, ct);
            await AuditAsync(uow, account.Id, "account.password_reset", "account", account.Id.ToString(), new { }, now, ct);
            await uow.CommitAsync(ct);
            return true;
        }, ct);
    }

    /// <summary>Changes the password (current one required); other sessions end, this one stays.</summary>
    public async Task<Result<bool>> ChangePasswordAsync(AccountId accountId, SessionId current, string? currentPassword, string? newPassword, CancellationToken ct)
    {
        if (ValidatePassword(newPassword) is { } passwordError) return passwordError;
        var now = clock.GetUtcNow();
        return await uowFactory.RunAsync<Result<bool>>(async uow =>
        {
            var account = await accounts.FindByIdAsync(uow, accountId, ct);
            if (account is null) return Error.NotFound("account_not_found", "Account not found.");
            if (string.IsNullOrEmpty(currentPassword) || hasher.VerifyHashedPassword(account, account.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
                return Error.Unauthorized("invalid_credentials", "Current password is wrong.");
            await accounts.UpdateAsync(uow, account with { PasswordHash = hasher.HashPassword(account, newPassword!) }, ct);
            var keep = (await sessions.FindByIdAsync(uow, current, ct))?.FamilyId;
            foreach (var s in await sessions.ListActiveForAccountAsync(uow, accountId, ct))
                if (s.FamilyId != keep) await sessions.UpdateAsync(uow, s with { RevokedAt = now, RevokeReason = SessionRevokeReason.Logout }, ct);
            await AuditAsync(uow, accountId, "account.password_changed", "account", accountId.ToString(), new { }, now, ct);
            await uow.CommitAsync(ct);
            return true;
        }, ct);
    }

    private async Task RevokeAllAsync(IUnitOfWork uow, AccountId accountId, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var s in await sessions.ListActiveForAccountAsync(uow, accountId, ct))
            await sessions.UpdateAsync(uow, s with { RevokedAt = now, RevokeReason = SessionRevokeReason.Logout }, ct);
    }

    public async Task LogoutAsync(SessionId sessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await uowFactory.RunAsync(async uow =>
        {
            var session = await sessions.FindByIdAsync(uow, sessionId, ct);
            if (session is null)
            {
                return;
            }

            await RevokeFamilyAsync(uow, session.FamilyId, SessionRevokeReason.Logout, now, ct);
            await AuditAsync(uow, session.AccountId, "session.logout", "session_family", session.FamilyId.ToString(), new { }, now, ct);
            await uow.CommitAsync(ct);
        }, ct);
    }

    public async Task<AuthenticatedSession?> AuthenticateAsync(string accessToken, CancellationToken ct)
    {
        if (!accessToken.StartsWith(Tokens.AccessPrefix, StringComparison.Ordinal) || accessToken.Length > 128)
        {
            return null;
        }

        return await uowFactory.RunAsync<AuthenticatedSession?>(async uow =>
        {
            var session = await sessions.FindByAccessHashAsync(uow, Tokens.Hash(accessToken), ct);
            return session is not null && session.IsAccessValid(clock.GetUtcNow())
                ? new AuthenticatedSession(session.AccountId, session.Id)
                : null;
        }, ct);
    }

    private async Task<TokenPair> IssueAsync(IUnitOfWork uow, AccountId accountId, Guid familyId, DateTimeOffset now, CancellationToken ct)
    {
        var access = Tokens.NewAccessToken();
        var refresh = Tokens.NewRefreshToken();
        var session = new Session(
            SessionId.New(), accountId, familyId,
            Tokens.Hash(access), now + Opt.AccessTokenLifetime,
            Tokens.Hash(refresh), now + Opt.RefreshTokenLifetime,
            now, null, null, null);
        await sessions.AddAsync(uow, session, ct);
        return new TokenPair(access, session.AccessExpiresAt, refresh, session.RefreshExpiresAt);
    }

    private async Task RevokeFamilyAsync(IUnitOfWork uow, Guid familyId, SessionRevokeReason reason, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var s in await sessions.ListFamilyAsync(uow, familyId, ct))
        {
            if (s.RevokedAt is null)
            {
                await sessions.UpdateAsync(uow, s with { RevokedAt = now, RevokeReason = reason }, ct);
            }
        }
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, string entityType, string entityId, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, entityType, entityId, JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);

    private static Error? ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !MailAddress.TryCreate(email.Trim(), out var parsed)
            || !string.Equals(parsed.Address, email.Trim(), StringComparison.Ordinal) || !parsed.Host.Contains('.', StringComparison.Ordinal))
        {
            return Error.Validation("email_invalid", "E-mail address is invalid.");
        }

        return null;
    }

    private Error? ValidatePassword(string? password)
    {
        if (password is null || password.Length < Opt.PasswordMinLength || password.Length > Opt.PasswordMaxLength)
        {
            return Error.Validation("password_invalid", $"Password must be {Opt.PasswordMinLength}-{Opt.PasswordMaxLength} characters long.");
        }

        return null;
    }
}

/// <summary>Recovery codes: 16 characters from an unambiguous alphabet in groups of four (~80 bits).</summary>
public static class RecoveryCodes
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string New()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var chars = bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray();
        return string.Join("-", Enumerable.Range(0, 4).Select(i => new string(chars, i * 4, 4)));
    }

    public static string Normalize(string code) => new string(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
