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
