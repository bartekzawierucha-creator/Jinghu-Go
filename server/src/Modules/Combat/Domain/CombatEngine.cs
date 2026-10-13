using JianghuGo.Domain;

namespace JianghuGo.Modules.Combat.Domain;

/// <summary>
/// A technique in a fighter's snapshot. <see cref="Definition"/> is set for Dao Lab (player-designed) techniques: their
/// rules are frozen into the battle, so the battle replays identically whatever happens to the design later.
/// </summary>
public sealed record EquippedTechnique(string Id, int Level, TechniqueDefinition? Definition = null);

/// <summary>Immutable snapshot of a fighter's build, frozen when the battle starts (later loadout changes do not leak in).</summary>
public sealed record Combatant(string Name, IReadOnlyDictionary<string, int> Stats, IReadOnlyList<EquippedTechnique?> Slots, int? HpOverride, int HpBonusPermille = 0,
    PathPerks? Perks = null)
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
    int Healed = 0);

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
        void Log(char actor, string action, string? tech = null, bool? hit = null, int dmg = 0, int reflected = 0, int stolen = 0, string? fx = null, int healed = 0) =>
            events.Add(new TurnEvent(actor.ToString(), action, tech, hit, dmg, reflected, stolen, fx, distance, Math.Max(0, a.Hp), Math.Max(0, b.Hp), a.Qi, b.Qi, healed));

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

        var initA = ca.Stat("agility") + Speed(ca, effA) + rng.NextInt(0, c.InitiativeRandom);
        var initB = cb.Stat("agility") + Speed(cb, effB) + rng.NextInt(0, c.InitiativeRandom);
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
                Log(side, "stunned");
                continue;
            }

            if (intent!.Slot is not { } slot)
            {
                me.Qi = Math.Min(me.MaxQi, me.Qi + c.Vitals.RestQiGain);
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
                if (sp.HealPermille > 0)
                {
                    var heal = me.MaxHp * (sp.HealPermille + (who.Perks?.HealPermille ?? 0)) / 1000;
                    healed = Math.Max(0, Math.Min(me.MaxHp, me.Hp + heal) - me.Hp);
                    me.Hp += healed;
                }

                if (sp.Power > 0)
                {
                    var (hit, damage, reflected, stolen, applied) = Strike(me, foe, who, foeWho, eq, sp.Power, sp.Scaling!, sp.AccuracyPermille, sp.Effect);
                    Log(side, "special", t.Id, hit: hit, dmg: damage, reflected: reflected, stolen: stolen, fx: applied, healed: healed);
                }
                else
                {
                    Log(side, "special", t.Id, healed: healed);
                }
            }
            else if (t.Attack is { } atk)
            {
                var (hit, damage, reflected, stolen, applied) = Strike(me, foe, who, foeWho, eq, atk.Power, atk.Scaling, atk.AccuracyPermille, atk.Effect);
                Log(side, "attack", t.Id, hit: hit, dmg: damage, reflected: reflected, stolen: stolen, fx: applied);
            }
        }

        // A strike (attack or special): hit roll, damage, counter-reflection, effect. Same RNG order as before 1.3.
        (bool Hit, int Damage, int Reflected, int Stolen, string? Applied) Strike(W me, W foe, Combatant who, Combatant foeWho, EquippedTechnique eq,
            int power, string scaling, int accuracy, EffectSpec? effect)
        {
            var chance = c.HitChance.BasePermille
                         + ((who.Stat("agility") - foeWho.Stat("agility")) * c.HitChance.AgilityDiffPermille)
                         + accuracy - foe.Sum(EffectTypes.Evasion);
            chance = Math.Clamp(chance, c.HitChance.MinPermille, c.HitChance.MaxPermille);
            var hit = rng.NextInt(0, 1000) < chance;
            var variance = rng.NextInt(-c.Damage.VariancePermille, c.Damage.VariancePermille + 1);
            if (!hit) return (false, 0, 0, 0, null);

            long dmg = power;
            dmg = dmg * (1000 + ((eq.Level - 1) * c.Damage.LevelScalingPermille)) / 1000;
            dmg = dmg * (1000 + (who.Stat(scaling) * c.Damage.StatScalingPermille)) / 1000;
            dmg = dmg * (1000 + (who.Perks?.DamageBonus(scaling) ?? 0)) / 1000;
            dmg = dmg * (1000 - me.Sum(EffectTypes.Weaken)) / 1000;
            dmg = dmg * (1000 + variance) / 1000;
            dmg = dmg * (1000 - foe.Sum(EffectTypes.Guard)) / 1000;
            var damage = (int)Math.Max(1, dmg);
            foe.Hp -= damage;

            int reflected = 0, stolen = 0;
            if (foe.Effects.FirstOrDefault(e => e.Type == EffectTypes.Counter) is { } counter)
            {
                reflected = damage * counter.Magnitude / 1000;
                me.Hp -= reflected;
                stolen = Math.Min(counter.Extra, me.Qi);
                me.Qi -= stolen;
                foe.Qi = Math.Min(foe.MaxQi, foe.Qi + stolen);
            }

            string? applied = null;
            if (effect is { } fx && foe.Hp > 0 && rng.NextInt(0, 1000) < Math.Min(1000, fx.ChancePermille + (who.Perks?.EffectChancePermille ?? 0)))
            {
                var magnitude = fx.Type == EffectTypes.Bleed ? fx.Magnitude * (1000 + (who.Perks?.BleedPermille ?? 0)) / 1000 : fx.Magnitude;
                foe.Effects.Add(new Effect(fx.Type, magnitude, 0, fx.Duration));
                applied = fx.Type;
            }

            return (true, damage, reflected, stolen, applied);
        }

        // 4. End of turn: bleeding, effect expiry, cooldowns, Qi regeneration.
        foreach (var (w, side) in new[] { (a, 'a'), (b, 'b') })
        {
            if (w.Hp <= 0) continue;
            var bleed = w.Effects.Where(e => e.Type == EffectTypes.Bleed).Sum(e => e.Magnitude);
            if (bleed > 0)
            {
                w.Hp -= bleed;
                Log(side, "bleed", dmg: bleed);
            }
        }

        foreach (var w in new[] { a, b })
        {
            for (var i = w.Effects.Count - 1; i >= 0; i--)
            {
                var e = w.Effects[i];
                if (e.Type is EffectTypes.Guard or EffectTypes.Evasion or EffectTypes.Counter)
                {
                    w.Effects.RemoveAt(i);
                }
                else if (e.Type != EffectTypes.Stun)
                {
                    w.Effects[i] = e with { TurnsLeft = e.TurnsLeft - 1 };
                    if (e.TurnsLeft - 1 <= 0) w.Effects.RemoveAt(i);
                }
            }

            for (var i = 0; i < 8; i++) w.Cd[i] = Math.Max(0, w.Cd[i] - 1);
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
    public static Intent Choose(CombatConfig c, Combatant self, Fighter me, int distance, DeterministicRandom rng)
    {
        if (me.IsStunned) return Intent.Rest;
        var options = new List<(Intent Intent, int Weight)> { (Intent.Rest, me.Qi < 10 ? 4 : 1) };
        for (var slot = 0; slot < 8; slot++)
        {
            if (self.Slots[slot] is not { } eq) continue;
            var candidate = new Intent(slot, null);
            if (CombatEngine.Validate(c, self, me, distance, candidate) is not null) continue;
            var t = c.Technique(eq);
            var weight = t.Attack is not null ? 6
                : t.Special is { } sp ? (sp.HealPermille > 0 && me.Hp * 2 < me.MaxHp ? 9 : sp.Power > 0 ? 7 : 3)
                : t.Movement is not null ? 2
                : 2;
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
}
