namespace JianghuGo.Modules.Combat.Domain;

// Data model of content/game-config/combat.json. Fractions are integers in permille (‰) so the engine
// is pure integer arithmetic: identical results on every platform and every replay.

public sealed record CombatConfig(
    string RulesVersion,
    int MaxTurns,
    int TurnDeadlineSeconds,
    int ChallengeExpirySeconds,
    DistanceRules Distance,
    VitalsRules Vitals,
    int InitiativeRandom,
    IReadOnlyDictionary<string, int> PathHpBonusPermille,
    HitChanceRules HitChance,
    DamageRules Damage,
    ProgressionRules Progression,
    IReadOnlyList<string> Slots,
    IReadOnlyList<TechniqueDefinition> Techniques,
    IReadOnlyDictionary<string, IReadOnlyList<string>> StarterLoadouts,
    IReadOnlyList<NpcDefinition> Npcs)
{
    public TechniqueDefinition Technique(string id) =>
        Techniques.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException($"Unknown technique '{id}'.");

    /// <summary>Rules of an equipped technique: its frozen Dao Lab definition, or the official one.</summary>
    public TechniqueDefinition Technique(EquippedTechnique equipped) => equipped.Definition ?? Technique(equipped.Id);

    public NpcDefinition? Npc(string id) => Npcs.FirstOrDefault(n => n.Id == id);

    /// <summary>Techniques a character of this path knows: all path-less ones plus its path's signature technique(s).</summary>
    public IEnumerable<TechniqueDefinition> KnownBy(string path) => Techniques.Where(t => t.Path is null || t.Path == path);
}

public sealed record DistanceRules(int Start, int Min, int Max);

public sealed record VitalsRules(int BaseHp, int HpPerConstitution, int BaseQi, int QiPerSpirit, int QiRegenPerTurn, int RestQiGain);

public sealed record HitChanceRules(int BasePermille, int AgilityDiffPermille, int MinPermille, int MaxPermille);

public sealed record DamageRules(int VariancePermille, int StatScalingPermille, int LevelScalingPermille);

public sealed record ProgressionRules(int MaxTechniqueLevel, int XpPerUse, int XpPerLevel, int DailyRewardedPveWins);

public sealed record TechniqueDefinition(
    string Id,
    string NamePl,
    string NameEn,
    string Slot,
    int QiCost,
    int Cooldown,
    int Speed,
    string? Path,
    AttackSpec? Attack,
    DefenseSpec? Defense,
    MovementSpec? Movement,
    CounterSpec? Counter);

public sealed record AttackSpec(int Power, string Scaling, int RangeMin, int RangeMax, int AccuracyPermille, EffectSpec? Effect);

public sealed record EffectSpec(string Type, int ChancePermille, int Magnitude, int Duration);

public sealed record DefenseSpec(int BlockPermille);

public sealed record MovementSpec(int EvasionPermille);

public sealed record CounterSpec(int ReflectPermille, int QiSteal);

public sealed record NpcDefinition(
    string Id,
    string NamePl,
    string NameEn,
    int? HpOverride,
    int RewardQi,
    IReadOnlyDictionary<string, int> Stats,
    IReadOnlyList<NpcSlot?> Loadout);

public sealed record NpcSlot(string Id, int Level);

public static class SlotKinds
{
    public const string Attack = "attack";
    public const string Defense = "defense";
    public const string Movement = "movement";
    public const string Counter = "counter";
    public static readonly IReadOnlyList<string> Layout = [Attack, Attack, Attack, Attack, Defense, Defense, Movement, Counter];
}

public static class EffectTypes
{
    public const string Bleed = "bleed";     // magnitude = damage per turn
    public const string Stun = "stun";       // lose the next action
    public const string Weaken = "weaken";   // magnitude = ‰ less outgoing damage
    public const string Guard = "guard";     // this turn: magnitude = ‰ damage blocked
    public const string Evasion = "evasion"; // this turn: magnitude = ‰ hit chance removed
    public const string Counter = "counter"; // this turn: magnitude = ‰ reflected, second value = qi steal
    public static readonly IReadOnlyList<string> Applicable = [Bleed, Stun, Weaken];
}

