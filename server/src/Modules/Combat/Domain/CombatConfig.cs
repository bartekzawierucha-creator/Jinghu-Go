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
    IReadOnlyList<NpcDefinition> Npcs,
    IdleRules? Idle = null,
    IReadOnlyDictionary<string, PathPerks>? PathPerks = null,
    ComboRules? Combo = null,
    TowerRules? Tower = null,
    WorldBossRules? WorldBoss = null,
    ElementRules? Elements = null,
    ArenaRules? Arena = null)
{
    /// <summary>Combat perks of a cultivation path (none for an unknown path or a config without them).</summary>
    public PathPerks? Perks(string? path) => path is not null && PathPerks is not null && PathPerks.TryGetValue(path, out var p) ? p : null;

    /// <summary>Idle hunts (automatic battles while away); defaults apply when the config has no "idle" section.</summary>
    public IdleRules IdleRules => Idle ?? new IdleRules(5, 4);

    public TechniqueDefinition Technique(string id) =>
        Techniques.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException($"Unknown technique '{id}'.");

    /// <summary>Rules of an equipped technique: its frozen Dao Lab definition, or the official one.</summary>
    public TechniqueDefinition Technique(EquippedTechnique equipped) => equipped.Definition ?? Technique(equipped.Id);

    public NpcDefinition? Npc(string id) => Npcs.FirstOrDefault(n => n.Id == id);

    /// <summary>Techniques a character of this path knows: all path-less ones plus its path's signature technique(s).</summary>
    public IEnumerable<TechniqueDefinition> KnownBy(string path) => Techniques.Where(t => t.Path is null || t.Path == path);
}

public sealed record DistanceRules(int Start, int Min, int Max);

/// <summary>Combos: each momentum stack (consecutive landed strikes, up to <see cref="MaxMomentum"/>) adds damage; so does striking an afflicted foe.</summary>
public sealed record ComboRules(int MomentumPermillePerStack, int MaxMomentum, int ExploitPermille);

public sealed record RewardItem(string Item, int Quantity);

/// <summary>Ranked arena: fights against frozen copies of other players, Elo rating, leagues and a daily attempt limit.</summary>
public sealed record ArenaRules(int StartRating, int KFactor, int AttemptsPerDay, int OpponentWindow, IReadOnlyList<ArenaLeague> Leagues, long QiPerWin);

public sealed record ArenaLeague(string Id, string NamePl, int MinRating, long WeeklyStones, string? TitlePl);

/// <summary>Rewards for clearing a tower floor for the first time (on top of <see cref="TowerRules.QiPerFloor"/> × floor).</summary>
public sealed record TowerReward(int Floor, long Stones, IReadOnlyList<RewardItem> Items);

/// <summary>
/// Tower of Trials: floors climbed one by one. The pool is ordered from weakest to strongest and spread evenly over the
/// floors; every <see cref="BossEvery"/>-th floor is a guardian from <see cref="BossPool"/>. Stats and HP grow per floor.
/// Rewards only for the first clear of each floor, so there is nothing to farm.
/// </summary>
public sealed record TowerRules(int Floors, IReadOnlyList<string> Pool, IReadOnlyList<string> BossPool, int BossEvery, int StatPermillePerFloor, int HpPermillePerFloor,
    int QiPerFloor, IReadOnlyList<TowerReward> Rewards)
{
    public bool IsBossFloor(int floor) => BossEvery > 0 && floor % BossEvery == 0;

    public string NpcFor(int floor) => IsBossFloor(floor) && BossPool.Count > 0
        ? BossPool[((floor / BossEvery) - 1) % BossPool.Count]
        : Pool[Math.Min(Pool.Count - 1, (floor - 1) * Pool.Count / Floors)];

    /// <summary>The floor's opponent: the pool NPC with stats and HP raised for the floor.</summary>
    public NpcDefinition Scaled(NpcDefinition npc, int floor)
    {
        var stat = 1000 + ((floor - 1) * StatPermillePerFloor);
        var hp = 1000 + ((floor - 1) * HpPermillePerFloor);
        return npc with
        {
            Stats = npc.Stats.ToDictionary(s => s.Key, s => (int)((long)s.Value * stat / 1000)),
            HpOverride = npc.HpOverride is { } h ? (int)((long)h * hp / 1000) : null,
        };
    }

    public TowerReward? RewardFor(int floor) => Rewards.FirstOrDefault(r => r.Floor == floor);
}

