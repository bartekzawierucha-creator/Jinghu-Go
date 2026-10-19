using JianghuGo.Domain;
using JianghuGo.Modules.Combat.Contracts;

namespace JianghuGo.Modules.Arts.Domain;

public sealed record OwnedFlame(string Id, int Level, int Xp, DateTimeOffset AbsorbedAt);

/// <summary>A subdue battle in progress: the flame's spirit, fought under a one-off reference.</summary>
public sealed record PendingSpirit(string FlameId, Guid Reference, Guid BattleId, DateTimeOffset StartedAt);

/// <summary>A subdued flame being absorbed; the outcome is rolled when the time is up.</summary>
public sealed record Absorption(string FlameId, DateTimeOffset StartedAt, DateTimeOffset Until, int ChancePermille);

public sealed record GuWorm(int Slot, string Type, int Level, int Rank, DateTimeOffset HatchedAt, DateTimeOffset? GrowingUntil);

/// <summary>What the last settled timer did (shown once in the arts screen).</summary>
public sealed record ArtsNotice(string Kind, string TextPl, DateTimeOffset At);

/// <summary>Everything a character has in the path arts, as one versioned document (arts.profiles).</summary>
public sealed record ArtsProfile(
    CharacterId CharacterId,
    IReadOnlyList<string> FlamesSeen,
    IReadOnlyList<OwnedFlame> Flames,
    string? SubduedFlame,
    PendingSpirit? Spirit,
    Absorption? Absorbing,
    DateTimeOffset? BurnedUntil,
    IReadOnlyList<string> Slots,
    IReadOnlyList<string> Fusions,
    IReadOnlyDictionary<string, int> Venoms,
    int ToxinXp,
    DateTimeOffset? PoisonedUntil,
    string? Coating,
    int CoatingCharges,
    IReadOnlyList<GuWorm> Gu,
    int Temper,
    int TemperXp,
    int TemperAttempts,
    string? Bath,
    DateTimeOffset? BathUntil,
    int SwordXp,
    string? Branch,
    IReadOnlyList<ArtsNotice> Notices,
    int Version,
    string? Capstone = null)
{
    public static ArtsProfile New(CharacterId id) =>
        new(id, [], [], null, null, null, null, [], [], new Dictionary<string, int>(), 0, null, null, 0, [], 0, 0, 0, null, null, 0, null, [], 0);

    public OwnedFlame? Owned(string id) => Flames.FirstOrDefault(f => f.Id == id);

    public ArtsProfile Notify(string kind, string text, DateTimeOffset at) => this with { Notices = [.. Notices.TakeLast(4), new ArtsNotice(kind, text, at)] };
}

public static class ArtsRules
{
    public const string FlamePath = "flame";
    public const string PoisonPath = "poison";
    public const string BodyPath = "body";
    public const string QiPath = "qi";

    // ---------------------------------------------------------------- flames

    public static int FlameSlots(ArtsConfig c, int realm) => c.FlameRules.SlotRealms.Count(r => realm >= r);

    public static int FlameLevelXp(ArtsConfig c, int level) => c.FlameRules.XpPerLevel * level;

    /// <summary>Feeding: XP rolls over into levels (each level needs xpPerLevel × level) up to the max.</summary>
    public static OwnedFlame Feed(ArtsConfig c, OwnedFlame f, int xp)
    {
        var level = f.Level;
        var total = f.Xp + xp;
        while (level < c.FlameRules.MaxLevel && total >= FlameLevelXp(c, level))
        {
            total -= FlameLevelXp(c, level);
            level++;
        }

        return f with { Level = level, Xp = level >= c.FlameRules.MaxLevel ? 0 : total };
    }

    /// <summary>Absorption chance: base + per realm, harder for higher ranks (rank 1 is the strongest).</summary>
    public static int AbsorbChance(ArtsConfig c, FlameDefinition f, int realm) =>
        Math.Clamp(c.FlameRules.AbsorbBasePermille + (c.FlameRules.AbsorbRealmPermille * realm) - ((18 - Math.Min(18, f.Rank)) * 15) - (Math.Max(0, f.MinRealm - realm) * 50), 250, 970);

