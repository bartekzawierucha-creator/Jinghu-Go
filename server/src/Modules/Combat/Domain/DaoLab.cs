using System.Text.RegularExpressions;

namespace JianghuGo.Modules.Combat.Domain;

// Data model of content/game-config/dao-lab.json.

public sealed record DaoLabConfig(
    string RulesVersion,
    long DesignFeeStones,
    int MaxDesignsPerCharacter,
    DaoBudget Budget,
    DaoWeights Weights,
    DaoLimits Limits,
    ScrollRules Scroll);

public sealed record DaoBudget(IReadOnlyDictionary<string, int> Base, int PerQiCost, int PerCooldownTurn);

public sealed record DaoWeights(
    int Power, int RangeSpan, int Reach, int AccuracyDivisor, int Speed, int BleedPerMagnitudeTurn, int StunPerTurn, int WeakenDivisor,
    int BlockDivisor, int EvasionDivisor, int ReflectDivisor, int QiSteal);

public sealed record Bounds(int Min, int Max)
{
    public bool Contains(int v) => v >= Min && v <= Max;
    public override string ToString() => $"{Min}-{Max}";
}

public sealed record DaoLimits(
    Bounds Power, Bounds QiCost, Bounds Cooldown, Bounds Speed, Bounds AccuracyPermille, Bounds EffectChancePermille, Bounds EffectDuration,
    Bounds BleedMagnitude, Bounds WeakenMagnitude, int StunMaxChancePermille, int StunMaxDuration, int StunMinCooldown,
    Bounds BlockPermille, Bounds EvasionPermille, Bounds ReflectPermille, Bounds QiSteal);

public sealed record ScrollRules(string ItemId, long Stones, string Material, int MaterialQuantity);

/// <summary>What a player submits in the Dao Lab: approved components only, no id, path or names of official techniques.</summary>
public sealed record DaoDesign(
    string? Name,
    string? Slot,
    int QiCost,
    int Cooldown,
    int Speed,
    AttackSpec? Attack,
    DefenseSpec? Defense,
    MovementSpec? Movement,
    CounterSpec? Counter);

public sealed record DaoAssessment(int Points, int Allowance, IReadOnlyList<string> Problems)
{
    public bool Valid => Problems.Count == 0;
}

/// <summary>
/// Pure validation of player-designed techniques. Two layers: hard limits per component (no extreme values, stun cannot
/// chain) and a power budget — what a technique does costs points, what it demands (Qi, cooldown) buys points. The
/// weights are calibrated so every path-less official technique fits; path signatures may exceed it (unit test).
/// </summary>
public static partial class DaoLabRules
{
    [GeneratedRegex(@"^[\p{L}][\p{L} '\-]{1,31}$")]
    private static partial Regex NameFormat();

    private static readonly string[] ScalingStats = ["strength", "spirit", "agility"];

    public static int Points(DaoLabConfig c, string slot, int speed, AttackSpec? attack, DefenseSpec? defense, MovementSpec? movement, CounterSpec? counter)
    {
        var w = c.Weights;
        var p = speed * w.Speed;
        if (attack is { } a)
        {
            p += a.Power * w.Power + (a.RangeMax - a.RangeMin) * w.RangeSpan + (a.RangeMax - 1) * w.Reach + a.AccuracyPermille / w.AccuracyDivisor;
            if (a.Effect is { } e)
            {
                var value = e.Type switch
                {
                    EffectTypes.Bleed => e.Magnitude * e.Duration * w.BleedPerMagnitudeTurn,
                    EffectTypes.Stun => w.StunPerTurn * e.Duration,
                    _ => e.Magnitude / w.WeakenDivisor * e.Duration,
                };
                p += value * e.ChancePermille / 1000;
            }
        }

        if (defense is { } d) p += d.BlockPermille / w.BlockDivisor;
        if (movement is { } m) p += m.EvasionPermille / w.EvasionDivisor;
        if (counter is { } k) p += k.ReflectPermille / w.ReflectDivisor + k.QiSteal * w.QiSteal;
        return p;
    }

    public static int Allowance(DaoLabConfig c, string slot, int qiCost, int cooldown) =>
        c.Budget.Base.GetValueOrDefault(slot) + qiCost * c.Budget.PerQiCost + cooldown * c.Budget.PerCooldownTurn;

