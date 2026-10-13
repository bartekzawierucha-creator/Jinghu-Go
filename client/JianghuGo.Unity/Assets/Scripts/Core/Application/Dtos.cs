using System;
using System.Collections.Generic;

namespace JianghuGo.Client.Application
{
    // Plain mutable DTOs: compatible with Newtonsoft and System.Text.Json (case-insensitive) in Unity's C# 9.
    // The client never computes stats, costs or outcomes; it only displays what the server returns.

    public sealed class TokenPairDto
    {
        public string AccessToken { get; set; }
        public DateTimeOffset AccessTokenExpiresAt { get; set; }
        public string RefreshToken { get; set; }
        public DateTimeOffset RefreshTokenExpiresAt { get; set; }
    }

    public sealed class TraitDto
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public string Rarity { get; set; }
        public string NamePl { get; set; }
        public string NameEn { get; set; }
        public Dictionary<string, int> Modifiers { get; set; } = new Dictionary<string, int>();
    }

    public sealed class StatDto
    {
        public string Id { get; set; }
        public int Value { get; set; }
    }

    public sealed class DraftDto
    {
        public Guid Id { get; set; }
        public int Version { get; set; }
        public string RulesVersion { get; set; }
        public List<TraitDto> Traits { get; set; } = new List<TraitDto>();
        public List<StatDto> Stats { get; set; } = new List<StatDto>();
        public int RerollCount { get; set; }
        public int RerollsRemaining { get; set; }
        public int DestinyTokens { get; set; }
        public int RerollBaseCost { get; set; }
        public int RerollCostPerLockedTrait { get; set; }
        public int MaxLockedTraits { get; set; }

        /// <summary>Display-only preview; the server charges authoritatively.</summary>
        public int PreviewRerollCost(int lockedCount) => RerollBaseCost + RerollCostPerLockedTrait * lockedCount;
    }

    public sealed class CharacterDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string RulesVersion { get; set; }
        public List<TraitDto> Traits { get; set; } = new List<TraitDto>();
        public List<StatDto> Stats { get; set; } = new List<StatDto>();
        public DateTimeOffset CreatedAt { get; set; }
    }

    public sealed class CharacterListDto
    {
        public List<CharacterDto> Items { get; set; } = new List<CharacterDto>();
    }

    public sealed class ProblemDto
    {
        public string Code { get; set; }
        public string Detail { get; set; }
        public int Status { get; set; }
        public string CorrelationId { get; set; }
    }
}

namespace JianghuGo.Client.Application
{
    public sealed class CultivationDto
    {
        public Guid CharacterId { get; set; }
        public string Path { get; set; }
        public string RealmId { get; set; }
        public string RealmNamePl { get; set; }
        public int Stage { get; set; }
        public int StagesInRealm { get; set; }
        public bool IsPeak { get; set; }
        public long Qi { get; set; }
        public long QiRequired { get; set; }
        public double BreakthroughChance { get; set; }
        public bool MajorBreakthrough { get; set; }
        public int Version { get; set; }
        public DateTimeOffset? DeviationUntil { get; set; }
        public DateTimeOffset? MeditationStartedAt { get; set; }
        public long DailyMeditationQi { get; set; }
        public long DailyMeditationQiCap { get; set; }
        public int DailySteps { get; set; }
        public int DailyStepCap { get; set; }

        /// <summary>Server's current meditation rate (Qi per minute) for this character.</summary>
        public double MeditationQiPerMinute { get; set; }
        public int MeditationMaxMinutes { get; set; }
        public int MeditationMinMinutes { get; set; }

        public bool IsMeditating => MeditationStartedAt.HasValue;
        public bool CanAttemptBreakthrough => !IsPeak && Qi >= QiRequired && !IsMeditating;
    }

    public sealed class MeditationResultDto
    {
        public long QiGained { get; set; }
        public int MinutesCounted { get; set; }
        public bool DailyCapReached { get; set; }
        public CultivationDto State { get; set; }
    }

    public sealed class StepsResultDto
    {
        public int AcceptedSteps { get; set; }
        public bool Clipped { get; set; }
        public long QiGained { get; set; }
        public CultivationDto State { get; set; }
    }

    public sealed class BreakthroughResultDto
    {
        public bool Success { get; set; }
        public double Chance { get; set; }
        public long QiSpent { get; set; }
        public CultivationDto State { get; set; }
    }

    public sealed class NotificationDto
    {
        public Guid Id { get; set; }
        public string Type { get; set; }
        public Dictionary<string, object> Payload { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ReadAt { get; set; }
    }

    public sealed class NotificationPageDto
    {
        public List<NotificationDto> Items { get; set; } = new List<NotificationDto>();
        public Guid? NextBefore { get; set; }
    }
}

namespace JianghuGo.Client.Application
{
    public sealed class CombatSlotDto
    {
        public int Slot { get; set; }
        public string Kind { get; set; }
        public string TechniqueId { get; set; }
        public string NamePl { get; set; }
        public int Level { get; set; }
        public int QiCost { get; set; }
        public int Cooldown { get; set; }
        public string Unavailable { get; set; }
        public bool Usable => TechniqueId != null && Unavailable == null;
    }