/// <summary>
/// World boss: one shared HP pool per cycle (<see cref="CycleDays"/> days, UTC). Each attempt is a battle against a
/// phase of at most <see cref="PhaseHp"/> HP; the damage dealt comes off the pool. Every attempt pays Qi for damage;
/// once the pool is empty, everyone who dealt at least <see cref="MinDamageForKillReward"/> claims the kill reward.
/// </summary>
public sealed record WorldBossRules(string NpcId, int PoolHp, int PhaseHp, int AttemptsPerDay, int CycleDays, int QiPerHundredDamage, int MaxQiPerAttempt,
    int MinDamageForKillReward, long KillStones, IReadOnlyList<RewardItem> KillItems)
{
    public static readonly DateTimeOffset Epoch = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero); // a Monday

    public int CycleOf(DateTimeOffset at) => (int)Math.Floor((at - Epoch).TotalDays / CycleDays);

    public DateTimeOffset CycleEnd(int cycle) => Epoch.AddDays((long)(cycle + 1) * CycleDays);

    public long QiFor(int damage) => Math.Min(MaxQiPerAttempt, (long)damage * QiPerHundredDamage / 100);
}

/// <summary>
/// What a cultivation path is good at in battle (all ‰ unless noted). Frozen into the combatant when a battle starts.
/// </summary>
public sealed record PathPerks(
    string DescriptionPl,
    int QiPermille = 0,
    int QiRegen = 0,
    int GuardPermille = 0,
    int EffectChancePermille = 0,
    int BleedPermille = 0,
    int HealPermille = 0,
    IReadOnlyDictionary<string, int>? ScalingDamagePermille = null,
    int DamagePermille = 0,
    int RegenPermille = 0,
    int ReflectPermille = 0,
    int LifestealPermille = 0,
    int FirstStrikePermille = 0,
    int SurviveOnce = 0,
    int PetPower = 0,
    int PetEvery = 0)
{
    public int DamageBonus(string? scaling) =>
        DamagePermille + (scaling is not null && ScalingDamagePermille is not null ? ScalingDamagePermille.GetValueOrDefault(scaling) : 0);

    /// <summary>The path's perks plus perks earned outside combat (frozen into the battle snapshot).</summary>
    public PathPerks With(JianghuGo.Modules.Combat.Contracts.PerkBonus b) => this with
    {
        QiPermille = QiPermille + b.QiPermille,
        QiRegen = QiRegen + b.QiRegen,
        GuardPermille = GuardPermille + b.GuardPermille,
        EffectChancePermille = EffectChancePermille + b.EffectChancePermille,
        BleedPermille = BleedPermille + b.BleedPermille,
        HealPermille = HealPermille + b.HealPermille,
        DamagePermille = DamagePermille + b.DamagePermille,
        RegenPermille = RegenPermille + b.RegenPermille,
        ReflectPermille = ReflectPermille + b.ReflectPermille,
        LifestealPermille = LifestealPermille + b.LifestealPermille,
        FirstStrikePermille = FirstStrikePermille + b.FirstStrikePermille,
        SurviveOnce = Math.Max(SurviveOnce, b.SurviveOnce),
        PetPower = Math.Max(PetPower, b.PetPower),
        PetEvery = PetPower >= b.PetPower ? PetEvery : b.PetEvery,
    };
}

/// <summary>
/// The five elements (wood, fire, earth, metal, water): a technique whose element overcomes the defender's element deals
/// more damage, one the defender's element overcomes deals less.
/// </summary>
public sealed record ElementRules(
    IReadOnlyDictionary<string, string> Overcomes,
    int AdvantagePermille,
    IReadOnlyDictionary<string, string> Paths,
    IReadOnlyDictionary<string, string> Npcs,
    IReadOnlyDictionary<string, string> Techniques)
{
    public string? OfTechnique(string id) => Techniques.TryGetValue(id, out var e) ? e : null;

    public int Modifier(string? attack, string? defender) =>
        attack is null || defender is null ? 0
        : Overcomes.GetValueOrDefault(attack) == defender ? AdvantagePermille
        : Overcomes.GetValueOrDefault(defender) == attack ? -AdvantagePermille
        : 0;
}

