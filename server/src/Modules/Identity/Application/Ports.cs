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
}

public interface ISessionRepository
{
    Task<Session?> FindByAccessHashAsync(IUnitOfWork uow, string accessTokenHash, CancellationToken ct);
    Task<Session?> FindByRefreshHashAsync(IUnitOfWork uow, string refreshTokenHash, CancellationToken ct);
    Task<Session?> FindByIdAsync(IUnitOfWork uow, SessionId id, CancellationToken ct);
    Task<IReadOnlyList<Session>> ListFamilyAsync(IUnitOfWork uow, Guid familyId, CancellationToken ct);
    Task AddAsync(IUnitOfWork uow, Session session, CancellationToken ct);
    Task UpdateAsync(IUnitOfWork uow, Session session, CancellationToken ct);
}