public static class CombatConfigValidator
{
    private static readonly string[] Paths = ["qi", "body", "flame", "poison"];
    private static readonly string[] ScalingStats = ["strength", "spirit", "agility"];

    public static IReadOnlyList<string> Validate(CombatConfig? c)
    {
        if (c is null) return ["Config is missing."];
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(c.RulesVersion)) e.Add("rulesVersion is required.");
        if (c.MaxTurns is < 5 or > 200) e.Add("maxTurns must be 5-200.");
        if (c.TurnDeadlineSeconds is < 10 or > 3600) e.Add("turnDeadlineSeconds must be 10-3600.");
        if (c.ChallengeExpirySeconds is < 30 or > 86400) e.Add("challengeExpirySeconds must be 30-86400.");
        if (c.Distance is null || !(1 <= c.Distance.Min && c.Distance.Min <= c.Distance.Start && c.Distance.Start <= c.Distance.Max && c.Distance.Max <= 10))
            e.Add("distance must satisfy 1 <= min <= start <= max <= 10.");
        if (c.Vitals is null || c.Vitals.BaseHp < 1 || c.Vitals.HpPerConstitution < 0 || c.Vitals.BaseQi < 0 || c.Vitals.QiPerSpirit < 0 || c.Vitals.QiRegenPerTurn < 0 || c.Vitals.RestQiGain < 0)
            e.Add("vitals invalid.");
        if (c.InitiativeRandom is < 1 or > 100) e.Add("initiativeRandom must be 1-100.");
        if (c.PathHpBonusPermille is null || !c.PathHpBonusPermille.Keys.Order().SequenceEqual(Paths.Order()) || c.PathHpBonusPermille.Values.Any(v => v is < 0 or > 1000))
            e.Add("pathHpBonusPermille must give 0-1000 for every path.");
        if (c.HitChance is null || !(0 < c.HitChance.MinPermille && c.HitChance.MinPermille <= c.HitChance.BasePermille && c.HitChance.BasePermille <= c.HitChance.MaxPermille && c.HitChance.MaxPermille < 1000))
            e.Add("hitChance must satisfy 0 < min <= base <= max < 1000 (no guaranteed hits or misses).");
        if (c.Damage is null || c.Damage.VariancePermille is < 0 or > 500 || c.Damage.StatScalingPermille < 0 || c.Damage.LevelScalingPermille < 0)
            e.Add("damage rules invalid.");
        if (c.Progression is null || c.Progression.MaxTechniqueLevel != 10 || c.Progression.XpPerUse < 1 || c.Progression.XpPerLevel < 1 || c.Progression.DailyRewardedPveWins < 0)
            e.Add("progression invalid (techniques have levels 1-10).");
        if (c.Slots is null || !c.Slots.SequenceEqual(SlotKinds.Layout))
            e.Add("slots must be 4 attack, 2 defense, 1 movement, 1 counter (in that order).");

        if (c.Techniques is null || c.Techniques.Count == 0)
        {
            e.Add("techniques are required.");
            return e;
        }

