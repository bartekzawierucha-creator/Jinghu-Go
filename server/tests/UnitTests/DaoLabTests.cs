using JianghuGo.Domain;
using JianghuGo.Modules.Combat;
using JianghuGo.Modules.Combat.Domain;
using JianghuGo.Modules.Inventory;
using JianghuGo.TestKit;
using Microsoft.Extensions.Configuration;

namespace JianghuGo.UnitTests;

public class DaoLabTests
{
    private static string Path(string f) => System.IO.Path.Combine(AppContext.BaseDirectory, "game-config", f);

    private static readonly CombatConfig Combat = CombatModule.LoadConfig(Path("combat.json"));

    private static readonly DaoLabConfig Dao = CombatModule.LoadDaoLabConfig(Path("dao-lab.json"), InventoryModule.LoadCatalog(new ConfigurationBuilder().Build()));

    private static DaoDesign Strike(int power = 12, int qiCost = 0, int cooldown = 0, EffectSpec? effect = null, int rangeMin = 1, int rangeMax = 1, string name = "Dłoń Burzy") =>
        new(name, "attack", qiCost, cooldown, 0, new AttackSpec(power, "strength", rangeMin, rangeMax, 0, effect), null, null, null);

    [Fact]
    public void Shipped_config_is_valid() => Assert.Equal(0, DaoLabConfigValidator.Validate(Dao).Count);

    /// <summary>Calibration: a design can match any path-less official technique; path signatures may exceed the budget.</summary>
    [Fact]
    public void Every_pathless_official_technique_fits_the_dao_budget()
    {
        foreach (var t in Combat.Techniques.Where(t => t.Path is null && t.Special is null)) // Dao specials are evasion-only
        {
            Assert.True(DaoLabRules.Points(Dao, t) <= DaoLabRules.Allowance(Dao, t), $"{t.Id}: {DaoLabRules.Points(Dao, t)} > {DaoLabRules.Allowance(Dao, t)}");
            var asDesign = new DaoDesign("Kopia", t.Slot, t.QiCost, t.Cooldown, t.Speed, t.Attack, t.Defense, t.Movement, t.Counter);
            Assert.True(DaoLabRules.Assess(Dao, Combat, asDesign).Valid, t.Id + ": " + string.Join(" ", DaoLabRules.Assess(Dao, Combat, asDesign).Problems));
        }
    }

    [Fact]
    public void Power_must_be_paid_for_with_qi_or_cooldown()
    {
        var free = DaoLabRules.Assess(Dao, Combat, Strike(power: 25));
        Assert.False(free.Valid);
        Assert.True(free.Problems.Single().StartsWith("too powerful", StringComparison.Ordinal));
        var paid = DaoLabRules.Assess(Dao, Combat, Strike(power: 25, qiCost: 6, cooldown: 2));
        Assert.True(paid.Valid, string.Join(" ", paid.Problems));
        Assert.Equal(250, paid.Points);
        Assert.Equal(140 + 6 * 12 + 2 * 40, paid.Allowance);
    }

    [Fact]
    public void Hard_limits_and_stun_lock_rules_apply_regardless_of_budget()
    {
        string Problems(DaoDesign d) => string.Join(" | ", DaoLabRules.Assess(Dao, Combat, d).Problems);
        Assert.True(Problems(Strike(power: 31, qiCost: 20, cooldown: 4)).Contains("attack.power", StringComparison.Ordinal));
        Assert.True(Problems(Strike(rangeMin: 3, rangeMax: 2)).Contains("range", StringComparison.Ordinal));
        Assert.True(Problems(Strike(power: 5, qiCost: 10, cooldown: 1, effect: new EffectSpec("stun", 200, 0, 1))).Contains("cooldown of at least 2", StringComparison.Ordinal));
        Assert.True(Problems(Strike(power: 5, qiCost: 10, cooldown: 3, effect: new EffectSpec("stun", 400, 0, 1))).Contains("stun chance", StringComparison.Ordinal));
        Assert.True(Problems(Strike(power: 5, qiCost: 10, cooldown: 3, effect: new EffectSpec("stun", 200, 0, 2))).Contains("stun lasts", StringComparison.Ordinal));
        Assert.True(Problems(Strike(effect: new EffectSpec("poison_cloud", 200, 1, 1))).Contains("effect.type", StringComparison.Ordinal));
        Assert.True(Problems(Strike(name: "x")).Contains("name", StringComparison.Ordinal));
        Assert.True(Problems(Strike(name: "<script>")).Contains("name", StringComparison.Ordinal));
        Assert.True(Problems(Strike() with { Defense = new DefenseSpec(200) }).Contains("exactly one", StringComparison.Ordinal));
        Assert.True(Problems(Strike() with { Slot = "defense" }).Contains("exactly one", StringComparison.Ordinal));
        Assert.True(Problems(Strike() with { Slot = "ultimate" }).Contains("slot", StringComparison.Ordinal));
        Assert.True(Problems(Strike(qiCost: -1)).Contains("qiCost", StringComparison.Ordinal));
        Assert.True(DaoLabRules.Assess(Dao, Combat, Strike(power: 5, qiCost: 8, cooldown: 2, effect: new EffectSpec("stun", 300, 0, 1))).Valid);
    }

    [Fact]
    public void Technique_ids_use_only_letters_allowed_for_technique_ids()
    {
        var id = DaoLabRules.NewTechniqueId(0x0123456789ABCDEFUL);
        Assert.Equal(20, id.Length);
        Assert.True(DaoLabRules.IsDaoId(id));
        Assert.False(DaoLabRules.IsDaoId("palm_strike"));
        Assert.False(DaoLabRules.IsDaoId("dao_zzzzzzzzzzzzzzzz"));
        Assert.NotEqual(DaoLabRules.NewTechniqueId(1), DaoLabRules.NewTechniqueId(2));
    }

    /// <summary>The engine plays a Dao technique from the definition frozen in the snapshot (it is not in combat.json).</summary>
    [Fact]
    public void Engine_uses_the_frozen_definition_of_a_dao_technique()
    {
        var design = Strike(power: 25, qiCost: 6, cooldown: 2);
        var def = DaoLabRules.ToDefinition(DaoLabRules.NewTechniqueId(42), design);
        var stats = new Dictionary<string, int> { ["strength"] = 10, ["spirit"] = 10, ["agility"] = 10, ["constitution"] = 10 };
        var slots = new EquippedTechnique?[] { new(def.Id, 1, def), null, null, null, null, null, null, null };
        var a = new Combatant("Autor", stats, slots, null);
        var b = new Combatant("Manekin", stats, [new EquippedTechnique("palm_strike", 1), null, null, null, null, null, null, null], null);
        var exact = Combat with { Damage = Combat.Damage with { VariancePermille = 0 }, HitChance = new HitChanceRules(999, 0, 999, 999) };
        var s = CombatEngine.Start(exact, a, b) with { Distance = 1 };
        var (next, events) = CombatEngine.ResolveTurn(exact, a, b, s, new Intent(0, null), Intent.Rest, new DeterministicRandom(1));
        var hit = events.Single(e => e.Actor == "a" && e.TechniqueId == def.Id);
        Assert.True(hit.Hit == true);
        Assert.True(hit.Damage > 0);
        Assert.Equal(next.A.MaxQi - 6 + exact.Vitals.QiRegenPerTurn, next.A.Qi);
        Assert.Equal(2, next.A.Cooldowns[0]);         // set to cooldown + 1, then ticked at the end of the turn
        Assert.Equal("on_cooldown", CombatEngine.Validate(exact, a, next.A, 1, new Intent(0, null)));
    }
}