/// <summary>One automatic battle every <see cref="MinutesPerBattle"/> minutes, for at most <see cref="MaxHours"/> hours per hunt.</summary>
public sealed record IdleRules(int MinutesPerBattle, int MaxHours);

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
    CounterSpec? Counter,
    SpecialSpec? Special = null)
{
    /// <summary>The slot this technique goes in. The old "movement" slot is now the special-skill slot.</summary>
    public string Kind => Slot == "movement" ? SlotKinds.Special : Slot;
}

public sealed record AttackSpec(int Power, string Scaling, int RangeMin, int RangeMax, int AccuracyPermille, EffectSpec? Effect);

public sealed record EffectSpec(string Type, int ChancePermille, int Magnitude, int Duration);

public sealed record DefenseSpec(int BlockPermille);

/// <summary>Legacy (pre-1.3) movement technique: now a special skill that only grants evasion for the turn.</summary>
public sealed record MovementSpec(int EvasionPermille);

/// <summary>
/// Special skill: any mix of a strike (power &gt; 0, scaled by a stat), healing (‰ of max HP), evasion and a guard for the
/// turn, and an effect on the target. Usually expensive with a long cooldown.
/// </summary>
public sealed record SpecialSpec(int Power, string? Scaling, int AccuracyPermille, int HealPermille, int EvasionPermille, int GuardPermille, EffectSpec? Effect);

public sealed record CounterSpec(int ReflectPermille, int QiSteal);

public sealed record NpcDefinition(
    string Id,
    string NamePl,
    string NameEn,
    int? HpOverride,
    int RewardQi,
    IReadOnlyDictionary<string, int> Stats,
    IReadOnlyList<NpcSlot?> Loadout,
    int Tier = 1,
    bool Boss = false,
    bool WorldOnly = false,
    bool Tameable = false);

public sealed record NpcSlot(string Id, int Level);

public static class SlotKinds
{
    public const string Attack = "attack";
    public const string Defense = "defense";
    public const string Special = "special";
    public const string Counter = "counter";
    public static readonly IReadOnlyList<string> Layout = [Attack, Attack, Attack, Attack, Defense, Defense, Special, Counter];
}

public static class EffectTypes
{
    public const string Bleed = "bleed";     // magnitude = damage per turn
    public const string Stun = "stun";       // lose the next action
    public const string Weaken = "weaken";   // magnitude = ‰ less outgoing damage
    public const string Guard = "guard";     // this turn: magnitude = ‰ damage blocked
    public const string Evasion = "evasion"; // this turn: magnitude = ‰ hit chance removed
    public const string Counter = "counter"; // this turn: magnitude = ‰ reflected, second value = qi steal
    public const string Momentum = "momentum"; // combo stacks (consecutive landed strikes); never expires on its own
    public const string Survived = "survived"; // the "survive once" perk was used in this battle
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
        if (c.Idle is { } idle && (idle.MinutesPerBattle is < 1 or > 120 || idle.MaxHours is < 1 or > 24))
            e.Add("idle: minutesPerBattle must be 1-120 and maxHours 1-24.");
        if (c.PathHpBonusPermille is null || !c.PathHpBonusPermille.Keys.Order().SequenceEqual(Paths.Order()) || c.PathHpBonusPermille.Values.Any(v => v is < 0 or > 1000))
            e.Add("pathHpBonusPermille must give 0-1000 for every path.");
        if (c.HitChance is null || !(0 < c.HitChance.MinPermille && c.HitChance.MinPermille <= c.HitChance.BasePermille && c.HitChance.BasePermille <= c.HitChance.MaxPermille && c.HitChance.MaxPermille < 1000))
            e.Add("hitChance must satisfy 0 < min <= base <= max < 1000 (no guaranteed hits or misses).");
        if (c.Damage is null || c.Damage.VariancePermille is < 0 or > 500 || c.Damage.StatScalingPermille < 0 || c.Damage.LevelScalingPermille < 0)
            e.Add("damage rules invalid.");
        if (c.Progression is null || c.Progression.MaxTechniqueLevel != 10 || c.Progression.XpPerUse < 1 || c.Progression.XpPerLevel < 1 || c.Progression.DailyRewardedPveWins < 0)
            e.Add("progression invalid (techniques have levels 1-10).");
        if (c.Slots is null || !c.Slots.SequenceEqual(SlotKinds.Layout))
            e.Add("slots must be 4 attack, 2 defense, 1 special, 1 counter (in that order).");
        if (c.PathPerks is not null && (!c.PathPerks.Keys.Order().SequenceEqual(Paths.Order()) || c.PathPerks.Values.Any(p =>
                string.IsNullOrWhiteSpace(p.DescriptionPl) || p.QiPermille is < 0 or > 1000 || p.QiRegen is < 0 or > 20 || p.GuardPermille is < 0 or > 300
                || p.EffectChancePermille is < 0 or > 500 || p.BleedPermille is < 0 or > 1000 || p.HealPermille is < 0 or > 500
                || (p.ScalingDamagePermille ?? new Dictionary<string, int>()).Any(kv => !ScalingStats.Contains(kv.Key) || kv.Value is < 0 or > 500))))
            e.Add("pathPerks must cover every path with values in range and a Polish description.");

