using JianghuGo.Domain;

namespace JianghuGo.Modules.Combat.Domain;

/// <summary>
/// The technique forge (1.8): an endless supply of techniques generated from a seed. Everything about a generated technique
/// (era, rarity, path, kind, element, numbers, effects, name) follows from its 64-bit id, so it can be checked and reproduced.
/// Generated techniques are stored like Dao Lab designs (frozen once created) and travel as scrolls.
/// </summary>
public sealed record ForgeRules(
    IReadOnlyList<ForgeRarity> Rarities,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Forms,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Epithets,
    IReadOnlyList<string> Motifs,
    IReadOnlyDictionary<string, IReadOnlyList<string>> EffectPools,
    int PowerBase,
    int PowerPerEra,
    int MaxPower,
    IReadOnlyList<int>? DropPermilleByTier = null,
    int BossDropMultiplier = 3,
    long MeditationStonesPerEra = 100,
    int TransmuteEssence = 10,
    string ScrollItem = "technique_scroll",
    string EssenceItem = "dao_essence")
{
    public int DropPermille(NpcDefinition n) =>
        DropPermilleByTier is { Count: > 0 } t ? t[Math.Clamp(n.Tier, 1, t.Count) - 1] * (n.Boss ? BossDropMultiplier : 1) : 0;

    public ForgeRarity Rarity(int index) => Rarities[Math.Clamp(index, 0, Rarities.Count - 1)];
}

/// <param name="Weight">Relative drop weight.</param>
/// <param name="PowerPermille">Numbers of the technique relative to an ordinary one.</param>
/// <param name="Effects">How many effects it carries.</param>
public sealed record ForgeRarity(string Id, string NamePl, int Weight, int PowerPermille, int Effects);

public static class TechniqueForge
{
    public const string RulesVersion = "forge-1";
    public static readonly string[] Paths = ["", "qi", "body", "flame", "poison"];
    private static readonly string[] Elements = ["wood", "fire", "earth", "metal", "water"];

    /// <summary>Packs the choices into an id: era (3 bits), rarity (3), path (3) and 55 random bits, as "dao_" + 16 letters.</summary>
    public static string NewId(int era, int rarity, string? path, ulong random)
    {
        var p = (uint)Math.Max(0, Array.IndexOf(Paths, path ?? string.Empty));
        var e = (uint)Math.Clamp(era, 0, 7);
        var r = (uint)Math.Clamp(rarity, 0, 7);
        var v = e | ((ulong)r << 3) | ((ulong)p << 6) | (random << 9);
        return DaoLabRules.NewTechniqueId(v);
    }

    public static ulong Decode(string id)
    {
        ulong v = 0;
        for (var i = 0; i < 16; i++) v |= (ulong)(id[4 + i] - 'a') << (i * 4);
        return v;
    }

    /// <summary>Rolls a rarity index by the configured weights, shifted up by a bonus (bosses, the tower, the world boss).</summary>
    public static int RollRarity(ForgeRules f, DeterministicRandom rng, int bonus = 0)
    {
        var total = f.Rarities.Sum(r => r.Weight);
        var pick = rng.NextInt(0, total);
        var index = 0;
        for (; index < f.Rarities.Count - 1; index++)
        {
            pick -= f.Rarities[index].Weight;
            if (pick < 0) break;
        }

        return Math.Min(f.Rarities.Count - 1, index + bonus);
    }

    /// <summary>Era of a realm (0-5, eleven realms each).</summary>
    public static int EraOf(int realm) => Math.Clamp(realm / 11, 0, 5);

    public static (int Era, int Rarity, string? Path) Header(string id)
    {
        var v = Decode(id);
        var path = Paths[(int)Math.Min(4, (v >> 6) & 7)];
        return ((int)(v & 7), (int)((v >> 3) & 7), path.Length == 0 ? null : path);
    }

