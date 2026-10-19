using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Identity.Api;
using JianghuGo.Modules.Inventory.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Notifications;

// 1.9 — friends, direct messages between friends and mail with attachments (the only way to give an item to another
// player outside the market). Server-authoritative: escrow on send, credit on claim, every move in the ledger and audit.

public sealed record FriendLink(CharacterId CharacterId, CharacterId OtherId, string Status, DateTimeOffset CreatedAt);

public sealed record DirectMessage(Guid Id, CharacterId FromId, CharacterId ToId, string Text, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record Mail(Guid Id, CharacterId FromId, string FromName, CharacterId ToId, string Text, long Stones, string? ItemId, long Quantity,
    DateTimeOffset CreatedAt, DateTimeOffset? ClaimedAt);

public static class SocialRules
{
    public const int MaxFriends = 100;
    public const int MaxText = 300;
    public const int BurstMessages = 5;
    public static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(30);
    public const int MessagesPerDay = 300;
    public const int MailPerDay = 10;
    public const int MailTaxPermille = 50;
    public const long MaxMailStones = 1_000_000;
    public const string Pending = "pending";
    public const string Accepted = "accepted";

    public static string? Clean(string? text, bool allowEmpty = false)
    {
        var t = (text ?? string.Empty).Trim();
        return (t.Length == 0 && !allowEmpty) || t.Length > MaxText || t.Any(char.IsControl) ? null : t;
    }

    public static long Tax(long stones) => (stones * MailTaxPermille + 999) / 1000;
}

public interface ISocialRepository
{
    Task<FriendLink?> LinkAsync(IUnitOfWork uow, CharacterId a, CharacterId b, CancellationToken ct);
    Task UpsertLinkAsync(IUnitOfWork uow, FriendLink link, CancellationToken ct);
    Task DeleteLinkAsync(IUnitOfWork uow, CharacterId a, CharacterId b, CancellationToken ct);

    /// <summary>Links owned by <paramref name="c"/> plus pending requests addressed to it.</summary>
    Task<IReadOnlyList<FriendLink>> LinksAsync(IUnitOfWork uow, CharacterId c, CancellationToken ct);

    Task AddMessageAsync(IUnitOfWork uow, DirectMessage m, CancellationToken ct);
    Task<IReadOnlyList<DirectMessage>> ConversationAsync(IUnitOfWork uow, CharacterId a, CharacterId b, Guid? before, int limit, CancellationToken ct);
    Task<int> CountMessagesSinceAsync(IUnitOfWork uow, CharacterId from, DateTimeOffset since, CancellationToken ct);
    Task MarkReadAsync(IUnitOfWork uow, CharacterId to, CharacterId from, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, int>> UnreadAsync(IUnitOfWork uow, CharacterId to, CancellationToken ct);

    Task AddMailAsync(IUnitOfWork uow, Mail m, CancellationToken ct);
    Task<IReadOnlyList<Mail>> MailAsync(IUnitOfWork uow, CharacterId to, int limit, CancellationToken ct);
    Task<Mail?> FindMailAsync(IUnitOfWork uow, Guid id, CancellationToken ct);

    /// <summary>Marks claimed only if still unclaimed (no double claim under concurrency).</summary>
    Task<bool> TryClaimAsync(IUnitOfWork uow, Guid id, DateTimeOffset now, CancellationToken ct);
    Task<int> CountMailSinceAsync(IUnitOfWork uow, CharacterId from, DateTimeOffset since, CancellationToken ct);
}

public sealed record FriendView(Guid CharacterId, string Name, string Path, string Status, bool Incoming, int Unread, DateTimeOffset Since);

public sealed record FriendsView(IReadOnlyList<FriendView> Friends, IReadOnlyList<FriendView> Incoming, IReadOnlyList<FriendView> Outgoing, int UnreadMail);

public sealed record MessageView(Guid Id, Guid FromCharacterId, string Text, DateTimeOffset CreatedAt, bool Mine);

public sealed record MailView(Guid Id, Guid FromCharacterId, string FromName, string Text, long Stones, string? ItemId, string? ItemNamePl, long Quantity,
    DateTimeOffset CreatedAt, bool Claimed);

public sealed record AddFriendCommand(string? Name);

public sealed record TextCommand(string? Text);

public sealed record SendMailCommand(Guid ToCharacterId, string? Text, long Stones, string? ItemId, long Quantity);

public sealed class SocialService(
    IUnitOfWorkFactory db,
    ISocialRepository repo,
    INotificationRepository notifications,
    ICharacterDirectory characters,
    IInventory inventory,
    IItemCatalog catalog,
    IIdempotencyStore idempotency,
    IAuditLog audit,
    ICorrelationContext correlation,
    TimeProvider clock)
{
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");
    private static readonly Error NotFriends = Error.Forbidden("not_friends", "Only friends can do that.");

    public Task<Result<FriendsView>> FriendsAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        db.RunAsync<Result<FriendsView>>(async uow =>
        {
            var me = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (me is null) return CharacterNotFound;
            return await ViewAsync(uow, me.Id, ct);
        }, ct);

    public Task<Result<FriendsView>> AddFriendAsync(AccountId accountId, Guid characterId, AddFriendCommand cmd, CancellationToken ct) =>
        Write<FriendsView, AddFriendCommand>(accountId, characterId, "social.friend_add", null, cmd, async (uow, me, now) =>
        {
            var other = await characters.FindByNameAsync(uow, cmd.Name ?? string.Empty, ct);
            if (other is null) return Error.NotFound("character_not_found", "No cultivator with that name.");
            if (other.Id == me.Id) return Error.Validation("friend_self", "You cannot befriend yourself.");
            var mine = await repo.LinkAsync(uow, me.Id, other.Id, ct);
            if (mine is not null) return Error.Conflict(mine.Status == SocialRules.Accepted ? "already_friends" : "request_pending", "Already requested or friends.");
            var links = await repo.LinksAsync(uow, me.Id, ct);
            if (links.Count(l => l.CharacterId == me.Id) >= SocialRules.MaxFriends) return Error.LimitReached("friend_limit", $"At most {SocialRules.MaxFriends} friends.");

            var theirs = await repo.LinkAsync(uow, other.Id, me.Id, ct);
            if (theirs is { Status: SocialRules.Pending })
            {
                // They asked first: adding them back accepts.
                await repo.UpsertLinkAsync(uow, theirs with { Status = SocialRules.Accepted }, ct);
                await repo.UpsertLinkAsync(uow, new FriendLink(me.Id, other.Id, SocialRules.Accepted, now), ct);
                await NotifyAsync(uow, other.AccountId, "friend_accepted", new { characterId = me.Id.Value, name = me.Name }, Source("fa", me.Id, other.Id, now), now, ct);
            }
            else
            {
                await repo.UpsertLinkAsync(uow, new FriendLink(me.Id, other.Id, SocialRules.Pending, now), ct);
                await NotifyAsync(uow, other.AccountId, "friend_request", new { characterId = me.Id.Value, name = me.Name }, Source("fr", me.Id, other.Id, now), now, ct);
            }

            await AuditAsync(uow, accountId, "social.friend_add", me.Id.Value, new { other = other.Id.Value }, now, ct);
            return await ViewAsync(uow, me.Id, ct);
        }, ct);

    public Task<Result<FriendsView>> AcceptAsync(AccountId accountId, Guid characterId, Guid otherId, CancellationToken ct) =>
        Write<FriendsView, Guid>(accountId, characterId, "social.friend_accept", null, otherId, async (uow, me, now) =>
        {
            var other = new CharacterId(otherId);
            var theirs = await repo.LinkAsync(uow, other, me.Id, ct);
            if (theirs is not { Status: SocialRules.Pending }) return Error.NotFound("request_not_found", "No pending request from that cultivator.");
            if ((await repo.LinksAsync(uow, me.Id, ct)).Count(l => l.CharacterId == me.Id) >= SocialRules.MaxFriends)
                return Error.LimitReached("friend_limit", $"At most {SocialRules.MaxFriends} friends.");
            await repo.UpsertLinkAsync(uow, theirs with { Status = SocialRules.Accepted }, ct);
            await repo.UpsertLinkAsync(uow, new FriendLink(me.Id, other, SocialRules.Accepted, now), ct);
            if (await characters.FindAsync(uow, otherId, ct) is { } o)
                await NotifyAsync(uow, o.AccountId, "friend_accepted", new { characterId = me.Id.Value, name = me.Name }, Source("fa", me.Id, other, now), now, ct);
            await AuditAsync(uow, accountId, "social.friend_accept", me.Id.Value, new { other = otherId }, now, ct);
            return await ViewAsync(uow, me.Id, ct);
        }, ct);

    /// <summary>Removes a friend, declines an incoming request or withdraws an outgoing one.</summary>
    public Task<Result<FriendsView>> RemoveAsync(AccountId accountId, Guid characterId, Guid otherId, CancellationToken ct) =>
        Write<FriendsView, Guid>(accountId, characterId, "social.friend_remove", null, otherId, async (uow, me, now) =>
        {
            var other = new CharacterId(otherId);
            await repo.DeleteLinkAsync(uow, me.Id, other, ct);
            await repo.DeleteLinkAsync(uow, other, me.Id, ct);
            await AuditAsync(uow, accountId, "social.friend_remove", me.Id.Value, new { other = otherId }, now, ct);
            return await ViewAsync(uow, me.Id, ct);
        }, ct);

    public Task<Result<IReadOnlyList<MessageView>>> ConversationAsync(AccountId accountId, Guid characterId, Guid otherId, Guid? before, int? limit, CancellationToken ct) =>
        db.RunAsync<Result<IReadOnlyList<MessageView>>>(async uow =>
        {
            var me = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (me is null) return CharacterNotFound;
            var other = new CharacterId(otherId);
            var rows = await repo.ConversationAsync(uow, me.Id, other, before, Math.Clamp(limit ?? 50, 1, 100), ct);
            await repo.MarkReadAsync(uow, me.Id, other, clock.GetUtcNow(), ct);
            await uow.CommitAsync(ct);
            return Result<IReadOnlyList<MessageView>>.Success([.. rows.Reverse().Select(m => new MessageView(m.Id, m.FromId.Value, m.Text, m.CreatedAt, m.FromId == me.Id))]);
        }, ct);

    public Task<Result<MessageView>> SendMessageAsync(AccountId accountId, Guid characterId, Guid otherId, string? key, TextCommand cmd, CancellationToken ct) =>
        Write<MessageView, object>(accountId, characterId, "social.message", key, new { otherId, cmd }, async (uow, me, now) =>
        {
            if (SocialRules.Clean(cmd.Text) is not { } text) return Error.Validation("message_invalid", $"A message has 1-{SocialRules.MaxText} characters and no control characters.");
            var other = new CharacterId(otherId);
            if (await repo.LinkAsync(uow, me.Id, other, ct) is not { Status: SocialRules.Accepted }) return NotFriends;
            if (await repo.CountMessagesSinceAsync(uow, me.Id, now - SocialRules.BurstWindow, ct) >= SocialRules.BurstMessages
                || await repo.CountMessagesSinceAsync(uow, me.Id, now.AddDays(-1), ct) >= SocialRules.MessagesPerDay)
                return Error.LimitReached("message_rate_limited", "Too many messages — wait a moment.");
            var m = new DirectMessage(Uuid7.New(), me.Id, other, text, now, null);
            await repo.AddMessageAsync(uow, m, ct);
            return new MessageView(m.Id, me.Id.Value, text, now, true);
        }, ct);

    public Task<Result<IReadOnlyList<MailView>>> MailAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        db.RunAsync<Result<IReadOnlyList<MailView>>>(async uow =>
        {
            var me = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (me is null) return CharacterNotFound;
            var rows = await repo.MailAsync(uow, me.Id, 50, ct);
            return Result<IReadOnlyList<MailView>>.Success([.. rows.Select(View)]);
        }, ct);

    public Task<Result<MailView>> SendMailAsync(AccountId accountId, Guid characterId, string? key, SendMailCommand cmd, CancellationToken ct) =>
        Write<MailView, SendMailCommand>(accountId, characterId, "social.mail_send", key, cmd, async (uow, me, now) =>
        {
            if (SocialRules.Clean(cmd.Text, allowEmpty: true) is not { } text) return Error.Validation("message_invalid", $"At most {SocialRules.MaxText} characters, no control characters.");
            if (cmd.Stones is < 0 or > SocialRules.MaxMailStones || cmd.Quantity < 0) return Error.Validation("mail_invalid", "Invalid attachment.");
            var itemId = string.IsNullOrWhiteSpace(cmd.ItemId) ? null : cmd.ItemId;
            if ((itemId is null) != (cmd.Quantity == 0)) return Error.Validation("mail_invalid", "An item needs a quantity.");
            if (text.Length == 0 && cmd.Stones == 0 && itemId is null) return Error.Validation("mail_empty", "Write something or attach a gift.");
            var to = new CharacterId(cmd.ToCharacterId);
            if (await repo.LinkAsync(uow, me.Id, to, ct) is not { Status: SocialRules.Accepted }) return NotFriends;
            var recipient = await characters.FindAsync(uow, cmd.ToCharacterId, ct);
            if (recipient is null) return CharacterNotFound;
            if (await repo.CountMailSinceAsync(uow, me.Id, now.AddDays(-1), ct) >= SocialRules.MailPerDay)
                return Error.LimitReached("mail_limit", $"At most {SocialRules.MailPerDay} letters a day.");

            var id = Uuid7.New();
            if (itemId is not null)
            {
                var item = catalog.Find(itemId);
                if (item is null || !item.Tradable || !item.Stackable) return Error.Validation("item_not_giftable", "Only tradable stackable items can be sent.");
                if (cmd.Quantity > catalog.MaxStack) return Error.Validation("mail_invalid", $"At most {catalog.MaxStack} at once.");
                var removed = await inventory.RemoveItemsAsync(uow, me.Id, itemId, cmd.Quantity, "mail_sent", id.ToString(), ct);
                if (!removed.IsSuccess) return removed.Error!;
            }

            if (cmd.Stones > 0)
            {
                // The sender pays a 5 % courier fee on top; it leaves the economy (sink against alt-account funnelling).
                var paid = await inventory.DebitAsync(uow, me.Id, cmd.Stones + SocialRules.Tax(cmd.Stones), "mail_sent", id.ToString(), ct);
                if (!paid.IsSuccess) return paid.Error!;
            }

            var mail = new Mail(id, me.Id, me.Name, to, text, cmd.Stones, itemId, cmd.Quantity, now, null);
            await repo.AddMailAsync(uow, mail, ct);
            await NotifyAsync(uow, recipient.AccountId, "mail_received", new { mailId = id, characterId = me.Id.Value, name = me.Name, stones = cmd.Stones, itemId, quantity = cmd.Quantity }, id, now, ct);
            await AuditAsync(uow, accountId, "social.mail_send", me.Id.Value, new { mailId = id, to = cmd.ToCharacterId, stones = cmd.Stones, tax = cmd.Stones > 0 ? SocialRules.Tax(cmd.Stones) : 0, itemId, quantity = cmd.Quantity }, now, ct);
            return View(mail);
        }, ct);

    public Task<Result<MailView>> ClaimMailAsync(AccountId accountId, Guid characterId, Guid mailId, CancellationToken ct) =>
        Write<MailView, Guid>(accountId, characterId, "social.mail_claim", null, mailId, async (uow, me, now) =>
        {
            var mail = await repo.FindMailAsync(uow, mailId, ct);
            if (mail is null || mail.ToId != me.Id) return Error.NotFound("mail_not_found", "Letter not found.");
            if (mail.ClaimedAt is not null) return Error.Conflict("mail_claimed", "Already received.");
            if (mail.ItemId is not null && !await inventory.HasRoomAsync(uow, me.Id, mail.ItemId, mail.Quantity, ct))
                return Error.Conflict("inventory_full", "No room in the bag.");
            if (!await repo.TryClaimAsync(uow, mailId, now, ct)) return Error.Conflict("mail_claimed", "Already received.");
            if (mail.ItemId is not null)
            {
                var added = await inventory.AddItemsAsync(uow, me.Id, mail.ItemId, mail.Quantity, "mail_claimed", mailId.ToString(), ct);
                if (!added.IsSuccess) return added.Error!;
            }

            if (mail.Stones > 0)
            {
                var credited = await inventory.CreditAsync(uow, me.Id, mail.Stones, "mail_claimed", mailId.ToString(), ct);
                if (!credited.IsSuccess) return credited.Error!;
            }

            await AuditAsync(uow, accountId, "social.mail_claim", me.Id.Value, new { mailId, stones = mail.Stones, mail.ItemId, mail.Quantity }, now, ct);
            return View(mail with { ClaimedAt = now });
        }, ct);

    private MailView View(Mail m) => new(m.Id, m.FromId.Value, m.FromName, m.Text, m.Stones, m.ItemId, m.ItemId is null ? null : catalog.Find(m.ItemId)?.NamePl ?? m.ItemId,
        m.Quantity, m.CreatedAt, m.ClaimedAt is not null);

    private async Task<FriendsView> ViewAsync(IUnitOfWork uow, CharacterId me, CancellationToken ct)
    {
        var links = await repo.LinksAsync(uow, me, ct);
        var unread = await repo.UnreadAsync(uow, me, ct);
        var friends = new List<FriendView>();
        var incoming = new List<FriendView>();
        var outgoing = new List<FriendView>();
        foreach (var l in links)
        {
            var mine = l.CharacterId == me;
            var otherId = mine ? l.OtherId : l.CharacterId;
            var o = await characters.FindAsync(uow, otherId.Value, ct);
            if (o is null) continue;
            var view = new FriendView(otherId.Value, o.Name, o.Path, l.Status, !mine, unread.GetValueOrDefault(otherId.Value), l.CreatedAt);
            if (l.Status == SocialRules.Accepted) { if (mine) friends.Add(view); }
            else if (mine) outgoing.Add(view);
            else incoming.Add(view);
        }

        var mail = await repo.MailAsync(uow, me, 50, ct);
        return new FriendsView([.. friends.OrderByDescending(f => f.Unread).ThenBy(f => f.Name, StringComparer.Ordinal)], incoming, outgoing,
            mail.Count(m => m.ClaimedAt is null && (m.Stones > 0 || m.ItemId is not null)));
    }

    private Task<Result<T>> Write<T, TRequest>(AccountId accountId, Guid characterId, string operation, string? key, TRequest request,
        Func<IUnitOfWork, CharacterSummary, DateTimeOffset, Task<Result<T>>> action, CancellationToken ct) =>
        db.RunAsync<Result<T>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, operation, key, request, async () =>
            {
                var me = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (me is null) return CharacterNotFound;
                return await action(uow, me, clock.GetUtcNow());
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    private async Task NotifyAsync(IUnitOfWork uow, AccountId to, string type, object payload, Guid source, DateTimeOffset now, CancellationToken ct)
    {
        if (await notifications.ExistsForSourceAsync(uow, to, source, ct)) return;
        await notifications.AddAsync(uow, new Notification(Uuid7.New(), to, type, JsonSerializer.Serialize(payload, Idempotency.Json), source, now, null), ct);
    }

    /// <summary>Stable per pair and day, so repeated requests do not flood the inbox.</summary>
    private static Guid Source(string kind, CharacterId a, CharacterId b, DateTimeOffset now)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}:{a.Value}:{b.Value}:{now:yyyy-MM-dd}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, Guid characterId, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "character", characterId.ToString(),
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}

internal sealed class InMemorySocialRepository : ISocialRepository
{
    private const string Links = "social.friends";
    private const string Messages = "social.messages";
    private const string Mails = "social.mail";

    private static InMemoryUnitOfWork U(IUnitOfWork uow) => InMemoryUnitOfWork.From(uow);

    private static string K(CharacterId a, CharacterId b) => a.Value + ":" + b.Value;

    public Task<FriendLink?> LinkAsync(IUnitOfWork uow, CharacterId a, CharacterId b, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<string, FriendLink>(Links).TryGetValue(K(a, b), out var l) ? l : null);

    public Task UpsertLinkAsync(IUnitOfWork uow, FriendLink link, CancellationToken ct)
    {
        var u = U(uow);
        u.SetTable(Links, u.Table<string, FriendLink>(Links).SetItem(K(link.CharacterId, link.OtherId), link));
        return Task.CompletedTask;
    }

    public Task DeleteLinkAsync(IUnitOfWork uow, CharacterId a, CharacterId b, CancellationToken ct)
    {
        var u = U(uow);
        u.SetTable(Links, u.Table<string, FriendLink>(Links).Remove(K(a, b)));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FriendLink>> LinksAsync(IUnitOfWork uow, CharacterId c, CancellationToken ct)
    {
        IReadOnlyList<FriendLink> list = [.. U(uow).Table<string, FriendLink>(Links).Values
            .Where(l => l.CharacterId == c || (l.OtherId == c && l.Status == SocialRules.Pending)).OrderBy(l => l.CreatedAt)];
        return Task.FromResult(list);
    }

    public Task AddMessageAsync(IUnitOfWork uow, DirectMessage m, CancellationToken ct)
    {
        var u = U(uow);
        u.SetTable(Messages, u.Table<Guid, DirectMessage>(Messages).Add(m.Id, m));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DirectMessage>> ConversationAsync(IUnitOfWork uow, CharacterId a, CharacterId b, Guid? before, int limit, CancellationToken ct)
    {
        IReadOnlyList<DirectMessage> list = [.. U(uow).Table<Guid, DirectMessage>(Messages).Values
            .Where(m => ((m.FromId == a && m.ToId == b) || (m.FromId == b && m.ToId == a)) && (before is null || m.Id.CompareTo(before.Value) < 0))
            .OrderByDescending(m => m.Id).Take(limit)];
        return Task.FromResult(list);
    }

    public Task<int> CountMessagesSinceAsync(IUnitOfWork uow, CharacterId from, DateTimeOffset since, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, DirectMessage>(Messages).Values.Count(m => m.FromId == from && m.CreatedAt >= since));

    public Task MarkReadAsync(IUnitOfWork uow, CharacterId to, CharacterId from, DateTimeOffset now, CancellationToken ct)
    {
        var u = U(uow);
        var t = u.Table<Guid, DirectMessage>(Messages);
        foreach (var m in t.Values.Where(m => m.ToId == to && m.FromId == from && m.ReadAt is null).ToList())
            t = t.SetItem(m.Id, m with { ReadAt = now });
        u.SetTable(Messages, t);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<Guid, int>> UnreadAsync(IUnitOfWork uow, CharacterId to, CancellationToken ct)
    {
        IReadOnlyDictionary<Guid, int> d = U(uow).Table<Guid, DirectMessage>(Messages).Values
            .Where(m => m.ToId == to && m.ReadAt is null).GroupBy(m => m.FromId.Value).ToDictionary(g => g.Key, g => g.Count());
        return Task.FromResult(d);
    }

    public Task AddMailAsync(IUnitOfWork uow, Mail m, CancellationToken ct)
    {
        var u = U(uow);
        u.SetTable(Mails, u.Table<Guid, Mail>(Mails).Add(m.Id, m));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Mail>> MailAsync(IUnitOfWork uow, CharacterId to, int limit, CancellationToken ct)
    {
        IReadOnlyList<Mail> list = [.. U(uow).Table<Guid, Mail>(Mails).Values.Where(m => m.ToId == to).OrderByDescending(m => m.Id).Take(limit)];
        return Task.FromResult(list);
    }

    public Task<Mail?> FindMailAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, Mail>(Mails).TryGetValue(id, out var m) ? m : null);

    public Task<bool> TryClaimAsync(IUnitOfWork uow, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var u = U(uow);
        var t = u.Table<Guid, Mail>(Mails);
        if (!t.TryGetValue(id, out var m) || m.ClaimedAt is not null) return Task.FromResult(false);
        u.SetTable(Mails, t.SetItem(id, m with { ClaimedAt = now }));
        return Task.FromResult(true);
    }

    public Task<int> CountMailSinceAsync(IUnitOfWork uow, CharacterId from, DateTimeOffset since, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, Mail>(Mails).Values.Count(m => m.FromId == from && m.CreatedAt >= since));
}

internal sealed class PostgresSocialRepository : ISocialRepository
{
    private static PostgresUnitOfWork P(IUnitOfWork uow) => PostgresUnitOfWork.From(uow);

    private static FriendLink MapLink(Npgsql.NpgsqlDataReader r) =>
        new(new CharacterId(r.GetGuid(0)), new CharacterId(r.GetGuid(1)), r.GetString(2), r.GetFieldValue<DateTimeOffset>(3));

    private static DirectMessage MapMessage(Npgsql.NpgsqlDataReader r) =>
        new(r.GetGuid(0), new CharacterId(r.GetGuid(1)), new CharacterId(r.GetGuid(2)), r.GetString(3), r.GetFieldValue<DateTimeOffset>(4),
            r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5));

    private static Mail MapMail(Npgsql.NpgsqlDataReader r) =>
        new(r.GetGuid(0), new CharacterId(r.GetGuid(1)), r.GetString(2), new CharacterId(r.GetGuid(3)), r.GetString(4), r.GetInt64(5),
            r.IsDBNull(6) ? null : r.GetString(6), r.GetInt64(7), r.GetFieldValue<DateTimeOffset>(8), r.IsDBNull(9) ? null : r.GetFieldValue<DateTimeOffset>(9));

    private const string MailColumns = "id, from_id, from_name, to_id, text, stones, item_id, quantity, created_at, claimed_at";

    public async Task<FriendLink?> LinkAsync(IUnitOfWork uow, CharacterId a, CharacterId b, CancellationToken ct) =>
        (await P(uow).QueryAsync("SELECT character_id, other_id, status, created_at FROM social.friends WHERE character_id = $1 AND other_id = $2",
            MapLink, ct, a.Value, b.Value)).FirstOrDefault();

    public Task UpsertLinkAsync(IUnitOfWork uow, FriendLink l, CancellationToken ct) =>
        P(uow).ExecuteAsync("INSERT INTO social.friends (character_id, other_id, status, created_at) VALUES ($1, $2, $3, $4) " +
            "ON CONFLICT (character_id, other_id) DO UPDATE SET status = EXCLUDED.status", ct, l.CharacterId.Value, l.OtherId.Value, l.Status, l.CreatedAt);

    public Task DeleteLinkAsync(IUnitOfWork uow, CharacterId a, CharacterId b, CancellationToken ct) =>
        P(uow).ExecuteAsync("DELETE FROM social.friends WHERE character_id = $1 AND other_id = $2", ct, a.Value, b.Value);

    public async Task<IReadOnlyList<FriendLink>> LinksAsync(IUnitOfWork uow, CharacterId c, CancellationToken ct) =>
        await P(uow).QueryAsync("SELECT character_id, other_id, status, created_at FROM social.friends " +
            "WHERE character_id = $1 OR (other_id = $1 AND status = 'pending') ORDER BY created_at", MapLink, ct, c.Value);

    public Task AddMessageAsync(IUnitOfWork uow, DirectMessage m, CancellationToken ct) =>
        P(uow).ExecuteAsync("INSERT INTO social.messages (id, from_id, to_id, text, created_at, read_at) VALUES ($1, $2, $3, $4, $5, $6)",
            ct, m.Id, m.FromId.Value, m.ToId.Value, m.Text, m.CreatedAt, m.ReadAt);

    public async Task<IReadOnlyList<DirectMessage>> ConversationAsync(IUnitOfWork uow, CharacterId a, CharacterId b, Guid? before, int limit, CancellationToken ct) =>
        await P(uow).QueryAsync("SELECT id, from_id, to_id, text, created_at, read_at FROM social.messages " +
            "WHERE ((from_id = $1 AND to_id = $2) OR (from_id = $2 AND to_id = $1)) AND ($3::uuid IS NULL OR id < $3::uuid) ORDER BY id DESC LIMIT $4",
            MapMessage, ct, a.Value, b.Value, before, limit);

    public async Task<int> CountMessagesSinceAsync(IUnitOfWork uow, CharacterId from, DateTimeOffset since, CancellationToken ct) =>
        (await P(uow).QueryAsync("SELECT count(*)::int FROM social.messages WHERE from_id = $1 AND created_at >= $2", r => r.GetInt32(0), ct, from.Value, since))[0];

    public Task MarkReadAsync(IUnitOfWork uow, CharacterId to, CharacterId from, DateTimeOffset now, CancellationToken ct) =>
        P(uow).ExecuteAsync("UPDATE social.messages SET read_at = $3 WHERE to_id = $1 AND from_id = $2 AND read_at IS NULL", ct, to.Value, from.Value, now);

    public async Task<IReadOnlyDictionary<Guid, int>> UnreadAsync(IUnitOfWork uow, CharacterId to, CancellationToken ct) =>
        (await P(uow).QueryAsync("SELECT from_id, count(*)::int FROM social.messages WHERE to_id = $1 AND read_at IS NULL GROUP BY from_id",
            r => (r.GetGuid(0), r.GetInt32(1)), ct, to.Value)).ToDictionary(x => x.Item1, x => x.Item2);

    public Task AddMailAsync(IUnitOfWork uow, Mail m, CancellationToken ct) =>
        P(uow).ExecuteAsync($"INSERT INTO social.mail ({MailColumns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)",
            ct, m.Id, m.FromId.Value, m.FromName, m.ToId.Value, m.Text, m.Stones, m.ItemId, m.Quantity, m.CreatedAt, m.ClaimedAt);

    public async Task<IReadOnlyList<Mail>> MailAsync(IUnitOfWork uow, CharacterId to, int limit, CancellationToken ct) =>
        await P(uow).QueryAsync($"SELECT {MailColumns} FROM social.mail WHERE to_id = $1 ORDER BY id DESC LIMIT $2", MapMail, ct, to.Value, limit);

    public async Task<Mail?> FindMailAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        (await P(uow).QueryAsync($"SELECT {MailColumns} FROM social.mail WHERE id = $1 FOR UPDATE", MapMail, ct, id)).FirstOrDefault();

    public async Task<bool> TryClaimAsync(IUnitOfWork uow, Guid id, DateTimeOffset now, CancellationToken ct) =>
        (await P(uow).QueryAsync("UPDATE social.mail SET claimed_at = $2 WHERE id = $1 AND claimed_at IS NULL RETURNING 1", _ => 1, ct, id, now)).Count > 0;

    public async Task<int> CountMailSinceAsync(IUnitOfWork uow, CharacterId from, DateTimeOffset since, CancellationToken ct) =>
        (await P(uow).QueryAsync("SELECT count(*)::int FROM social.mail WHERE from_id = $1 AND created_at >= $2", r => r.GetInt32(0), ct, from.Value, since))[0];
}

public static class SocialEndpoints
{
    public static IEndpointRouteBuilder MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/characters/{characterId:guid}/social").WithTags("Social").RequireAuthorization();

        g.MapGet("/friends", async (Guid characterId, HttpContext http, SocialService svc, CancellationToken ct) =>
            ApiResults.From(await svc.FriendsAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/friends", async (Guid characterId, AddFriendCommand cmd, HttpContext http, SocialService svc, CancellationToken ct) =>
            ApiResults.From(await svc.AddFriendAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapPost("/friends/{otherId:guid}/accept", async (Guid characterId, Guid otherId, HttpContext http, SocialService svc, CancellationToken ct) =>
            ApiResults.From(await svc.AcceptAsync(http.User.GetAccountId(), characterId, otherId, ct), Results.Ok));
        g.MapDelete("/friends/{otherId:guid}", async (Guid characterId, Guid otherId, HttpContext http, SocialService svc, CancellationToken ct) =>
            ApiResults.From(await svc.RemoveAsync(http.User.GetAccountId(), characterId, otherId, ct), Results.Ok));

        g.MapGet("/messages/{otherId:guid}", async (Guid characterId, Guid otherId, Guid? before, int? limit, HttpContext http, SocialService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ConversationAsync(http.User.GetAccountId(), characterId, otherId, before, limit, ct), list => Results.Ok(new { items = list })));
        g.MapPost("/messages/{otherId:guid}", async (Guid characterId, Guid otherId, TextCommand cmd, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, SocialService svc, CancellationToken ct) =>
            ApiResults.From(await svc.SendMessageAsync(http.User.GetAccountId(), characterId, otherId, key, cmd, ct), Results.Ok));

        g.MapGet("/mail", async (Guid characterId, HttpContext http, SocialService svc, CancellationToken ct) =>
            ApiResults.From(await svc.MailAsync(http.User.GetAccountId(), characterId, ct), list => Results.Ok(new { items = list })));
        g.MapPost("/mail", async (Guid characterId, SendMailCommand cmd, [FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, SocialService svc, CancellationToken ct) =>
            ApiResults.From(await svc.SendMailAsync(http.User.GetAccountId(), characterId, key, cmd, ct), Results.Ok));
        g.MapPost("/mail/{mailId:guid}/claim", async (Guid characterId, Guid mailId, HttpContext http, SocialService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ClaimMailAsync(http.User.GetAccountId(), characterId, mailId, ct), Results.Ok));
        return app;
    }
}
