using JianghuGo.Domain;

namespace JianghuGo.Modules.Combat.Domain;

/// <summary>
/// A technique in a fighter's snapshot. <see cref="Definition"/> is set for Dao Lab (player-designed) techniques: their
/// rules are frozen into the battle, so the battle replays identically whatever happens to the design later.
/// </summary>
/// <param name="Awakening">1.7: the extra effect a level-10 technique awakened for this cultivator (null below the level).</param>
public sealed record EquippedTechnique(string Id, int Level, TechniqueDefinition? Definition = null, EffectSpec? Awakening = null);

/// <summary>Immutable snapshot of a fighter's build, frozen when the battle starts (later loadout changes do not leak in).</summary>
public sealed record Combatant(string Name, IReadOnlyDictionary<string, int> Stats, IReadOnlyList<EquippedTechnique?> Slots, int? HpOverride, int HpBonusPermille = 0,
    PathPerks? Perks = null, string? Element = null, string? Title = null, IReadOnlyList<string>? Badges = null, string? TitleRarity = null,
    string? Path = null, bool Enrages = false)
{
    public int Stat(string id) => Stats.GetValueOrDefault(id);
}

/// <summary>An active effect. <see cref="Extra"/> carries a second value (counter: Qi stolen).</summary>
public sealed record Effect(string Type, int Magnitude, int Extra, int TurnsLeft);

public sealed record Fighter(int Hp, int MaxHp, int Qi, int MaxQi, IReadOnlyList<int> Cooldowns, IReadOnlyList<Effect> Effects)
{
    public bool IsStunned => Effects.Any(e => e.Type == EffectTypes.Stun);
}

public static class Outcomes
{
    public const string AWon = "a_won";
    public const string BWon = "b_won";
    public const string Draw = "draw";
}

public sealed record BattleState(int Turn, int Distance, Fighter A, Fighter B, string? Outcome)
{
    public Fighter Of(char side) => side == 'a' ? A : B;
}

/// <summary>What a player wants to do. Slot null = rest (recover Qi). Direction is obsolete (there is no distance any more) and ignored.</summary>
public sealed record Intent(int? Slot, string? Direction)
{
    public static readonly Intent Rest = new(null, null);
}

public static class Directions
{
    public const string Advance = "advance";
    public const string Retreat = "retreat";
}

/// <summary>One step of the turn timeline, in execution order. Enough for the client to animate the side view.</summary>
public sealed record TurnEvent(
    string Actor,
    string Action,
    string? TechniqueId,
    bool? Hit,
    int Damage,
    int Reflected,
    int QiStolen,
    string? EffectApplied,
    int Distance,
    int HpA,
    int HpB,
    int QiA,
    int QiB,
    int Healed = 0,
    int Momentum = 0,
    bool Exploit = false,
    bool Crit = false,
    string? Mechanic = null);

/// <summary>
/// The whole combat ruleset as a pure function: (config, combatants, state, intents, rng) → (state', events).
/// No clock, no I/O, integer-only arithmetic. Given the stored battle seed and the recorded intents, every battle
/// can be replayed bit-for-bit (audit, dispute resolution, regression tests across rule changes).
/// </summary>
public static class CombatEngine
{
    public static DeterministicRandom TurnRng(ulong battleSeed, int turn) => new(battleSeed ^ ((ulong)turn * 0x9E3779B97F4A7C15UL));

    public static BattleState Start(CombatConfig c, Combatant a, Combatant b) =>
        new(1, c.Distance.Start, NewFighter(c, a), NewFighter(c, b), null);

    private static Fighter NewFighter(CombatConfig c, Combatant x)
    {
        var hp = x.HpOverride ?? c.Vitals.BaseHp + (x.Stat("constitution") * c.Vitals.HpPerConstitution);
        hp = hp * (1000 + x.HpBonusPermille) / 1000;
        var qi = c.Vitals.BaseQi + (x.Stat("spirit") * c.Vitals.QiPerSpirit);
        qi = qi * (1000 + (x.Perks?.QiPermille ?? 0)) / 1000;
        return new Fighter(hp, hp, qi, qi, [0, 0, 0, 0, 0, 0, 0, 0], []);
    }

    /// <summary>
    /// Null when the intent is legal right now; otherwise a stable error code. There is no distance: every attack reaches.
    /// (The distance parameter is kept for call-site compatibility and ignored.)
    /// </summary>
    public static string? Validate(CombatConfig c, Combatant who, Fighter f, int distance, Intent intent)
    {
        if (intent.Slot is not { } slot) return null;
        if (slot is < 0 or > 7) return "slot_invalid";
        if (who.Slots.ElementAtOrDefault(slot) is not { } equipped) return "slot_empty";
        var t = c.Technique(equipped);
        if (f.Cooldowns[slot] > 0) return "on_cooldown";
        if (f.Qi < t.QiCost) return "insufficient_qi";
        return null;
    }

