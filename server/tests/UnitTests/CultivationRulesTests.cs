using JianghuGo.Domain;
using JianghuGo.Modules.Cultivation;
using JianghuGo.Modules.Cultivation.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

public class CultivationRulesTests
{
    private static readonly CultivationConfig Config =
        CultivationModule.LoadConfig(Path.Combine(AppContext.BaseDirectory, "game-config", "cultivation.json"));

    private static readonly Dictionary<string, int> Average = new() { ["insight"] = 10, ["willpower"] = 10, ["fate"] = 5, ["spirit"] = 10 };

    // 2026-10-12 is a Monday; CEST (UTC+2) in Europe/Warsaw.
    private static DateTimeOffset Warsaw(int hour, int minute = 0) => new(2026, 10, 12, hour, minute, 0, TimeSpan.FromHours(2));

    private static readonly CharacterId Hero = new(Guid.Parse("0f3c1b6e-1111-4c4c-9a9a-123456789abc"));

    [Fact]
    public void Shipped_config_is_valid() => Assert.Equal(0, CultivationConfigValidator.Validate(Config).Count);

    [Fact]
    public void Ladder_has_66_realms_of_12_stages_in_six_eras()
    {
        Assert.Equal(66, Config.Realms.Count);
        Assert.True(Config.Realms.All(r => r.Stages == 12));
        Assert.Equal(12, Config.StageNames.Count);
        Assert.Equal(6, Config.Eras.Count);
        Assert.Equal(791, CultivationRules.GlobalIndex(Config, 65, 12));
        Assert.True(CultivationRules.IsBottleneck(Config, 11));
        Assert.False(CultivationRules.IsBottleneck(Config, 12));
    }

    [Fact]
    public void Validator_rejects_unknown_time_zone_and_guaranteed_success()
    {
        var bad = Config with { WorldTimeZone = "Mars/Olympus", Breakthrough = Config.Breakthrough with { MaxChance = 1m } };
        var errors = CultivationConfigValidator.Validate(bad);
        Assert.True(errors.Any(e => e.Contains("Mars/Olympus", StringComparison.Ordinal)));
        Assert.True(errors.Any(e => e.Contains("maxChance", StringComparison.Ordinal)));
    }

    [Fact]
    public void Qi_required_grows_by_stage_and_realm_with_the_perfection_multiplier()
    {
        Assert.Equal(100, CultivationRules.QiRequired(Config, 0, 1));
        Assert.Equal(115, CultivationRules.QiRequired(Config, 0, 2));
        Assert.Equal(160, CultivationRules.QiRequired(Config, 1, 1));                       // × 1.6 per realm
        Assert.Equal((long)Math.Round(100 * Math.Pow(1.15, 11) * 3), CultivationRules.QiRequired(Config, 0, 12));
        Assert.True(CultivationRules.IsMajor(Config, 0, 12));
        Assert.False(CultivationRules.IsMajor(Config, 0, 11));
        Assert.Equal((1, 1), CultivationRules.Next(Config, 0, 12));
        Assert.Equal((0, 12), CultivationRules.Next(Config, 0, 11));
        Assert.True(CultivationRules.IsPeak(Config, 65, 12));
        Assert.False(CultivationRules.IsPeak(Config, 64, 12));
    }

    [Fact]
    public void Minor_chance_falls_with_stage_and_realm_and_is_clamped()
    {
        Assert.Equal(970_000, CultivationRules.SuccessChancePpm(Config, 0, 1, Average));                        // 1.03 → clamped 0.97
        Assert.Equal(744_000, CultivationRules.SuccessChancePpm(Config, 2, 11, new Dictionary<string, int>())); // 0.95 − 0.2 − 0.006
        Assert.Equal(570_000, CultivationRules.SuccessChancePpm(Config, 60, 11, new Dictionary<string, int>())); // 0.95 − 0.2 − 0.18
        var harsh = Config with { Breakthrough = Config.Breakthrough with { MinorBaseChance = 0.1m } };
        Assert.Equal(50_000, CultivationRules.SuccessChancePpm(harsh, 60, 11, new Dictionary<string, int>()));  // floor
    }

    [Fact]
    public void Heart_demons_lower_the_chance()
    {
        var calm = CultivationProfile.New(Hero, "qi", Warsaw(10));
        var shaken = calm with { Instability = 90 };
        Assert.True(CultivationRules.SuccessChancePpm(Config, 2, 5, new Dictionary<string, int>(), shaken)
                    < CultivationRules.SuccessChancePpm(Config, 2, 5, new Dictionary<string, int>(), calm));
    }

    [Fact]
    public void Flow_rates_meditation_five_times_retreat_eight_times_weekend_faster()
    {
        var p = CultivationProfile.New(Hero, "flame", Warsaw(10));
        var monday = Warsaw(12);
        var idle = CultivationRules.FlowPerHour(Config, "flame", 3, 10, p, CultivationModes.Idle, monday, null);
        Assert.True(idle > 0);
        var methodMed = 1000 + (Config.Method(p.MethodId)?.MeditationPermille ?? 0);
        Assert.Equal(Math.Round(idle * 5 * methodMed / 1000m, 6), Math.Round(CultivationRules.FlowPerHour(Config, "flame", 3, 10, p, CultivationModes.Meditation, monday, null), 6));
        Assert.Equal(Math.Round(idle * 8 * methodMed / 1000m, 6), Math.Round(CultivationRules.FlowPerHour(Config, "flame", 3, 10, p, CultivationModes.Retreat, monday, null), 6));
        var saturday = monday.AddDays(5);
        Assert.Equal(Math.Round(idle * 1.5m, 6), Math.Round(CultivationRules.FlowPerHour(Config, "flame", 3, 10, p, CultivationModes.Idle, saturday, null), 6));
        Assert.Equal(Math.Round(idle / 2, 6), Math.Round(CultivationRules.FlowPerHour(Config, "flame", 3, 10, p, CultivationModes.Idle, monday, monday.AddHours(1)), 6));
    }

