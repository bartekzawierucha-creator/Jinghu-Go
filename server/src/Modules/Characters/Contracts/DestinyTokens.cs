using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Application;

namespace JianghuGo.Modules.Characters.Contracts;

/// <summary>
/// Destiny Tokens for other modules (1.9): earned from check-in streaks, the season pass and world bosses; spent on an extra
/// Meditation on the Dao. Grants are idempotent by (reason, reference).
/// </summary>
public interface IDestinyTokens
{
    Task<int> BalanceAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct);

    /// <summary>Adds tokens once per (reason, reference); a repeat returns the balance without a second entry.</summary>
    Task<Result<int>> GrantOnceAsync(IUnitOfWork uow, AccountId accountId, int amount, string reason, string referenceId, CancellationToken ct);

    Task<Result<int>> SpendAsync(IUnitOfWork uow, AccountId accountId, int amount, string reason, string referenceId, CancellationToken ct);
}

public static class DestinyTokenSources
{
    public const string Streak = "streak";
    public const string Season = "season";
    public const string Boss = "boss";
    public const string DaoMeditation = "dao_meditation";
}

internal sealed class DestinyTokens(IDestinyTokenLedger ledger, TimeProvider clock) : IDestinyTokens
{
    public async Task<int> BalanceAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct) =>
        await ledger.GetBalanceAsync(uow, accountId, ct) ?? 0;

    public async Task<Result<int>> GrantOnceAsync(IUnitOfWork uow, AccountId accountId, int amount, string reason, string referenceId, CancellationToken ct)
    {
        if (amount <= 0) return Error.Validation("amount_invalid", "Amount must be positive.");
        if ((await ledger.ListEntriesAsync(uow, accountId, ct)).Any(e => e.Reason == reason && e.ReferenceId == referenceId))
            return await BalanceAsync(uow, accountId, ct);
        var posted = await ledger.PostAsync(uow, accountId, amount, reason, referenceId, clock.GetUtcNow(), ct);
        return posted.IsSuccess ? posted.Value.BalanceAfter : posted.Error!;
    }

    public async Task<Result<int>> SpendAsync(IUnitOfWork uow, AccountId accountId, int amount, string reason, string referenceId, CancellationToken ct)
    {
        if (amount <= 0) return Error.Validation("amount_invalid", "Amount must be positive.");
        var posted = await ledger.PostAsync(uow, accountId, -amount, reason, referenceId, clock.GetUtcNow(), ct);
        return posted.IsSuccess ? posted.Value.BalanceAfter : posted.Error!;
    }
}