    private sealed class W(Fighter f)
    {
        public int Hp = f.Hp;
        public readonly int MaxHp = f.MaxHp;
        public int Qi = f.Qi;
        public readonly int MaxQi = f.MaxQi;
        public readonly int[] Cd = [.. f.Cooldowns];
        public readonly List<Effect> Effects = [.. f.Effects];

        public int Sum(string type) => Math.Min(900, Effects.Where(e => e.Type == type).Sum(e => e.Magnitude));

        /// <summary>Momentum stacks (consecutive turns with a landed strike).</summary>
        public int Momentum => Effects.FirstOrDefault(e => e.Type == EffectTypes.Momentum)?.Magnitude ?? 0;

        public void SetMomentum(int stacks)
        {
            Effects.RemoveAll(e => e.Type == EffectTypes.Momentum);
            if (stacks > 0) Effects.Add(new Effect(EffectTypes.Momentum, stacks, 0, 1));
        }

        public bool Weakened => Effects.Any(e => EffectTypes.Ailments.Contains(e.Type));

        public int Count(string type) => Effects.Count(e => e.Type == type);

        public int Value(string type) => Effects.FirstOrDefault(e => e.Type == type)?.Magnitude ?? 0;

        public void SetValue(string type, int value)
        {
            Effects.RemoveAll(e => e.Type == type);
            if (value > 0) Effects.Add(new Effect(type, value, 0, 1));
        }

        /// <summary>Adds an effect; at the stack limit the oldest stack of that type is replaced (its duration refreshed).</summary>
        public void Add(Effect e, int maxStacks)
        {
            if (maxStacks > 0 && Count(e.Type) >= maxStacks)
            {
                var oldest = Effects.Where(x => x.Type == e.Type).OrderBy(x => x.TurnsLeft).First();
                Effects.Remove(oldest);
            }

            Effects.Add(e);
        }

        /// <summary>Heals, reduced while poisoned (1.7).</summary>
        public int Heal(int amount, int poisonBlockPermille)
        {
            if (amount <= 0 || Hp <= 0) return 0;
            if (Count(EffectTypes.Poison) > 0) amount = amount * (1000 - poisonBlockPermille) / 1000;
            var healed = Math.Max(0, Math.Min(MaxHp, Hp + amount) - Hp);
            Hp += healed;
            return healed;
        }
        public Fighter Freeze() => new(Math.Max(0, Hp), MaxHp, Qi, MaxQi, [.. Cd], [.. Effects]);
    }

