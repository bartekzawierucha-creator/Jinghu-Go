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

        public bool IsActive => Status == "active";
        public bool IsMine(TurnEventDto e) => e.Actor == MySide;
    }
}
