using System;
using System.Collections.Generic;

namespace JianghuGo.Client.Application
{
    // 1.9: friends, direct messages and mail. All limits, fees and validity are decided by the server.

    public sealed class FriendDto
    {
        public Guid CharacterId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Status { get; set; }
        public bool Incoming { get; set; }
        public int Unread { get; set; }
        public DateTimeOffset Since { get; set; }
    }

    public sealed class FriendsDto
    {
        public List<FriendDto> Friends { get; set; } = new List<FriendDto>();
        public List<FriendDto> Incoming { get; set; } = new List<FriendDto>();
        public List<FriendDto> Outgoing { get; set; } = new List<FriendDto>();
        public int UnreadMail { get; set; }
    }

    public sealed class DirectMessageDto
    {
        public Guid Id { get; set; }
        public Guid FromCharacterId { get; set; }
        public string Text { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public bool Mine { get; set; }
    }

    public sealed class DirectMessagePageDto
    {
        public List<DirectMessageDto> Items { get; set; } = new List<DirectMessageDto>();
    }

    public sealed class MailDto
    {
        public Guid Id { get; set; }
        public Guid FromCharacterId { get; set; }
        public string FromName { get; set; }
        public string Text { get; set; }
        public long Stones { get; set; }
        public string ItemId { get; set; }
        public string ItemNamePl { get; set; }
        public long Quantity { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public bool Claimed { get; set; }
    }

    public sealed class MailPageDto
    {
        public List<MailDto> Items { get; set; } = new List<MailDto>();
    }
}
