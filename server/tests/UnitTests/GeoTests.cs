using JianghuGo.Domain;
using JianghuGo.Modules.Geolocation;
using JianghuGo.Modules.Geolocation.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

public class GeoTests
{
    private static readonly GeolocationConfig Config =
        GeolocationModule.LoadConfig(Path.Combine(AppContext.BaseDirectory, "game-config", "geolocation.json"));

    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly GeoPoint Warsaw = new(52.2297, 21.0122);
    private static readonly GeoPoint Krakow = new(50.0647, 19.9450);

    private static ExplorationSession NewSession() => ExplorationSession.Start(CharacterId.New(), AccountId.New(), T0);

    /// <summary>Point moved north by <paramref name="meters"/>.</summary>
    private static GeoPoint North(GeoPoint p, double meters) => new(p.Lat + GeoMath.MetersToLatDegrees(meters), p.Lon);

    private static GeoSample S(GeoPoint p, DateTimeOffset at, double accuracy = 10) => new(p.Lat, p.Lon, accuracy, at);

    private static (ExplorationSession, IReadOnlyList<string>) Feed(ExplorationSession s, DateTimeOffset now, params GeoSample[] samples) =>
        SampleProcessor.Apply(Config, s, samples, now);

    [Fact]
    public void Haversine_matches_known_distances()
    {
        Assert.InRange((int)(GeoMath.DistanceM(Warsaw, Krakow) / 1000), 251, 253);
        Assert.InRange((int)GeoMath.DistanceM(new GeoPoint(0, 0), new GeoPoint(1, 0)), 111_194, 111_196);
        Assert.Equal(0, (int)GeoMath.DistanceM(Warsaw, Warsaw));
        Assert.InRange((int)GeoMath.DistanceM(Warsaw, North(Warsaw, 100)), 99, 100);
    }

    [Fact]
    public void Walking_samples_are_accepted_and_accumulate_distance()
    {
        var s = NewSession();
        (s, var o1) = Feed(s, T0, S(Warsaw, T0.AddSeconds(-5)));
        (s, var o2) = Feed(s, T0.AddSeconds(30), S(North(Warsaw, 20), T0.AddSeconds(15)), S(North(Warsaw, 40), T0.AddSeconds(25)));
        Assert.SequenceEqual([SampleOutcomes.Accepted], o1);
        Assert.SequenceEqual([SampleOutcomes.Accepted, SampleOutcomes.Accepted], o2);
        Assert.InRange((int)s.DistanceM, 39, 40);
        Assert.Null(SampleProcessor.Verify(Config, s, T0.AddSeconds(30)));
    }

    // Acceptance #9 (domain part): an unrealistic jump does not yield a trusted position.
    [Fact]
    public void Teleport_moves_the_position_but_blocks_rewards_until_the_cooldown_ends()
    {
        var s = NewSession();
        (s, _) = Feed(s, T0, S(Warsaw, T0));
        (s, var o) = Feed(s, T0.AddSeconds(60), S(Krakow, T0.AddSeconds(60)));
        Assert.SequenceEqual([SampleOutcomes.Relocated], o);
        Assert.Equal(1, s.Anomalies);
        Assert.Equal(0, (int)s.DistanceM, "the jump is not counted as travelled distance");
        Assert.Equal(PositionProblems.RelocationCooldown, SampleProcessor.Verify(Config, s, T0.AddSeconds(61)));
        Assert.False(s.Flagged, "one anomaly never flags or punishes");

        // A real train ride: the player keeps sending plausible samples; after the cooldown rewards return.
        var t = T0.AddSeconds(60);
        for (var i = 1; i <= 7; i++)
        {
            t = t.AddSeconds(50);
            (s, _) = Feed(s, t, S(North(Krakow, i * 30), t));
        }

        Assert.Null(SampleProcessor.Verify(Config, s, t));
    }