    public static (BattleState State, IReadOnlyList<TurnEvent> Events) ResolveTurn(
        CombatConfig c, Combatant ca, Combatant cb, BattleState s, Intent ia, Intent ib, DeterministicRandom rng)
    {
        if (s.Outcome is not null) throw new InvalidOperationException("Battle is already finished.");

        var a = new W(s.A);
        var b = new W(s.B);
        var distance = s.Distance;
        var events = new List<TurnEvent>();
        void Log(char actor, string action, string? tech = null, bool? hit = null, int dmg = 0, int reflected = 0, int stolen = 0, string? fx = null, int healed = 0,
            bool exploit = false, bool crit = false, string? mechanic = null) =>
            events.Add(new TurnEvent(actor.ToString(), action, tech, hit, dmg, reflected, stolen, fx, distance, Math.Max(0, a.Hp), Math.Max(0, b.Hp), a.Qi, b.Qi, healed,
                (actor == 'a' ? a : b).Momentum, exploit, crit, mechanic));

        // 1. Effective intents: stun consumes the action; anything no longer legal degrades to rest (defence in depth).
        (Intent? Intent, bool Stunned) Effective(Combatant who, Fighter f, W w, Intent intent)
        {
            if (f.IsStunned)
            {
                w.Effects.RemoveAll(e => e.Type == EffectTypes.Stun);
                return (null, true);
            }

            return (Validate(c, who, f, distance, intent) is null ? intent : Intent.Rest, false);
        }

        var (effA, stunA) = Effective(ca, s.A, a, ia);
        var (effB, stunB) = Effective(cb, s.B, b, ib);

        // 2. Timeline order: stances/movement/rest before attacks; within a phase by initiative.
        int Priority(Combatant who, Intent? intent) =>
            intent?.Slot is { } slot && c.Technique(who.Slots[slot]!) is var t && (t.Attack is not null || t.Special is { Power: > 0 }) ? 1 : 0;
        int Speed(Combatant who, Intent? intent) => intent?.Slot is { } slot ? c.Technique(who.Slots[slot]!).Speed : 0;

        var initA = ca.Stat("agility") + Speed(ca, effA) + rng.NextInt(0, c.InitiativeRandom) + a.Sum(EffectTypes.Haste) - a.Sum(EffectTypes.Slow);
        var initB = cb.Stat("agility") + Speed(cb, effB) + rng.NextInt(0, c.InitiativeRandom) + b.Sum(EffectTypes.Haste) - b.Sum(EffectTypes.Slow);
        var (pA, pB) = (Priority(ca, effA), Priority(cb, effB));
        bool aFirst = pA != pB ? pA < pB : initA != initB ? initA > initB : rng.NextInt(0, 2) == 0;
        char[] order = aFirst ? ['a', 'b'] : ['b', 'a'];

        // 3. Execute.
        foreach (var side in order)
        {
            var (me, foe, who, foeWho, intent, stunned) = side == 'a' ? (a, b, ca, cb, effA, stunA) : (b, a, cb, ca, effB, stunB);
            if (me.Hp <= 0 || foe.Hp <= 0) break;

            if (stunned)
            {
                me.SetMomentum(0);
                me.SetValue(EffectTypes.Charge, 0); // a stun breaks the Qi charge
                Log(side, "stunned");
                continue;
            }

            if (intent!.Slot is not { } slot)
            {
                me.Qi = Math.Min(me.MaxQi, me.Qi + c.Vitals.RestQiGain);
                me.SetMomentum(0);
                if (c.Mechanics is { } restMech && who.Path == "qi") me.SetValue(EffectTypes.Charge, Math.Min(100, me.Value(EffectTypes.Charge) + restMech.ChargeOnRest));
                Log(side, "rest");
                continue;
            }

            var eq = who.Slots[slot]!;
            var t = c.Technique(eq);
            me.Qi -= t.QiCost;
            me.Cd[slot] = t.Cooldown + 1;

            if (t.Defense is { } d)
            {
                me.Effects.Add(new Effect(EffectTypes.Guard, Math.Min(900, d.BlockPermille + (who.Perks?.GuardPermille ?? 0)), 0, 1));
                if (c.Mechanics is { } guardMech && who.Path == "qi") me.SetValue(EffectTypes.Charge, Math.Min(100, me.Value(EffectTypes.Charge) + guardMech.ChargeOnRest));
                Log(side, "defend", t.Id);
            }
            else if (t.Movement is { } m)
            {
                // legacy movement technique = special skill granting evasion only
                me.Effects.Add(new Effect(EffectTypes.Evasion, m.EvasionPermille, 0, 1));
                Log(side, "special", t.Id);
            }
            else if (t.Counter is { } k)
            {
                me.Effects.Add(new Effect(EffectTypes.Counter, k.ReflectPermille, k.QiSteal, 1));
                Log(side, "counter_stance", t.Id);
            }
            else if (t.Special is { } sp)
            {
                if (sp.EvasionPermille > 0) me.Effects.Add(new Effect(EffectTypes.Evasion, sp.EvasionPermille, 0, 1));
                if (sp.GuardPermille > 0) me.Effects.Add(new Effect(EffectTypes.Guard, Math.Min(900, sp.GuardPermille + (who.Perks?.GuardPermille ?? 0)), 0, 1));
                var healed = 0;
                if (sp.HealPermille > 0) healed = me.Heal(me.MaxHp * (sp.HealPermille + (who.Perks?.HealPermille ?? 0)) / 1000, c.PoisonHealBlockPermille);
                if (sp.SelfEffect is { } self) ApplySelf(me, self);

                if (sp.Power > 0)
                {
                    var (hit, damage, reflected, stolen, applied, exploit, crit, mechanic) = Strike(me, foe, who, foeWho, eq, sp.Power, sp.Scaling!, sp.AccuracyPermille, sp.Effect);
                    Log(side, "special", t.Id, hit: hit, dmg: damage, reflected: reflected, stolen: stolen, fx: applied, healed: healed, exploit: exploit, crit: crit, mechanic: mechanic);
                }
                else
                {
                    Log(side, "special", t.Id, healed: healed);
                }
            }
            else if (t.Attack is { } atk)
            {
                var (hit, damage, reflected, stolen, applied, exploit, crit, mechanic) = Strike(me, foe, who, foeWho, eq, atk.Power, atk.Scaling, atk.AccuracyPermille, atk.Effect, atk.Extra);
                Log(side, "attack", t.Id, hit: hit, dmg: damage, reflected: reflected, stolen: stolen, fx: applied, exploit: exploit, crit: crit, mechanic: mechanic);
            }
        }

        // A strike (attack or special): hit roll, damage, counter-reflection, effect. Same RNG order as before 1.3.
        // Combo rules (1.4): every momentum stack adds damage; striking a bleeding/weakened/stunned foe exploits the opening.
        // A landed strike builds momentum, a miss loses it (as do resting and being stunned).
        // 1.6: the agility edge is relative, Focus (vs Willpower) gives critical strikes, Willpower (vs Focus) resists effects,
        // Elemental Affinity weights the element modifier and bleeding grows with the attacker's stat like damage does.
        (bool Hit, int Damage, int Reflected, int Stolen, string? Applied, bool Exploit, bool Crit, string? Mechanic) Strike(W me, W foe, Combatant who, Combatant foeWho, EquippedTechnique eq,
            int power, string scaling, int accuracy, EffectSpec? effect, EffectSpec? extra = null)
        {
            var chance = c.HitChance.BasePermille
                         + c.HitChance.AgilityTerm(who.Stat("agility"), foeWho.Stat("agility"))
                         + accuracy - foe.Sum(EffectTypes.Evasion);
            chance = Math.Clamp(chance, c.HitChance.MinPermille, c.HitChance.MaxPermille);
            var hit = rng.NextInt(0, 1000) < chance;
            var variance = rng.NextInt(-c.Damage.VariancePermille, c.Damage.VariancePermille + 1);
            if (!hit)
            {
                me.SetMomentum(0);
                return (false, 0, 0, 0, null, false, false, null);
            }

            var mech = c.Mechanics;
            string? mechanic = null;
            var combo = c.Combo;
            var exploit = combo is not null && combo.ExploitPermille > 0 && foe.Weakened;
            long dmg = power;
            dmg = dmg * (1000 + ((eq.Level - 1) * c.Damage.LevelScalingPermille)) / 1000;
            dmg = dmg * (1000 + (who.Stat(scaling) * c.Damage.StatScalingPermille)) / 1000;
            dmg = dmg * (1000 + (who.Perks?.DamageBonus(scaling) ?? 0)) / 1000;
            if (combo is not null) dmg = dmg * (1000 + (me.Momentum * combo.MomentumPermillePerStack) + (exploit ? combo.ExploitPermille : 0)) / 1000;
            if (c.Elements is { } el)
                dmg = dmg * (1000 + el.Modifier(el.OfTechnique(c.Technique(eq).Id) ?? who.Element, foeWho.Element, who.Stat("elementalAffinity"), foeWho.Stat("elementalAffinity"))) / 1000;
            if (s.Turn == 1 && (who.Perks?.FirstStrikePermille ?? 0) > 0) dmg = dmg * (1000 + who.Perks!.FirstStrikePermille) / 1000;
            dmg = dmg * (1000 - me.Sum(EffectTypes.Weaken) + me.Sum(EffectTypes.Empower)) / 1000;

            // path mechanics on the attacker's side (1.7)
            var piercing = false;
            if (mech is not null && who.Path == "qi" && scaling == "spirit" && me.Value(EffectTypes.Charge) >= 100)
            {
                dmg = dmg * (1000 + mech.ChargeDamagePermille) / 1000;
                piercing = true;
                me.SetValue(EffectTypes.Charge, 0);
                var buff = foe.Effects.FirstOrDefault(e => EffectTypes.Buffs.Contains(e.Type));
                if (buff is not null) foe.Effects.Remove(buff);
                mechanic = "charge";
            }

            if (mech is not null && who.Path == "body") dmg = dmg * (1000 + (me.Value(EffectTypes.Fury) * mech.FuryDamagePermille)) / 1000;

            dmg = dmg * (1000 + variance) / 1000;
            var crit = c.Crit is { } cr && rng.NextInt(0, 1000) < cr.ChancePermille(who.Stat("focus"), foeWho.Stat("willpower"));
            if (crit) dmg = dmg * (1000 + c.Crit!.DamagePermille) / 1000;

            // the defender's side: defence, fury, frailty, poison bloom, fortify, guard (scorched or pierced guards do not hold)
            if (c.Defense is { } def) dmg = dmg * (1000 - def.ReductionPermille(foeWho.Stat("constitution"), foeWho.Stat("willpower"), who.Stat(scaling))) / 1000;
            if (mech is not null && foeWho.Path == "body") dmg = dmg * (1000 - Math.Min(600, foe.Value(EffectTypes.Fury) * mech.FuryReductionPermille)) / 1000;
            var blossom = mech is not null && EffectTypes.Toxins.Count(t => foe.Count(t) > 0) >= 3 ? mech.BlossomPermille : 0;
            dmg = dmg * (1000 + foe.Sum(EffectTypes.Frail) + blossom - foe.Sum(EffectTypes.Fortify)) / 1000;
            if (!piercing && foe.Count(EffectTypes.Scorched) == 0) dmg = dmg * (1000 - foe.Sum(EffectTypes.Guard)) / 1000;
            var damage = (int)Math.Max(1, dmg);

            // a shield soaks the blow first
            if (foe.Effects.FirstOrDefault(e => e.Type == EffectTypes.Shield) is { } shield)
            {
                var soaked = Math.Min(shield.Magnitude, damage);
                foe.Effects.Remove(shield);
                if (shield.Magnitude > soaked) foe.Effects.Add(shield with { Magnitude = shield.Magnitude - soaked });
                damage -= soaked;
            }

            foe.Hp -= damage;
            Survive(foe, foeWho);
            if (mech is not null && foeWho.Path == "body" && damage > 0) foe.SetValue(EffectTypes.Fury, Math.Min(mech.FuryMaxStacks, foe.Value(EffectTypes.Fury) + 1));
            if ((who.Perks?.LifestealPermille ?? 0) > 0) me.Heal(damage * who.Perks!.LifestealPermille / 1000, c.PoisonHealBlockPermille);
            if ((foeWho.Perks?.ReflectPermille ?? 0) > 0 && foe.Hp > 0)
            {
                me.Hp -= damage * foeWho.Perks!.ReflectPermille / 1000;
                Survive(me, who);
            }

            int reflected = 0, stolen = 0;
            if (foe.Effects.FirstOrDefault(e => e.Type == EffectTypes.Counter) is { } counter)
            {
                var fullFury = mech is not null && foeWho.Path == "body" && foe.Value(EffectTypes.Fury) >= mech.FuryMaxStacks;
                reflected = damage * counter.Magnitude / 1000 * (fullFury ? 2 : 1);
                me.Hp -= reflected;
                stolen = Math.Min(counter.Extra, me.Qi);
                me.Qi -= stolen;
                foe.Qi = Math.Min(foe.MaxQi, foe.Qi + stolen);
                if (fullFury) mechanic = "fury";
            }

            string? applied = null;
            var resist = c.Resist?.ResistPermille(foeWho.Stat("willpower"), who.Stat("focus")) ?? 0;
            var milestone = c.Milestones is { } ms && eq.Level >= 5 ? ms.Level5EffectPermille : 0;
            if (effect is { } fx && foe.Hp > 0 && rng.NextInt(0, 1000) < Math.Min(1000, fx.ChancePermille + milestone + (who.Perks?.EffectChancePermille ?? 0)) * (1000 - resist) / 1000)
            {
                Apply(me, foe, who, fx, scaling);
                applied = fx.Type;
            }

            if (extra is { } ex && foe.Hp > 0 && rng.NextInt(0, 1000) < Math.Min(1000, ex.ChancePermille + milestone) * (1000 - resist) / 1000)
            {
                Apply(me, foe, who, ex, scaling);
                applied ??= ex.Type;
            }

            // an awakened technique (level 10) carries a second effect
            if (eq.Awakening is { } aw && foe.Hp > 0 && rng.NextInt(0, 1000) < aw.ChancePermille * (1000 - resist) / 1000)
            {
                Apply(me, foe, who, aw, scaling);
                applied ??= aw.Type;
            }

            // Flame: embers pile up and burst; Poison: a random toxin
            if (mech is not null && foe.Hp > 0 && who.Path == "flame" && rng.NextInt(0, 1000) < mech.EmberChancePermille * (1000 - resist) / 1000)
            {
                Apply(me, foe, who, new EffectSpec(EffectTypes.Burn, 1000, mech.EmberMagnitude, 3), scaling, mech.EmberStacks);
                if (foe.Count(EffectTypes.Burn) >= mech.EmberStacks)
                {
                    var burst = Math.Max(1, (int)((long)foe.MaxHp * mech.EmberBurstPermille / 1000));
                    foe.Hp -= burst;
                    Survive(foe, foeWho);
                    foe.Effects.RemoveAll(e => e.Type == EffectTypes.Burn);
                    foe.Effects.Add(new Effect(EffectTypes.Scorched, 1, 0, 1));
                    damage += burst;
                    mechanic = "ember_burst";
                }
            }
            else if (mech is not null && foe.Hp > 0 && who.Path == "poison" && rng.NextInt(0, 1000) < mech.ToxinChancePermille * (1000 - resist) / 1000)
            {
                var toxin = EffectTypes.Toxins[rng.NextInt(0, EffectTypes.Toxins.Count)];
                var magnitude = toxin == EffectTypes.Slow ? 10 : toxin == EffectTypes.Poison ? mech.ToxinMagnitude : 100;
                Apply(me, foe, who, new EffectSpec(toxin, 1000, magnitude, mech.ToxinDuration), scaling);
                if (EffectTypes.Toxins.Count(t => foe.Count(t) > 0) >= 3) mechanic = "blossom";
            }

            if (combo is not null) me.SetMomentum(Math.Min(combo.MaxMomentum, me.Momentum + 1));
            return (true, damage, reflected, stolen, applied, exploit, crit, mechanic);
        }

        // Puts an effect on the target (or acts at once: dispel, drain); damage over time grows with the attacker's stat.
        void Apply(W me, W foe, Combatant who, EffectSpec fx, string scaling, int? maxStacks = null)
        {
            switch (fx.Type)
            {
                case EffectTypes.Dispel:
                    var buff = foe.Effects.FirstOrDefault(e => EffectTypes.Buffs.Contains(e.Type));
                    if (buff is not null) foe.Effects.Remove(buff);
                    return;
                case EffectTypes.Drain:
                    var taken = Math.Min(fx.Magnitude, foe.Qi);
                    foe.Qi -= taken;
                    me.Qi = Math.Min(me.MaxQi, me.Qi + taken);
                    return;
                case EffectTypes.Shield or EffectTypes.Empower or EffectTypes.Fortify or EffectTypes.Haste or EffectTypes.Cleanse:
                    ApplySelf(me, fx);
                    return;
            }

            var magnitude = fx.Type == EffectTypes.Bleed ? fx.Magnitude * (1000 + (who.Perks?.BleedPermille ?? 0)) / 1000 : fx.Magnitude;
            if (EffectTypes.DamageOverTime.Contains(fx.Type) && c.Damage.BleedStatScalingPermille > 0)
                magnitude = (int)((long)magnitude * (1000 + ((long)who.Stat(scaling) * c.Damage.BleedStatScalingPermille)) / 1000); // grows with the stat (1.6)
            if (fx.Type == EffectTypes.Burn) magnitude = magnitude * (1000 + (who.Perks?.BleedPermille ?? 0)) / 1000; // the Flame path's burn perk
            foe.Add(new Effect(fx.Type, magnitude, 0, fx.Duration), maxStacks ?? (fx.Type == EffectTypes.Stun ? 1 : c.MaxEffectStacks));
        }

        // Effects a fighter puts on itself (special skills, awakenings): shields are stored as HP points.
        void ApplySelf(W me, EffectSpec fx)
        {
            if (fx.Type == EffectTypes.Cleanse)
            {
                var ailment = me.Effects.FirstOrDefault(e => EffectTypes.Ailments.Contains(e.Type));
                if (ailment is not null) me.Effects.Remove(ailment);
                return;
            }

            var magnitude = fx.Type == EffectTypes.Shield ? (int)((long)me.MaxHp * fx.Magnitude / 1000) : fx.Magnitude;
            me.Add(new Effect(fx.Type, magnitude, 0, fx.Duration), fx.Type == EffectTypes.Shield ? 1 : c.MaxEffectStacks);
        }

        // "survive once" perk: the first lethal blow of the battle leaves 1 HP (remembered as a permanent effect)
        static void Survive(W w, Combatant who)
        {
            if (w.Hp > 0 || (who.Perks?.SurviveOnce ?? 0) == 0 || w.Effects.Any(e => e.Type == EffectTypes.Survived)) return;
            w.Hp = 1;
            w.Effects.Add(new Effect(EffectTypes.Survived, 1, 0, 1));
        }

        // spirit pets strike on their own every few turns; regeneration perks heal at the end of the turn
        foreach (var (w, foeW, who, side) in new[] { (a, b, ca, 'a'), (b, a, cb, 'b') })
        {
            if (w.Hp <= 0 || foeW.Hp <= 0) continue;
            if (who.Perks is { PetPower: > 0, PetEvery: > 0 } pp && s.Turn % pp.PetEvery == 0)
            {
                foeW.Hp -= pp.PetPower;
                Log(side, "pet", dmg: pp.PetPower);
            }
        }

        foreach (var (w, who, side) in new[] { (a, ca, 'a'), (b, cb, 'b') })
        {
            if (w.Hp <= 0 || (who.Perks?.RegenPermille ?? 0) <= 0) continue;
            var heal = w.Heal(Math.Max(1, w.MaxHp * who.Perks!.RegenPermille / 1000), c.PoisonHealBlockPermille);
            if (heal <= 0) continue;
            Log(side, "regen", healed: heal);
        }

        // 4. End of turn: bleeding, effect expiry, cooldowns, Qi regeneration.
        foreach (var (w, side) in new[] { (a, 'a'), (b, 'b') })
        {
            if (w.Hp <= 0) continue;
            foreach (var dot in EffectTypes.DamageOverTime)
            {
                var ticks = w.Effects.Where(e => e.Type == dot).Sum(e => e.Magnitude);
                if (ticks <= 0) continue;
                w.Hp -= ticks;
                Log(side, dot, dmg: ticks); // "bleed" / "burn" / "poison"
            }
        }

        foreach (var w in new[] { a, b })
        {
            for (var i = w.Effects.Count - 1; i >= 0; i--)
            {
                var e = w.Effects[i];
                if (e.Type is EffectTypes.Guard or EffectTypes.Evasion or EffectTypes.Counter or EffectTypes.Scorched)
                {
                    w.Effects.RemoveAt(i);
                }
                else if (!EffectTypes.Persistent.Contains(e.Type))
                {
                    w.Effects[i] = e with { TurnsLeft = e.TurnsLeft - 1 };
                    if (e.TurnsLeft - 1 <= 0) w.Effects.RemoveAt(i);
                }
            }

            for (var i = 0; i < 8; i++) w.Cd[i] = Math.Max(0, w.Cd[i] - 1);
            var owner = ReferenceEquals(w, a) ? ca : cb;
            if (w.Hp > 0 && c.Mechanics is { } turnMech && owner.Path == "qi")
                w.SetValue(EffectTypes.Charge, Math.Min(100, w.Value(EffectTypes.Charge) + turnMech.ChargePerTurn));
            // bosses enter a second phase at half health: stronger blows for the rest of the battle
            if (w.Hp > 0 && owner.Enrages && c.EnragePermille > 0 && w.Hp * 2 < w.MaxHp && w.Count(EffectTypes.Enraged) == 0)
            {
                w.Effects.Add(new Effect(EffectTypes.Enraged, 1, 0, 1));
                w.Effects.Add(new Effect(EffectTypes.Empower, c.EnragePermille, 0, 99));
            }
            if (w.Hp > 0) w.Qi = Math.Min(w.MaxQi, w.Qi + c.Vitals.QiRegenPerTurn + ((ReferenceEquals(w, a) ? ca : cb).Perks?.QiRegen ?? 0));
        }

        // 5. Outcome.
        string? outcome = (a.Hp <= 0, b.Hp <= 0) switch
        {
            (true, true) => Outcomes.Draw,
            (true, false) => Outcomes.BWon,
            (false, true) => Outcomes.AWon,
            _ => null,
        };
        if (outcome is null && s.Turn >= c.MaxTurns)
        {
            var (ra, rb) = ((long)a.Hp * 1000 / a.MaxHp, (long)b.Hp * 1000 / b.MaxHp);
            outcome = ra > rb ? Outcomes.AWon : rb > ra ? Outcomes.BWon : Outcomes.Draw;
            Log(ra >= rb ? 'a' : 'b', "time_limit");
        }

        return (new BattleState(outcome is null ? s.Turn + 1 : s.Turn, distance, a.Freeze(), b.Freeze(), outcome), events);
    }
}