    /// <summary>A flame's spirit grows with its rank (power ‰ for the battle).</summary>
    /// <summary>Health that damage-based tempering is measured against (a fresh cultivator's HP).</summary>
    public const int TemperReferenceHp = 200;

    /// <summary>
    /// Power (‰) of a flame's spirit. 1.6: Combat raises the spirit to the fighter's realm, so the power only sets how fierce the
    /// spirit is for its kind (lesser / greater / primal) with a small step by rank (rank 1 fiercest). Tuned with tools/BalanceSim.
    /// </summary>
    public static int SpiritPower(FlameDefinition f) => (f.Spirit switch
    {
        "flame_spirit_lesser" => 900,
        "flame_spirit_greater" => 780,
        _ => 680,
    }) + ((13 - Math.Clamp(f.Rank, 1, 26)) * 3);

    public static bool IsFullMoon(DateTimeOffset at)
    {
        var knownNew = new DateTimeOffset(2000, 1, 6, 18, 14, 0, TimeSpan.Zero);
        const double synodic = 29.530588853;
        var age = (at - knownNew).TotalDays % synodic;
        if (age < 0) age += synodic;
        return Math.Abs(age - (synodic / 2)) <= 1.0;
    }

    /// <summary>Flames ember traces can lead to right now (world time: night 20-5, noon 11-13, full moon).</summary>
    public static IReadOnlyList<FlameDefinition> TraceCandidates(ArtsConfig c, DateTimeOffset now, bool rare)
    {
        var local = TimeZoneInfo.ConvertTime(now, c.TimeZone);
        var night = local.Hour >= 20 || local.Hour < 5;
        var noon = local.Hour is >= 11 and < 13;
        var moon = IsFullMoon(now);
        return [.. c.Flames.Where(f => f.Discover.Any(s => s.Kind switch
        {
            FlameSourceKinds.Trace => s.Ref switch { "any" => true, "night" => night, "rare" => rare, _ => false },
            FlameSourceKinds.Moon => moon,
            FlameSourceKinds.Noon => noon,
            _ => false,
        }))];
    }

    public static ArtsProfile Discover(ArtsProfile p, FlameDefinition f, DateTimeOffset at) =>
        p.FlamesSeen.Contains(f.Id) || p.Owned(f.Id) is not null
            ? p
            : (p with { FlamesSeen = [.. p.FlamesSeen, f.Id] }).Notify("flame_found", $"Odnaleziono {f.NamePl}! Pokonaj jego ducha, aby go ujarzmić.", at);

    // ---------------------------------------------------------------- toxin body

    public static int ToxinLevel(ArtsConfig c, int xp) => c.Toxin.Levels.Count(l => xp >= l);

    public static int ToxinNext(ArtsConfig c, int xp) => c.Toxin.Levels.FirstOrDefault(l => xp < l);

    // ---------------------------------------------------------------- Gu

    public static int GuSlots(ArtsConfig c, int realm) => Math.Min(c.Gu.MaxSlots, c.Gu.SlotsBase + (realm / c.Gu.RealmsPerSlot));

    // ---------------------------------------------------------------- tempering

    public static int TemperXpNeeded(ArtsConfig c, int level) => (int)Math.Min(int.MaxValue / 4, Math.Round(c.Tempering.XpBase * Math.Pow(c.Tempering.XpGrowth, level)));

    public static int TemperChance(ArtsConfig c, int level) => Math.Max(c.Tempering.ChanceMin, c.Tempering.ChanceStart - (c.Tempering.ChanceDecay * level));

    public static bool TemperNeedsPill(ArtsConfig c, int level) => (level + 1) % c.Tempering.PillEvery == 0;

    public static TemperStage? TemperStageOf(ArtsConfig c, int level) => level < 1 ? null : c.Tempering.Stages.LastOrDefault(s => s.From <= level);

    public static ArtsProfile AddTemperXp(ArtsConfig c, ArtsProfile p, long xp) =>
        xp <= 0 || p.Temper >= c.Tempering.MaxLevel ? p : p with { TemperXp = (int)Math.Min(int.MaxValue / 2, p.TemperXp + xp) };

