namespace JianghuGo.Modules.Combat.Domain;

/// <summary>A spirit beast that follows a cultivator: tamed after a won PvE battle, grows with further victories.</summary>
/// <param name="Stage">Evolution stage (2.2): 0 young … 3 mythic; each stage raises the level cap by 5 and strengthens the bond.</param>
public sealed record SpiritPet(JianghuGo.Domain.CharacterId CharacterId, string NpcId, int Xp, Guid TamedInBattle, DateTimeOffset TamedAt, int Stage = 0)
{
    public int Level => SpiritPetRules.Level(Xp, Stage);
}

/// <summary>
/// An idle hunt: the character fights <see cref="NpcId"/> automatically, one battle per interval, while the player is
/// away. Battles are real battles (same engine, rewards, loot, quests, daily reward cap), settled lazily on read.
/// </summary>
public sealed record IdleHunt(JianghuGo.Domain.CharacterId CharacterId, JianghuGo.Domain.AccountId AccountId, string NpcId, DateTimeOffset StartedAt, DateTimeOffset SettledAt,
    int Battles, int Wins, int Losses, long Qi, int Version)
{
    public DateTimeOffset EndsAt(IdleRules r) => StartedAt.AddHours(r.MaxHours);
}

public static class SpiritPetRules
{
    public const int MaxLevel = 10;
    public const int XpPerLevel = 10;
    public const int MaxStage = 3;
    public const int LevelsPerStage = 5;

    public static readonly string[] StageNamesPl = ["Młody", "Dojrzały", "Starożytny", "Mityczny"];

    public static int Cap(int stage) => MaxLevel + (LevelsPerStage * Math.Clamp(stage, 0, MaxStage));

    public static int Level(int xp, int stage = 0) => Math.Min(Cap(stage), 1 + Math.Max(0, xp) / XpPerLevel);

    /// <summary>Evolution cost (2.2): stones and demon blood, growing with the stage.</summary>
    public static (long Stones, string Item, int Quantity) EvolveCost(int stage) => (2000L * (stage + 1) * (stage + 1), "demon_blood", 2 * (stage + 1));

    /// <summary>Taming chance in ‰ after a victory: 150‰ base, +5‰ per point of fate, at most 400‰.</summary>
    public static int TameChancePermille(int fate) => Math.Clamp(150 + 5 * fate, 150, 400);

    /// <summary>Deterministic roll from the battle seed (replayable, not influenced by the client).</summary>
    public static bool Tamed(ulong battleSeed, int chancePermille) => (int)((battleSeed ^ 0x5EED_9E7Au) % 1000UL) < chancePermille;

    /// <summary>XP for a victory with the pet at your side; bosses teach more.</summary>
    public static int XpForWin(NpcDefinition foe) => foe.Boss ? 5 : 1 + Math.Max(0, foe.Tier - 1);

    /// <summary>The pet fights at your side: it strikes every 3 turns for (4 × tier + 2 × level)% of a basic blow.</summary>
    public static JianghuGo.Modules.Combat.Contracts.PerkBonus Companion(NpcDefinition species, int level, int stage = 0) =>
        new(PetPower: (4 * Math.Max(1, species.Tier)) + (2 * level) + (5 * stage), PetEvery: stage >= MaxStage ? 2 : 3);

    /// <summary>Stat bonus: the species' three strongest stats, (5 + level)% of their value each, at least +1.</summary>
    public static IReadOnlyDictionary<string, int> Bonuses(NpcDefinition species, int level, int stage = 0) =>
        species.Stats.OrderByDescending(s => s.Value).ThenBy(s => s.Key, StringComparer.Ordinal).Take(3)
            .ToDictionary(s => s.Key, s => Math.Max(1, s.Value * (5 + level + (5 * stage)) / 100));
}
