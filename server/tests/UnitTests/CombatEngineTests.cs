using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JianghuGo.Domain;
using JianghuGo.Modules.Combat;
using JianghuGo.Modules.Combat.Domain;
using JianghuGo.TestKit;

namespace JianghuGo.UnitTests;

public class CombatEngineTests
{
    private static readonly CombatConfig Config =
        CombatModule.LoadConfig(Path.Combine(AppContext.BaseDirectory, "game-config", "combat.json"));

    /// <summary>Variance 0 and fixed hit chance make damage exactly predictable.</summary>
    private static readonly CombatConfig Exact = Config with
    {
        Damage = Config.Damage with { VariancePermille = 0 },
        HitChance = new HitChanceRules(999, 0, 999, 999),
    };

    private static readonly Dictionary<string, int> Tens = new()
    {
        ["strength"] = 10, ["spirit"] = 10, ["agility"] = 10, ["constitution"] = 10, ["insight"] = 10,
        ["willpower"] = 10, ["focus"] = 10, ["elementalAffinity"] = 5, ["fate"] = 5,
    };

    private static Combatant Player(string path, Dictionary<string, int>? stats = null, int level = 1) =>
        new("P-" + path, stats ?? Tens, [.. Config.StarterLoadouts[path].Select(id => new EquippedTechnique(id, level))], null, Config.PathHpBonusPermille[path]);

    private static Combatant FromNpc(string id)
    {
        var n = Config.Npc(id)!;
        return new Combatant(n.NamePl, n.Stats, [.. n.Loadout.Select(s => s is null ? null : new EquippedTechnique(s.Id, s.Level))], n.HpOverride);
    }

    private static int SlotOf(Combatant c, string techniqueId) => c.Slots.Select((s, i) => (s, i)).First(x => x.s?.Id == techniqueId).i;

    /// <summary>Resolves with a seed whose hit rolls succeed (hit chance 999‰: only roll 999 misses).</summary>
    private static (BattleState, IReadOnlyList<TurnEvent>) Resolve(CombatConfig c, Combatant a, Combatant b, BattleState s, Intent ia, Intent ib, ulong seed = 1) =>
        CombatEngine.ResolveTurn(c, a, b, s, ia, ib, new DeterministicRandom(seed));

    [Fact]
    public void Shipped_config_is_valid() => Assert.Equal(0, CombatConfigValidator.Validate(Config).Count);

    [Fact]
    public void Validator_rejects_full_block_guaranteed_hits_and_wrong_slot_layout()
    {
        var bad = Config with
        {
            HitChance = Config.HitChance with { MaxPermille = 1000 },
            Slots = ["attack", "attack", "attack", "attack", "attack", "defense", "movement", "counter"],
            Techniques = [.. Config.Techniques.Select(t => t.Id == "iron_shirt" ? t with { Defense = new DefenseSpec(1000) } : t)],
        };
        var errors = CombatConfigValidator.Validate(bad);
        Assert.True(errors.Any(e => e.Contains("hitChance", StringComparison.Ordinal)));
        Assert.True(errors.Any(e => e.Contains("slots must be", StringComparison.Ordinal)));
        Assert.True(errors.Any(e => e.Contains("iron_shirt", StringComparison.Ordinal)));
    }

    [Fact]
    public void Loadout_rules_enforce_slot_kinds_path_knowledge_and_uniqueness()
    {
        var ok = Config.StarterLoadouts["qi"].Cast<string?>().ToList();
        Assert.Null(LoadoutRules.Validate(Config, "qi", ok));
        Assert.Contains("not known", LoadoutRules.Validate(Config, "qi", [.. ok.Select((x, i) => i == 1 ? "iron_fist" : x)]));
        Assert.Contains("accepts defense", LoadoutRules.Validate(Config, "qi", [.. ok.Select((x, i) => i == 4 ? "palm_strike" : x)]));
        Assert.Contains("only once", LoadoutRules.Validate(Config, "qi", [.. ok.Select((x, i) => i == 1 ? "palm_strike" : x)]));
        Assert.Contains("at least one attack", LoadoutRules.Validate(Config, "qi", [null, null, null, null, "flowing_water", null, null, null]));
        Assert.Contains("exactly 8", LoadoutRules.Validate(Config, "qi", ["palm_strike"]));
    }

    [Fact]
    public void Vitals_come_from_stats_and_npc_overrides()
    {
        var s = CombatEngine.Start(Config, Player("qi"), FromNpc("training_dummy"));
        Assert.Equal(180, s.A.MaxHp);   // 100 + 10 × 8
        Assert.Equal(60, s.A.MaxQi);    // 30 + 10 × 3
        Assert.Equal(80, s.B.MaxHp);
        Assert.Equal(2, s.Distance);
        Assert.Equal(1, s.Turn);
    }