    // ---------------------------------------------------------------- sword intent

    public static int SwordLevel(ArtsConfig c, int xp) => c.SwordIntent.Levels.Count(l => xp >= l);

    // ---------------------------------------------------------------- branches

    public static int BranchTier(ArtsConfig c, int realm) => c.Branches.TierRealms.Count(r => realm >= r);

    // ---------------------------------------------------------------- timers

    /// <summary>
    /// Finishes what time has finished: absorption (rolled with a seed fixed by the character and the absorption),
    /// a body bath, Gu growth (with a chance to mutate into a higher rank). Poison and burns simply expire.
    /// </summary>
    public static ArtsProfile Settle(ArtsConfig c, ArtsProfile p, DateTimeOffset now)
    {
        if (p.Absorbing is { } a && a.Until <= now && c.Flame(a.FlameId) is { } flame)
        {
            var roll = new DeterministicRandom(Seed(p.CharacterId.Value, "absorb", a.Until)).NextInt(0, 1000);
            if (roll < a.ChancePermille)
            {
                p = (p with
                {
                    Absorbing = null,
                    Flames = [.. p.Flames, new OwnedFlame(flame.Id, 1, 0, a.Until)],
                    FlamesSeen = [.. p.FlamesSeen.Where(x => x != flame.Id)],
                }).Notify("flame_absorbed", $"{flame.NamePl} płonie teraz w twoim dantian!", a.Until);
            }
            else
            {
                p = (p with { Absorbing = null, BurnedUntil = a.Until.AddHours(c.FlameRules.BurnHours) })
                    .Notify("flame_burned", $"{flame.NamePl} wyrwał się i poparzył twoje meridiany. Spróbuj ponownie po zagojeniu.", a.Until);
            }
        }

        if (p.Bath is { } bathId && p.BathUntil is { } until && until <= now)
        {
            var bath = c.Bath(bathId);
            p = AddTemperXp(c, p with { Bath = null, BathUntil = null }, bath?.Xp ?? 0);
            if (bath is not null) p = p.Notify("bath_done", $"{bath.NamePl} zakończona: +{bath.Xp} hartowania.", until);
        }

        if (p.Gu.Any(w => w.GrowingUntil is { } g && g <= now))
        {
            var worms = new List<GuWorm>();
            foreach (var w in p.Gu)
            {
                if (w.GrowingUntil is not { } g || g > now)
                {
                    worms.Add(w);
                    continue;
                }

                var next = w with { Level = Math.Min(c.Gu.MaxLevel, w.Level + 1), GrowingUntil = null };
                var mutate = next.Level >= c.Gu.MaxLevel && next.Rank < c.Gu.MaxRank
                    && new DeterministicRandom(Seed(p.CharacterId.Value, "gu" + w.Slot, g)).NextInt(0, 1000) < c.Gu.MutationPermille * 10;
                if (mutate) next = next with { Rank = next.Rank + 1, Level = 1 };
                worms.Add(next);
                p = p.Notify("gu_grown", mutate ? $"Gu w gnieździe {w.Slot + 1} zmutował w wyższą rangę ({next.Rank})!" : $"Gu w gnieździe {w.Slot + 1} urósł do poziomu {next.Level}.", g);
            }

            p = p with { Gu = worms };
        }

        if (p.BurnedUntil is { } b && b <= now) p = p with { BurnedUntil = null };
        if (p.PoisonedUntil is { } po && po <= now) p = p with { PoisonedUntil = null };
        return p;
    }

    public static ulong Seed(Guid characterId, string purpose, DateTimeOffset at)
    {
        var h = 14695981039346656037UL;
        foreach (var b in characterId.ToByteArray()) h = (h ^ b) * 1099511628211UL;
        foreach (var ch in purpose) h = (h ^ ch) * 1099511628211UL;
        return h ^ ((ulong)at.UtcTicks * 0x9E3779B97F4A7C15UL);
    }

    public static ulong Seed(Guid messageId, Guid characterId, string purpose) => Seed(characterId, purpose + messageId.ToString("N"), DateTimeOffset.UnixEpoch);