    [Fact]
    public void Weak_gps_drift_is_not_treated_as_cheating()
    {
        var s = NewSession();
        (s, _) = Feed(s, T0, S(Warsaw, T0, accuracy: 300));
        (s, var o) = Feed(s, T0.AddSeconds(10), S(North(Warsaw, 500), T0.AddSeconds(10), accuracy: 300)); // 50 m/s raw, but within error radii
        Assert.SequenceEqual([SampleOutcomes.AcceptedLowAccuracy], o);
        Assert.Equal(0, s.Anomalies);
        Assert.Equal(PositionProblems.LowAccuracy, SampleProcessor.Verify(Config, s, T0.AddSeconds(10)));
        Assert.Equal(0, (int)s.DistanceM, "inaccurate fixes do not add distance");
    }

    [Fact]
    public void Bad_samples_are_dropped_with_reasons()
    {
        var s = NewSession();
        (s, _) = Feed(s, T0, S(Warsaw, T0));
        (s, var o) = Feed(s, T0.AddSeconds(10),
            new GeoSample(91, 0, 10, T0.AddSeconds(5)),
            new GeoSample(double.NaN, 0, 10, T0.AddSeconds(6)),
            new GeoSample(Warsaw.Lat, Warsaw.Lon, 0, T0.AddSeconds(7)),
            S(Warsaw, T0.AddSeconds(100)),           // > 30 s in the future
            S(Warsaw, T0.AddSeconds(-200)),          // older than 120 s
            S(Warsaw, T0));                          // duplicate timestamp
        Assert.SequenceEqual(
            [SampleOutcomes.Stale, SampleOutcomes.OutOfOrder, SampleOutcomes.Invalid, SampleOutcomes.Invalid, SampleOutcomes.Invalid, SampleOutcomes.Future], o);
        Assert.Equal(6, s.DroppedSamples);
        Assert.Equal(1, s.AcceptedSamples);
    }

    [Fact]
    public void Sample_rate_is_limited_per_minute()
    {
        var s = NewSession();
        var samples = Enumerable.Range(0, 20).Select(i => S(North(Warsaw, i), T0.AddSeconds(-60 + i))).ToArray();
        (s, _) = Feed(s, T0, samples);
        (s, var o) = Feed(s, T0.AddSeconds(5), [.. Enumerable.Range(20, 20).Select(i => S(North(Warsaw, i), T0.AddSeconds(-60 + i)))]);
        Assert.Equal(10, o.Count(x => x == SampleOutcomes.RateLimited));
        (_, var later) = Feed(s, T0.AddSeconds(65), S(North(Warsaw, 50), T0.AddSeconds(64)));
        Assert.SequenceEqual([SampleOutcomes.Accepted], later);
    }

    [Fact]
    public void Repeated_jumps_flag_the_session_only()
    {
        var s = NewSession();
        var t = T0;
        (s, _) = Feed(s, t, S(Warsaw, t));
        for (var i = 0; i < 5; i++)
        {
            t = t.AddSeconds(10);
            (s, _) = Feed(s, t, S(i % 2 == 0 ? Krakow : Warsaw, t));
        }

        Assert.True(s.Flagged);
        Assert.Equal(PositionProblems.SessionFlagged, SampleProcessor.Verify(Config, s, t));
    }

    [Fact]
    public void Freshness_speed_and_session_expiry()
    {
        var s = NewSession();
        (s, _) = Feed(s, T0, S(Warsaw, T0));
        Assert.Equal(PositionProblems.StalePosition, SampleProcessor.Verify(Config, s, T0.AddSeconds(61)));
        (s, _) = Feed(s, T0.AddSeconds(10), S(North(Warsaw, 200), T0.AddSeconds(10))); // 20 m/s: driving
        Assert.Equal(PositionProblems.MovingTooFast, SampleProcessor.Verify(Config, s, T0.AddSeconds(10)));
        Assert.False(SampleProcessor.IsExpired(Config, s, T0.AddMinutes(5)));
        Assert.True(SampleProcessor.IsExpired(Config, s, T0.AddMinutes(11)));
        var ended = s.End(T0.AddMinutes(11));
        Assert.Null(ended.LastPosition);
        Assert.Equal(PositionProblems.NoSession, SampleProcessor.Verify(Config, ended, T0.AddMinutes(11)));
    }
}