    [Fact]
    public void Validation_reports_stable_codes()
    {
        var p = Player("qi");
        var f = CombatEngine.Start(Config, p, p).A;
        Assert.Null(CombatEngine.Validate(Config, p, f, 2, Intent.Rest));
        // no distance any more: every attack reaches, and the obsolete direction is ignored
        Assert.Null(CombatEngine.Validate(Config, p, f, 3, new Intent(SlotOf(p, "palm_strike"), null)));
        Assert.Null(CombatEngine.Validate(Config, p, f, 1, new Intent(SlotOf(p, "palm_strike"), "retreat")));
        Assert.Null(CombatEngine.Validate(Config, p, f, 2, new Intent(6, null)));                // the special skill
        Assert.Equal("slot_invalid", CombatEngine.Validate(Config, p, f, 2, new Intent(8, null)));
        Assert.Equal("insufficient_qi", CombatEngine.Validate(Config, p, f with { Qi = 1 }, 2, new Intent(SlotOf(p, "qi_blast"), null)));
        Assert.Equal("on_cooldown", CombatEngine.Validate(Config, p, f with { Cooldowns = [0, 0, 1, 0, 0, 0, 0, 0] }, 2, new Intent(2, null)));
        var dummy = FromNpc("training_dummy");
        Assert.Equal("slot_empty", CombatEngine.Validate(Config, dummy, CombatEngine.Start(Config, dummy, dummy).A, 1, new Intent(1, null)));
    }

    [Fact]
    public void Damage_follows_the_formula_and_guard_acts_first_on_the_timeline()
    {
        var a = Player("qi");
        var b = Player("qi", new Dictionary<string, int>(Tens) { ["agility"] = 1 }); // slower defender
        var s = CombatEngine.Start(Exact, a, b) with { Distance = 1 };
        var (s1, ev) = Resolve(Exact, a, b, s, new Intent(SlotOf(a, "palm_strike"), null), new Intent(SlotOf(b, "flowing_water"), null));
        Assert.Equal("defend", ev[0].Action);                          // stance resolves before the attack
        var hit = ev.Single(e => e.Action == "attack");
        Assert.True(hit.Hit!.Value);
        Assert.Equal(10, hit.Damage);                                  // 12 × 1.3 = 15 → × (1 − 0.3 block) = 10
        Assert.Equal(b.Stats["constitution"] * 8 + 100 - 10, s1.B.Hp);
        Assert.Equal(2, s1.Turn);
    }

    [Fact]
    public void Technique_level_scales_damage()
    {
        var a = Player("qi", level: 5);
        var b = Player("qi");
        var s = CombatEngine.Start(Exact, a, b) with { Distance = 1 };
        var (_, ev) = Resolve(Exact, a, b, s, new Intent(SlotOf(a, "palm_strike"), null), Intent.Rest);
        Assert.Equal(20, ev.Single(e => e.Action == "attack").Damage); // 12 × 1.4 = 16 (truncated) → × 1.3 = 20
    }

    [Fact]
    public void Counter_stance_reflects_damage_and_steals_qi()
    {
        var a = Player("body");
        var b = Player("body");
        var s = CombatEngine.Start(Exact, a, b) with { Distance = 1 };
        var (s1, ev) = Resolve(Exact, a, b, s, new Intent(SlotOf(a, "palm_strike"), null), new Intent(SlotOf(b, "mirror_palm"), null));
        var attack = ev.Single(e => e.Action == "attack");
        Assert.Equal(15, attack.Damage);
        Assert.Equal(7, attack.Reflected);                             // 50 % of 15
        Assert.Equal(4, attack.QiStolen);
        Assert.Equal(225 - 7, s1.A.Hp);                                // body path: 180 HP + 25 % path bonus
        Assert.Equal(60, s1.A.Qi);                                     // 60 − 4 stolen + 5 regen, capped at max 60
    }

    [Fact]
    public void Special_skill_heals_and_guards_before_the_strike_lands_with_path_perks()
    {
        var a = Player("qi");
        var body = Player("body") with { Perks = Config.Perks("body") };            // Vajra Body: heal 25% (+5% body perk), guard 50% (+10)
        var s = CombatEngine.Start(Exact, a, body);
        s = s with { B = s.B with { Hp = 100 } };
        var (s1, ev) = Resolve(Exact, a, body, s, new Intent(SlotOf(a, "palm_strike"), null), new Intent(6, null));
        var special = ev.Single(e => e.Actor == "b");
        Assert.Equal("special", special.Action);
        Assert.Equal("vajra_body", special.TechniqueId);
        Assert.Equal(s.B.MaxHp * 300 / 1000, special.Healed);
        var strike = ev.Single(e => e.Actor == "a");
        Assert.Equal(6, strike.Damage);                                            // 12 × 1.3 = 15 → × (1 − 0.6 guard) = 6
        Assert.Equal(100 + special.Healed - 6, s1.B.Hp);
        Assert.Equal(s.Distance, s1.Distance);                                    // nobody moves: there is no distance
    }

