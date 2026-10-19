using System;
using System.Collections.Generic;

namespace JianghuGo.Client.Application
{
    // ---- path arts: flames, venoms, Gu, tempering, sword intent, branches ------------------------------------------

    public sealed class ItemLineDto
    {
        public string Item { get; set; }
        public string NamePl { get; set; }
        public int Quantity { get; set; }
        public long Owned { get; set; }
    }

    public sealed class FlameDto
    {
        public string Id { get; set; }
        public int Rank { get; set; }
        public string NamePl { get; set; }
        public string SourcePl { get; set; }

        /// <summary>unknown / seen / subdued / absorbing / owned.</summary>
        public string State { get; set; }
        public int Level { get; set; }
        public int Xp { get; set; }
        public int XpNext { get; set; }
        public bool Slotted { get; set; }
        public int MinRealm { get; set; }
        public int AbsorbHours { get; set; }
        public Dictionary<string, int> Stats { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> Perks { get; set; } = new Dictionary<string, int>();
        public int AlchemyPermille { get; set; }
        public int ForgePermille { get; set; }
        public string FusedWith { get; set; }
    }

    public sealed class FlamesDto
    {
        public List<FlameDto> All { get; set; } = new List<FlameDto>();
        public int Slots { get; set; }
        public int SlotsMax { get; set; }
        public List<int> SlotRealms { get; set; } = new List<int>();
        public List<string> Slotted { get; set; } = new List<string>();
        public string Subdued { get; set; }
        public Guid? SpiritBattleId { get; set; }
        public string AbsorbingId { get; set; }
        public DateTimeOffset? AbsorbUntil { get; set; }
        public int? AbsorbChancePermille { get; set; }
        public DateTimeOffset? BurnedUntil { get; set; }
        public long Traces { get; set; }
        public int TracesPerUse { get; set; }
        public long FeedItems { get; set; }
        public int XpPerFeed { get; set; }
        public int MaxLevel { get; set; }
        public List<string> Fusions { get; set; } = new List<string>();
        public int Owned { get; set; }
        public int FusionBonusPermille { get; set; }
    }

    public sealed class VenomDto
    {
        public string Item { get; set; }
        public string NamePl { get; set; }
        public string Rarity { get; set; }
        public string Element { get; set; }
        public int Xp { get; set; }
        public int Absorbed { get; set; }
        public long Owned { get; set; }
    }

    public sealed class ToxinPerkDto
    {
        public int Level { get; set; }
        public string DescriptionPl { get; set; }
        public bool Unlocked { get; set; }
    }

    public sealed class CoatingDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public List<ItemLineDto> Venoms { get; set; } = new List<ItemLineDto>();
        public int Battles { get; set; }
        public Dictionary<string, int> Perks { get; set; } = new Dictionary<string, int>();
        public bool Craftable { get; set; }
    }

    public sealed class ToxinDto
    {
        public int Level { get; set; }
        public int Xp { get; set; }
        public int? NextXp { get; set; }
        public int MaxLevel { get; set; }
        public List<VenomDto> Venoms { get; set; } = new List<VenomDto>();
        public List<ToxinPerkDto> Perks { get; set; } = new List<ToxinPerkDto>();
        public DateTimeOffset? PoisonedUntil { get; set; }
        public long Antidotes { get; set; }
        public string ActiveCoating { get; set; }
        public int CoatingCharges { get; set; }
        public List<CoatingDto> Coatings { get; set; } = new List<CoatingDto>();
        public int FailPermille { get; set; }
    }

    public sealed class GuWormDto
    {
        public int Slot { get; set; }
        public string Type { get; set; }
        public string NamePl { get; set; }
        public int Level { get; set; }
        public int Rank { get; set; }
        public DateTimeOffset? GrowingUntil { get; set; }
    }

