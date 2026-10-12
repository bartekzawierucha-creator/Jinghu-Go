using System.Globalization;
using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Cultivation.Domain;

namespace JianghuGo.Modules.Cultivation.Application;

public sealed record CultivationView(
    Guid CharacterId,
    string Path,
    string RulesVersion,
    string RealmId,
    string RealmNamePl,
    int Stage,
    int StagesInRealm,
    bool IsPeak,
    long Qi,
    long QiRequired,
    double BreakthroughChance,
    bool MajorBreakthrough,
    int Version,
    DateTimeOffset? DeviationUntil,
    DateTimeOffset? MeditationStartedAt,
    long DailyMeditationQi,
    long DailyMeditationQiCap,
    int DailySteps,
    int DailyStepCap);

public sealed record MeditationResult(long QiGained, int MinutesCounted, bool DailyCapReached, CultivationView State);

public sealed record StepsCommand(int Steps);

public sealed record StepsResult(int AcceptedSteps, bool Clipped, long QiGained, CultivationView State);

public sealed record BreakthroughCommand(int ExpectedVersion);

public sealed record BreakthroughResult(bool Success, double Chance, long QiSpent, CultivationView State);

public sealed class CultivationService(
    IUnitOfWorkFactory uowFactory,
    ICultivationRepository repo,
    ICharacterDirectory characters,
    IIdempotencyStore idempotency,
    IAuditLog audit,
    IOutbox outbox,
    ICorrelationContext correlation,
    IRandomSeedSource seeds,
    CultivationConfig config,
    TimeProvider clock)
{
    public const string FinishMeditationOperation = "cultivation.meditation.finish";
    public const string StepsOperation = "cultivation.steps";
    public const string BreakthroughOperation = "cultivation.breakthrough";

    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");
    private static readonly Error Concurrent = Error.Conflict("version_conflict", "Cultivation state changed concurrently; reload and retry.");

    private sealed record Loaded(CharacterSummary Character, CultivationState State, int StoredVersion);

    public Task<Result<CultivationView>> GetAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<CultivationView>>(async uow =>
        {
            var loaded = await LoadAsync(uow, accountId, characterId, clock.GetUtcNow(), ct);
            return loaded is null ? CharacterNotFound : View(loaded.Character, loaded.State);
        }, ct);

    /// <summary>Starts a session at server time. Repeating the call while a session runs changes nothing.</summary>
    public Task<Result<CultivationView>> StartMeditationAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<CultivationView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var loaded = await LoadAsync(uow, accountId, characterId, now, ct);
            if (loaded is null) return CharacterNotFound;
            if (loaded.State.MeditationStartedAt is not null) return View(loaded.Character, loaded.State);

            var state = loaded.State with { MeditationStartedAt = now };
            if (!await SaveAsync(uow, state, loaded.StoredVersion, now, ct)) return Concurrent;
            await uow.CommitAsync(ct);
            return View(loaded.Character, state with { Version = loaded.StoredVersion + 1 });
        }, ct);

    public Task<Result<MeditationResult>> FinishMeditationAsync(AccountId accountId, Guid characterId, string? key, CancellationToken ct) =>
        uowFactory.RunAsync<Result<MeditationResult>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, FinishMeditationOperation, key, new { characterId },
                async Task<Result<MeditationResult>> () =>
                {
                    var now = clock.GetUtcNow();
                    var loaded = await LoadAsync(uow, accountId, characterId, now, ct);
                    if (loaded is null) return CharacterNotFound;
                    if (loaded.State.MeditationStartedAt is not { } started)
                        return Error.Conflict("no_active_meditation", "No meditation session is in progress.");

                    var (raw, minutes) = CultivationRules.MeditationQi(config, loaded.State.Path, loaded.Character.Stats.GetValueOrDefault("spirit"),
                        started, now, loaded.State.DeviationUntil);
                    var room = Math.Max(0, config.Meditation.DailyQiCap - loaded.State.DailyMeditationQi);
                    var gained = Math.Min(raw, room);
                    var state = loaded.State with
                    {
                        MeditationStartedAt = null,
                        Qi = checked(loaded.State.Qi + gained),
                        DailyMeditationQi = loaded.State.DailyMeditationQi + gained,
                    };
                    if (!await SaveAsync(uow, state, loaded.StoredVersion, now, ct)) return Concurrent;
                    await AuditAsync(uow, accountId, "cultivation.meditation_finished", characterId,
                        new { started, minutes, raw, gained, dailyTotal = state.DailyMeditationQi }, now, ct);
                    return new MeditationResult(gained, minutes, gained < raw, View(loaded.Character, state with { Version = loaded.StoredVersion + 1 }));
                }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    public Task<Result<StepsResult>> ReportStepsAsync(AccountId accountId, Guid characterId, string? key, StepsCommand command, CancellationToken ct) =>
        uowFactory.RunAsync<Result<StepsResult>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, StepsOperation, key, new { characterId, command },
                async Task<Result<StepsResult>> () =>
                {
                    if (command.Steps is < 1 or > 100_000)
                        return Error.Validation("steps_invalid", "Steps must be between 1 and 100000 per report.");

                    var now = clock.GetUtcNow();
                    var loaded = await LoadAsync(uow, accountId, characterId, now, ct);
                    if (loaded is null) return CharacterNotFound;
                    if (loaded.State.Path != "body")
                        return Error.Conflict("path_mismatch", "Only the Body path cultivates through steps.");

                    // First report of a character: allow one hour of walking (pedometer history before install is not trusted).
                    var since = now - (loaded.State.LastStepsReportAt ?? now.AddHours(-1));
                    var (accepted, clipped) = CultivationRules.PlausibleSteps(config, command.Steps, since, loaded.State.DailySteps);
                    var after = loaded.State.DailySteps + accepted;
                    var qi = CultivationRules.StepsQi(config, loaded.State.DailySteps, after);
                    var state = loaded.State with { DailySteps = after, Qi = checked(loaded.State.Qi + qi), LastStepsReportAt = now };
                    if (!await SaveAsync(uow, state, loaded.StoredVersion, now, ct)) return Concurrent;
                    if (clipped)
                    {
                        // Anti-abuse signal only: implausible input is clipped, never punished on a single report.
                        await AuditAsync(uow, accountId, "cultivation.steps_clipped", characterId,
                            new { reported = command.Steps, accepted, sinceSeconds = (int)since.TotalSeconds }, now, ct);
                    }

                    return new StepsResult(accepted, clipped, qi, View(loaded.Character, state with { Version = loaded.StoredVersion + 1 }));
                }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    public Task<Result<BreakthroughResult>> BreakthroughAsync(AccountId accountId, Guid characterId, string? key, BreakthroughCommand command, CancellationToken ct) =>
        uowFactory.RunAsync<Result<BreakthroughResult>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, BreakthroughOperation, key, new { characterId, command },
                () => BreakthroughCoreAsync(uow, accountId, characterId, command, ct), ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    private async Task<Result<BreakthroughResult>> BreakthroughCoreAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, BreakthroughCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var loaded = await LoadAsync(uow, accountId, characterId, now, ct);
        if (loaded is null) return CharacterNotFound;
        var s = loaded.State;
        if (s.Version != command.ExpectedVersion) return Concurrent;
        if (CultivationRules.IsPeak(config, s.RealmIndex, s.Stage)) return Error.LimitReached("peak_reached", "This is the highest cultivation stage currently available.");
        if (s.InDeviation(now)) return Error.Conflict("qi_deviation_active", $"Qi deviation: breakthroughs are impossible until {s.DeviationUntil:O}.");
        if (s.MeditationStartedAt is not null) return Error.Conflict("meditation_in_progress", "Finish meditating before attempting a breakthrough.");

        var required = CultivationRules.QiRequired(config, s.RealmIndex, s.Stage);
        if (s.Qi < required) return Error.InsufficientFunds("insufficient_qi", $"Breakthrough needs {required} Qi (have {s.Qi}).");

        var chance = CultivationRules.SuccessChancePpm(config, s.RealmIndex, s.Stage, loaded.Character.Stats);
        var seed = seeds.NextSeed();
        var roll = CultivationRules.Roll(seed);
        var success = roll < chance;
        var (toRealm, toStage) = success ? CultivationRules.Next(config, s.RealmIndex, s.Stage) : (s.RealmIndex, s.Stage);
        var spent = success ? required : CultivationRules.FailureQiLoss(config, required);
        var state = s with
        {
            Qi = s.Qi - spent,
            RealmIndex = toRealm,
            Stage = toStage,
            DeviationUntil = success ? s.DeviationUntil : now.AddMinutes(config.Breakthrough.DeviationMinutes),
        };
        if (!await SaveAsync(uow, state, loaded.StoredVersion, now, ct)) return Concurrent;

        var attempt = new BreakthroughAttempt(Uuid7.New(), new CharacterId(characterId), accountId, s.RealmIndex, s.Stage, toRealm, toStage,
            success, chance, roll, seed, spent, config.RulesVersion, now);
        await repo.AddBreakthroughAsync(uow, attempt, ct);
        await AuditAsync(uow, accountId, success ? "cultivation.breakthrough_succeeded" : "cultivation.breakthrough_failed", characterId,
            new { attemptId = attempt.Id, chance, roll, seed = seed.ToString(CultureInfo.InvariantCulture), spent, from = new[] { s.RealmIndex, s.Stage }, to = new[] { toRealm, toStage } }, now, ct);
        await outbox.EnqueueAsync(uow, OutboxMessage.Create(
            success ? CultivationEvents.BreakthroughSucceeded : CultivationEvents.BreakthroughFailed,
            new CultivationEvents.BreakthroughV1(accountId.Value, characterId, attempt.Id, config.Realms[s.RealmIndex].Id, s.Stage, config.Realms[toRealm].Id, toStage, success),
            now, correlation.CorrelationId), ct);

        return new BreakthroughResult(success, chance / (double)CultivationRules.ChanceScale, spent,
            View(loaded.Character, state with { Version = loaded.StoredVersion + 1 }));
    }

    public Task<Result<IReadOnlyList<BreakthroughAttempt>>> HistoryAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<BreakthroughAttempt>>>(async uow =>
        {
            var character = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (character is null) return CharacterNotFound;
            return Result<IReadOnlyList<BreakthroughAttempt>>.Success(await repo.ListBreakthroughsAsync(uow, character.Id, ct));
        }, ct);

    /// <summary>Ownership check + state (initial state for a character that never cultivated; not persisted until changed).</summary>
    private async Task<Loaded?> LoadAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, DateTimeOffset now, CancellationToken ct)
    {
        var character = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
        if (character is null) return null;
        var today = CultivationRules.WorldDate(config, now);
        var stored = await repo.FindAsync(uow, character.Id, ct);
        var state = (stored ?? CultivationState.Initial(character.Id, accountId, character.Path, today, now)).ForDay(today);
        return new Loaded(character, state, stored?.Version ?? 0);
    }

    private Task<bool> SaveAsync(IUnitOfWork uow, CultivationState state, int storedVersion, DateTimeOffset now, CancellationToken ct) =>
        repo.TrySaveAsync(uow, state with { Version = storedVersion + 1, UpdatedAt = now }, storedVersion, ct);

    private CultivationView View(CharacterSummary c, CultivationState s)
    {
        var realm = config.Realms[s.RealmIndex];
        var peak = CultivationRules.IsPeak(config, s.RealmIndex, s.Stage);
        return new CultivationView(c.Id.Value, s.Path, config.RulesVersion, realm.Id, realm.NamePl, s.Stage, realm.Stages, peak, s.Qi,
            peak ? 0 : CultivationRules.QiRequired(config, s.RealmIndex, s.Stage),
            peak ? 0 : CultivationRules.SuccessChancePpm(config, s.RealmIndex, s.Stage, c.Stats) / (double)CultivationRules.ChanceScale,
            CultivationRules.IsMajor(config, s.RealmIndex, s.Stage), s.Version, s.DeviationUntil, s.MeditationStartedAt,
            s.DailyMeditationQi, config.Meditation.DailyQiCap, s.DailySteps, config.Paths.Body.DailyStepCap);
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, Guid characterId, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "character", characterId.ToString(),
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}
