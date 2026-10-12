using System;
using System.Collections.Generic;

namespace JianghuGo.Client.Application
{
    // Sect and territory DTOs. Permissions are enforced by the server; the client only hides buttons it knows will fail.

    public sealed class SectMemberDto
    {
        public Guid CharacterId { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
        public long Contribution { get; set; }
        public DateTimeOffset JoinedAt { get; set; }
    }

    public sealed class SectDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid LeaderCharacterId { get; set; }
        public int MemberCount { get; set; }
        public int MaxMembers { get; set; }
        public double? SeatLat { get; set; }
        public double? SeatLon { get; set; }
        public int Version { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public bool Disbanded { get; set; }
        public List<SectMemberDto> Members { get; set; } = new List<SectMemberDto>();
    }

    public sealed class SectSummaryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public int MemberCount { get; set; }
        public bool HasSeat { get; set; }
    }

    public sealed class SectPageDto
    {
        public List<SectSummaryDto> Items { get; set; } = new List<SectSummaryDto>();
        public Guid? Next { get; set; }
    }

    public sealed class SectApplicationDto
    {
        public Guid Id { get; set; }
        public Guid SectId { get; set; }
        public Guid CharacterId { get; set; }
        public string CharacterName { get; set; }
        public string Message { get; set; }
        public string Status { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public sealed class MySectDto
    {
        public SectDto Sect { get; set; }
        public string MyRole { get; set; }
        public long Treasury { get; set; }
        public List<SectApplicationDto> PendingApplications { get; set; } = new List<SectApplicationDto>();

        public bool IsLeader => MyRole == "leader";
        public bool CanManage => MyRole == "leader" || MyRole == "elder";
    }

    public sealed class WarDto
    {
        public Guid Id { get; set; }
        public string TerritoryKey { get; set; }
        public Guid AttackerSectId { get; set; }
        public Guid DefenderSectId { get; set; }
        public string Status { get; set; }
        public string Phase { get; set; }
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset EndsAt { get; set; }
        public int AttackerScore { get; set; }
        public int DefenderScore { get; set; }
        public Guid? WinnerSectId { get; set; }
    }

    public sealed class OwnershipChangeDto
    {
        public Guid? FromSectId { get; set; }
        public Guid? ToSectId { get; set; }
        public string Reason { get; set; }
        public DateTimeOffset At { get; set; }
    }

    public sealed class TerritoryDto
    {
        public string Key { get; set; }
        public double SouthLat { get; set; }
        public double WestLon { get; set; }
        public double SizeDegrees { get; set; }
        public Guid? OwnerSectId { get; set; }
        public string OwnerName { get; set; }
        public DateTimeOffset? NextTributeAt { get; set; }
        public WarDto War { get; set; }
        public List<OwnershipChangeDto> History { get; set; } = new List<OwnershipChangeDto>();
    }

    public sealed class SectTerritoriesDto
    {
        public Guid SectId { get; set; }
        public int Owned { get; set; }
        public int Max { get; set; }
        public List<TerritoryDto> Territories { get; set; } = new List<TerritoryDto>();
        public List<WarDto> Wars { get; set; } = new List<WarDto>();
    }
}
