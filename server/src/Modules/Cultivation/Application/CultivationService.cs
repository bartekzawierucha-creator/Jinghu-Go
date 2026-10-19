using System.Globalization;
using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Cultivation.Domain;
using JianghuGo.Modules.Inventory.Contracts;

namespace JianghuGo.Modules.Cultivation.Application;

public sealed record MethodView(string Id, string NamePl, string DescriptionPl, bool Known, bool Active, int Mastery, int MinRealm, string? Path, string? ItemId, bool Available);

public sealed record SummaryView(DateTimeOffset From, DateTimeOffset To, long Qi, int Enlightenments, int AutoBreakthroughs, bool VesselFull, bool RetreatFinished);

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
    int DailyStepCap,
    double MeditationQiPerMinute,
    int MeditationMaxMinutes,
    int MeditationMinMinutes,
    // cultivation-3
    int RealmIndex,
    int RealmCount,
    string RealmHanzi,
    int Era,
    string EraNamePl,
    string StageName,
    string RealmUnlockPl,
    string RealmTitlePl,
    string? NextRealmNamePl,
    string? NextRealmUnlockPl,
    string? NextTribulationPl,
    string Mode,
    DateTimeOffset? RetreatUntil,
    IReadOnlyList<int> RetreatOptions,
    long Vessel,
    int VesselLevel,
    long VesselUpgradeCrystals,
    long Crystals,
    double FlowPerHour,
    double MeditationPerHour,
    double RetreatPerHour,
    int OfflineCapHours,
    string? MethodId,
    IReadOnlyList<MethodView> Methods,
    int Instability,
    IReadOnlyDictionary<string, string> Qualities,
    string? CoreColor,
    string CoreColorPl,
    int Karma,
    int Reincarnations,
    bool CanReincarnate,
    int KarmaOnReincarnation,
    int? AutoBreakthroughPermille,
    bool AutoBreakthroughAvailable,
    bool AcceleratorAvailable,
    int AcceleratorHours,
    string? BottleneckItem,
    bool TribulationRequired,
    Guid? TribulationBattleId,
    int Enlightenments,
    long LifetimeQi,
    SummaryView? Summary,
    bool CompressionAvailable,
    int WeekendPermille = 1000);

public sealed record MeditationResult(long QiGained, int MinutesCounted, bool DailyCapReached, CultivationView State);

public sealed record StepsCommand(int Steps);

public sealed record StepsResult(int AcceptedSteps, bool Clipped, long QiGained, CultivationView State);

public sealed record BreakthroughCommand(int ExpectedVersion);

public sealed record BreakthroughResult(bool Success, double Chance, long QiSpent, CultivationView State);

public sealed record RetreatCommand(int Hours);

public sealed record CompressCommand(long Qi);

public sealed record MethodCommand(string? MethodId);

public sealed record AutoBreakthroughCommand(int? MinChancePermille);

public sealed record TribulationView(Guid BattleId, CultivationView State);

