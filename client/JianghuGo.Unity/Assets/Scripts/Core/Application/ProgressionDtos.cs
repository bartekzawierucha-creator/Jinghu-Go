using System;
using System.Collections.Generic;

namespace JianghuGo.Client.Application
{
    public sealed class QuestDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string DescriptionPl { get; set; }
        public string Metric { get; set; }
        public long Target { get; set; }
        public long Progress { get; set; }
        public long Stones { get; set; }
        public bool Completed { get; set; }
        public bool Claimed { get; set; }
        public string TitlePl { get; set; }
        public Dictionary<string, int> Bonus { get; set; }
    }

    public sealed class CheckinDto
    {
        public int Streak { get; set; }
        public int BestStreak { get; set; }
        public bool CheckedInToday { get; set; }
        public long NextReward { get; set; }
    }

    public sealed class QuestBoardDto
    {
        public string Day { get; set; }
        public List<QuestDto> Daily { get; set; } = new List<QuestDto>();
        public List<QuestDto> Achievements { get; set; } = new List<QuestDto>();
        public CheckinDto Checkin { get; set; } = new CheckinDto();
        public List<string> Titles { get; set; } = new List<string>();
        public Dictionary<string, int> TitleBonuses { get; set; } = new Dictionary<string, int>();
    }

    public sealed class ClaimResultDto
    {
        public string QuestId { get; set; }
        public long Stones { get; set; }
        public long Balance { get; set; }
        public QuestBoardDto Board { get; set; }
    }

    public sealed class CheckinResultDto
    {
        public int Streak { get; set; }
        public long Stones { get; set; }
        public long Balance { get; set; }
        public QuestBoardDto Board { get; set; }
    }

    public sealed class LeaderDto
    {
        public int Rank { get; set; }
        public Guid CharacterId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public long Value { get; set; }
    }

    public sealed class LeaderPageDto
    {
        public List<LeaderDto> Items { get; set; } = new List<LeaderDto>();
    }

    public sealed class MeridiansDto
    {
        public int PointsEarned { get; set; }
        public int PointsSpent { get; set; }
        public int RealmBonus { get; set; }
        public Dictionary<string, int> Allocated { get; set; } = new Dictionary<string, int>();
        public long ResetQiCost { get; set; }
        public int Free => PointsEarned - PointsSpent;
    }

    public sealed class RankingEntryDto
    {
        public int Rank { get; set; }
        public Guid CharacterId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string RealmId { get; set; }
        public string RealmNamePl { get; set; }
        public int Stage { get; set; }
    }

    public sealed class RankingDto
    {
        public List<RankingEntryDto> Top { get; set; } = new List<RankingEntryDto>();
        public RankingEntryDto Me { get; set; }
    }

    public sealed class NearbyCultivatorDto
    {
        public Guid CharacterId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string DistanceBand { get; set; }
    }

    public sealed class NearbyCultivatorsDto
    {
        public List<NearbyCultivatorDto> Items { get; set; } = new List<NearbyCultivatorDto>();
    }

    public sealed class PetDto
    {
        public string NpcId { get; set; }
        public string NamePl { get; set; }
        public int Level { get; set; }
        public int Xp { get; set; }
        public int XpToNextLevel { get; set; }
        public Dictionary<string, int> Bonuses { get; set; } = new Dictionary<string, int>();
        public DateTimeOffset TamedAt { get; set; }
    }

    public sealed class PetEnvelopeDto
    {
        public PetDto Pet { get; set; }
    }

    public sealed class CombatNpcDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public long RewardQi { get; set; }
        public int Tier { get; set; }
        public bool Boss { get; set; }
        public bool WorldOnly { get; set; }
        public bool Tameable { get; set; }
    }

    public sealed class PathPerksDto
    {
        public string DescriptionPl { get; set; }
        public int QiPermille { get; set; }
        public int QiRegen { get; set; }
        public int GuardPermille { get; set; }
        public int EffectChancePermille { get; set; }
        public int BleedPermille { get; set; }
        public int HealPermille { get; set; }
        public Dictionary<string, int> ScalingDamagePermille { get; set; }
    }

    public sealed class CombatCatalogDto
    {
        public List<CombatNpcDto> Npcs { get; set; } = new List<CombatNpcDto>();
        public Dictionary<string, PathPerksDto> Paths { get; set; } = new Dictionary<string, PathPerksDto>();
    }

    /// <summary>Idle hunt: automatic battles while away.</summary>
    public sealed class HuntDto
    {
        public string NpcId { get; set; }
        public string NpcNamePl { get; set; }
        public int Tier { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset EndsAt { get; set; }
        public DateTimeOffset? NextBattleAt { get; set; }
        public bool Finished { get; set; }
        public int Battles { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public long Qi { get; set; }
        public int MinutesPerBattle { get; set; }
        public int MaxHours { get; set; }
    }

    public sealed class HuntEnvelopeDto
    {
        public HuntDto Hunt { get; set; }
    }

    public sealed class OpenBattlesDto
    {
        public List<BattleDto> Items { get; set; } = new List<BattleDto>();
    }
}
