using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Geolocation.Contracts;
using JianghuGo.Modules.Geolocation.Domain;

namespace JianghuGo.Modules.Geolocation.Application;

public interface IExplorationRepository
{
    Task<ExplorationSession?> FindActiveAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
    Task AddAsync(IUnitOfWork uow, ExplorationSession session, CancellationToken ct);
    Task<bool> TryUpdateAsync(IUnitOfWork uow, ExplorationSession session, int expectedVersion, CancellationToken ct);
}

public sealed record SampleDto(double Lat, double Lon, double AccuracyM, DateTimeOffset CapturedAt);

public sealed record SamplesCommand(IReadOnlyList<SampleDto>? Samples);

public sealed record SessionView(
    Guid Id,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    double DistanceM,
    int AcceptedSamples,
    int DroppedSamples,
    int Anomalies,
    bool Flagged,
    bool PositionVerified,
    string? PositionProblem,
    DateTimeOffset? RelocationUntil);

public sealed record SamplesResult(IReadOnlyList<string> Outcomes, SessionView Session);

public sealed class GeolocationService(
    IUnitOfWorkFactory uowFactory,
    IExplorationRepository repo,
    ICharacterDirectory characters,
    IAuditLog audit,
    ICorrelationContext correlation,
    GeolocationConfig config,
    TimeProvider clock) : IPositionVerifier
{
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");
    private static readonly Error NoSession = Error.Conflict("no_session", "Start an exploration session first.");

    public Task<Result<SessionView>> StartAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<SessionView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var active = await ActiveAsync(uow, c.Id, now, ct);
            if (active is null)
            {
                active = ExplorationSession.Start(c.Id, accountId, now);
                await repo.AddAsync(uow, active, ct);
            }

            await uow.CommitAsync(ct);
            return View(active, now);
        }, ct);

    public Task<Result<SessionView>> StopAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<SessionView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var active = await ActiveAsync(uow, c.Id, now, ct);
            if (active is null) return NoSession;
            var ended = active.End(now) with { Version = active.Version + 1 };
            if (!await repo.TryUpdateAsync(uow, ended, active.Version, ct)) throw new TransientConflictException("session changed");
            await uow.CommitAsync(ct);
            return View(ended, now);
        }, ct);

    public Task<Result<SessionView>> GetAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<SessionView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var active = await ActiveAsync(uow, c.Id, now, ct);
            await uow.CommitAsync(ct);
            return active is null ? NoSession : View(active, now);
        }, ct);

    /// <summary>
    /// Batch of device samples. Re-sending a batch is harmless: already-seen timestamps are dropped as out_of_order,
    /// so no idempotency key is needed. Outcomes are returned per sample, in time order.
    /// </summary>
    public Task<Result<SamplesResult>> SubmitAsync(AccountId accountId, Guid characterId, SamplesCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<SamplesResult>>(async uow =>
        {
            if (cmd.Samples is null || cmd.Samples.Count == 0 || cmd.Samples.Count > config.MaxSamplesPerBatch)
                return Error.Validation("samples_invalid", $"Send 1-{config.MaxSamplesPerBatch} samples per batch.");

            var now = clock.GetUtcNow();
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var active = await ActiveAsync(uow, c.Id, now, ct);
            if (active is null)
            {
                await uow.CommitAsync(ct); // persist a lazily expired session
                return NoSession;
            }

            var (next, outcomes) = SampleProcessor.Apply(config, active, [.. cmd.Samples.Select(x => new GeoSample(x.Lat, x.Lon, x.AccuracyM, x.CapturedAt))], now);
            next = next with { Version = active.Version + 1 };
            if (!await repo.TryUpdateAsync(uow, next, active.Version, ct)) throw new TransientConflictException("session changed");
            if (next.Flagged && !active.Flagged)
            {
                // Anti-abuse signal. Deliberately no coordinates in the audit trail.
                await audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, "geolocation.session_flagged", "exploration_session", next.Id.ToString(),
                    JsonSerializer.Serialize(new { characterId, next.Anomalies, next.AcceptedSamples, next.DroppedSamples }, Idempotency.Json), correlation.CorrelationId, now), ct);
            }

            await uow.CommitAsync(ct);
            return new SamplesResult(outcomes, View(next, now));
        }, ct);

    public async Task<PositionCheck> CheckAsync(IUnitOfWork uow, AccountId accountId, CharacterId characterId, DateTimeOffset now, CancellationToken ct)
    {
        var s = await repo.FindActiveAsync(uow, characterId, ct);
        if (s is not null && (s.AccountId != accountId || SampleProcessor.IsExpired(config, s, now))) s = null;
        var problem = SampleProcessor.Verify(config, s, now);
        return new PositionCheck(
            problem is null ? new VerifiedPosition(s!.LastPosition!.Value, s.LastAccuracyM!.Value, s.Id) : null,
            s?.LastPosition, problem);
    }

    private async Task<ExplorationSession?> ActiveAsync(IUnitOfWork uow, CharacterId characterId, DateTimeOffset now, CancellationToken ct)
    {
        var s = await repo.FindActiveAsync(uow, characterId, ct);
        if (s is null || !SampleProcessor.IsExpired(config, s, now)) return s;
        var ended = s.End(now) with { Version = s.Version + 1 };
        if (!await repo.TryUpdateAsync(uow, ended, s.Version, ct)) throw new TransientConflictException("session changed");
        return null;
    }

    private SessionView View(ExplorationSession s, DateTimeOffset now)
    {
        var problem = SampleProcessor.Verify(config, s, now);
        return new SessionView(s.Id, s.Status, s.StartedAt, s.EndedAt, Math.Round(s.DistanceM, 1), s.AcceptedSamples, s.DroppedSamples, s.Anomalies, s.Flagged,
            problem is null, problem, s.RelocationUntil);
    }
}