/// <summary>
/// Cultivation (rules cultivation-3): Qi flows all the time (×1), faster in meditation (×5) or a closed-door retreat
/// (×8), up to the vessel's capacity and an offline cap; every read settles the flow first. Stages 1-11 are climbed by
/// breakthrough rolls (the Dog stage needs the era's pill); stage 12 (Pig, Perfection) ends with a heavenly
/// tribulation — a real battle — that opens the next great realm. 66 realms × 12 stages.
/// </summary>
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
    TimeProvider clock,
    IInventory inventory,
    ICombatEncounters encounters)
{
    public const string FinishMeditationOperation = "cultivation.meditation.finish";
    public const string StepsOperation = "cultivation.steps";
    public const string BreakthroughOperation = "cultivation.breakthrough";

    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");
    private static readonly Error Concurrent = Error.Conflict("version_conflict", "Cultivation state changed concurrently; reload and retry.");

    internal sealed record Loaded(CharacterSummary Character, CultivationState State, int StoredVersion, CultivationProfile Profile, int ProfileVersion);

    // ---------------------------------------------------------------- reading

    public Task<Result<CultivationView>> GetAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, (_, l, _) => Task.FromResult<Result<Loaded>>(l), ct);

    /// <summary>Clears the offline summary once the player has seen it.</summary>
    public Task<Result<CultivationView>> AcknowledgeSummaryAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, (_, l, _) => Task.FromResult<Result<Loaded>>(l with { Profile = l.Profile with { LastSummary = null } }), ct);

    // ---------------------------------------------------------------- modes

    /// <summary>Meditation: the flow ×5 until stopped (or the offline cap). Repeating the call changes nothing.</summary>
    public Task<Result<CultivationView>> StartMeditationAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, (_, l, now) =>
        {
            if (l.Profile.Mode == CultivationModes.Retreat) return Task.FromResult<Result<Loaded>>(Error.Conflict("in_retreat", "You are in a closed-door retreat."));
            return Task.FromResult<Result<Loaded>>(l with
            {
                Profile = l.Profile with { Mode = CultivationModes.Meditation },
                State = l.State with { MeditationStartedAt = l.State.MeditationStartedAt ?? now },
            });
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
                    if (loaded.Profile.Mode != CultivationModes.Meditation)
                        return Error.Conflict("no_active_meditation", "No meditation session is in progress.");
                    var before = loaded.State.Qi;
                    var minutes = loaded.State.MeditationStartedAt is { } s ? (int)Math.Max(0, (now - s).TotalMinutes) : 0;
                    var settled = await SettleAsync(uow, loaded, now, ct);
                    var stopped = settled with
                    {
                        Profile = settled.Profile with { Mode = CultivationModes.Idle },
                        State = settled.State with { MeditationStartedAt = null },
                    };
                    if (!await SaveAsync(uow, stopped, now, ct)) return Concurrent;
                    var gained = stopped.State.Qi - before;
                    return new MeditationResult(gained, minutes, false, View(stopped.Character, stopped.State with { Version = stopped.StoredVersion + 1 }, stopped.Profile, now));
                }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>Closed-door retreat for a fixed number of hours (×8). Leaving early costs a Qi deviation.</summary>
    public Task<Result<CultivationView>> StartRetreatAsync(AccountId accountId, Guid characterId, RetreatCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, (_, l, now) =>
        {
            var f = config.Flow;
            if (l.State.RealmIndex < f.RetreatMinRealm) return Fail(Error.Conflict("realm_too_low", $"Retreats open at {config.Realms[f.RetreatMinRealm].NamePl}."));
            if (!f.RetreatHours.Contains(cmd.Hours)) return Fail(Error.Validation("retreat_hours_invalid", "Choose one of the offered retreat lengths."));
            if (cmd.Hours > 12 && l.State.RealmIndex < f.LongRetreatMinRealm) return Fail(Error.Conflict("realm_too_low", $"Long retreats open at {config.Realms[f.LongRetreatMinRealm].NamePl}."));
            if (l.Profile.Mode == CultivationModes.Retreat) return Fail(Error.Conflict("in_retreat", "You are already in a retreat."));
            return Ok(l with
            {
                Profile = l.Profile with { Mode = CultivationModes.Retreat, RetreatUntil = now.AddHours(cmd.Hours), RetreatHours = cmd.Hours },
                State = l.State with { MeditationStartedAt = null },
            });
        }, ct);

    public Task<Result<CultivationView>> LeaveRetreatAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, (_, l, now) =>
        {
            if (l.Profile.Mode != CultivationModes.Retreat) return Fail(Error.Conflict("not_in_retreat", "You are not in a retreat."));
            return Ok(l with
            {
                Profile = l.Profile with { Mode = CultivationModes.Idle, RetreatUntil = null },
                State = l.State with { DeviationUntil = now.AddMinutes(config.Breakthrough.DeviationMinutes) },
            });
        }, ct);

    // ---------------------------------------------------------------- Qi economy

    /// <summary>Compresses Qi into crystals (ratio:1). Crystals raise the flow and upgrade the vessel.</summary>
    public Task<Result<CultivationView>> CompressAsync(AccountId accountId, Guid characterId, CompressCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, (_, l, _) =>
        {
            var c = config.Compression;
            if (l.State.RealmIndex < c.MinRealm) return Fail(Error.Conflict("realm_too_low", $"Compression opens at {config.Realms[c.MinRealm].NamePl}."));
            if (cmd.Qi < c.Ratio || cmd.Qi % c.Ratio != 0) return Fail(Error.Validation("compress_amount_invalid", $"Compress a multiple of {c.Ratio} Qi."));
            if (cmd.Qi > l.State.Qi) return Fail(Error.InsufficientFunds("insufficient_qi", "Not enough Qi."));
            return Ok(l with { State = l.State with { Qi = l.State.Qi - cmd.Qi }, Profile = l.Profile with { Crystals = l.Profile.Crystals + (cmd.Qi / c.Ratio) } });
        }, ct);

    public Task<Result<CultivationView>> UpgradeVesselAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, (_, l, _) =>
        {
            var p = l.Profile;
            if (p.VesselLevel >= config.Flow.VesselMaxLevel) return Fail(Error.LimitReached("vessel_max", "The vessel cannot grow further."));
            var cost = VesselCost(p.VesselLevel);
            if (p.Crystals < cost) return Fail(Error.InsufficientFunds("insufficient_crystals", $"Needs {cost} crystals."));
            return Ok(l with { Profile = p with { Crystals = p.Crystals - cost, VesselLevel = p.VesselLevel + 1 } });
        }, ct);

    private long VesselCost(int level) => config.Flow.VesselCrystalCost * (1L << Math.Min(level, 40));

    /// <summary>Once per world day: some hours of idle flow at once (more for a God of Time).</summary>
    public Task<Result<CultivationView>> UseAcceleratorAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, (_, l, now) =>
        {
            var today = CultivationRules.WorldDate(config, now);
            if (l.Profile.AcceleratorDate == today) return Fail(Error.Conflict("accelerator_used", "The accelerator is used for today."));
            var hours = AcceleratorHours(l.State.RealmIndex);
            var perHour = CultivationRules.FlowPerHour(config, l.State.Path, l.State.RealmIndex, l.Character.Stats.GetValueOrDefault("spirit"), l.Profile, CultivationModes.Idle, now, l.State.DeviationUntil);
            var qi = (long)(perHour * hours);
            return Ok(l with
            {
                State = l.State with { Qi = checked(l.State.Qi + qi) },
                Profile = l.Profile with { AcceleratorDate = today, LifetimeQi = l.Profile.LifetimeQi + qi },
            });
        }, ct);

    private int AcceleratorHours(int realm) => config.Flow.DailyAcceleratorHours + (realm >= config.Flow.TimeGodRealm ? config.Flow.TimeGodHours : 0);

    // ---------------------------------------------------------------- methods

    public Task<Result<CultivationView>> SetMethodAsync(AccountId accountId, Guid characterId, MethodCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, (_, l, _) =>
        {
            if (config.Method(cmd.MethodId) is not { } m) return Fail(Error.Validation("method_unknown", "Unknown cultivation method."));
            if (!l.Profile.KnownMethods.Contains(m.Id)) return Fail(Error.Conflict("method_not_known", "Learn the method first."));
            return Ok(l with { Profile = l.Profile with { MethodId = m.Id } });
        }, ct);

    /// <summary>Learns a method: path methods are free for their path, others need the realm and consume the manual.</summary>
    public Task<Result<CultivationView>> LearnMethodAsync(AccountId accountId, Guid characterId, MethodCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, async (uow, l, _) =>
        {
            if (config.Method(cmd.MethodId) is not { } m) return Error.Validation("method_unknown", "Unknown cultivation method.");
            if (l.Profile.KnownMethods.Contains(m.Id)) return Error.Conflict("method_known", "You already know this method.");
            if (m.Path is not null && m.Path != l.State.Path && m.Item is null) return Error.Conflict("method_wrong_path", "This method belongs to another path.");
            if (l.State.RealmIndex < m.MinRealm) return Error.Conflict("realm_too_low", $"Needs {config.Realms[m.MinRealm].NamePl}.");
            if (m.Item is { } item)
            {
                var taken = await inventory.RemoveItemsAsync(uow, l.Character.Id, item, 1, "learn_method", m.Id, ct);
                if (!taken.IsSuccess) return Error.Conflict("manual_missing", "You need the manual in your bag.");
            }

            return l with { Profile = l.Profile with { KnownMethods = [.. l.Profile.KnownMethods, m.Id] } };
        }, ct);

    // ---------------------------------------------------------------- automatic breakthroughs, reincarnation

    public Task<Result<CultivationView>> SetAutoBreakthroughAsync(AccountId accountId, Guid characterId, AutoBreakthroughCommand cmd, CancellationToken ct) =>
        Mutate(accountId, characterId, (_, l, _) =>
        {
            if (l.State.RealmIndex < config.AutoBreakthrough.MinRealm)
                return Fail(Error.Conflict("realm_too_low", $"Automatic breakthroughs open at {config.Realms[config.AutoBreakthrough.MinRealm].NamePl}."));
            if (cmd.MinChancePermille is { } p && p is < 100 or > 1000) return Fail(Error.Validation("threshold_invalid", "Threshold 100-1000‰."));
            return Ok(l with { Profile = l.Profile with { AutoBreakthroughPermille = cmd.MinChancePermille } });
        }, ct);

    /// <summary>Starts over from the first realm with karma (a permanent flow bonus); titles and collections stay.</summary>
    public Task<Result<CultivationView>> ReincarnateAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        Mutate(accountId, characterId, async (uow, l, now) =>
        {
            if (l.State.RealmIndex < config.Karma.MinRealm) return Error.Conflict("realm_too_low", $"Reincarnation opens at {config.Realms[config.Karma.MinRealm].NamePl}.");
            var karma = KarmaFor(l.State.RealmIndex);
            await AuditAsync(uow, accountId, "cultivation.reincarnated", characterId, new { realm = l.State.RealmIndex, karma }, now, ct);
            return l with
            {
                State = l.State with { RealmIndex = 0, Stage = 1, Qi = 0, DeviationUntil = null, MeditationStartedAt = null },
                Profile = l.Profile with
                {
                    Karma = l.Profile.Karma + karma, Reincarnations = l.Profile.Reincarnations + 1, Mode = CultivationModes.Idle, RetreatUntil = null,
                    VesselLevel = 0, Instability = 0, Qualities = new Dictionary<string, string>(), CoreColor = null, PeakSince = null, TribulationBattleId = null,
                },
            };
        }, ct);

    private int KarmaFor(int realm) => (realm - config.Karma.MinRealm + 1) * config.Karma.PerRealm;

    // ---------------------------------------------------------------- steps (Body path)

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

                    loaded = await SettleAsync(uow, loaded, now, ct);
                    var since = now - (loaded.State.LastStepsReportAt ?? now.AddHours(-1));
                    var (accepted, clipped) = CultivationRules.PlausibleSteps(config, command.Steps, since, loaded.State.DailySteps);
                    var after = loaded.State.DailySteps + accepted;
                    var qi = CultivationRules.StepsQi(config, loaded.State.RealmIndex, loaded.State.DailySteps, after, config.Method(loaded.Profile.MethodId)?.StepsPermille ?? 0);
                    var next = loaded with
                    {
                        State = loaded.State with { DailySteps = after, Qi = checked(loaded.State.Qi + qi), LastStepsReportAt = now },
                        Profile = loaded.Profile with { LifetimeQi = loaded.Profile.LifetimeQi + qi },
                    };
                    if (!await SaveAsync(uow, next, now, ct)) return Concurrent;
                    if (accepted > 0)
                    {
                        await outbox.EnqueueAsync(uow, OutboxMessage.Create(JianghuGo.Modules.Cultivation.Contracts.CultivationStepEvents.StepsAccepted,
                            new JianghuGo.Modules.Cultivation.Contracts.CultivationStepEvents.StepsAcceptedV1(accountId.Value, characterId, accepted), now, correlation.CorrelationId), ct);
                    }

                    if (clipped)
                    {
                        await AuditAsync(uow, accountId, "cultivation.steps_clipped", characterId,
                            new { reported = command.Steps, accepted, sinceSeconds = (int)since.TotalSeconds }, now, ct);
                    }

                    return new StepsResult(accepted, clipped, qi, View(next.Character, next.State with { Version = next.StoredVersion + 1 }, next.Profile, now));
                }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    // ---------------------------------------------------------------- breakthroughs

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
        if (loaded.State.Version != command.ExpectedVersion) return Concurrent;
        loaded = await SettleAsync(uow, loaded, now, ct);
        var s = loaded.State;
        if (CultivationRules.IsPeak(config, s.RealmIndex, s.Stage)) return Error.LimitReached("peak_reached", "This is the highest cultivation stage.");
        if (CultivationRules.IsMajor(config, s.RealmIndex, s.Stage))
            return Error.Conflict("tribulation_required", "Perfection is reached: face the heavenly tribulation to enter the next realm.");
        if (s.InDeviation(now)) return Error.Conflict("qi_deviation_active", $"Qi deviation: breakthroughs are impossible until {s.DeviationUntil:O}.");
        if (loaded.Profile.Mode == CultivationModes.Meditation) return Error.Conflict("meditation_in_progress", "Finish meditating before attempting a breakthrough.");

        var required = CultivationRules.QiRequired(config, s.RealmIndex, s.Stage);
        if (s.Qi < required) return Error.InsufficientFunds("insufficient_qi", $"Breakthrough needs {required} Qi (have {s.Qi}).");
        if (CultivationRules.IsBottleneck(config, s.Stage))
        {
            var pill = config.EraOf(s.RealmIndex).BottleneckItem;
            var taken = await inventory.RemoveItemsAsync(uow, loaded.Character.Id, pill, 1, "bottleneck", $"{s.RealmIndex}-{s.Stage}-{Uuid7.New()}", ct);
            if (!taken.IsSuccess) return Error.Conflict("bottleneck_pill_missing", "The bottleneck needs a breakthrough pill of this era.");
        }

        var (after, attempt) = await RollAsync(uow, loaded, now, ct);
        if (!await SaveAsync(uow, after, now, ct)) return Concurrent;
        return new BreakthroughResult(attempt.Success, attempt.ChancePpm / (double)CultivationRules.ChanceScale, attempt.QiSpent,
            View(after.Character, after.State with { Version = after.StoredVersion + 1 }, after.Profile, now));
    }

    /// <summary>One minor breakthrough roll (state not saved): success climbs a stage, failure costs Qi and a deviation.</summary>
    private async Task<(Loaded After, BreakthroughAttempt Attempt)> RollAsync(IUnitOfWork uow, Loaded l, DateTimeOffset now, CancellationToken ct)
    {
        var s = l.State;
        var required = CultivationRules.QiRequired(config, s.RealmIndex, s.Stage);
        var chance = CultivationRules.SuccessChancePpm(config, s.RealmIndex, s.Stage, l.Character.Stats, l.Profile);
        var seed = seeds.NextSeed();
        var roll = CultivationRules.Roll(seed);
        var success = roll < chance;
        var (toRealm, toStage) = success ? CultivationRules.Next(config, s.RealmIndex, s.Stage) : (s.RealmIndex, s.Stage);
        var spent = success ? required : CultivationRules.FailureQiLoss(config, required);
        var h = config.HeartDemons;
        var fast = l.Profile.LastBreakthroughAt is { } last && now - last < TimeSpan.FromMinutes(h.FastBreakthroughMinutes);
        var demon = success ? h.PerBreakthrough + (fast ? h.FastBreakthroughExtra : 0) : 0;
        demon = demon * (1000 + (config.Method(l.Profile.MethodId)?.DemonPermille ?? 0)) / 1000;
        var state = s with
        {
            Qi = s.Qi - spent,
            RealmIndex = toRealm,
            Stage = toStage,
            DeviationUntil = success ? s.DeviationUntil : now.AddMinutes(config.Breakthrough.DeviationMinutes),
        };
        var profile = l.Profile with
        {
            Instability = Math.Clamp(l.Profile.Instability + demon, 0, 100),
            LastBreakthroughAt = success ? now : l.Profile.LastBreakthroughAt,
            PeakSince = success && CultivationRules.IsMajor(config, toRealm, toStage) ? now : l.Profile.PeakSince,
        };
        var attempt = new BreakthroughAttempt(Uuid7.New(), l.Character.Id, l.Character.AccountId, s.RealmIndex, s.Stage, toRealm, toStage,
            success, chance, roll, seed, spent, config.RulesVersion, now);
        await repo.AddBreakthroughAsync(uow, attempt, ct);
        await AuditAsync(uow, l.Character.AccountId, success ? "cultivation.breakthrough_succeeded" : "cultivation.breakthrough_failed", l.Character.Id.Value,
            new { attemptId = attempt.Id, chance, roll, seed = seed.ToString(CultureInfo.InvariantCulture), spent, from = new[] { s.RealmIndex, s.Stage }, to = new[] { toRealm, toStage } }, now, ct);
        await outbox.EnqueueAsync(uow, OutboxMessage.Create(
            success ? CultivationEvents.BreakthroughSucceeded : CultivationEvents.BreakthroughFailed,
            new CultivationEvents.BreakthroughV1(l.Character.AccountId.Value, l.Character.Id.Value, attempt.Id, config.Realms[s.RealmIndex].Id, s.Stage,
                config.Realms[toRealm].Id, toStage, success), now, correlation.CorrelationId), ct);
        return (l with { State = state, Profile = profile }, attempt);
    }

    /// <summary>
    /// Perfection reached: the heavenly tribulation is a battle against the sky itself (stronger every realm). Winning it
    /// (handled when the battle ends) opens the next great realm; losing it costs Qi and a deviation.
    /// </summary>
    public Task<Result<TribulationView>> StartTribulationAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<TribulationView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var loaded = await LoadAsync(uow, accountId, characterId, now, ct);
            if (loaded is null) return CharacterNotFound;
            loaded = await SettleAsync(uow, loaded, now, ct);
            var s = loaded.State;
            if (CultivationRules.IsPeak(config, s.RealmIndex, s.Stage)) return Error.LimitReached("peak_reached", "This is the highest cultivation stage.");
            if (!CultivationRules.IsMajor(config, s.RealmIndex, s.Stage)) return Error.Conflict("not_at_perfection", "Reach Perfection (the 12th stage) first.");
            if (s.InDeviation(now)) return Error.Conflict("qi_deviation_active", "Qi deviation: wait until it passes.");
            var required = CultivationRules.QiRequired(config, s.RealmIndex, s.Stage);
            if (s.Qi < required) return Error.InsufficientFunds("insufficient_qi", $"The tribulation needs {required} Qi (have {s.Qi}).");
            var power = 1000 + (CultivationRules.TribulationPowerPerRealmPermille * s.RealmIndex) + (config.Method(loaded.Profile.MethodId)?.TribulationPermille ?? 0);
            var started = await encounters.StartTribulationAsync(uow, accountId, characterId, s.RealmIndex, power, config.Realms[s.RealmIndex + 1].TribulationPl, ct);
            if (!started.IsSuccess) return started.Error!;
            var next = loaded with
            {
                Profile = loaded.Profile with { TribulationBattleId = started.Value, Mode = CultivationModes.Idle, RetreatUntil = null },
                State = loaded.State with { MeditationStartedAt = null },
            };
            if (!await SaveAsync(uow, next, now, ct)) return Concurrent;
            await AuditAsync(uow, accountId, "cultivation.tribulation_started", characterId, new { realm = s.RealmIndex, battleId = started.Value }, now, ct);
            await uow.CommitAsync(ct);
            return new TribulationView(started.Value, View(next.Character, next.State with { Version = next.StoredVersion + 1 }, next.Profile, now));
        }, ct);

    /// <summary>Applies the end of a tribulation battle (called by the outbox handler, exactly once per battle).</summary>
    internal async Task ApplyTribulationAsync(IUnitOfWork uow, Guid accountId, Guid characterId, Guid battleId, bool won, DateTimeOffset at, CancellationToken ct)
    {
        var loaded = await LoadAsync(uow, new AccountId(accountId), characterId, at, ct);
        if (loaded is null || loaded.Profile.TribulationBattleId != battleId) return;
        loaded = await SettleAsync(uow, loaded, at, ct);
        var s = loaded.State;
        var required = CultivationRules.QiRequired(config, s.RealmIndex, s.Stage);
        Loaded next;
        string? quality = null;
        string? color = null;
        if (won && CultivationRules.IsMajor(config, s.RealmIndex, s.Stage) && s.Qi >= required)
        {
            var toRealm = s.RealmIndex + 1;
            var profile = loaded.Profile with { TribulationBattleId = null, PeakSince = null, LastBreakthroughAt = at };
            var stageKey = toRealm switch { 3 => "foundation", 4 => "core", 5 => "soul", _ => null };
            if (stageKey is not null)
            {
                var meridians = (await repo.ListMeridiansAsync(uow, loaded.Character.Id, ct)).Values.Sum();
                var earned = MeridianRules.PointsEarned(config, s);
                quality = CultivationRules.Grade(config, CultivationRules.QualityScore(config, loaded.Profile, meridians, earned, at));
                profile = profile with { Qualities = new Dictionary<string, string>(profile.Qualities) { [stageKey] = quality } };
                if (stageKey == "core")
                {
                    color = CultivationRules.CoreColor(profile.Counters);
                    profile = profile with { CoreColor = color };
                }
            }

            next = loaded with { State = s with { Qi = s.Qi - required, RealmIndex = toRealm, Stage = 1 }, Profile = profile };
            await outbox.EnqueueAsync(uow, OutboxMessage.Create(CultivationEvents.RealmReached,
                new CultivationEvents.RealmReachedV1(accountId, characterId, toRealm, config.Realms[toRealm].Id, quality, color), at, correlation.CorrelationId), ct);
            await outbox.EnqueueAsync(uow, OutboxMessage.Create(CultivationEvents.BreakthroughSucceeded,
                new CultivationEvents.BreakthroughV1(accountId, characterId, battleId, config.Realms[s.RealmIndex].Id, s.Stage, config.Realms[toRealm].Id, 1, true),
                at, correlation.CorrelationId), ct);
        }
        else
        {
            next = loaded with
            {
                State = s with { Qi = s.Qi - Math.Min(s.Qi, CultivationRules.FailureQiLoss(config, required)), DeviationUntil = at.AddMinutes(config.Breakthrough.DeviationMinutes) },
                Profile = loaded.Profile with { TribulationBattleId = null },
            };
        }

        if (!await SaveAsync(uow, next, at, ct)) throw new TransientConflictException("Cultivation changed while applying a tribulation.");
        await AuditAsync(uow, new AccountId(accountId), won ? "cultivation.tribulation_passed" : "cultivation.tribulation_failed", characterId,
            new { battleId, realm = next.State.RealmIndex, quality, color }, at, ct);
    }

    // ---------------------------------------------------------------- loading, settling, saving

    /// <summary>Loads (or creates) state and profile. Characters from the old 6-realm ladder are moved to the same named realm once.</summary>
    internal async Task<Loaded?> LoadAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, DateTimeOffset now, CancellationToken ct)
    {
        var character = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
        if (character is null) return null;
        var today = CultivationRules.WorldDate(config, now);
        var stored = await repo.FindAsync(uow, character.Id, ct);
        var state = (stored ?? CultivationState.Initial(character.Id, accountId, character.Path, today, now)).ForDay(today);
        var storedProfile = await repo.FindProfileAsync(uow, character.Id, ct);
        var profile = storedProfile ?? CultivationProfile.New(character.Id, character.Path, now);
        if (storedProfile is null && stored is not null)
        {
            // cultivation-2 had 6 realms starting at Qi Condensation (now the 3rd realm): keep the same named realm
            var moved = Math.Min(config.Realms.Count - 1, stored.RealmIndex + 2);
            state = state with { RealmIndex = moved, Stage = Math.Min(12, state.Stage) };
            if (stored.MeditationStartedAt is not null) profile = profile with { Mode = CultivationModes.Meditation };
        }

        return new Loaded(character, state, stored?.Version ?? 0, profile, storedProfile?.Version ?? 0);
    }

    /// <summary>Credits the Qi flow since the last settlement, then runs automatic breakthroughs if enabled.</summary>
    internal async Task<Loaded> SettleAsync(IUnitOfWork uow, Loaded l, DateTimeOffset now, CancellationToken ct)
    {
        var s = l.State;
        var p = l.Profile;
        var flow = CultivationRules.SettleFlow(config, l.Character.Id, s.Path, s.RealmIndex, s.Stage, l.Character.Stats.GetValueOrDefault("spirit"), s.Qi, p, now, s.DeviationUntil);
        var calm = (int)(flow.MeditationMinutes / 60 * config.HeartDemons.CalmPerMeditationHour);
        var minutes = new Dictionary<string, long>(p.MethodMinutes);
        if (p.MethodId is { } mid) minutes[mid] = minutes.GetValueOrDefault(mid) + flow.MeditationMinutes;
        var gained = flow.Qi + flow.EnlightenmentQi;
        p = p with
        {
            FlowSettledAt = flow.SettledAt,
            Mode = flow.ModeAfter,
            RetreatUntil = flow.ModeAfter == CultivationModes.Retreat ? p.RetreatUntil : null,
            Instability = Math.Max(0, p.Instability - calm),
            MethodMinutes = minutes,
            Enlightenments = p.Enlightenments + flow.Enlightenments,
            Counters = p.Counters with { MeditationMinutes = p.Counters.MeditationMinutes + flow.MeditationMinutes, NightMinutes = p.Counters.NightMinutes + flow.NightMinutes },
            LifetimeQi = p.LifetimeQi + gained,
        };
        var next = l with { State = s with { Qi = checked(s.Qi + gained), MeditationStartedAt = flow.ModeAfter == CultivationModes.Meditation ? s.MeditationStartedAt : null }, Profile = p };

        if (flow.MeditationMinutes > 0)
        {
            await outbox.EnqueueAsync(uow, OutboxMessage.Create(CultivationEvents.MeditationFinished,
                new CultivationEvents.MeditationFinishedV1(l.Character.AccountId.Value, l.Character.Id.Value, (int)flow.MeditationMinutes, flow.Qi), now, correlation.CorrelationId), ct);
        }

        // automatic minor breakthroughs (opt-in, from Body Integration)
        var autos = 0;
        if (p.AutoBreakthroughPermille is { } threshold && next.State.RealmIndex >= config.AutoBreakthrough.MinRealm)
        {
            for (var i = 0; i < config.AutoBreakthrough.MaxPerSettle; i++)
            {
                var st = next.State;
                if (CultivationRules.IsMajor(config, st.RealmIndex, st.Stage) || CultivationRules.IsBottleneck(config, st.Stage) || st.InDeviation(now)) break;
                if (st.Qi < CultivationRules.QiRequired(config, st.RealmIndex, st.Stage)) break;
                if (CultivationRules.SuccessChancePpm(config, st.RealmIndex, st.Stage, l.Character.Stats, next.Profile) < threshold * 1000) break;
                (next, _) = await RollAsync(uow, next, now, ct);
                autos++;
            }
        }

        var away = now - l.Profile.FlowSettledAt;
        if (away >= TimeSpan.FromMinutes(30) && (gained > 0 || autos > 0))
        {
            var prev = l.Profile.LastSummary;
            var summary = new SettleSummary(prev?.From ?? l.Profile.FlowSettledAt, now, (prev?.Qi ?? 0) + gained, (prev?.Enlightenments ?? 0) + flow.Enlightenments,
                (prev?.AutoBreakthroughs ?? 0) + autos, flow.VesselFull, flow.RetreatFinished || (prev?.RetreatFinished ?? false));
            next = next with { Profile = next.Profile with { LastSummary = summary } };
        }

        if (flow.Enlightenments > 0)
            await AuditAsync(uow, l.Character.AccountId, "cultivation.enlightenment", l.Character.Id.Value, new { count = flow.Enlightenments, qi = flow.EnlightenmentQi }, now, ct);
        return next;
    }

    internal async Task<bool> SaveAsync(IUnitOfWork uow, Loaded l, DateTimeOffset now, CancellationToken ct)
    {
        if (!await repo.TrySaveAsync(uow, l.State with { Version = l.StoredVersion + 1, UpdatedAt = now }, l.StoredVersion, ct)) return false;
        return await repo.TrySaveProfileAsync(uow, l.Profile with { Version = l.ProfileVersion + 1, LadderVersion = CultivationProfile.CurrentLadder }, l.ProfileVersion, ct);
    }

    private static Task<Result<Loaded>> Ok(Loaded l) => Task.FromResult<Result<Loaded>>(l);

    private static Task<Result<Loaded>> Fail(Error e) => Task.FromResult<Result<Loaded>>(e);

    /// <summary>Load → settle → change → save → view, in one unit of work.</summary>
    private Task<Result<CultivationView>> Mutate(AccountId accountId, Guid characterId, Func<IUnitOfWork, Loaded, DateTimeOffset, Task<Result<Loaded>>> change, CancellationToken ct) =>
        uowFactory.RunAsync<Result<CultivationView>>(async uow =>
        {
            var now = clock.GetUtcNow();
            var loaded = await LoadAsync(uow, accountId, characterId, now, ct);
            if (loaded is null) return CharacterNotFound;
            loaded = await SettleAsync(uow, loaded, now, ct);
            var changed = await change(uow, loaded, now);
            if (!changed.IsSuccess) return changed.Error!;
            var next = changed.Value;
            if (!await SaveAsync(uow, next, now, ct)) return Concurrent;
            await uow.CommitAsync(ct);
            return View(next.Character, next.State with { Version = next.StoredVersion + 1 }, next.Profile, now);
        }, ct);

    public Task<Result<IReadOnlyList<BreakthroughAttempt>>> HistoryAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<BreakthroughAttempt>>>(async uow =>
        {
            var character = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (character is null) return CharacterNotFound;
            return Result<IReadOnlyList<BreakthroughAttempt>>.Success(await repo.ListBreakthroughsAsync(uow, character.Id, ct));
        }, ct);

    private CultivationView View(CharacterSummary c, CultivationState s, CultivationProfile p, DateTimeOffset now)
    {
        var realm = config.Realms[s.RealmIndex];
        var peak = CultivationRules.IsPeak(config, s.RealmIndex, s.Stage);
        var major = CultivationRules.IsMajor(config, s.RealmIndex, s.Stage);
        var spirit = c.Stats.GetValueOrDefault("spirit");
        double Rate(string mode) => (double)CultivationRules.FlowPerHour(config, s.Path, s.RealmIndex, spirit, p, mode, now, s.DeviationUntil);
        var nextRealm = s.RealmIndex + 1 < config.Realms.Count ? config.Realms[s.RealmIndex + 1] : null;
        var methods = config.Methods.Select(m => new MethodView(m.Id, m.NamePl, m.DescriptionPl, p.KnownMethods.Contains(m.Id), p.MethodId == m.Id,
            (int)Math.Min(10, p.MinutesWith(m.Id) / config.MethodMasteryMinutesPerLevel), m.MinRealm, m.Path, m.Item,
            (m.Path is null || m.Path == s.Path || m.Item is not null) && s.RealmIndex >= m.MinRealm)).ToList();
        var era = config.EraOf(s.RealmIndex);
        var summary = p.LastSummary is { } x ? new SummaryView(x.From, x.To, x.Qi, x.Enlightenments, x.AutoBreakthroughs, x.VesselFull, x.RetreatFinished) : null;
        return new CultivationView(c.Id.Value, s.Path, config.RulesVersion, realm.Id, realm.NamePl, s.Stage, realm.Stages, peak, s.Qi,
            peak ? 0 : CultivationRules.QiRequired(config, s.RealmIndex, s.Stage),
            peak || major ? 0 : CultivationRules.SuccessChancePpm(config, s.RealmIndex, s.Stage, c.Stats, p) / (double)CultivationRules.ChanceScale,
            major, s.Version, s.DeviationUntil, p.Mode == CultivationModes.Meditation ? s.MeditationStartedAt ?? p.FlowSettledAt : null,
            0, 0, s.DailySteps, config.Paths.Body.DailyStepCap,
            Rate(CultivationModes.Meditation) / 60.0, config.Flow.OfflineCap(s.RealmIndex) * 60, 1,
            s.RealmIndex, config.Realms.Count, realm.Hanzi, realm.Era, era.NamePl, CultivationRules.StageName(config, s.Stage), realm.UnlockPl, realm.TitlePl,
            nextRealm?.NamePl, nextRealm?.UnlockPl, nextRealm?.TribulationPl,
            p.Mode, p.RetreatUntil,
            [.. config.Flow.RetreatHours.Where(h => s.RealmIndex >= config.Flow.RetreatMinRealm && (h <= 12 || s.RealmIndex >= config.Flow.LongRetreatMinRealm))],
            CultivationRules.VesselCapacity(config, s.RealmIndex, s.Stage, p.VesselLevel), p.VesselLevel,
            p.VesselLevel >= config.Flow.VesselMaxLevel ? 0 : VesselCost(p.VesselLevel), p.Crystals,
            Rate(CultivationModes.Idle), Rate(CultivationModes.Meditation), Rate(CultivationModes.Retreat), config.Flow.OfflineCap(s.RealmIndex),
            p.MethodId, methods, p.Instability, p.Qualities, p.CoreColor, CultivationRules.CoreColorPl(p.CoreColor), p.Karma, p.Reincarnations,
            s.RealmIndex >= config.Karma.MinRealm, s.RealmIndex >= config.Karma.MinRealm ? KarmaFor(s.RealmIndex) : 0,
            p.AutoBreakthroughPermille, s.RealmIndex >= config.AutoBreakthrough.MinRealm,
            p.AcceleratorDate != CultivationRules.WorldDate(config, now), AcceleratorHours(s.RealmIndex),
            CultivationRules.IsBottleneck(config, s.Stage) ? era.BottleneckItem : null,
            major && !peak, p.TribulationBattleId, p.Enlightenments, p.LifetimeQi, summary, s.RealmIndex >= config.Compression.MinRealm,
            FlowRules.IsWeekend(now) ? config.Flow.WeekendPermille : 1000);
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, Guid characterId, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "character", characterId.ToString(),
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}
