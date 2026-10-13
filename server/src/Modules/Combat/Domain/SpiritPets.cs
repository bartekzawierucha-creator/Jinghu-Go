namespace JianghuGo.Modules.Combat.Domain;

/// <summary>A spirit beast that follows a cultivator: tamed after a won PvE battle, grows with further victories.</summary>
public sealed record SpiritPet(JianghuGo.Domain.CharacterId CharacterId, string NpcId, int Xp, Guid TamedInBattle, DateTimeOffset TamedAt)
{
    public int Level => SpiritPetRules.Level(Xp);
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

    public static int Level(int xp) => Math.Min(MaxLevel, 1 + Math.Max(0, xp) / XpPerLevel);

    /// <summary>Taming chance in ‰ after a victory: 150‰ base, +5‰ per point of fate, at most 400‰.</summary>
    public static int TameChancePermille(int fate) => Math.Clamp(150 + 5 * fate, 150, 400);

    /// <summary>Deterministic roll from the battle seed (replayable, not influenced by the client).</summary>
    public static bool Tamed(ulong battleSeed, int chancePermille) => (int)((battleSeed ^ 0x5EED_9E7Au) % 1000UL) < chancePermille;

    /// <summary>XP for a victory with the pet at your side; bosses teach more.</summary>
    public static int XpForWin(NpcDefinition foe) => foe.Boss ? 5 : 1 + Math.Max(0, foe.Tier - 1);

    /// <summary>Stat bonus: the species' three strongest stats, (5 + level)% of their value each, at least +1.</summary>
    public static IReadOnlyDictionary<string, int> Bonuses(NpcDefinition species, int level) =>
        species.Stats.OrderByDescending(s => s.Value).ThenBy(s => s.Key, StringComparer.Ordinal).Take(3)
            .ToDictionary(s => s.Key, s => Math.Max(1, s.Value * (5 + level) / 100));
}