        if (c.Combo is { } combo && (combo.MomentumPermillePerStack is < 0 or > 300 || combo.MaxMomentum is < 0 or > 10 || combo.ExploitPermille is < 0 or > 500))
            e.Add("combo: momentum 0-300‰ per stack, max 0-10 stacks, exploit 0-500‰.");
        if (c.Tower is { } tower)
        {
            if (tower.Floors is < 1 or > 500 || tower.Pool is null || tower.Pool.Count == 0 || tower.BossPool is null || tower.BossEvery is < 0 or > 100
                || tower.StatPermillePerFloor is < 0 or > 1000 || tower.HpPermillePerFloor is < 0 or > 1000 || tower.QiPerFloor < 0 || tower.Rewards is null)
                e.Add("tower: floors 1-500, a non-empty pool, bossEvery 0-100, per-floor growth 0-1000‰, qiPerFloor >= 0.");
            else
            {
                foreach (var id in tower.Pool.Concat(tower.BossPool))
                    if (c.Npcs?.Any(n => n.Id == id) != true || id == c.WorldBoss?.NpcId) e.Add($"tower: '{id}' must be a known opponent (not the world boss).");
                foreach (var r in tower.Rewards)
                    if (r.Floor < 1 || r.Floor > tower.Floors || r.Stones < 0 || r.Items is null || r.Items.Any(i => string.IsNullOrWhiteSpace(i.Item) || i.Quantity is < 1 or > 100))
                        e.Add($"tower: reward for floor {r.Floor} invalid.");
                if (tower.Rewards.GroupBy(r => r.Floor).Any(g => g.Count() > 1)) e.Add("tower: one reward per floor.");
            }
        }

        if (c.WorldBoss is { } boss && (c.Npcs?.FirstOrDefault(n => n.Id == boss.NpcId) is not { WorldOnly: true }
                || boss.PoolHp < 1 || boss.PhaseHp < 1 || boss.PhaseHp > boss.PoolHp || boss.AttemptsPerDay is < 1 or > 20 || boss.CycleDays is < 1 or > 28
                || boss.QiPerHundredDamage < 0 || boss.MaxQiPerAttempt < 0 || boss.MinDamageForKillReward < 0 || boss.KillStones < 0
                || boss.KillItems is null || boss.KillItems.Any(i => string.IsNullOrWhiteSpace(i.Item) || i.Quantity is < 1 or > 100)))
            e.Add("worldBoss: a world-only opponent, 1 <= phaseHp <= poolHp, 1-20 attempts a day, cycle 1-28 days, non-negative rewards.");

        if (c.Techniques is null || c.Techniques.Count == 0)
        {
            e.Add("techniques are required.");
            return e;
        }