    [Fact]
    public void Path_perks_shape_qi_damage_and_effects()
    {
        var qi = Player("qi") with { Perks = Config.Perks("qi") };
        var plain = Player("qi");
        Assert.Equal(CombatEngine.Start(Exact, plain, plain).A.MaxQi * 1250 / 1000, CombatEngine.Start(Exact, qi, plain).A.MaxQi);
        var (s1, _) = Resolve(Exact, qi, plain, CombatEngine.Start(Exact, qi, plain), Intent.Rest, Intent.Rest);
        Assert.Equal(CombatEngine.Start(Exact, qi, plain).A.Qi, s1.A.Qi);          // already full: regen is capped
        var blast = SlotOf(qi, "qi_blast");
        var (h1, e1) = Resolve(Exact, qi, plain, CombatEngine.Start(Exact, qi, plain), new Intent(blast, null), Intent.Rest);
        var (h2, e2) = Resolve(Exact, plain, plain, CombatEngine.Start(Exact, plain, plain), new Intent(blast, null), Intent.Rest);
        Assert.True(e1.Single(e => e.Action == "attack").Damage > e2.Single(e => e.Action == "attack").Damage, "Qi path: spirit techniques hit harder");
        foreach (var path in new[] { "qi", "body", "flame", "poison" }) Assert.NotNull(Config.Perks(path)!.DescriptionPl);
    }

    [Fact]
    public void Cooldowns_and_qi_costs_apply()
    {
        var a = Player("qi");
        var b = Player("qi");
        var s = CombatEngine.Start(Exact, a, b);
        var blast = SlotOf(a, "qi_blast");                            // cost 12, cooldown 2
        var (s1, _) = Resolve(Exact, a, b, s, new Intent(blast, null), Intent.Rest);
        Assert.Equal(53, s1.A.Qi);                                     // 60 − 12 cost + 5 regen
        Assert.Equal("on_cooldown", CombatEngine.Validate(Exact, a, s1.A, s1.Distance, new Intent(blast, null)));
        var (s2, _) = Resolve(Exact, a, b, s1, Intent.Rest, Intent.Rest);
        Assert.Equal("on_cooldown", CombatEngine.Validate(Exact, a, s2.A, s2.Distance, new Intent(blast, null)));
        var (s3, _) = Resolve(Exact, a, b, s2, Intent.Rest, Intent.Rest);
        Assert.Null(CombatEngine.Validate(Exact, a, s3.A, s3.Distance, new Intent(blast, null)));
    }

    [Fact]
    public void Bleed_ticks_for_its_duration_and_stun_costs_the_next_action()
    {
        var a = Player("qi");
        var b = Player("qi");
        var bleeding = CombatEngine.Start(Exact, a, b) with { };
        bleeding = bleeding with { B = bleeding.B with { Effects = [new Effect(EffectTypes.Bleed, 5, 0, 2)] } };
        var (t1, e1) = Resolve(Exact, a, b, bleeding, Intent.Rest, Intent.Rest);
        var (t2, e2) = Resolve(Exact, a, b, t1, Intent.Rest, Intent.Rest);
        var (t3, e3) = Resolve(Exact, a, b, t2, Intent.Rest, Intent.Rest);
        Assert.Equal(1, e1.Count(e => e.Action == "bleed"));
        Assert.Equal(1, e2.Count(e => e.Action == "bleed"));
        Assert.Equal(0, e3.Count(e => e.Action == "bleed"));
        Assert.Equal(180 - 10, t3.B.Hp);

        var stunned = CombatEngine.Start(Exact, a, b) with { Distance = 1 };
        stunned = stunned with { B = stunned.B with { Effects = [new Effect(EffectTypes.Stun, 0, 0, 1)] } };
        var (s1, ev) = Resolve(Exact, a, b, stunned, Intent.Rest, new Intent(SlotOf(b, "palm_strike"), null));
        Assert.Equal("stunned", ev.Single(e => e.Actor == "b").Action);
        Assert.False(s1.B.IsStunned);
        Assert.Equal(180, s1.A.Hp);
    }

    [Fact]
    public void Illegal_intent_at_resolution_degrades_to_rest()
    {
        var a = Player("qi");
        var b = Player("qi");
        var s = CombatEngine.Start(Exact, a, b);
        s = s with { A = s.A with { Qi = 0 } };                              // A can no longer pay for Qi Blast
        var (_, ev) = Resolve(Exact, a, b, s, new Intent(SlotOf(a, "qi_blast"), null), new Intent(42, null));
        Assert.SequenceEqual(["rest", "rest"], ev.Select(e => e.Action));
    }