    public static int Points(DaoLabConfig c, TechniqueDefinition t) => Points(c, t.Slot, t.Speed, t.Attack, t.Defense, t.Movement, t.Counter);

    public static int Allowance(DaoLabConfig c, TechniqueDefinition t) => Allowance(c, t.Slot, t.QiCost, t.Cooldown);

    public static DaoAssessment Assess(DaoLabConfig c, CombatConfig combat, DaoDesign d)
    {
        var e = new List<string>();
        var l = c.Limits;
        if (d.Name is null || !NameFormat().IsMatch(d.Name.Trim())) e.Add("name: 2-32 letters, spaces, apostrophes or hyphens.");
        if (d.Slot is null || !SlotKinds.Layout.Contains(d.Slot)) return new DaoAssessment(0, 0, [.. e, "slot must be attack, defense, movement or counter."]);

        var specs = new object?[] { d.Attack, d.Defense, d.Movement, d.Counter }.Count(x => x is not null);
        var matching = d.Slot switch
        {
            SlotKinds.Attack => d.Attack is not null,
            SlotKinds.Defense => d.Defense is not null,
            SlotKinds.Movement => d.Movement is not null,
            _ => d.Counter is not null,
        };
        if (specs != 1 || !matching) e.Add($"a {d.Slot} technique needs exactly one '{d.Slot}' component.");
        if (!l.QiCost.Contains(d.QiCost)) e.Add($"qiCost must be {l.QiCost}.");
        if (!l.Cooldown.Contains(d.Cooldown)) e.Add($"cooldown must be {l.Cooldown}.");
        if (!l.Speed.Contains(d.Speed)) e.Add($"speed must be {l.Speed}.");

        if (d.Attack is { } a)
        {
            if (!l.Power.Contains(a.Power)) e.Add($"attack.power must be {l.Power}.");
            if (!ScalingStats.Contains(a.Scaling)) e.Add("attack.scaling must be strength, spirit or agility.");
            if (a.RangeMin < combat.Distance.Min || a.RangeMax > combat.Distance.Max || a.RangeMin > a.RangeMax)
                e.Add($"attack range must lie within {combat.Distance.Min}-{combat.Distance.Max} with rangeMin <= rangeMax.");
            if (!l.AccuracyPermille.Contains(a.AccuracyPermille)) e.Add($"attack.accuracyPermille must be {l.AccuracyPermille}.");
            if (a.Effect is { } fx)
            {
                if (!EffectTypes.Applicable.Contains(fx.Type)) e.Add("effect.type must be bleed, stun or weaken.");
                if (!l.EffectChancePermille.Contains(fx.ChancePermille)) e.Add($"effect.chancePermille must be {l.EffectChancePermille}.");
                if (!l.EffectDuration.Contains(fx.Duration)) e.Add($"effect.duration must be {l.EffectDuration}.");
                switch (fx.Type)
                {
                    case EffectTypes.Bleed when !l.BleedMagnitude.Contains(fx.Magnitude):
                        e.Add($"bleed magnitude must be {l.BleedMagnitude}.");
                        break;
                    case EffectTypes.Weaken when !l.WeakenMagnitude.Contains(fx.Magnitude):
                        e.Add($"weaken magnitude must be {l.WeakenMagnitude}.");
                        break;
                    case EffectTypes.Stun:
                        // Stun-lock prevention: rare, short, and never usable two turns in a row.
                        if (fx.Magnitude != 0) e.Add("stun magnitude must be 0.");
                        if (fx.ChancePermille > l.StunMaxChancePermille) e.Add($"stun chance must be at most {l.StunMaxChancePermille}‰.");
                        if (fx.Duration > l.StunMaxDuration) e.Add($"stun lasts at most {l.StunMaxDuration} turn(s).");
                        if (d.Cooldown < l.StunMinCooldown) e.Add($"a stunning technique needs a cooldown of at least {l.StunMinCooldown}.");
                        break;
                }
            }
        }

        if (d.Defense is { } def && !l.BlockPermille.Contains(def.BlockPermille)) e.Add($"defense.blockPermille must be {l.BlockPermille}.");
        if (d.Movement is { } mov && !l.EvasionPermille.Contains(mov.EvasionPermille)) e.Add($"movement.evasionPermille must be {l.EvasionPermille}.");
        if (d.Counter is { } ctr)
        {
            if (!l.ReflectPermille.Contains(ctr.ReflectPermille)) e.Add($"counter.reflectPermille must be {l.ReflectPermille}.");
            if (!l.QiSteal.Contains(ctr.QiSteal)) e.Add($"counter.qiSteal must be {l.QiSteal}.");
        }

        if (e.Count > 0) return new DaoAssessment(0, 0, e);
        var points = Points(c, d.Slot, d.Speed, d.Attack, d.Defense, d.Movement, d.Counter);
        var allowance = Allowance(c, d.Slot, d.QiCost, d.Cooldown);
        if (points > allowance) e.Add($"too powerful: {points} points exceed the allowance of {allowance} (raise the Qi cost or cooldown, or weaken a component).");
        return new DaoAssessment(points, allowance, e);
    }

