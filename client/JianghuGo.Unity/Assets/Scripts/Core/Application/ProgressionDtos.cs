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
        public bool Secret { get; set; }
        public string RiddlePl { get; set; }
        public string Rarity { get; set; }

        /// <summary>Seasonal quests: the last day (MM-dd).</summary>
        public string Until { get; set; }
    }

    public sealed class StoryDto
    {
        public int Chapter { get; set; }
        public int Chapters { get; set; }
        public string Id { get; set; }
        public string TitlePl { get; set; }
        public string TextPl { get; set; }
        public string GoalPl { get; set; }
        public string Metric { get; set; }
        public long Target { get; set; }
        public long Progress { get; set; }
        public long Stones { get; set; }
        public string Item { get; set; }
        public string ItemNamePl { get; set; }
        public int ItemQuantity { get; set; }
        public bool Completed { get; set; }
        public bool Finished { get; set; }
    }

    public sealed class TitleDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string Rarity { get; set; }
        public string Source { get; set; }
        public bool Selected { get; set; }
    }

    public sealed class TitleListDto
    {
        public List<TitleDto> Items { get; set; } = new List<TitleDto>();
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
        public List<QuestDto> Seasonal { get; set; } = new List<QuestDto>();
        public StoryDto Story { get; set; }
        public List<TitleDto> AllTitles { get; set; } = new List<TitleDto>();
        public string SelectedTitleId { get; set; }
        public int SecretsFound { get; set; }
        public int SecretsTotal { get; set; }

        /// <summary>This week's world event and community challenge (1.9).</summary>
        public WeeklyDto Weekly { get; set; }

        /// <summary>Season pass of this quarter (1.9) and the account's Destiny Tokens.</summary>
        public SeasonPassDto Pass { get; set; }
        public int DestinyTokens { get; set; }

        /// <summary>Birth sign (2.2).</summary>
        public ZodiacDto Zodiac { get; set; }
    }

    public sealed class ZodiacDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string Hanzi { get; set; }
        public string Stat { get; set; }
        public int Bonus { get; set; }
        public string DescriptionPl { get; set; }
        public string TodayId { get; set; }
        public string TodayNamePl { get; set; }
        public bool LuckyToday { get; set; }

        /// <summary>Special physique, if the cultivator was born with one (2.2).</summary>
        public string PhysiqueId { get; set; }
        public string PhysiqueNamePl { get; set; }
        public string PhysiqueDescriptionPl { get; set; }
        public string PhysiqueRarity { get; set; }
    }

    public sealed class PassLevelDto
    {
        public int Level { get; set; }
        public long Stones { get; set; }
        public string Item { get; set; }
        public string ItemNamePl { get; set; }
        public int ItemQuantity { get; set; }
        public int Tokens { get; set; }
        public bool Reached { get; set; }
        public bool Claimed { get; set; }
    }

    public sealed class SeasonPassDto
    {
        public string Season { get; set; }
        public string EndsOn { get; set; }
        public long Xp { get; set; }
        public int Level { get; set; }
        public int Levels { get; set; }
        public int XpPerLevel { get; set; }
        public int Claimable { get; set; }
        public List<PassLevelDto> Rewards { get; set; } = new List<PassLevelDto>();
        public Dictionary<string, int> XpPerMetric { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>One realm on the Road of 66 Realms (1.9).</summary>
    public sealed class RoadRealmDto
    {
        public int Index { get; set; }
        public string NamePl { get; set; }
        public string Hanzi { get; set; }
        public int Era { get; set; }
        public string EraPl { get; set; }
        public string TribulationPl { get; set; }
        public string UnlockPl { get; set; }
        public bool Reached { get; set; }
        public bool Current { get; set; }
        public List<string> Unlocks { get; set; } = new List<string>();
    }

    public sealed class RoadDto
    {
        public int Realm { get; set; }
        public int Realms { get; set; }
        public List<RoadRealmDto> Steps { get; set; } = new List<RoadRealmDto>();
    }

    public sealed class ChallengeTierDto
    {
        public int Tier { get; set; }
        public long Threshold { get; set; }
        public long Stones { get; set; }
        public bool Reached { get; set; }
        public bool Claimed { get; set; }
    }

    public sealed class WeeklyDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string DescriptionPl { get; set; }
        public string Metric { get; set; }
        public string Until { get; set; }
        public QuestDto Personal { get; set; }
        public long CommunityTotal { get; set; }
        public long CommunityTarget { get; set; }
        public long MyContribution { get; set; }
        public long MinContribution { get; set; }
        public List<ChallengeTierDto> Tiers { get; set; } = new List<ChallengeTierDto>();
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

        /// <summary>Evolution (2.2).</summary>
        public int Stage { get; set; }
        public string StagePl { get; set; }
        public int LevelCap { get; set; } = 10;
        public bool CanEvolve { get; set; }
        public long EvolveStones { get; set; }
        public string EvolveItem { get; set; }
        public int EvolveQuantity { get; set; }
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

    /// <summary>The cultivator's combat power and each foe scaled to their realm, with what a win pays (2.2).</summary>
    public sealed class CombatScoutDto
    {
        public int Realm { get; set; }
        public long MyPower { get; set; }
        public List<FoeScoutDto> Foes { get; set; } = new List<FoeScoutDto>();
    }

    public sealed class FoeScoutDto
    {
        public string NpcId { get; set; }
        public long Power { get; set; }
        public long RewardQi { get; set; }
        public long RewardStones { get; set; }
    }

    public sealed class CombatCatalogDto
    {
        public List<CombatNpcDto> Npcs { get; set; } = new List<CombatNpcDto>();
        public Dictionary<string, PathPerksDto> Paths { get; set; } = new Dictionary<string, PathPerksDto>();
        public ComboDto Combo { get; set; }
        public string WorldBossNpcId { get; set; }
    }

    public sealed class ComboDto
    {
        public int MomentumPermillePerStack { get; set; }
        public int MaxMomentum { get; set; }
        public int ExploitPermille { get; set; }
    }

    public sealed class RankDto
    {
        public int Rank { get; set; }
        public Guid CharacterId { get; set; }
        public string Name { get; set; }
        public long Value { get; set; }
    }

    public sealed class RewardLineDto
    {
        public string ItemId { get; set; }
        public string NamePl { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class TowerFloorDto
    {
        public int Floor { get; set; }
        public string NpcId { get; set; }
        public string NamePl { get; set; }
        public int Tier { get; set; }
        public bool Guardian { get; set; }
        public long Qi { get; set; }
        public long Stones { get; set; }
        public List<RewardLineDto> Items { get; set; } = new List<RewardLineDto>();
    }

    /// <summary>Tower of Trials: floors cleared, the next fight, the next milestone and the leaderboard.</summary>
    public sealed class TowerDto
    {
        public int BestFloor { get; set; }
        public int Floors { get; set; }
        public TowerFloorDto Next { get; set; }
        public TowerFloorDto NextMilestone { get; set; }
        public List<RankDto> Top { get; set; } = new List<RankDto>();
        public RankDto Me { get; set; }
    }

    /// <summary>The shared world boss of the current cycle.</summary>
    public sealed class WorldBossDto
    {
        public int Cycle { get; set; }
        public string NpcId { get; set; }
        public string NamePl { get; set; }
        public int PoolHp { get; set; }
        public int HpLeft { get; set; }
        public bool Defeated { get; set; }
        public DateTimeOffset EndsAt { get; set; }
        public int AttemptsLeft { get; set; }
        public int AttemptsPerDay { get; set; }
        public long MyDamage { get; set; }
        public int? MyRank { get; set; }
        public bool CanClaim { get; set; }
        public bool Claimed { get; set; }
        public int MinDamageForKillReward { get; set; }
        public long KillStones { get; set; }
        public List<RewardLineDto> KillItems { get; set; } = new List<RewardLineDto>();
        public List<RankDto> Top { get; set; } = new List<RankDto>();
        public int? UnclaimedCycle { get; set; }
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
