using System;
using System.Collections.Generic;

namespace JianghuGo.Client.Application
{
    // Loadout and Dao Lab DTOs. Points, allowance and validity are always computed by the server.

    public sealed class LoadoutSlotDto
    {
        public int Slot { get; set; }
        public string Kind { get; set; }
        public string TechniqueId { get; set; }
        public string NamePl { get; set; }
        public int Level { get; set; }
        public int QiCost { get; set; }
    }

    public sealed class KnownTechniqueDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string Slot { get; set; }
        public int Level { get; set; }
        public int Xp { get; set; }
        public int XpToNextLevel { get; set; }

        /// <summary>Realm the technique opens at (1.6); locked ones are listed so the player sees what is coming.</summary>
        public int MinRealm { get; set; }
        public bool Locked { get; set; }

        /// <summary>Highest level usable at the current realm (mastery beyond it waits).</summary>
        public int LevelCap { get; set; } = 10;
    }

    public sealed class LoadoutDto
    {
        public Guid CharacterId { get; set; }
        public int Version { get; set; }
        public List<LoadoutSlotDto> Slots { get; set; } = new List<LoadoutSlotDto>();
        public List<KnownTechniqueDto> Known { get; set; } = new List<KnownTechniqueDto>();
    }

    public sealed class EffectSpecDto
    {
        public string Type { get; set; }
        public int ChancePermille { get; set; }
        public int Magnitude { get; set; }
        public int Duration { get; set; }
    }

    public sealed class AttackSpecDto
    {
        public int Power { get; set; }
        public string Scaling { get; set; }
        public int RangeMin { get; set; }
        public int RangeMax { get; set; }
        public int AccuracyPermille { get; set; }
        public EffectSpecDto Effect { get; set; }
    }

    public sealed class DefenseSpecDto { public int BlockPermille { get; set; } }

    public sealed class MovementSpecDto { public int EvasionPermille { get; set; } }

    public sealed class CounterSpecDto
    {
        public int ReflectPermille { get; set; }
        public int QiSteal { get; set; }
    }

    /// <summary>A technique design as the player composes it in the lab (exactly one component matching the slot).</summary>
    public sealed class DaoDesignDto
    {
        public string Name { get; set; }
        public string Slot { get; set; }
        public int QiCost { get; set; }
        public int Cooldown { get; set; }
        public int Speed { get; set; }
        public AttackSpecDto Attack { get; set; }
        public DefenseSpecDto Defense { get; set; }
        public MovementSpecDto Movement { get; set; }
        public CounterSpecDto Counter { get; set; }
    }

    public sealed class DaoPreviewDto
    {
        public bool Valid { get; set; }
        public int Points { get; set; }
        public int Allowance { get; set; }
        public List<string> Problems { get; set; } = new List<string>();
    }

    public sealed class DaoTechniqueDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string Slot { get; set; }
        public int QiCost { get; set; }
        public int Cooldown { get; set; }
        public int Speed { get; set; }
        public AttackSpecDto Attack { get; set; }
        public DefenseSpecDto Defense { get; set; }
        public MovementSpecDto Movement { get; set; }
        public CounterSpecDto Counter { get; set; }
        public Guid AuthorCharacterId { get; set; }
        public string AuthorName { get; set; }
        public int Points { get; set; }
        public int Allowance { get; set; }
        public string RulesVersion { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public sealed class KnownDaoTechniqueDto
    {
        public DaoTechniqueDto Technique { get; set; }
        public string Source { get; set; }
        public DateTimeOffset LearnedAt { get; set; }
    }

    public sealed class KnownDaoPageDto
    {
        public List<KnownDaoTechniqueDto> Items { get; set; } = new List<KnownDaoTechniqueDto>();
    }

    public sealed class ScrollDto
    {
        public Guid InstanceId { get; set; }
        public int Version { get; set; }
        public string TechniqueId { get; set; }
        public long StonesSpent { get; set; }
        public long Balance { get; set; }
    }
}
