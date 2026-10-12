using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Modules.Identity.Application;
using JianghuGo.Modules.Identity.Domain;

namespace JianghuGo.Modules.Identity.Infrastructure;

internal sealed class InMemoryAccountRepository : IAccountRepository
{
    private const string Accounts = "identity.accounts";
    private const string ByEmail = "identity.accounts_by_email"; // mirrors UNIQUE(email) in SQL

    public Task<Account?> FindByIdAsync(IUnitOfWork uow, AccountId id, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<AccountId, Account>(Accounts).GetValueOrDefault(id));

    public Task<Account?> FindByEmailAsync(IUnitOfWork uow, string normalizedEmail, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        return Task.FromResult(
            u.Table<string, AccountId>(ByEmail).TryGetValue(normalizedEmail, out var id)
                ? u.Table<AccountId, Account>(Accounts).GetValueOrDefault(id)
                : null);
    }

    public Task AddAsync(IUnitOfWork uow, Account account, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var byEmail = u.Table<string, AccountId>(ByEmail);
        var all = u.Table<AccountId, Account>(Accounts);
        if (byEmail.ContainsKey(account.Email) || all.ContainsKey(account.Id))
        {
            throw new DuplicateKeyException("Account already exists.");
        }

        u.SetTable(ByEmail, byEmail.Add(account.Email, account.Id));
        u.SetTable(Accounts, all.Add(account.Id, account));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(IUnitOfWork uow, Account account, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var all = u.Table<AccountId, Account>(Accounts);
        var existing = all.GetValueOrDefault(account.Id) ?? throw new InvalidOperationException("Account not found.");
        if (existing.Email != account.Email)
        {
            throw new InvalidOperationException("E-mail change is not supported by UpdateAsync.");
        }

        u.SetTable(Accounts, all.SetItem(account.Id, account));
        return Task.CompletedTask;
    }
}

internal sealed class InMemorySessionRepository : ISessionRepository
{
    private const string Sessions = "identity.sessions";
    private const string ByAccess = "identity.sessions_by_access";
    private const string ByRefresh = "identity.sessions_by_refresh";

    public Task<Session?> FindByAccessHashAsync(IUnitOfWork uow, string accessTokenHash, CancellationToken ct) =>
        Task.FromResult(Lookup(InMemoryUnitOfWork.From(uow), ByAccess, accessTokenHash));

    public Task<Session?> FindByRefreshHashAsync(IUnitOfWork uow, string refreshTokenHash, CancellationToken ct) =>
        Task.FromResult(Lookup(InMemoryUnitOfWork.From(uow), ByRefresh, refreshTokenHash));

    public Task<Session?> FindByIdAsync(IUnitOfWork uow, SessionId id, CancellationToken ct) =>
        Task.FromResult(InMemoryUnitOfWork.From(uow).Table<SessionId, Session>(Sessions).GetValueOrDefault(id));

    public Task<IReadOnlyList<Session>> ListFamilyAsync(IUnitOfWork uow, Guid familyId, CancellationToken ct)
    {
        IReadOnlyList<Session> list = [.. InMemoryUnitOfWork.From(uow).Table<SessionId, Session>(Sessions).Values.Where(s => s.FamilyId == familyId)];
        return Task.FromResult(list);
    }

    public Task AddAsync(IUnitOfWork uow, Session session, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var access = u.Table<string, SessionId>(ByAccess);
        var refresh = u.Table<string, SessionId>(ByRefresh);
        if (access.ContainsKey(session.AccessTokenHash) || refresh.ContainsKey(session.RefreshTokenHash))
        {
            throw new DuplicateKeyException("Token hash collision.");
        }

        u.SetTable(Sessions, u.Table<SessionId, Session>(Sessions).Add(session.Id, session));
        u.SetTable(ByAccess, access.Add(session.AccessTokenHash, session.Id));
        u.SetTable(ByRefresh, refresh.Add(session.RefreshTokenHash, session.Id));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(IUnitOfWork uow, Session session, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var all = u.Table<SessionId, Session>(Sessions);
        var existing = all.GetValueOrDefault(session.Id) ?? throw new InvalidOperationException("Session not found.");
        if (existing.AccessTokenHash != session.AccessTokenHash || existing.RefreshTokenHash != session.RefreshTokenHash)
        {
            throw new InvalidOperationException("Token hashes are immutable.");
        }

        u.SetTable(Sessions, all.SetItem(session.Id, session));
        return Task.CompletedTask;
    }

    private static Session? Lookup(InMemoryUnitOfWork u, string index, string hash) =>
        u.Table<string, SessionId>(index).TryGetValue(hash, out var id)
            ? u.Table<SessionId, Session>(Sessions).GetValueOrDefault(id)
            : null;
}
