using JianghuGo.Modules.Cultivation;
using JianghuGo.Modules.Cultivation.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

public class CultivationRulesTests
{
    private static readonly CultivationConfig Config =
        CultivationModule.LoadConfig(Path.Combine(AppContext.BaseDirectory, "game-config", "cultivation.json"));

    private static readonly Dictionary<string, int> Average = new() { ["insight"] = 10, ["willpower"] = 10, ["fate"] = 5, ["spirit"] = 10 };

    // 2026-10-12 is CEST (UTC+2) in Europe/Warsaw.
    private static DateTimeOffset Warsaw(int hour, int minute = 0) => new(2026, 10, 12, hour, minute, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Shipped_config_is_valid() => Assert.Equal(0, CultivationConfigValidator.Validate(Config).Count);

    [Fact]
    public void Validator_rejects_unknown_time_zone_and_guaranteed_success()
    {
        var bad = Config with { WorldTimeZone = "Mars/Olympus", Breakthrough = Config.Breakthrough with { MaxChance = 1m } };
        var errors = CultivationConfigValidator.Validate(bad);
        Assert.True(errors.Any(e => e.Contains("Mars/Olympus", StringComparison.Ordinal)));
        Assert.True(errors.Any(e => e.Contains("maxChance", StringComparison.Ordinal)));
    }

    [Fact]
    public void Qi_required_grows_geometrically_with_major_multiplier()
    {
        Assert.Equal(100, CultivationRules.QiRequired(Config, 0, 1));
        Assert.Equal(110, CultivationRules.QiRequired(Config, 0, 2));
        Assert.Equal(121, CultivationRules.QiRequired(Config, 0, 3));
        Assert.Equal(643, CultivationRules.QiRequired(Config, 0, 9));    // 100 × 1.1^8 × 3 = 643.08 → 643
        Assert.True(CultivationRules.IsMajor(Config, 0, 9));
        Assert.Equal((1, 1), CultivationRules.Next(Config, 0, 9));
        var last = Config.Realms.Count - 1;
        Assert.True(CultivationRules.IsPeak(Config, last, Config.Realms[last].Stages));
        Assert.False(CultivationRules.IsPeak(Config, 2, 9));
    }

    [Fact]
    public void Chance_is_lower_for_major_breakthroughs_and_clamped()
    {
        var minor = CultivationRules.SuccessChancePpm(Config, 0, 1, Average);
        var major = CultivationRules.SuccessChancePpm(Config, 0, 9, Average);
        Assert.Equal(970_000, minor);                                     // 0.92 + 0.08 = 1.00 → clamped to 0.97
        Assert.Equal(630_000, major);                                     // 0.55 + 0.04 + 0.03 + 0.01
        var hopeless = CultivationRules.SuccessChancePpm(Config, 2, 9, new Dictionary<string, int>());
        Assert.Equal(550_000, hopeless);
        Assert.Equal(50_000, CultivationRules.SuccessChancePpm(Config with { Breakthrough = Config.Breakthrough with { MajorBaseChance = 0.01m } }, 2, 9, new Dictionary<string, int>()));
    }

    [Fact]
    public void Meditation_counts_whole_minutes_with_path_multiplier_and_cap()
    {
        var start = Warsaw(14);
        Assert.Equal((108L, 30), CultivationRules.MeditationQi(Config, "qi", 10, start, start.AddMinutes(30).AddSeconds(59), null)); // 30 × 2 × 1.2 × 1.5
        Assert.Equal((72L, 30), CultivationRules.MeditationQi(Config, "flame", 10, start, start.AddMinutes(30), null));
        Assert.Equal(120, CultivationRules.MeditationQi(Config, "qi", 10, start, start.AddHours(5), null).Minutes);
        Assert.Equal(0L, CultivationRules.MeditationQi(Config, "qi", 10, start, start.AddSeconds(59), null).Qi);
        Assert.Equal(0L, CultivationRules.MeditationQi(Config, "qi", 10, start, start.AddMinutes(-5), null).Qi);
    }

    [Fact]
    public void Poison_path_doubles_qi_only_inside_the_night_window()
    {
        // 20:30 → 21:30 Warsaw: 30 day minutes × 2.4 + 30 night minutes × 4.8
        Assert.Equal((216L, 60), CultivationRules.MeditationQi(Config, "poison", 10, Warsaw(20, 30), Warsaw(21, 30), null));
        Assert.True(CultivationRules.IsPoisonNight(Config, Warsaw(23)));
        Assert.True(CultivationRules.IsPoisonNight(Config, Warsaw(4, 59)));
        Assert.False(CultivationRules.IsPoisonNight(Config, Warsaw(5)));
        Assert.False(CultivationRules.IsPoisonNight(Config, Warsaw(20, 59)));
    }

    [Fact]
    public void Qi_deviation_halves_the_minutes_it_covers()
    {
        var start = Warsaw(14);
        // 10 minutes under deviation at 1.8/min, then 10 at 3.6/min
        Assert.Equal((54L, 20), CultivationRules.MeditationQi(Config, "qi", 10, start, start.AddMinutes(20), start.AddMinutes(10)));
    }

    [Fact]
    public void Steps_are_limited_by_elapsed_time_and_daily_cap_without_losing_remainders()
    {
        Assert.Equal((2200, true), CultivationRules.PlausibleSteps(Config, 5000, TimeSpan.FromMinutes(10), 0));
        Assert.Equal((1000, false), CultivationRules.PlausibleSteps(Config, 1000, TimeSpan.FromMinutes(10), 0));
        Assert.Equal((500, true), CultivationRules.PlausibleSteps(Config, 5000, TimeSpan.FromHours(5), 29_500));
        Assert.Equal((0, true), CultivationRules.PlausibleSteps(Config, 10, TimeSpan.FromSeconds(30), 0));
        Assert.Equal(2, CultivationRules.StepsQi(Config, 15, 45));   // 45/20 - 15/20
        Assert.Equal(1, CultivationRules.StepsQi(Config, 39, 40));
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