/// <summary>Server-side opponent for PvE. Deterministic for a given RNG, so NPC choices replay exactly too.</summary>
public static class CombatAi
{
    /// <summary>Weighted random choice among legal actions: attacks first, the special skill when it pays off, guards now and then.</summary>
    public static Intent Choose(CombatConfig c, Combatant self, Fighter me, int distance, DeterministicRandom rng, Combatant? foe = null, Fighter? foeState = null)
    {
        if (me.IsStunned) return Intent.Rest;
        var options = new List<(Intent Intent, int Weight)> { (Intent.Rest, me.Qi < 10 ? 4 : 1) };
        var foeBuffed = foeState?.Effects.Any(e => EffectTypes.Buffs.Contains(e.Type)) == true;
        var charged = self.Path == "qi" && me.Effects.Any(e => e.Type == EffectTypes.Charge && e.Magnitude >= 100);
        var furious = c.Mechanics is { } m && self.Path == "body" && me.Effects.Any(e => e.Type == EffectTypes.Fury && e.Magnitude >= m.FuryMaxStacks);
        for (var slot = 0; slot < 8; slot++)
        {
            if (self.Slots[slot] is not { } eq) continue;
            var candidate = new Intent(slot, null);
            if (CombatEngine.Validate(c, self, me, distance, candidate) is not null) continue;
            var t = c.Technique(eq);
            var weight = t.Attack is not null ? 6
                : t.Special is { } sp ? (sp.HealPermille > 0 && me.Hp * 2 < me.MaxHp ? 9 : sp.SelfEffect?.Type == EffectTypes.Shield && me.Hp * 5 < me.MaxHp * 3 ? 8 : sp.Power > 0 ? 7 : 3)
                : t.Movement is not null ? 2
                : 2;
            // 1.7: read the fight — strike with the overcoming element, strip buffs, spend a full charge, counter in full fury
            var effect = t.Attack?.Effect ?? t.Special?.Effect;
            if (foe is not null && c.Elements is { } el && (t.Attack is not null || t.Special is { Power: > 0 })
                && el.Modifier(el.OfTechnique(t.Id) ?? self.Element, foe.Element) > 0) weight += 3;
            if (foeBuffed && effect?.Type == EffectTypes.Dispel) weight += 6;
            if (charged && (t.Attack?.Scaling ?? t.Special?.Scaling) == "spirit") weight += 6;
            if (furious && t.Counter is not null) weight += 5;
            options.Add((candidate, weight));
        }

        var total = options.Sum(o => o.Weight);
        var pick = rng.NextInt(0, total);
        foreach (var (intent, weight) in options)
        {
            pick -= weight;
            if (pick < 0) return intent;
        }

        return Intent.Rest;
    }

