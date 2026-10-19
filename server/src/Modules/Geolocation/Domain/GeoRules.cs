using JianghuGo.Domain;

namespace JianghuGo.Modules.Geolocation.Domain;

public sealed record GeolocationConfig(
    string RulesVersion,
    double MaxAccuracyM,
    double TrustedAccuracyM,
    int MaxClockSkewSeconds,
    int MaxSampleAgeSeconds,
    int MaxSamplesPerBatch,
    int MaxSamplesPerMinute,
    double TeleportSpeedMps,
    double InteractionMaxSpeedMps,
    int RelocationCooldownSeconds,
    int FreshnessSeconds,
    int SessionMaxMinutes,
    int SessionIdleMinutes,
    int FlagAfterAnomalies);

public static class GeolocationConfigValidator
{
    public static IReadOnlyList<string> Validate(GeolocationConfig? c)
    {
        if (c is null) return ["Config is missing."];
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(c.RulesVersion)) e.Add("rulesVersion is required.");
        if (!(0 < c.TrustedAccuracyM && c.TrustedAccuracyM <= c.MaxAccuracyM)) e.Add("need 0 < trustedAccuracyM <= maxAccuracyM.");
        if (c.MaxClockSkewSeconds is < 0 or > 300 || c.MaxSampleAgeSeconds is < 10 or > 3600) e.Add("clock skew / sample age out of range.");
        if (c.MaxSamplesPerBatch is < 1 or > 200 || c.MaxSamplesPerMinute < c.MaxSamplesPerBatch) e.Add("sample limits invalid (per minute must be >= per batch).");
        if (!(0 < c.InteractionMaxSpeedMps && c.InteractionMaxSpeedMps < c.TeleportSpeedMps)) e.Add("need 0 < interactionMaxSpeedMps < teleportSpeedMps.");
        if (c.RelocationCooldownSeconds < 0 || c.FreshnessSeconds < 5 || c.SessionMaxMinutes < 5 || c.SessionIdleMinutes < 1 || c.FlagAfterAnomalies < 1) e.Add("timing/threshold values invalid.");
        return e;
    }
}

/// <param name="Mock">The phone reported a mock location provider (2.1 anti-spoofing): the sample is dropped and counted as an anomaly.</param>
public sealed record GeoSample(double Lat, double Lon, double AccuracyM, DateTimeOffset CapturedAt, bool Mock = false);

public static class SessionStatuses
{
    public const string Active = "active";
    public const string Ended = "ended";
}

/// <summary>
/// Exploration session. Deliberately keeps only the LAST accepted position plus aggregates: no location history
/// is stored (docs/operations/data-retention.md). The position is cleared when the session ends.
/// </summary>
public sealed record ExplorationSession(
    Guid Id,
    CharacterId CharacterId,
    AccountId AccountId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? EndedAt,
    GeoPoint? LastPosition,
    double? LastAccuracyM,
    DateTimeOffset? LastCapturedAt,
    DateTimeOffset? LastReceivedAt,
    double LastSpeedMps,
    DateTimeOffset? RelocationUntil,
    double DistanceM,
    int AcceptedSamples,
    int DroppedSamples,
    int Anomalies,
    bool Flagged,
    DateTimeOffset RateWindowStart,
    int RateWindowCount,
    int Version)
{
    public static ExplorationSession Start(CharacterId characterId, AccountId accountId, DateTimeOffset now) =>
        new(Uuid7.New(), characterId, accountId, SessionStatuses.Active, now, now, null, null, null, null, null, 0, null, 0, 0, 0, 0, false, now, 0, 0);

    /// <summary>Ends the session and forgets the position (data minimisation).</summary>
    public ExplorationSession End(DateTimeOffset at) => this with
    {
        Status = SessionStatuses.Ended, EndedAt = at, LastPosition = null, LastAccuracyM = null, LastCapturedAt = null, LastReceivedAt = null, LastSpeedMps = 0,
    };
}

public static class SampleOutcomes
{
    public const string Accepted = "accepted";
    public const string AcceptedLowAccuracy = "accepted_low_accuracy";
    public const string Relocated = "relocated";          // implausible jump: position moves, rewards wait for the cooldown
    public const string Invalid = "invalid";
    public const string Future = "future";
    public const string Stale = "stale";
    public const string OutOfOrder = "out_of_order";      // duplicates and older samples
    public const string RateLimited = "rate_limited";
    public const string Mock = "mock";
}

public static class PositionProblems
{
    public const string NoSession = "no_session";
    public const string SessionFlagged = "session_flagged";
    public const string StalePosition = "stale_position";
    public const string LowAccuracy = "low_accuracy";
    public const string RelocationCooldown = "relocation_cooldown";
    public const string MovingTooFast = "moving_too_fast";
}

