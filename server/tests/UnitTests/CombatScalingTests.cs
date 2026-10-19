using JianghuGo.Domain;
using JianghuGo.Modules.Combat;
using JianghuGo.Modules.Combat.Application;
using JianghuGo.Modules.Combat.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

/// <summary>1.6 combat rules: relative agility, Focus/Willpower/Affinity/Fate, perk caps and realm scaling.</summary>
public class CombatScalingTests
{
    private static readonly CombatConfig Config =
        CombatModule.LoadConfig(Path.Combine(AppContext.BaseDirectory, "game-config", "combat.json"));

    private static ScalingRules Sc => Config.Scaling!;

    [Fact]
    public void Agility_edge_is_relative_so_it_means_the_same_at_every_realm()
    {
        var h = Config.HitChance;
        Assert.Equal(h.AgilityTerm(12, 10), h.AgilityTerm(1200, 1000));
        Assert.Equal(-h.AgilityTerm(12, 10), h.AgilityTerm(10, 12));
        Assert.True(h.AgilityTerm(1000, 500) <= h.AgilityRatioPermille, "a huge lead is still bounded");
        Assert.Equal(0, h.AgilityTerm(0, 0));
    }

    [Fact]
    public void Focus_against_willpower_gives_bounded_critical_strikes_and_resistance()
    {
        var crit = Config.Crit!;
        Assert.Equal(crit.BasePermille, crit.ChancePermille(50, 50));
        Assert.True(crit.ChancePermille(100, 50) > crit.ChancePermille(50, 50));
        Assert.True(crit.ChancePermille(10, 1000) >= 0);
        Assert.True(crit.ChancePermille(100_000, 1) <= crit.MaxPermille);
        var resist = Config.Resist!;
        Assert.Equal(0, resist.ResistPermille(10, 50));                       // weaker will resists nothing
        Assert.True(resist.ResistPermille(100, 50) > 0);
        Assert.True(resist.ResistPermille(100_000, 1) <= resist.MaxPermille);
    }

    [Fact]
    public void Elemental_affinity_weights_the_element_modifier_both_ways()
    {
        var el = Config.Elements!;
        Assert.Equal(el.AdvantagePermille, el.Modifier("water", "fire", 20, 20));            // equal affinity: plain modifier
        Assert.True(el.Modifier("water", "fire", 40, 0) > el.AdvantagePermille);            // strong affinity deepens an advantage
        Assert.True(el.Modifier("water", "fire", 0, 40) < el.AdvantagePermille);
        Assert.True(el.Modifier("fire", "water", 40, 0) > -el.AdvantagePermille);           // …and softens a disadvantage
        Assert.Equal(0, el.Modifier("wood", "wood", 40, 0));
    }

    [Fact]
    public void Fate_luck_is_capped()
    {
        Assert.Equal(30, CombatService.LuckPermille(10));
        Assert.Equal(500, CombatService.LuckPermille(100_000));
        Assert.Equal(0, CombatService.LuckPermille(-5));
    }

    [Fact]
    public void Perk_caps_keep_every_stack_finite()
    {
        var huge = new PathPerks("x", QiPermille: 99_999, QiRegen: 999, GuardPermille: 9999, EffectChancePermille: 9999, BleedPermille: 99_999, HealPermille: 9999,
            DamagePermille: 99_999, RegenPermille: 9999, ReflectPermille: 9999, LifestealPermille: 9999, FirstStrikePermille: 99_999);
        var c = huge.Capped(Config.PerkCaps);
        var k = Config.PerkCaps!;
        Assert.Equal(k.RegenPermille, c.RegenPermille);
        Assert.Equal(k.LifestealPermille, c.LifestealPermille);
        Assert.Equal(k.DamagePermille, c.DamagePermille);
        Assert.Equal(k.ReflectPermille, c.ReflectPermille);
        var small = new PathPerks("y", DamagePermille: 50);
        Assert.Equal(50, small.Capped(Config.PerkCaps).DamagePermille);
        Assert.True(k.RegenPermille <= 100, "no immortal regeneration");
    }