    // ---------------------------------------------------------------- combat

    public static PerkBonus Scale(PerkBonus p, int permille) => permille == 1000 ? p : new(
        p.DamagePermille * permille / 1000, p.GuardPermille * permille / 1000, p.EffectChancePermille * permille / 1000, p.BleedPermille * permille / 1000,
        p.HealPermille * permille / 1000, p.QiRegen * permille / 1000, p.QiPermille * permille / 1000, p.RegenPermille * permille / 1000, p.ReflectPermille * permille / 1000,
        p.LifestealPermille * permille / 1000, p.FirstStrikePermille * permille / 1000, p.HpPermille * permille / 1000, permille >= 1000 ? p.SurviveOnce : 0,
        p.PetPower * permille / 1000, p.PetEvery);

    public static int FlamePower(ArtsConfig c, OwnedFlame f, string path) =>
        (1000 + (f.Level * c.FlameRules.LevelPermille)) * (path == FlamePath ? 1000 : c.FlameRules.OtherPathPowerPermille) / 1000;

    public static IEnumerable<(FlameDefinition Def, OwnedFlame Owned)> SlottedFlames(ArtsConfig c, ArtsProfile p, int realm) =>
        p.Slots.Take(FlameSlots(c, realm)).Select(id => (c.Flame(id), p.Owned(id))).Where(x => x.Item1 is not null && x.Item2 is not null).Select(x => (x.Item1!, x.Item2!));

    private static bool FusionActive(ArtsProfile p, string fusion, IReadOnlyCollection<string> slotted)
    {
        var ids = fusion.Split('+');
        return ids.Length == 2 && slotted.Contains(ids[0]) && slotted.Contains(ids[1]);
    }

    /// <summary>All perks the arts give in battle (flames, toxin body, coating, Gu, tempering, sword intent, branch).</summary>
    public static PerkBonus Perks(ArtsConfig c, ArtsProfile p, string path, int realm)
    {
        var sum = PerkBonus.None;
        var slotted = SlottedFlames(c, p, realm).ToList();
        var slottedIds = slotted.Select(s => s.Def.Id).ToList();
        foreach (var (def, owned) in slotted)
        {
            var power = FlamePower(c, owned, path);
            if (p.Fusions.Any(f => f.Split('+').Contains(def.Id) && FusionActive(p, f, slottedIds))) power = power * (1000 + c.FlameRules.FusionBonusPermille) / 1000;
            sum += Scale(def.Perks, power);
        }

        var toxin = ToxinLevel(c, p.ToxinXp);
        var toxinPower = path == PoisonPath ? 1000 : 300;
        foreach (var perk in c.Toxin.Perks.Where(x => x.Level <= toxin)) sum += Scale(perk.Perks, toxinPower);
        if (c.Coating(p.Coating) is { } coating && p.CoatingCharges > 0) sum += coating.Perks;

        foreach (var w in p.Gu.Where(w => w.Level > 0))
            if (c.GuType(w.Type) is { } type)
            {
                var permille = 1000 * w.Level * (2 + w.Rank) / 3;
                var g = Scale(type.PerLevel, permille);
                sum += g with { PetEvery = type.PerLevel.PetEvery };
            }

        if (p.Temper > 0)
        {
            var bodyPower = path == BodyPath ? 1000 : 300;
            var hp = 0;
            foreach (var stage in c.Tempering.Stages)
            {
                if (stage.From > p.Temper) break;
                var next = c.Tempering.Stages.FirstOrDefault(s => s.From > stage.From)?.From ?? int.MaxValue;
                hp += stage.HpPermillePerLevel * (Math.Min(p.Temper + 1, next) - stage.From);
                sum += Scale(stage.Perks, bodyPower);
            }

            sum += new PerkBonus(HpPermille: hp * bodyPower / 1000 / 10);
        }

        var swords = SwordLevel(c, p.SwordXp);
        if (swords > 0)
        {
            var swordPower = path == QiPath ? 1000 : 300;
            sum += Scale(new PerkBonus(DamagePermille: swords * c.SwordIntent.DamagePermillePerLevel, FirstStrikePermille: swords * c.SwordIntent.FirstStrikePermillePerLevel), swordPower);
            if (swords >= c.SwordIntent.Levels.Count) sum += Scale(c.SwordIntent.FullArrayPerks, swordPower);
        }

        if (p.Branch is { } branchId && c.BranchesOf(path).FirstOrDefault(b => b.Id == branchId) is { } branch)
        {
            var tier = BranchTier(c, realm);
            if (tier > 0) sum += Scale(branch.PerTier, 1000 * tier);
        }

        // the path's ultimate (1.7), chosen at the capstone realm
        if (p.Capstone is { } capId && realm >= c.Branches.CapstoneRealm && c.Branches.CapstonesOf(path).FirstOrDefault(x => x.Id == capId) is { } cap)
            sum += cap.PerTier;

        return sum;
    }

