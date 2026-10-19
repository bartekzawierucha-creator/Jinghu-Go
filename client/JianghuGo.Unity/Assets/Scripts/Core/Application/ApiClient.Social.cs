using System;
using System.Threading;
using System.Threading.Tasks;

namespace JianghuGo.Client.Application
{
    public sealed partial class ApiClient
    {
        private static string Social(Guid characterId) => "/api/v1/characters/" + characterId + "/social";

        public Task<RoadDto> GetRoadAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<RoadDto>("GET", "/api/v1/characters/" + characterId + "/road", null, true, null, ct);

        public Task<FriendsDto> GetFriendsAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<FriendsDto>("GET", Social(characterId) + "/friends", null, true, null, ct);

        public Task<FriendsDto> AddFriendAsync(Guid characterId, string name, CancellationToken ct) =>
            SendAsync<FriendsDto>("POST", Social(characterId) + "/friends", new { name }, true, null, ct);

        public Task<FriendsDto> AcceptFriendAsync(Guid characterId, Guid otherId, CancellationToken ct) =>
            SendAsync<FriendsDto>("POST", Social(characterId) + "/friends/" + otherId + "/accept", null, true, null, ct);

        public Task<FriendsDto> RemoveFriendAsync(Guid characterId, Guid otherId, CancellationToken ct) =>
            SendAsync<FriendsDto>("DELETE", Social(characterId) + "/friends/" + otherId, null, true, null, ct);

        public Task<DirectMessagePageDto> GetConversationAsync(Guid characterId, Guid otherId, CancellationToken ct) =>
            SendAsync<DirectMessagePageDto>("GET", Social(characterId) + "/messages/" + otherId, null, true, null, ct);

        public Task<DirectMessageDto> SendDirectMessageAsync(Guid characterId, Guid otherId, string text, CancellationToken ct) =>
            SendAsync<DirectMessageDto>("POST", Social(characterId) + "/messages/" + otherId, new { text }, true, NewIdempotencyKey(), ct);

        public Task<object> BlockAsync(Guid characterId, Guid otherId, bool on, CancellationToken ct) =>
            SendAsync<object>(on ? "PUT" : "DELETE", Social(characterId) + "/blocks/" + otherId, null, true, null, ct);

        public Task ReportAsync(Guid characterId, Guid reportedCharacterId, string reason, string excerpt, CancellationToken ct) =>
            SendAsync<object>("POST", Social(characterId) + "/reports", new { reportedCharacterId, reason, excerpt }, true, null, ct);

        public Task<MailPageDto> GetMailAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<MailPageDto>("GET", Social(characterId) + "/mail", null, true, null, ct);

        public Task<MailDto> SendMailAsync(Guid characterId, Guid toCharacterId, string text, long stones, string itemId, long quantity, CancellationToken ct) =>
            SendAsync<MailDto>("POST", Social(characterId) + "/mail", new { toCharacterId, text, stones, itemId, quantity }, true, NewIdempotencyKey(), ct);

        public Task<MailDto> ClaimMailAsync(Guid characterId, Guid mailId, CancellationToken ct) =>
            SendAsync<MailDto>("POST", Social(characterId) + "/mail/" + mailId + "/claim", null, true, null, ct);
    }
}