        foreach (var d in c.Techniques.GroupBy(t => t.Id).Where(g => g.Count() > 1)) e.Add($"duplicate technique '{d.Key}'.");
        foreach (var t in c.Techniques)
        {
            var specs = new object?[] { t.Attack, t.Defense, t.Movement, t.Counter }.Count(x => x is not null);
            if (specs != 1) e.Add($"technique {t.Id}: exactly one of attack/defense/movement/counter is required.");
            if (t.QiCost is < 0 or > 100 || t.Cooldown is < 0 or > 10 || t.Speed is < -10 or > 10) e.Add($"technique {t.Id}: qiCost 0-100, cooldown 0-10, speed -10..10.");
            if (t.Path is not null && !Paths.Contains(t.Path)) e.Add($"technique {t.Id}: unknown path '{t.Path}'.");
            switch (t.Slot)
            {
                case SlotKinds.Attack when t.Attack is { } a:
                    if (a.Power is < 1 or > 200) e.Add($"technique {t.Id}: power 1-200.");
                    if (!ScalingStats.Contains(a.Scaling)) e.Add($"technique {t.Id}: scaling must be strength/spirit/agility.");
                    if (c.Distance is not null && !(c.Distance.Min <= a.RangeMin && a.RangeMin <= a.RangeMax && a.RangeMax <= c.Distance.Max)) e.Add($"technique {t.Id}: range outside distance bounds.");
                    if (a.AccuracyPermille is < -500 or > 500) e.Add($"technique {t.Id}: accuracy -500..500.");
                    if (a.Effect is { } fx && (!EffectTypes.Applicable.Contains(fx.Type) || fx.ChancePermille is < 1 or > 1000 || fx.Duration is < 1 or > 5 || fx.Magnitude is < 0 or > 900))
                        e.Add($"technique {t.Id}: invalid effect.");
                    break;
                case SlotKinds.Defense when t.Defense is { } d:
                    if (d.BlockPermille is < 1 or > 900) e.Add($"technique {t.Id}: block 1-900‰ (never a full block).");
                    break;
                case SlotKinds.Movement when t.Movement is { } m:
                    if (m.EvasionPermille is < 0 or > 600) e.Add($"technique {t.Id}: evasion 0-600‰.");
                    break;
                case SlotKinds.Counter when t.Counter is { } k:
                    if (k.ReflectPermille is < 0 or > 1000 || k.QiSteal is < 0 or > 100) e.Add($"technique {t.Id}: counter values invalid.");
                    break;
                default:
                    e.Add($"technique {t.Id}: slot '{t.Slot}' does not match its spec.");
                    break;
            }
        }

        if (c.StarterLoadouts is null || !c.StarterLoadouts.Keys.Order().SequenceEqual(Paths.Order()))
            e.Add("starterLoadouts must cover every path.");
        else
        {
            foreach (var (path, ids) in c.StarterLoadouts)
            {
                var err = LoadoutRules.Validate(c, path, ids.Cast<string?>().ToList());
                if (err is not null) e.Add($"starterLoadouts.{path}: {err}");
            }
        }

        foreach (var n in c.Npcs ?? [])
        {
            if (n.Loadout.Count != 8) e.Add($"npc {n.Id}: loadout must have 8 slots.");
            for (var i = 0; i < n.Loadout.Count && i < 8; i++)
            {
                if (n.Loadout[i] is not { } s) continue;
                var t = c.Techniques.FirstOrDefault(x => x.Id == s.Id);
                if (t is null) e.Add($"npc {n.Id}: unknown technique '{s.Id}'.");
                else if (t.Slot != SlotKinds.Layout[i]) e.Add($"npc {n.Id}: '{s.Id}' cannot go in slot {i} ({SlotKinds.Layout[i]}).");
                if (s.Level is < 1 or > 10) e.Add($"npc {n.Id}: technique level 1-10.");
            }

            if (!n.Loadout.Take(4).Any(s => s is not null)) e.Add($"npc {n.Id}: needs at least one attack.");
            if (n.RewardQi < 0) e.Add($"npc {n.Id}: rewardQi >= 0.");
        }

        return e;
    }
}

public static class LoadoutRules
{
    /// <summary>Null = valid. 8 entries; each non-null technique must be known by the path and fit the slot; no duplicates; ≥1 attack.</summary>
    /// <param name="daoTechniques">Dao Lab techniques the character knows (designed or learned from scrolls).</param>
    public static string? Validate(CombatConfig c, string path, IReadOnlyList<string?> slots, IReadOnlyDictionary<string, TechniqueDefinition>? daoTechniques = null)
    {
        if (slots.Count != 8) return "a loadout has exactly 8 slots.";
        var known = c.KnownBy(path).ToDictionary(t => t.Id);
        foreach (var (id, t) in daoTechniques ?? new Dictionary<string, TechniqueDefinition>()) known[id] = t;
        for (var i = 0; i < 8; i++)
        {
            if (slots[i] is not { } id) continue;
            if (!known.TryGetValue(id, out var t)) return $"slot {i}: technique '{id}' is not known by the {path} path.";
            if (t.Slot != SlotKinds.Layout[i]) return $"slot {i} accepts {SlotKinds.Layout[i]} techniques, '{id}' is {t.Slot}.";
        }

        if (slots.Where(s => s is not null).GroupBy(s => s).Any(g => g.Count() > 1)) return "a technique can be equipped only once.";
        if (!slots.Take(4).Any(s => s is not null)) return "at least one attack technique is required.";
        return null;
    }
}