/// <summary>
/// Pure GPS validation. Principles (spec 7.5): never trust client time or position blindly, never punish on one sample.
/// Bad samples are dropped or neutralised; repeated anomalies only disable rewards for the session (flag), which is
/// audited for the anti-abuse team — no automatic account sanction.
/// </summary>
public static class SampleProcessor
{
    public static (ExplorationSession Session, IReadOnlyList<string> Outcomes) Apply(
        GeolocationConfig c, ExplorationSession s, IReadOnlyList<GeoSample> samples, DateTimeOffset now)
    {
        var outcomes = new List<string>(samples.Count);
        if (now - s.RateWindowStart >= TimeSpan.FromMinutes(1))
        {
            s = s with { RateWindowStart = now, RateWindowCount = 0 };
        }

        foreach (var x in samples.OrderBy(x => x.CapturedAt))
        {
            string outcome;
            if (x.Mock)
            {
                // A spoofing app is running: never move the position; repeated mocks flag the session for review.
                var anomalies = s.Anomalies + 1;
                s = s with { DroppedSamples = s.DroppedSamples + 1, Anomalies = anomalies, Flagged = s.Flagged || anomalies >= c.FlagAfterAnomalies };
                outcomes.Add(SampleOutcomes.Mock);
                continue;
            }

            if (!GeoPoint.IsValid(x.Lat, x.Lon) || !double.IsFinite(x.AccuracyM) || x.AccuracyM <= 0 || x.AccuracyM > c.MaxAccuracyM)
                outcome = SampleOutcomes.Invalid;
            else if (x.CapturedAt > now.AddSeconds(c.MaxClockSkewSeconds))
                outcome = SampleOutcomes.Future;
            else if (x.CapturedAt < now.AddSeconds(-c.MaxSampleAgeSeconds))
                outcome = SampleOutcomes.Stale;
            else if (s.LastCapturedAt is { } last && x.CapturedAt <= last)
                outcome = SampleOutcomes.OutOfOrder;
            else if (s.RateWindowCount >= c.MaxSamplesPerMinute)
                outcome = SampleOutcomes.RateLimited;
            else
            {
                var p = new GeoPoint(x.Lat, x.Lon);
                double speed = 0, distance = 0;
                var teleport = false;
                if (s.LastPosition is { } prev)
                {
                    distance = GeoMath.DistanceM(prev, p);
                    var dt = Math.Max(1, (x.CapturedAt - s.LastCapturedAt!.Value).TotalSeconds);
                    speed = distance / dt;
                    // Weak GPS: subtract both error radii before judging a jump, so drifting fixes are not called cheating.
                    var certainDistance = Math.Max(0, distance - s.LastAccuracyM!.Value - x.AccuracyM);
                    teleport = certainDistance / dt > c.TeleportSpeedMps;
                }

                var lowAccuracy = x.AccuracyM > c.TrustedAccuracyM;
                outcome = teleport ? SampleOutcomes.Relocated : lowAccuracy ? SampleOutcomes.AcceptedLowAccuracy : SampleOutcomes.Accepted;
                var anomalies = s.Anomalies + (teleport ? 1 : 0);
                s = s with
                {
                    LastPosition = p,
                    LastAccuracyM = x.AccuracyM,
                    LastCapturedAt = x.CapturedAt,
                    LastReceivedAt = now,
                    LastSpeedMps = teleport ? 0 : speed,
                    RelocationUntil = teleport ? now.AddSeconds(c.RelocationCooldownSeconds) : s.RelocationUntil,
                    DistanceM = s.DistanceM + (teleport || lowAccuracy ? 0 : distance),
                    AcceptedSamples = s.AcceptedSamples + 1,
                    Anomalies = anomalies,
                    Flagged = s.Flagged || anomalies >= c.FlagAfterAnomalies,
                    RateWindowCount = s.RateWindowCount + 1,
                };
                outcomes.Add(outcome);
                continue;
            }

            s = s with { DroppedSamples = s.DroppedSamples + 1 };
            outcomes.Add(outcome);
        }

        return (s with { LastSeenAt = now }, outcomes);
    }

    /// <summary>Null when the session's position may be used for rewards right now; otherwise the reason.</summary>
    public static string? Verify(GeolocationConfig c, ExplorationSession? s, DateTimeOffset now)
    {
        if (s is null || s.Status != SessionStatuses.Active || s.LastPosition is null) return PositionProblems.NoSession;
        if (s.Flagged) return PositionProblems.SessionFlagged;
        if (s.LastReceivedAt is not { } received || now - received > TimeSpan.FromSeconds(c.FreshnessSeconds)) return PositionProblems.StalePosition;
        if (s.LastAccuracyM > c.TrustedAccuracyM) return PositionProblems.LowAccuracy;
        if (s.RelocationUntil is { } until && until > now) return PositionProblems.RelocationCooldown;
        if (s.LastSpeedMps > c.InteractionMaxSpeedMps) return PositionProblems.MovingTooFast;
        return null;
    }

    /// <summary>Sessions end by themselves after inactivity or the maximum duration.</summary>
    public static bool IsExpired(GeolocationConfig c, ExplorationSession s, DateTimeOffset now) =>
        s.Status == SessionStatuses.Active &&
        (now - s.LastSeenAt > TimeSpan.FromMinutes(c.SessionIdleMinutes) || now - s.StartedAt > TimeSpan.FromMinutes(c.SessionMaxMinutes));
}