    [Fact]
    public void Time_limit_decides_by_remaining_health_ratio()
    {
        var a = Player("qi");
        var b = Player("qi");
        var s = CombatEngine.Start(Exact, a, b) with { Turn = Exact.MaxTurns };
        s = s with { B = s.B with { Hp = 100 } };
        var (end, ev) = Resolve(Exact, a, b, s, Intent.Rest, Intent.Rest);
        Assert.Equal(Outcomes.AWon, end.Outcome);
        Assert.Equal("time_limit", ev[^1].Action);
        Assert.Throws<InvalidOperationException>(() => Resolve(Exact, a, b, end, Intent.Rest, Intent.Rest));
    }

    private static (BattleState Final, List<string> Log) Simulate(Combatant a, Combatant b, ulong seed)
    {
        var s = CombatEngine.Start(Config, a, b);
        var log = new List<string>();
        while (s.Outcome is null)
        {
            var rng = CombatEngine.TurnRng(seed, s.Turn);
            var ia = CombatAi.Choose(Config, a, s.A, s.Distance, rng);
            var ib = CombatAi.Choose(Config, b, s.B, s.Distance, rng);
            var (next, events) = CombatEngine.ResolveTurn(Config, a, b, s, ia, ib, rng);
            log.AddRange(events.Select(e => JsonSerializer.Serialize(e)));
            s = next;
        }

        return (s, log);
    }

    [Fact]
    public void Same_seed_and_intents_replay_identically()
    {
        var (f1, l1) = Simulate(FromNpc("mountain_bandit"), FromNpc("grey_wolf"), 777);
        var (f2, l2) = Simulate(FromNpc("mountain_bandit"), FromNpc("grey_wolf"), 777);
        Assert.Equal(JsonSerializer.Serialize(f1), JsonSerializer.Serialize(f2));
        Assert.SequenceEqual(l1, l2);
        var (f3, _) = Simulate(FromNpc("mountain_bandit"), FromNpc("grey_wolf"), 778);
        Assert.NotEqual(JsonSerializer.Serialize(f1), JsonSerializer.Serialize(f3));
    }

    /// <summary>
    /// Regression pin for rules "combat-1": any change to the engine or combat.json that alters outcomes must be a
    /// deliberate rules-version bump (then update this hash). Replays of stored battles depend on it.
    /// </summary>
    [Fact]
    public void Golden_battle_is_unchanged_for_rules_combat_2()
    {
        var (final, log) = Simulate(FromNpc("mountain_bandit"), Player("poison"), 20261012);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(final) + string.Join("\n", log))))[..16];
        Assert.Equal("combat-2", Config.RulesVersion);
        Assert.Equal(GoldenHash, hash, $"outcome {final.Outcome} after {final.Turn} turns, {log.Count} events");
    }

    private const string GoldenHash = "C9A37960B899952B"; // bandit vs poison starter, seed 20261012: b_won after 15 turns, 36 events (rules combat-2)

    [Fact]
    public void Random_battles_always_terminate_with_consistent_state()
    {
        var builds = new[] { Player("qi"), Player("body"), Player("flame"), Player("poison"), FromNpc("grey_wolf"), FromNpc("mountain_bandit"), FromNpc("training_dummy") };
        var outcomes = new Dictionary<string, int>();
        for (ulong seed = 1; seed <= 600; seed++)
        {
            var a = builds[(int)(seed % (ulong)builds.Length)];
            var b = builds[(int)(seed * 7 % (ulong)builds.Length)];
            var s = CombatEngine.Start(Config, a, b);
            while (s.Outcome is null)
            {
                var rng = CombatEngine.TurnRng(seed, s.Turn);
                var ia = CombatAi.Choose(Config, a, s.A, s.Distance, rng);
                var ib = CombatAi.Choose(Config, b, s.B, s.Distance, rng);
                Assert.Null(CombatEngine.Validate(Config, a, s.A, s.Distance, ia), "AI picks only legal intents");
                s = CombatEngine.ResolveTurn(Config, a, b, s, ia, ib, rng).State;
                foreach (var f in new[] { s.A, s.B })
                {
                    Assert.InRange(f.Hp, 0, f.MaxHp);
                    Assert.InRange(f.Qi, 0, f.MaxQi);
                    Assert.True(f.Cooldowns.All(cd => cd is >= 0 and <= 10));
                }

                Assert.InRange(s.Distance, Config.Distance.Min, Config.Distance.Max);
                Assert.True(s.Turn <= Config.MaxTurns);
            }

            outcomes[s.Outcome] = outcomes.GetValueOrDefault(s.Outcome) + 1;
        }

        Assert.True(outcomes.Count >= 2, "battles are not one-sided: " + string.Join(", ", outcomes));
    }
}