    /// <summary>
    /// Auto-battle for a player: the same legal options, weighted against what the opponent is about to do (the
    /// telegraphed intent): guard or counter an incoming blow, strike hard into a resting or stunned foe, gather Qi
    /// while the foe only defends.
    /// </summary>
    public static Intent ChooseAgainst(CombatConfig c, Combatant self, Fighter me, int distance, IntentPreview? foe, DeterministicRandom rng)
    {
        if (me.IsStunned) return Intent.Rest;
        var foeAction = foe?.Action;
        var options = new List<(Intent Intent, int Weight)> { (Intent.Rest, me.Qi < 10 ? 6 : foeAction == "defend" ? 3 : 1) };
        for (var slot = 0; slot < 8; slot++)
        {
            if (self.Slots[slot] is not { } eq) continue;
            var candidate = new Intent(slot, null);
            if (CombatEngine.Validate(c, self, me, distance, candidate) is not null) continue;
            var t = c.Technique(eq);
            var weight = (t.Kind, foeAction) switch
            {
                (SlotKinds.Attack, "rest" or "stunned") => 10,
                (SlotKinds.Attack, "defend") => 2,
                (SlotKinds.Attack, "counter") => 1,
                (SlotKinds.Attack, _) => 6,
                (SlotKinds.Defense, "attack" or "special") => 5,
                (SlotKinds.Defense, _) => 1,
                (SlotKinds.Counter, "attack") => 8,
                (SlotKinds.Counter, _) => 1,
                (SlotKinds.Special, _) when t.Special is { HealPermille: > 0 } && me.Hp * 2 < me.MaxHp => 10,
                (SlotKinds.Special, "rest" or "stunned") when t.Special is { Power: > 0 } => 9,
                (SlotKinds.Special, "counter") when t.Special is { Power: > 0 } => 1,
                (SlotKinds.Special, _) when t.Special is { Power: > 0 } => 6,
                _ => 2,
            };
            options.Add((candidate, weight));
        }

        var total = options.Sum(o => o.Weight);
        var pick = rng.NextInt(0, total);
        foreach (var (intent, weight) in options)
        {
            pick -= weight;
            if (pick < 0) return intent;
        }

        return Intent.Rest;
    }

    /// <summary>What an intent will do, as shown to the other side (telegraph): action kind and technique.</summary>
    public static IntentPreview Preview(CombatConfig c, Combatant who, Fighter f, Intent intent)
    {
        if (f.IsStunned) return new IntentPreview("stunned", null, null);
        if (intent.Slot is not { } slot || who.Slots.ElementAtOrDefault(slot) is not { } eq) return new IntentPreview("rest", null, null);
        var t = c.Technique(eq);
        var action = t.Kind switch
        {
            SlotKinds.Attack => "attack",
            SlotKinds.Defense => "defend",
            SlotKinds.Counter => "counter",
            _ => "special",
        };
        return new IntentPreview(action, t.Id, t.NamePl);
    }
}

/// <summary>A telegraphed action: attack / defend / special / counter / rest / stunned, with the technique when there is one.</summary>
public sealed record IntentPreview(string Action, string? TechniqueId, string? NamePl);
