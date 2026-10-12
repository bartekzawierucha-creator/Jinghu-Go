using JianghuGo.Application;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JianghuGo.Modules.Inventory.Application;

/// <summary>
/// Economy reconciliation: every wallet balance and stack quantity must equal the sum of its ledger lines.
/// The scan runs at most once per <see cref="Interval"/>; other calls get the cached result, so the anonymous
/// /health/economy endpoint cannot be used to load the database.
/// </summary>
public sealed class EconomyReconciliation(IUnitOfWorkFactory uowFactory, IInventoryRepository repo, TimeProvider clock) : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset At, int Wallets, int Stacks)? _last;

    public async Task<(int Wallets, int Stacks, DateTimeOffset CheckedAt)> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (_last is not { } last || now - last.At >= Interval)
            {
                var (w, s) = await uowFactory.RunAsync(uow => repo.CountMismatchesAsync(uow, ct), ct);
                _last = last = (now, w, s);
            }

            return (last.Wallets, last.Stacks, last.At);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

/// <summary>A mismatch is an operations alert, not a reason to stop serving traffic: it is not part of /health/ready.</summary>
public sealed class EconomyHealthCheck(EconomyReconciliation reconciliation) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var (wallets, stacks, at) = await reconciliation.GetAsync(cancellationToken);
        var data = new Dictionary<string, object> { ["walletMismatches"] = wallets, ["stackMismatches"] = stacks, ["checkedAt"] = at.ToString("O") };
        return wallets + stacks == 0
            ? HealthCheckResult.Healthy(data: data)
            : HealthCheckResult.Unhealthy($"{wallets} wallet(s) and {stacks} stack(s) disagree with their ledgers.", data: data);
    }
}
