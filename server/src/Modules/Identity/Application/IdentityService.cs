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

        await using var uow = await uowFactory.BeginAsync(ct);
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
    }

    public async Task<Result<TokenPair>> LoginAsync(string? email, string? password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password) || password.Length > Opt.PasswordMaxLength)
        {
            return InvalidCredentials;
        }

        var now = clock.GetUtcNow();
        await using var uow = await uowFactory.BeginAsync(ct);
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
        var pair = await IssueAsync(uow, account.Id, Guid.CreateVersion7(), now, ct);
        await uow.CommitAsync(ct);
        return pair;
    }

    public async Task<Result<TokenPair>> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken) || !refreshToken.StartsWith(Tokens.RefreshPrefix, StringComparison.Ordinal) || refreshToken.Length > 128)
        {
            return InvalidRefreshToken;
        }

        var now = clock.GetUtcNow();
        await using var uow = await uowFactory.BeginAsync(ct);
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
    }

    public async Task LogoutAsync(SessionId sessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var uow = await uowFactory.BeginAsync(ct);
        var session = await sessions.FindByIdAsync(uow, sessionId, ct);
        if (session is null)
        {
            return;
        }

        await RevokeFamilyAsync(uow, session.FamilyId, SessionRevokeReason.Logout, now, ct);
        await AuditAsync(uow, session.AccountId, "session.logout", "session_family", session.FamilyId.ToString(), new { }, now, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<AuthenticatedSession?> AuthenticateAsync(string accessToken, CancellationToken ct)
    {
        if (!accessToken.StartsWith(Tokens.AccessPrefix, StringComparison.Ordinal) || accessToken.Length > 128)
        {
            return null;
        }

        await using var uow = await uowFactory.BeginAsync(ct);
        var session = await sessions.FindByAccessHashAsync(uow, Tokens.Hash(accessToken), ct);
        return session is not null && session.IsAccessValid(clock.GetUtcNow())
            ? new AuthenticatedSession(session.AccountId, session.Id)
            : null;
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
        audit.AppendAsync(uow, new AuditEntry(Guid.CreateVersion7(), accountId, action, entityType, entityId, JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);

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
