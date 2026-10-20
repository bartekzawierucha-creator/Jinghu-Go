using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Identity.Domain;

namespace JianghuGo.Modules.Identity.Application;

public interface IAccountRepository
{
    Task<Account?> FindByIdAsync(IUnitOfWork uow, AccountId id, CancellationToken ct);
    Task<Account?> FindByEmailAsync(IUnitOfWork uow, string normalizedEmail, CancellationToken ct);

    /// <summary>Throws <see cref="DuplicateKeyException"/> when the e-mail is taken.</summary>
    Task AddAsync(IUnitOfWork uow, Account account, CancellationToken ct);
    Task UpdateAsync(IUnitOfWork uow, Account account, CancellationToken ct);

    /// <summary>Erasure (2.5): replaces the e-mail and password so the account can never sign in again.</summary>
    Task AnonymizeAsync(IUnitOfWork uow, AccountId id, string email, string passwordHash, CancellationToken ct);

    /// <summary>Hash of the account's one-time recovery code (2.0), or null.</summary>
    Task<string?> FindRecoveryHashAsync(IUnitOfWork uow, AccountId id, CancellationToken ct);

    /// <summary>Sets (or with null removes) the recovery code hash.</summary>
    Task SetRecoveryHashAsync(IUnitOfWork uow, AccountId id, string? hash, DateTimeOffset now, CancellationToken ct);
}

public interface ISessionRepository
{
    Task<Session?> FindByAccessHashAsync(IUnitOfWork uow, string accessTokenHash, CancellationToken ct);
    Task<Session?> FindByRefreshHashAsync(IUnitOfWork uow, string refreshTokenHash, CancellationToken ct);
    Task<Session?> FindByIdAsync(IUnitOfWork uow, SessionId id, CancellationToken ct);
    Task<IReadOnlyList<Session>> ListFamilyAsync(IUnitOfWork uow, Guid familyId, CancellationToken ct);
    Task<IReadOnlyList<Session>> ListActiveForAccountAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct);

    /// <summary>Distinct accounts that started or refreshed a session since a moment (active players, analytics).</summary>
    Task<long> CountActiveAccountsSinceAsync(IUnitOfWork uow, DateTimeOffset since, CancellationToken ct);
    Task AddAsync(IUnitOfWork uow, Session session, CancellationToken ct);
    Task UpdateAsync(IUnitOfWork uow, Session session, CancellationToken ct);
}