    /// <summary>Flat stats from slotted flames (scaled like their perks); poison costs a few points while it lasts.</summary>
    /// <summary>(10 + realm) / (10 + the flame's realm), never below 1: a soul candle is still worth slotting at realm 60.</summary>
    public static int FlameResonancePermille(FlameDefinition f, int realm) => Math.Max(1000, (10 + Math.Max(0, realm)) * 1000 / (10 + f.MinRealm));

    public static IReadOnlyDictionary<string, int> Stats(ArtsConfig c, ArtsProfile p, string path, int realm, DateTimeOffset now)
    {
        var stats = new Dictionary<string, int>();
        foreach (var (def, owned) in SlottedFlames(c, p, realm))
        {
            var power = FlamePower(c, owned, path);
            // 1.6: a flame resonates with its owner's cultivation: its stats grow as the realm rises past the flame's own
            var resonance = FlameResonancePermille(def, realm);
            foreach (var (stat, v) in def.Stats) stats[stat] = stats.GetValueOrDefault(stat) + (int)((long)v * power / 1000 * resonance / 1000);
        }

        if (p.PoisonedUntil is { } until && until > now)
            foreach (var stat in new[] { "strength", "agility", "spirit" }) stats[stat] = stats.GetValueOrDefault(stat) - c.Toxin.PoisonedStatMalus;
        return stats;
    }

    public static (int Alchemy, int Forge) Crafting(ArtsConfig c, ArtsProfile p, string path, int realm)
    {
        int alchemy = 0, forge = 0;
        foreach (var (def, owned) in SlottedFlames(c, p, realm))
        {
            var power = FlamePower(c, owned, path);
            alchemy += def.AlchemyPermille * power / 1000;
            forge += def.ForgePermille * power / 1000;
        }

        return (alchemy, forge);
    }

    /// <summary>Codex titles: number of flames owned, distinct venoms absorbed; tempering and sword milestones.</summary>
    public static IReadOnlyList<(string Id, string NamePl, string Rarity)> Titles(ArtsConfig c, ArtsProfile p)
    {
        var list = new List<(string, string, string)>();
        string Rarity(int i, int n) => n <= 1 ? "epic" : (i * 4 / n) switch { 0 => "rare", 1 => "epic", 2 => "legendary", _ => "mythic" };
        var codex = c.FlameRules.Codex;
        for (var i = 0; i < codex.Count; i++)
            if (p.Flames.Count >= codex[i].Count) list.Add(("flames_" + codex[i].Count, codex[i].TitlePl, Rarity(i, codex.Count)));
        var venoms = p.Venoms.Count(v => v.Value > 0);
        var tc = c.Toxin.Codex;
        for (var i = 0; i < tc.Count; i++)
            if (venoms >= tc[i].Count) list.Add(("venoms_" + tc[i].Count, tc[i].TitlePl, Rarity(i, tc.Count)));
        if (TemperStageOf(c, p.Temper) is { } stage && p.Temper >= 30) list.Add(("temper_" + stage.From, stage.NamePl, p.Temper >= 90 ? "mythic" : p.Temper >= 70 ? "legendary" : "epic"));
        if (SwordLevel(c, p.SwordXp) >= c.SwordIntent.Levels.Count) list.Add(("sword_array", "Dziewięć Mieczy", "legendary"));
        return list;
    }
}
