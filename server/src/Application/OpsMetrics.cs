namespace JianghuGo.Application;

/// <summary>
/// An aggregate number for the operators' dashboard (2.0 analytics): counts only, never per-player data or locations.
/// Modules contribute their own metrics.
/// </summary>
public interface IOpsMetric
{
    string Name { get; }

    Task<long> ValueAsync(IUnitOfWork uow, DateTimeOffset now, CancellationToken ct);
}