    [Fact]
    public void Settling_credits_elapsed_time_up_to_the_offline_cap_and_the_vessel()
    {
        var start = Warsaw(8);
        var p = CultivationProfile.New(Hero, "flame", start);
        var rate = CultivationRules.FlowPerHour(Config, "flame", 3, 10, p, CultivationModes.Idle, start, null);
        var three = CultivationRules.SettleFlow(Config, Hero, "flame", 3, 1, 10, 0, p, start.AddHours(3), null);
        Assert.Equal((long)Math.Floor(rate * 3), three.Qi);

        // away 20 h on a weekday, realm 3: only 12 h count (and never more than the vessel)
        var capacity = CultivationRules.VesselCapacity(Config, 3, 1, 0);
        var away = CultivationRules.SettleFlow(Config, Hero, "flame", 3, 1, 10, 0, p, start.AddHours(20), null);
        Assert.Equal(Math.Min(capacity, (long)Math.Floor(rate * 12)), away.Qi);

        var nearlyFull = CultivationRules.SettleFlow(Config, Hero, "flame", 3, 1, 10, capacity - 5, p, start.AddHours(3), null);
        Assert.Equal(5, nearlyFull.Qi);
        Assert.True(nearlyFull.VesselFull);
    }

    [Fact]
    public void Retreat_ends_on_time_and_falls_back_to_idle()
    {
        var start = Warsaw(8);
        var p = CultivationProfile.New(Hero, "flame", start) with { Mode = CultivationModes.Retreat, RetreatUntil = start.AddHours(2), RetreatHours = 2 };
        var r = CultivationRules.SettleFlow(Config, Hero, "flame", 0, 1, 0, 0, p, start.AddHours(4), null);
        Assert.True(r.RetreatFinished);
        Assert.Equal(CultivationModes.Idle, r.ModeAfter);

        // even when the vessel filled up long before, the retreat still ends on time
        var full = CultivationRules.SettleFlow(Config, Hero, "flame", 5, 1, 10, CultivationRules.VesselCapacity(Config, 5, 1, 0), p, start.AddHours(4), null);
        Assert.True(full.RetreatFinished);
        Assert.Equal(CultivationModes.Idle, full.ModeAfter);
    }

    [Fact]
    public void Offline_cap_grows_with_the_realm()
    {
        Assert.Equal(12, Config.Flow.OfflineCap(0));
        Assert.Equal(24, Config.Flow.OfflineCap(16));
        Assert.Equal(72, Config.Flow.OfflineCap(60));
    }

    [Fact]
    public void Core_colour_follows_how_the_character_lived()
    {
        Assert.Equal("golden", CultivationRules.CoreColor(new ProfileCounters(0, 0, 0, 0, 0)));
        Assert.Equal("crimson", CultivationRules.CoreColor(new ProfileCounters(200, 0, 0, 0, 0)));
    }

    [Fact]
    public void Poison_night_window_follows_the_world_time_zone()
    {
        Assert.True(CultivationRules.IsPoisonNight(Config, Warsaw(23)));
        Assert.True(CultivationRules.IsPoisonNight(Config, Warsaw(4, 59)));
        Assert.False(CultivationRules.IsPoisonNight(Config, Warsaw(5)));
        Assert.False(CultivationRules.IsPoisonNight(Config, Warsaw(20, 59)));
    }

    [Fact]
    public void Steps_are_limited_by_elapsed_time_and_daily_cap_without_losing_remainders()
    {
        Assert.Equal((2200, true), CultivationRules.PlausibleSteps(Config, 5000, TimeSpan.FromMinutes(10), 0));
        Assert.Equal((1000, false), CultivationRules.PlausibleSteps(Config, 1000, TimeSpan.FromMinutes(10), 0));
        Assert.Equal((500, true), CultivationRules.PlausibleSteps(Config, 5000, TimeSpan.FromHours(5), 29_500));
        Assert.Equal((0, true), CultivationRules.PlausibleSteps(Config, 10, TimeSpan.FromSeconds(30), 0));
        Assert.Equal(2, CultivationRules.StepsQi(Config, 0, 15, 45));   // 45/20 − 15/20
        Assert.Equal(1, CultivationRules.StepsQi(Config, 0, 39, 40));
        Assert.True(CultivationRules.StepsQi(Config, 10, 0, 2000) > CultivationRules.StepsQi(Config, 0, 0, 2000));
    }

    [Fact]
    public void World_date_follows_the_world_time_zone()
    {
        Assert.Equal(new DateOnly(2026, 10, 13), CultivationRules.WorldDate(Config, new DateTimeOffset(2026, 10, 12, 22, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 10, 12), CultivationRules.WorldDate(Config, new DateTimeOffset(2026, 10, 12, 21, 59, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Rolls_are_reproducible_and_uniform()
    {
        Assert.Equal(CultivationRules.Roll(12345), CultivationRules.Roll(12345));
        var below = Enumerable.Range(1, 20_000).Count(s => CultivationRules.Roll((ulong)s) < 630_000);
        Assert.InRange(below, 12_300, 12_900); // ≈ 63 %
    }
}