    [Fact]
    public void Npcs_keep_their_difficulty_as_the_cultivator_climbs()
    {
        var wolf = Config.Npc("grey_wolf")!;
        Assert.True(ReferenceEquals(wolf, Sc.ForFighter(wolf, Sc.Home(wolf))), "unchanged at home");
        var at30 = Sc.ForFighter(wolf, 30);
        Assert.Equal((int)(wolf.Stats["agility"] * Sc.Budget(30) / Sc.Budget(Sc.Home(wolf))), at30.Stats["agility"]);
        Assert.True(at30.Loadout.Where(s => s is not null).All(s => s!.Level > 1), "techniques level up with the realm");
        var lord = Config.Npc("blood_demon_lord")!;
        Assert.True(ReferenceEquals(lord, Sc.ForFighter(lord, 0)), "world foes are never weakened below home");
        Assert.True(Sc.AtRealm(lord, 0).Stats["strength"] < lord.Stats["strength"], "avatars (sect boss) can be weaker below home");
        Assert.Equal(wolf.RewardQi, Sc.Qi(wolf.RewardQi, 0));
        Assert.True(Sc.Qi(wolf.RewardQi, 10) > 100 * wolf.RewardQi, "Qi follows the cultivation curve");
        Assert.True(Sc.Qi(1, 1000) > 0, "no overflow");
        Assert.Equal(1000 + (10 * Sc.StonesPermillePerRealm), Sc.StonesPermille(10));
        foreach (var n in Config.Npcs) Assert.True(Sc.Home(n) is >= 0 and <= 65, n.Id);
    }

    [Fact]
    public void Tribulations_are_raised_to_their_realm()
    {
        var t = Config.Npc("heavenly_tribulation")!;
        var low = CombatService.TribulationNpc(Config, t, 1, 1000, "a");
        var high = CombatService.TribulationNpc(Config, t, 60, 1000, "b");
        Assert.True(high.Stats["spirit"] > 10 * low.Stats["spirit"]);
        Assert.Equal("b", high.NamePl);
        var fierce = CombatService.TribulationNpc(Config, t, 60, 1500, "c");
        Assert.True(fierce.Stats["spirit"] > high.Stats["spirit"]);
    }

    [Fact]
    public void Techniques_open_realm_by_realm_and_their_level_is_capped_by_the_realm()
    {
        foreach (var path in new[] { "qi", "body", "flame", "poison" })
        {
            Assert.True(Config.StarterLoadouts[path].All(id => Config.Technique(id).MinRealm == 0), path + " starter set is open at realm 0");
            var eras = Config.KnownBy(path).Select(t => t.MinRealm).Distinct().ToList();
            foreach (var era in new[] { 11, 22, 33, 44, 55 }) Assert.True(eras.Contains(era), $"{path} gains a technique at realm {era}");
            Assert.True(Config.KnownBy(path, 0).Count() < Config.KnownBy(path, 65).Count());
        }

        var slots = Config.StarterLoadouts["qi"].Cast<string?>().ToList();
        slots[0] = "dao_severing_sword";
        Assert.Contains("unlocks at realm 55", LoadoutRules.Validate(Config, "qi", slots, null, 10));
        Assert.Null(LoadoutRules.Validate(Config, "qi", slots, null, 55));
        Assert.Null(LoadoutRules.Validate(Config, "qi", slots, null, 10, ["dao_severing_sword"]));       // already equipped: kept

        var p = Config.Progression;
        Assert.True(p.LevelCap(0) < p.MaxTechniqueLevel);
        Assert.Equal(p.MaxTechniqueLevel, p.LevelCap(65));
        Assert.True(p.XpFor(10, 50) > p.XpFor(10, 0), "insight speeds up mastery");
        Assert.True(p.XpFor(10, 100_000) <= 3 * p.XpFor(10, 0), "at most ×3");
    }
}