    public sealed class FighterDto
    {
        public string Name { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Qi { get; set; }
        public int MaxQi { get; set; }
        public List<EffectDto> Effects { get; set; } = new List<EffectDto>();

        public int EffectMagnitude(string type)
        {
            foreach (var e in Effects) if (e.Type == type) return e.Magnitude;
            return 0;
        }

        public bool Has(string type) => Effects.Exists(e => e.Type == type);
    }

    public sealed class TurnEventDto
    {
        public string Actor { get; set; }
        public string Action { get; set; }
        public string TechniqueId { get; set; }
        public bool? Hit { get; set; }
        public int Damage { get; set; }
        public int Reflected { get; set; }
        public int QiStolen { get; set; }
        public string EffectApplied { get; set; }
        public int Distance { get; set; }
        public int HpA { get; set; }
        public int HpB { get; set; }
        public int Healed { get; set; }
        public int Momentum { get; set; }
        public bool Exploit { get; set; }
    }

    /// <summary>What the opponent will do this turn (telegraphed by the server for NPC battles).</summary>
    public sealed class IntentPreviewDto
    {
        public string Action { get; set; }
        public string TechniqueId { get; set; }
        public string NamePl { get; set; }
    }

    public sealed class EffectDto
    {
        public string Type { get; set; }
        public int Magnitude { get; set; }
        public int TurnsLeft { get; set; }
    }

    public sealed class BattleDto
    {
        public Guid Id { get; set; }
        public string Kind { get; set; }
        public string Status { get; set; }
        public string MySide { get; set; }
        public int Turn { get; set; }
        public int Distance { get; set; }
        public DateTimeOffset Deadline { get; set; }
        public FighterDto Me { get; set; }
        public FighterDto Opponent { get; set; }
        public List<CombatSlotDto> MySlots { get; set; } = new List<CombatSlotDto>();
        public bool MyActionSubmitted { get; set; }
        public bool OpponentActionSubmitted { get; set; }
        public List<TurnEventDto> LastTurnEvents { get; set; } = new List<TurnEventDto>();
        public string Result { get; set; }
        public string NpcId { get; set; }
        public string SeedHash { get; set; }
        public string Seed { get; set; }
        public long RewardQi { get; set; }
        public string TamedPetNpcId { get; set; }
        public IntentPreviewDto OpponentIntent { get; set; }
        public int? TowerFloor { get; set; }
        public int? BossCycle { get; set; }
        public int DamageDealt { get; set; }

        public bool IsActive => Status == "active";
        public bool IsMine(TurnEventDto e) => e.Actor == MySide;
    }
}

namespace JianghuGo.Client.Application
{
    public sealed class ExplorationSessionDto
    {
        public Guid Id { get; set; }
        public string Status { get; set; }
        public double DistanceM { get; set; }
        public int AcceptedSamples { get; set; }
        public int DroppedSamples { get; set; }
        public int Anomalies { get; set; }
        public bool Flagged { get; set; }
        public bool PositionVerified { get; set; }
        public string PositionProblem { get; set; }
        public DateTimeOffset? RelocationUntil { get; set; }
    }

    public sealed class SamplesResultDto
    {
        public List<string> Outcomes { get; set; } = new List<string>();
        public ExplorationSessionDto Session { get; set; }
    }

    public sealed class NodeDto
    {
        public string Key { get; set; }
        public string Kind { get; set; }
        public string NamePl { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public int DistanceM { get; set; }
        public bool InRange { get; set; }
        public bool Harvested { get; set; }
        public long RewardQi { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }

    public sealed class FlameSiteDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public long DistanceM { get; set; }
        public bool InRange { get; set; }
        public DateTimeOffset? AvailableAt { get; set; }
    }

    public sealed class FlameSiteInfoDto
    {
        public string Id { get; set; }
        public string NamePl { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    public sealed class FlameSitesDto
    {
        public List<FlameSiteInfoDto> Items { get; set; } = new List<FlameSiteInfoDto>();
        public double RadiusM { get; set; }
        public int CooldownHours { get; set; }
    }

    public sealed class WorldEventDto
    {
        public string Kind { get; set; }
        public string NamePl { get; set; }
        public int RewardMultiplierPermille { get; set; }
        public DateTimeOffset EndsAt { get; set; }
    }

    public sealed class NearbyDto
    {
        public bool PositionVerified { get; set; }
        public string PositionProblem { get; set; }
        public WorldEventDto Event { get; set; }
        public List<NodeDto> Nodes { get; set; } = new List<NodeDto>();
        public FlameSiteDto NearestFlameSite { get; set; }
    }

    public sealed class WorldInteractionDto
    {
        public string Kind { get; set; }
        public long RewardQi { get; set; }
        public Guid? BattleId { get; set; }

        /// <summary>Materials gathered; they arrive in the bag shortly after (credited asynchronously by the server).</summary>
        public List<GatheredItemDto> Items { get; set; } = new List<GatheredItemDto>();
    }

    public sealed class GatheredItemDto
    {
        public string ItemId { get; set; }
        public int Quantity { get; set; }
    }
}