    /// <summary>Technique ids of Dao designs: "dao_" + 16 letters a-p (64 random bits; letters only, as technique ids allow).</summary>
    public static string NewTechniqueId(ulong random) =>
        "dao_" + new string([.. Enumerable.Range(0, 16).Select(i => (char)('a' + (int)((random >> (i * 4)) & 0xF)))]);

    public static bool IsDaoId(string? id) => id is { Length: 20 } && id.StartsWith("dao_", StringComparison.Ordinal) && id[4..].All(ch => ch is >= 'a' and <= 'p');

    public static TechniqueDefinition ToDefinition(string id, DaoDesign d) =>
        new(id, d.Name!.Trim(), d.Name!.Trim(), d.Slot!, d.QiCost, d.Cooldown, d.Speed, null, d.Attack, d.Defense, d.Movement, d.Counter);
}

public static class DaoLabConfigValidator
{
    public static IReadOnlyList<string> Validate(DaoLabConfig? c)
    {
        if (c is null) return ["Config is missing."];
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(c.RulesVersion)) e.Add("rulesVersion is required.");
        if (c.DesignFeeStones < 0 || c.MaxDesignsPerCharacter is < 1 or > 50) e.Add("design fee/limit invalid.");
        if (c.Budget?.Base is null || SlotKinds.Layout.Distinct().Any(s => !c.Budget.Base.ContainsKey(s) || c.Budget.Base[s] < 0) || c.Budget.PerQiCost < 0 || c.Budget.PerCooldownTurn < 0)
            e.Add("budget needs a non-negative base for every slot kind.");
        var w = c.Weights;
        if (w is null || w.AccuracyDivisor < 1 || w.WeakenDivisor < 1 || w.BlockDivisor < 1 || w.EvasionDivisor < 1 || w.ReflectDivisor < 1
            || new[] { w.Power, w.RangeSpan, w.Reach, w.Speed, w.BleedPerMagnitudeTurn, w.StunPerTurn, w.QiSteal }.Any(x => x < 0))
            e.Add("weights invalid (divisors >= 1, weights >= 0).");
        var l = c.Limits;
        if (l is null || new[] { l.Power, l.QiCost, l.Cooldown, l.Speed, l.AccuracyPermille, l.EffectChancePermille, l.EffectDuration, l.BleedMagnitude,
                l.WeakenMagnitude, l.BlockPermille, l.EvasionPermille, l.ReflectPermille, l.QiSteal }.Any(b => b is null || b.Min > b.Max)
            || l.StunMaxDuration < 1 || l.StunMinCooldown < 1 || l.StunMaxChancePermille is < 0 or > 1000
            || l.Power.Min < 1 || l.EffectChancePermille.Max > 1000 || l.BlockPermille.Max >= 1000 || l.EvasionPermille.Max >= 1000 || l.ReflectPermille.Max > 1000)
            e.Add("limits invalid.");
        if (c.Scroll is null || string.IsNullOrWhiteSpace(c.Scroll.ItemId) || c.Scroll.Stones < 0 || c.Scroll.MaterialQuantity < 0) e.Add("scroll rules invalid.");
        return e;
    }
}
