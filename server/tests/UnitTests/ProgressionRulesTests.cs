using JianghuGo.Domain;
using JianghuGo.Modules.Combat;
using JianghuGo.Modules.Combat.Domain;
using JianghuGo.Modules.Cultivation;
using JianghuGo.Modules.Cultivation.Application;
using JianghuGo.Modules.Cultivation.Domain;
using JianghuGo.Modules.Quests;
using JianghuGo.Modules.Quests.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

public class ProgressionRulesTests
{
    private static string Cfg(string name) => Path.Combine(AppContext.BaseDirectory, "game-config", name);

    private static readonly QuestsConfig Quests = QuestsModule.LoadConfig(Cfg("quests.json"));
    private static readonly CombatConfig Combat = CombatModule.LoadConfig(Cfg("combat.json"));
    private static readonly CultivationConfig Cultivation = CultivationModule.LoadConfig(Cfg("cultivation.json"));

    [Fact]
    public void Daily_quests_are_deterministic_distinct_and_vary_between_days_and_players()
    {
        var a = Guid.Parse("01a00000-0000-7000-8000-000000000001");
        var b = Guid.Parse("01a00000-0000-7000-8000-000000000002");
        var day = new DateOnly(2026, 10, 13);
        var first = QuestRules.DailyFor(Quests, a, day).Select(q => q.Id).ToList();
        Assert.SequenceEqual(first, QuestRules.DailyFor(Quests, a, day).Select(q => q.Id));
        Assert.Equal(Quests.DailyCount, first.Count);
        Assert.Equal(first.Count, QuestRules.DailyFor(Quests, a, day).Select(q => q.Metric).Distinct().Count());

        var differs = Enumerable.Range(1, 20).Any(i => !QuestRules.DailyFor(Quests, a, day.AddDays(i)).Select(q => q.Id).SequenceEqual(first));
        Assert.True(differs, "the pick must change from day to day");
        var otherPlayer = Enumerable.Range(0, 20).Any(i => !QuestRules.DailyFor(Quests, b, day.AddDays(i)).Select(q => q.Id)
            .SequenceEqual(QuestRules.DailyFor(Quests, a, day.AddDays(i)).Select(q => q.Id)));
        Assert.True(otherPlayer, "players get different picks");
    }

    [Fact]
    public void Checkin_streak_grows_on_consecutive_days_and_resets_after_a_gap()
    {
        var id = new CharacterId(Guid.NewGuid());
        var day = new DateOnly(2026, 10, 13);
        Assert.Equal((1, 1), QuestRules.NextStreak(null, day));
        var s = new CheckinState(id, day, 3, 5, DateTimeOffset.UnixEpoch);
        Assert.Equal((4, 5), QuestRules.NextStreak(s, day.AddDays(1)));
        Assert.Equal((1, 5), QuestRules.NextStreak(s, day.AddDays(2)));
        Assert.Equal(20, QuestRules.CheckinStones(Quests, 1));
        Assert.Equal(80, QuestRules.CheckinStones(Quests, 7));
        Assert.Equal(80, QuestRules.CheckinStones(Quests, 30)); // capped at maxBonusDays
    }

    [Fact]
    public void Quest_config_validator_rejects_unknown_metrics_and_duplicates()
    {
        var bad = Quests with
        {
            Daily = [.. Quests.Daily, Quests.Daily[0], new QuestDefinition("x_1", "X", "X", "teleports", 1, 1)],
        };
        var errors = QuestsConfigValidator.Validate(bad);
        Assert.True(errors.Any(e => e.Contains("duplicated")), string.Join("; ", errors));
        Assert.True(errors.Any(e => e.Contains("unknown metric")), string.Join("; ", errors));
    }

    [Fact]
    public void Spirit_pet_levels_chance_and_bonuses()
    {
        Assert.Equal(1, SpiritPetRules.Level(0));
        Assert.Equal(2, SpiritPetRules.Level(SpiritPetRules.XpPerLevel));
        Assert.Equal(SpiritPetRules.MaxLevel, SpiritPetRules.Level(1_000_000));
        Assert.Equal(150, SpiritPetRules.TameChancePermille(-10));
        Assert.Equal(200, SpiritPetRules.TameChancePermille(10));
        Assert.Equal(400, SpiritPetRules.TameChancePermille(500));
        Assert.True(SpiritPetRules.Tamed(0x5EED_9E7AUL, 1));        // roll 0
        Assert.False(SpiritPetRules.Tamed(0x5EED_9E7AUL ^ 999, 999)); // roll 999

        var wolf = Combat.Npcs.Single(n => n.Id == "grey_wolf");
        Assert.True(wolf.Tameable);
        var low = SpiritPetRules.Bonuses(wolf, 1);
        var high = SpiritPetRules.Bonuses(wolf, SpiritPetRules.MaxLevel);
        Assert.Equal(3, low.Count);
        Assert.True(low.All(kv => kv.Value >= 1));
        Assert.True(high.Sum(kv => kv.Value) > low.Sum(kv => kv.Value));
        Assert.True(Combat.Npcs.Where(n => n.Boss).All(n => n.WorldOnly), "bosses are fought in the world only");
    }

    [Fact]
    public void Meridian_points_follow_the_ladder_and_bonuses_add_realm()
    {
        var id = new CharacterId(Guid.NewGuid());
        var s = CultivationState.Initial(id, new AccountId(Guid.NewGuid()), "qi", new DateOnly(2026, 10, 13), DateTimeOffset.UnixEpoch);
        Assert.Equal(0, MeridianRules.PointsEarned(Cultivation, s));
        Assert.Equal(4, MeridianRules.PointsEarned(Cultivation, s with { Stage = 5 }));
        Assert.Equal(Cultivation.Realms[0].Stages, MeridianRules.PointsEarned(Cultivation, s with { RealmIndex = 1, Stage = 1 }));
        var bonuses = MeridianRules.Bonuses(s with { RealmIndex = 2 }, new Dictionary<string, int> { ["fate"] = 3 });
        Assert.Equal(9, bonuses.Count);
        Assert.Equal(4, bonuses["strength"]);
        Assert.Equal(7, bonuses["fate"]);
        Assert.Equal(0, MeridianRules.Bonuses(s, new Dictionary<string, int>()).Count);
    }
}