    /// <summary>The technique an id stands for.</summary>
    public static TechniqueDefinition Generate(CombatConfig c, string id)
    {
        var f = c.Forge ?? throw new InvalidOperationException("The forge is not configured.");
        var (era, rarityIndex, path) = Header(id);
        var rarity = f.Rarity(rarityIndex);
        var rng = new DeterministicRandom(Decode(id) >> 9 ^ 0x5DEECE66DUL);
        int Pick(int n) => rng.NextInt(0, n);
        int Scale(int v) => (int)((long)v * rarity.PowerPermille / 1000);

        var kindRoll = Pick(100);
        var kind = kindRoll < 50 ? SlotKinds.Attack : kindRoll < 70 ? SlotKinds.Special : kindRoll < 85 ? SlotKinds.Defense : SlotKinds.Counter;
        var element = path is not null && Pick(100) < 60 && c.Elements?.Paths.GetValueOrDefault(path) is { } pe ? pe : Elements[Pick(Elements.Length)];
        var scaling = path switch
        {
            "qi" or "flame" => "spirit",
            "body" => "strength",
            "poison" => "agility",
            _ => new[] { "strength", "spirit", "agility" }[Pick(3)],
        };

        // effects from the path's pool (or the shared one), more for rarer techniques
        var pool = f.EffectPools.GetValueOrDefault(path ?? "any") ?? f.EffectPools.GetValueOrDefault("any") ?? [];
        var effects = new List<EffectSpec>();
        for (var i = 0; i < rarity.Effects && pool.Count > 0; i++)
        {
            var type = pool[Pick(pool.Count)];
            if (effects.Any(e => e.Type == type)) continue;
            effects.Add(EffectFor(type, era, rarity, rng));
        }

        var power = Math.Min(f.MaxPower, Scale(f.PowerBase + (f.PowerPerEra * era) + Pick(5) - 2));
        var cooldown = Math.Clamp(power / 14 + Pick(2) - (kind == SlotKinds.Attack ? 1 : 0) + (effects.Count > 1 ? 1 : 0), 0, 5);
        var qiCost = Math.Max(0, (power / 2) + (effects.Count * 3) - (cooldown * 2) + Pick(4));
        var speed = Pick(3) - 1;
        var accuracy = (Pick(5) - 1) * 25;

        var foeEffects = effects.Where(e => EffectTypes.Applicable.Contains(e.Type)).ToList();
        var selfEffects = effects.Where(e => EffectTypes.SelfApplicable.Contains(e.Type)).ToList();
        AttackSpec? attack = null;
        DefenseSpec? defense = null;
        CounterSpec? counter = null;
        SpecialSpec? special = null;
        switch (kind)
        {
            case SlotKinds.Attack:
                attack = new AttackSpec(power, scaling, 1, 3, accuracy, foeEffects.FirstOrDefault());
                break;
            case SlotKinds.Defense:
                defense = new DefenseSpec(Math.Min(850, Scale(400 + (era * 30) + (Pick(5) * 20))));
                qiCost = Math.Max(0, qiCost / 3);
                cooldown = Math.Min(cooldown, 2);
                break;
            case SlotKinds.Counter:
                counter = new CounterSpec(Math.Min(800, Scale(300 + (era * 30) + (Pick(5) * 20))), Math.Min(30, Scale(4 + (era * 2) + Pick(4))));
                qiCost = Math.Max(0, qiCost / 3);
                cooldown = Math.Max(1, Math.Min(cooldown, 3));
                break;
            default:
                var heal = Pick(3) == 0 ? Scale(120 + (era * 15)) : 0;
                var guard = Pick(3) == 0 ? Scale(250 + (era * 20)) : 0;
                var evasion = heal == 0 && guard == 0 ? Scale(150 + (era * 15)) : 0;
                special = new SpecialSpec(heal > 0 ? power / 2 : power, scaling, accuracy, Math.Min(500, heal), Math.Min(500, evasion), Math.Min(700, guard),
                    foeEffects.FirstOrDefault(), selfEffects.FirstOrDefault());
                cooldown = Math.Max(2, cooldown);
                qiCost += 4;
                break;
        }

        // a rarer attack carries a second on-hit effect
        if (attack is not null && foeEffects.Count > 1) attack = attack with { Extra = foeEffects[1] };

        var name = Name(f, kind, element, rng);
        return new TechniqueDefinition(id, name, name, kind == SlotKinds.Special ? "movement" : kind, qiCost, cooldown, speed, path, attack, defense, null, counter, special);
    }

    private static EffectSpec EffectFor(string type, int era, ForgeRarity rarity, DeterministicRandom rng)
    {
        int Scale(int v) => (int)((long)v * rarity.PowerPermille / 1000);
        var chance = Math.Min(900, Scale(350 + (rng.NextInt(0, 5) * 50)));
        var duration = 2 + rng.NextInt(0, 2);
        return type switch
        {
            EffectTypes.Bleed or EffectTypes.Burn or EffectTypes.Poison => new EffectSpec(type, chance, Math.Min(12, Scale(3 + era)), duration),
            EffectTypes.Stun => new EffectSpec(type, Math.Min(350, chance / 2), 0, 1),
            EffectTypes.Weaken or EffectTypes.Frail => new EffectSpec(type, chance, Math.Min(400, Scale(120 + (era * 20))), duration),
            EffectTypes.Slow or EffectTypes.Haste => new EffectSpec(type, chance, Math.Min(30, Scale(8 + (era * 2))), duration),
            EffectTypes.Drain => new EffectSpec(type, chance, Math.Min(40, Scale(8 + (era * 3))), 1),
            EffectTypes.Dispel or EffectTypes.Cleanse => new EffectSpec(type, Math.Min(1000, chance + 200), 0, 1),
            EffectTypes.Shield => new EffectSpec(type, 1000, Math.Min(400, Scale(120 + (era * 20))), 2),
            EffectTypes.Empower or EffectTypes.Fortify => new EffectSpec(type, 1000, Math.Min(400, Scale(120 + (era * 20))), duration),
            _ => new EffectSpec(type, chance, 0, 1),
        };
    }

    private static string Name(ForgeRules f, string kind, string element, DeterministicRandom rng)
    {
        var forms = f.Forms.GetValueOrDefault(kind) ?? f.Forms.Values.First();
        var epithets = f.Epithets.GetValueOrDefault(element) ?? f.Epithets.Values.First();
        var form = forms[rng.NextInt(0, forms.Count)];
        var epithet = epithets[rng.NextInt(0, epithets.Count)];
        var motif = f.Motifs[rng.NextInt(0, f.Motifs.Count)];
        return rng.NextInt(0, 2) == 0 ? $"{form} {epithet} {motif}" : $"{form} {motif}";
    }
}