    public sealed class GuTypeDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string DescriptionPl { get; set; }
        public Dictionary<string, int> PerLevel { get; set; } = new Dictionary<string, int>();
    }

    public sealed class GuDto
    {
        public int Slots { get; set; }
        public List<GuWormDto> Worms { get; set; } = new List<GuWormDto>();
        public List<GuTypeDto> Types { get; set; } = new List<GuTypeDto>();
        public long Herbs { get; set; }
        public int HatchHerbs { get; set; }
        public int FeedHerbs { get; set; }
        public int HoursPerLevel { get; set; }
        public int MaxLevel { get; set; }
        public int MaxRank { get; set; }
    }

    public sealed class BathDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public List<ItemLineDto> Items { get; set; } = new List<ItemLineDto>();
        public int Hours { get; set; }
        public int Xp { get; set; }
        public bool Affordable { get; set; }
    }

    public sealed class TemperDto
    {
        public int Level { get; set; }
        public int MaxLevel { get; set; }
        public int Xp { get; set; }
        public int XpNeeded { get; set; }
        public int ChancePermille { get; set; }
        public bool NeedsPill { get; set; }
        public long Pills { get; set; }
        public string StageNamePl { get; set; }
        public string NextStageNamePl { get; set; }
        public int? NextStageAt { get; set; }
        public string Bath { get; set; }
        public DateTimeOffset? BathUntil { get; set; }
        public List<BathDto> Baths { get; set; } = new List<BathDto>();
        public int Attempts { get; set; }
        public string Badge { get; set; }
        public int StepsPerXp { get; set; }
    }

    public sealed class SwordDto
    {
        public int Level { get; set; }
        public int MaxLevel { get; set; }
        public int Xp { get; set; }
        public int? NextXp { get; set; }
        public long Echoes { get; set; }
        public int XpPerEcho { get; set; }
        public string Badge { get; set; }
    }

    public sealed class BranchOptionDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string DescriptionPl { get; set; }
        public Dictionary<string, int> PerTier { get; set; } = new Dictionary<string, int>();
    }

    public sealed class BranchDto
    {
        public string Chosen { get; set; }
        public int Tier { get; set; }
        public List<int> TierRealms { get; set; } = new List<int>();
        public List<BranchOptionDto> Options { get; set; } = new List<BranchOptionDto>();
        public long RespecStones { get; set; }

        /// <summary>The path's ultimate (1.7): chosen once at the capstone realm.</summary>
        public int CapstoneRealm { get; set; } = 65;
        public string Capstone { get; set; }
        public List<BranchOptionDto> Capstones { get; set; } = new List<BranchOptionDto>();
    }

    public sealed class NoticeDto
    {
        public string Kind { get; set; }
        public string TextPl { get; set; }
        public DateTimeOffset At { get; set; }
    }

    public sealed class ArtsTitleDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string Rarity { get; set; }
    }

    public sealed class ArtsDto
    {
        public Guid CharacterId { get; set; }
        public string Path { get; set; }
        public int Realm { get; set; }
        public string RulesVersion { get; set; }
        public FlamesDto Flames { get; set; } = new FlamesDto();
        public ToxinDto Toxin { get; set; } = new ToxinDto();
        public GuDto Gu { get; set; } = new GuDto();
        public TemperDto Temper { get; set; } = new TemperDto();
        public SwordDto Sword { get; set; } = new SwordDto();
        public BranchDto Branch { get; set; } = new BranchDto();
        public List<NoticeDto> Notices { get; set; } = new List<NoticeDto>();
        public List<ArtsTitleDto> Titles { get; set; } = new List<ArtsTitleDto>();
        public Dictionary<string, int> TotalPerks { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> Stats { get; set; } = new Dictionary<string, int>();
        public int AlchemyPermille { get; set; }
        public int ForgePermille { get; set; }
        public int Version { get; set; }
    }

    public sealed class TraceResultDto
    {
        public string FoundFlameId { get; set; }
        public string FoundNamePl { get; set; }
        public ArtsDto State { get; set; }
    }

    public sealed class VenomResultDto
    {
        public int Absorbed { get; set; }
        public int Failed { get; set; }
        public int XpGained { get; set; }
        public bool Poisoned { get; set; }
        public ArtsDto State { get; set; }
    }

    public sealed class TemperResultDto
    {
        public bool Success { get; set; }
        public int ChancePermille { get; set; }
        public ArtsDto State { get; set; }
    }

    public sealed class SpiritBattleDto
    {
        public Guid BattleId { get; set; }
        public ArtsDto State { get; set; }
    }

    // ---- estate: garden, disciples, expeditions -------------------------------------------------------------------

    public sealed class DropDto
    {
        public string Item { get; set; }
        public string NamePl { get; set; }
        public int ChancePermille { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
    }

    public sealed class CropDto
    {
        public string Seed { get; set; }
        public string SeedNamePl { get; set; }
        public string NamePl { get; set; }
        public int Hours { get; set; }
        public int MinRealm { get; set; }
        public List<DropDto> Yields { get; set; } = new List<DropDto>();
        public long SeedsOwned { get; set; }
        public bool Available { get; set; }
    }

    public sealed class PlotDto
    {
        public int Slot { get; set; }
        public string Seed { get; set; }
        public string CropNamePl { get; set; }
        public DateTimeOffset? PlantedAt { get; set; }
        public DateTimeOffset? ReadyAt { get; set; }
        public bool Ready { get; set; }
    }

    public sealed class DiscipleDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Talent { get; set; }
        public string TalentNamePl { get; set; }
        public int Level { get; set; }
        public int Xp { get; set; }
        public int XpNext { get; set; }
        public string Expedition { get; set; }
        public string ExpeditionNamePl { get; set; }
        public DateTimeOffset? Until { get; set; }
        public bool Returned { get; set; }
    }

    public sealed class ExpeditionDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string DescriptionPl { get; set; }
        public int Hours { get; set; }
        public int MinRealm { get; set; }
        public int MinLevel { get; set; }
        public long StonesMin { get; set; }
        public long StonesMax { get; set; }
        public string Talent { get; set; }
        public string TalentNamePl { get; set; }
        public List<DropDto> Drops { get; set; } = new List<DropDto>();
        public bool Available { get; set; }
    }

    public sealed class LootDto
    {
        public string Item { get; set; }
        public string NamePl { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class EstateDto
    {
        public Guid CharacterId { get; set; }
        public int Realm { get; set; }
        public List<PlotDto> Plots { get; set; } = new List<PlotDto>();
        public int PlotsMax { get; set; }
        public List<CropDto> Crops { get; set; } = new List<CropDto>();
        public List<DiscipleDto> Disciples { get; set; } = new List<DiscipleDto>();
        public int DiscipleSlots { get; set; }
        public int DiscipleSlotsMax { get; set; }
        public long? RecruitPrice { get; set; }
        public List<ExpeditionDto> Expeditions { get; set; } = new List<ExpeditionDto>();
        public List<LootDto> Gained { get; set; } = new List<LootDto>();
        public long StonesGained { get; set; }
        public int Version { get; set; }
    }

    // ---- arena ------------------------------------------------------------------------------------------------------

    public sealed class ArenaOpponentDto
    {
        public Guid CharacterId { get; set; }
        public string Name { get; set; }
        public int Rating { get; set; }
        public string LeagueId { get; set; }
        public string LeagueNamePl { get; set; }
        public string Title { get; set; }
        public List<string> Badges { get; set; } = new List<string>();
    }

    public sealed class ArenaDto
    {
        public int Rating { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public string LeagueId { get; set; }
        public string LeagueNamePl { get; set; }
        public string NextLeagueNamePl { get; set; }
        public int? NextLeagueRating { get; set; }
        public int AttemptsLeft { get; set; }
        public int AttemptsPerDay { get; set; }
        public int Rank { get; set; }
        public List<ArenaOpponentDto> Opponents { get; set; } = new List<ArenaOpponentDto>();
        public List<RankDto> Top { get; set; } = new List<RankDto>();
        public bool WeeklyClaimable { get; set; }
        public long WeeklyStones { get; set; }
        public string Week { get; set; }
    }

    // ---- cauldron / forge queue -----------------------------------------------------------------------------------

    public sealed class QueueEntryDto
    {
        public Guid Id { get; set; }
        public string RecipeId { get; set; }
        public string Kind { get; set; }
        public string OutputItem { get; set; }
        public string OutputNamePl { get; set; }
        public int Times { get; set; }
        public int ChancePermille { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset ReadyAt { get; set; }
        public bool Ready { get; set; }
        public int MinutesPerCraft { get; set; }
    }

    public sealed class CollectedDto
    {
        public string RecipeId { get; set; }
        public string OutputItem { get; set; }
        public int Times { get; set; }
        public int Successes { get; set; }
        public int Quantity { get; set; }
    }

    public sealed class CraftQueueDto
    {
        public List<QueueEntryDto> Entries { get; set; } = new List<QueueEntryDto>();
        public List<CollectedDto> Collected { get; set; } = new List<CollectedDto>();
        public int MaxTimes { get; set; }
        public int AlchemyMinutesPerCraft { get; set; }
        public int ForgeMinutesPerCraft { get; set; }
    }

    // ---- sect development -----------------------------------------------------------------------------------------

    public sealed class BuildingDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string DescriptionPl { get; set; }
        public int Level { get; set; }
        public int MaxLevel { get; set; }
        public long? NextCost { get; set; }
        public Dictionary<string, int> PerLevel { get; set; }
        public int AlchemyPermillePerLevel { get; set; }
        public int ForgePermillePerLevel { get; set; }
        public int MissionRewardPermillePerLevel { get; set; }
    }

    public sealed class SectMissionDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public string DescriptionPl { get; set; }
        public string Metric { get; set; }
        public long Target { get; set; }
        public long Progress { get; set; }
        public long TreasuryReward { get; set; }
        public long ContributionReward { get; set; }
        public bool Completed { get; set; }
        public bool Claimed { get; set; }
    }

    public sealed class SectBossDto
    {
        public string NpcId { get; set; }
        public string NamePl { get; set; }
        public int Seals { get; set; }
        public int SealsBroken { get; set; }
        public bool Defeated { get; set; }
        public bool Rewarded { get; set; }
        public int Fighters { get; set; }
        public int AttemptsLeft { get; set; }
        public long TreasuryReward { get; set; }
        public long MemberStones { get; set; }
    }

    public sealed class SectDevelopmentDto
    {
        public Guid SectId { get; set; }
        public string Week { get; set; }
        public long Treasury { get; set; }
        public string MyRole { get; set; }
        public bool CanBuild { get; set; }
        public List<BuildingDto> Buildings { get; set; } = new List<BuildingDto>();
        public List<SectMissionDto> Missions { get; set; } = new List<SectMissionDto>();
        public SectBossDto Boss { get; set; } = new SectBossDto();
    }

    public sealed class SectBossBattleDto
    {
        public Guid BattleId { get; set; }
        public SectDevelopmentDto State { get; set; }
    }
}
