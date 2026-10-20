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

        public Task<NewsPageDto> GetNewsAsync(CancellationToken ct) =>
            SendAsync<NewsPageDto>("GET", "/api/v1/news?limit=40", null, true, null, ct);

        public Task<BountyPageDto> GetBountiesAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<BountyPageDto>("GET", "/api/v1/bounties?characterId=" + characterId, null, true, null, ct);

        public Task<BountyDto> PostBountyAsync(Guid characterId, string targetName, long stones, CancellationToken ct) =>
            SendAsync<BountyDto>("POST", "/api/v1/bounties", new { characterId, targetName, stones }, true, NewIdempotencyKey(), ct);

        public Task<object> WithdrawBountyAsync(Guid characterId, Guid bountyId, CancellationToken ct) =>
            SendAsync<object>("POST", "/api/v1/bounties/" + bountyId + "/withdraw", new { characterId }, true, null, ct);

        public Task<CodexPageDto> GetCodexAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<CodexPageDto>("GET", "/api/v1/characters/" + characterId + "/quests/codex", null, true, null, ct);

        public Task<CraftOrderPageDto> GetCraftOrdersAsync(Guid characterId, CancellationToken ct) =>
            SendAsync<CraftOrderPageDto>("GET", "/api/v1/market/orders?characterId=" + characterId, null, true, null, ct);

        public Task<CraftOrderDto> PlaceCraftOrderAsync(Guid characterId, string itemId, long quantity, long reward, CancellationToken ct) =>
            SendAsync<CraftOrderDto>("POST", "/api/v1/market/orders", new { characterId, itemId, quantity, reward }, true, NewIdempotencyKey(), ct);

        public Task<object> FulfilCraftOrderAsync(Guid characterId, Guid orderId, CancellationToken ct) =>
            SendAsync<object>("POST", "/api/v1/market/orders/" + orderId + "/fulfil", new { characterId }, true, null, ct);

        public Task<object> CancelCraftOrderAsync(Guid characterId, Guid orderId, CancellationToken ct) =>
            SendAsync<object>("POST", "/api/v1/market/orders/" + orderId + "/cancel", new { characterId }, true, null, ct);

        public Task<object> ReportCrashAsync(string appVersion, int androidSdk, string exceptionType, string stack, CancellationToken ct) =>
            SendAsync<object>("POST", "/api/v1/telemetry/crash", new { appVersion, androidSdk, exceptionType, stack }, true, null, ct);

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