        foreach (var d in c.Techniques.GroupBy(t => t.Id).Where(g => g.Count() > 1)) e.Add($"duplicate technique '{d.Key}'.");
        foreach (var t in c.Techniques)
        {
            var specs = new object?[] { t.Attack, t.Defense, t.Movement, t.Counter, t.Special }.Count(x => x is not null);
            if (specs != 1) e.Add($"technique {t.Id}: exactly one of attack/defense/special/counter is required.");
            if (t.QiCost is < 0 or > 100 || t.Cooldown is < 0 or > 10 || t.Speed is < -10 or > 10) e.Add($"technique {t.Id}: qiCost 0-100, cooldown 0-10, speed -10..10.");
            if (t.Path is not null && !Paths.Contains(t.Path)) e.Add($"technique {t.Id}: unknown path '{t.Path}'.");
            switch (t.Kind)
            {
                case SlotKinds.Attack when t.Attack is { } a:
                    if (a.Power is < 1 or > 200) e.Add($"technique {t.Id}: power 1-200.");
                    if (!ScalingStats.Contains(a.Scaling)) e.Add($"technique {t.Id}: scaling must be strength/spirit/agility.");
                    if (a.AccuracyPermille is < -500 or > 500) e.Add($"technique {t.Id}: accuracy -500..500.");
                    if (a.Effect is { } fx && (!EffectTypes.Applicable.Contains(fx.Type) || fx.ChancePermille is < 1 or > 1000 || fx.Duration is < 1 or > 5 || fx.Magnitude is < 0 or > 900))
                        e.Add($"technique {t.Id}: invalid effect.");
                    break;
                case SlotKinds.Defense when t.Defense is { } d:
                    if (d.BlockPermille is < 1 or > 900) e.Add($"technique {t.Id}: block 1-900‰ (never a full block).");
                    break;
                case SlotKinds.Special when t.Movement is { } m:
                    if (m.EvasionPermille is < 0 or > 600) e.Add($"technique {t.Id}: evasion 0-600‰.");
                    break;
                case SlotKinds.Special when t.Special is { } sp:
                    if (sp.Power is < 0 or > 300 || (sp.Power > 0 && !ScalingStats.Contains(sp.Scaling ?? string.Empty)) || sp.AccuracyPermille is < -500 or > 500
                        || sp.HealPermille is < 0 or > 600 || sp.EvasionPermille is < 0 or > 600 || sp.GuardPermille is < 0 or > 900
                        || sp.Power + sp.HealPermille + sp.EvasionPermille + sp.GuardPermille == 0)
                        e.Add($"technique {t.Id}: special values invalid (power 0-300 with a scaling stat, heal/evasion 0-600‰, guard 0-900‰, something to do).");
                    if (sp.Effect is { } sfx && (!EffectTypes.Applicable.Contains(sfx.Type) || sfx.ChancePermille is < 1 or > 1000 || sfx.Duration is < 1 or > 5 || sfx.Magnitude is < 0 or > 900))
                        e.Add($"technique {t.Id}: invalid effect.");
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
                else if (t.Kind != SlotKinds.Layout[i]) e.Add($"npc {n.Id}: '{s.Id}' cannot go in slot {i} ({SlotKinds.Layout[i]}).");
                if (s.Level is < 1 or > 10) e.Add($"npc {n.Id}: technique level 1-10.");
            }

            if (!n.Loadout.Take(4).Any(s => s is not null)) e.Add($"npc {n.Id}: needs at least one attack.");
            if (n.RewardQi < 0) e.Add($"npc {n.Id}: rewardQi >= 0.");
            if (n.Tier is < 1 or > 9) e.Add($"npc {n.Id}: tier 1-9.");
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
            if (t.Kind != SlotKinds.Layout[i]) return $"slot {i} accepts {SlotKinds.Layout[i]} techniques, '{id}' is {t.Kind}.";
        }

        if (slots.Where(s => s is not null).GroupBy(s => s).Any(g => g.Count() > 1)) return "a technique can be equipped only once.";
        if (!slots.Take(4).Any(s => s is not null)) return "at least one attack technique is required.";
        return null;
    }
}
