using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Cultivation.Application;
using JianghuGo.Modules.Identity.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JianghuGo.Modules.Notifications;

/// <summary>
/// In-game notification. Text is NOT stored: the client localises <see cref="Type"/> + <see cref="PayloadJson"/>.
/// <see cref="SourceMessageId"/> (the outbox message) is unique per account, which makes delivery idempotent.
/// </summary>
public sealed record Notification(Guid Id, AccountId AccountId, string Type, string PayloadJson, Guid SourceMessageId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public interface INotificationRepository
{
    Task<bool> ExistsForSourceAsync(IUnitOfWork uow, AccountId accountId, Guid sourceMessageId, CancellationToken ct);
    Task AddAsync(IUnitOfWork uow, Notification n, CancellationToken ct);
    Task<IReadOnlyList<Notification>> ListAsync(IUnitOfWork uow, AccountId accountId, bool unreadOnly, Guid? before, int limit, CancellationToken ct);
    Task<bool> MarkReadAsync(IUnitOfWork uow, AccountId accountId, Guid id, DateTimeOffset now, CancellationToken ct);
    Task DeleteForAccountAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct);
}

public static class NotificationTypes
{
    public const string Welcome = "welcome";
    public const string BreakthroughSucceeded = "breakthrough_succeeded";
    public const string BreakthroughFailed = "breakthrough_failed";
    public const string PvpChallenge = "pvp_challenge";
    public const string BattleWon = "battle_won";
    public const string BattleLost = "battle_lost";
    public const string BattleDraw = "battle_draw";
    public const string MarketSold = "market_sold";
    public const string SectApplicationAccepted = "sect_application_accepted";
    public const string SectApplicationRejected = "sect_application_rejected";
    public const string WarDeclared = "war_declared";
    public const string WarResolved = "war_resolved";
}

internal sealed class ChallengeReceivedHandler(INotificationRepository repo) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Combat.Contracts.CombatEvents.ChallengeReceived;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Combat.Contracts.CombatEvents.ChallengeReceivedV1>();
        return NotificationWriter.WriteOnceAsync(repo, uow, e.OpponentAccountId, NotificationTypes.PvpChallenge,
            new { e.BattleId, e.ChallengerCharacterId, e.ChallengerName, e.OpponentCharacterId }, message, ct);
    }
}

internal sealed class BattleFinishedHandler(INotificationRepository repo) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Combat.Contracts.CombatEvents.BattleFinished;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Combat.Contracts.CombatEvents.BattleFinishedV1>();
        foreach (var p in e.Participants)
        {
            var type = p.Result switch { "won" => NotificationTypes.BattleWon, "lost" => NotificationTypes.BattleLost, _ => NotificationTypes.BattleDraw };
            await NotificationWriter.WriteOnceAsync(repo, uow, p.AccountId, type, new { e.BattleId, e.Kind, e.NpcId, p.CharacterId, p.RewardQi }, message, ct);
        }
    }
}

internal sealed class ListingSoldHandler(INotificationRepository repo) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Market.Contracts.MarketEvents.ListingSold;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Market.Contracts.MarketEvents.ListingSoldV1>();
        return NotificationWriter.WriteOnceAsync(repo, uow, e.SellerAccountId, NotificationTypes.MarketSold,
            new { e.ListingId, e.SellerCharacterId, e.ItemId, e.Quantity, e.UpgradeLevel, e.Price, e.Tax, e.Proceeds }, message, ct);
    }
}

internal sealed class ApplicationDecidedHandler(INotificationRepository repo) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Sects.Contracts.SectEvents.ApplicationDecided;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Sects.Contracts.SectEvents.ApplicationDecidedV1>();
        return NotificationWriter.WriteOnceAsync(repo, uow, e.AccountId,
            e.Accepted ? NotificationTypes.SectApplicationAccepted : NotificationTypes.SectApplicationRejected,
            new { e.SectId, e.SectName, e.CharacterId }, message, ct);
    }
}

internal sealed class WarDeclaredHandler(INotificationRepository repo) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Territory.Contracts.TerritoryEvents.WarDeclared;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Territory.Contracts.TerritoryEvents.WarDeclaredV1>();
        return NotificationWriter.WriteOnceAsync(repo, uow, e.DefenderLeaderAccountId, NotificationTypes.WarDeclared,
            new { e.WarId, e.TerritoryKey, e.AttackerSectId, e.AttackerName, e.StartsAt, e.EndsAt }, message, ct);
    }
}

internal sealed class WarResolvedHandler(INotificationRepository repo) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Territory.Contracts.TerritoryEvents.WarResolved;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Territory.Contracts.TerritoryEvents.WarResolvedV1>();
        foreach (var account in e.LeaderAccountIds.Distinct())
        {
            await NotificationWriter.WriteOnceAsync(repo, uow, account, NotificationTypes.WarResolved,
                new { e.WarId, e.TerritoryKey, e.WinnerSectId, e.AttackerScore, e.DefenderScore }, message, ct);
        }
    }
}

internal static class NotificationWriter
{
    /// <summary>Idempotent by source message: redelivery of the same event creates nothing new.</summary>
    public static async Task WriteOnceAsync(INotificationRepository repo, IUnitOfWork uow, Guid accountId, string type, object payload, OutboxMessage source, CancellationToken ct)
    {
        var account = new AccountId(accountId);
        if (await repo.ExistsForSourceAsync(uow, account, source.Id, ct)) return;
        await repo.AddAsync(uow, new Notification(Uuid7.New(), account, type, JsonSerializer.Serialize(payload, Idempotency.Json), source.Id, source.OccurredAt, null), ct);
    }
}

internal sealed class CharacterCreatedHandler(INotificationRepository repo) : IOutboxHandler
{
    public string EventType => CharacterEvents.CharacterCreated;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<CharacterEvents.CharacterCreatedV1>();
        return NotificationWriter.WriteOnceAsync(repo, uow, e.AccountId, NotificationTypes.Welcome, new { e.CharacterId, e.Name, e.Path }, message, ct);
    }
}

internal sealed class BreakthroughHandler(INotificationRepository repo, string eventType) : IOutboxHandler
{
    public string EventType { get; } = eventType;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<CultivationEvents.BreakthroughV1>();
        return NotificationWriter.WriteOnceAsync(repo, uow, e.AccountId,
            e.Success ? NotificationTypes.BreakthroughSucceeded : NotificationTypes.BreakthroughFailed,
            new { e.CharacterId, e.FromRealm, e.FromStage, e.ToRealm, e.ToStage }, message, ct);
    }
}

internal sealed class InMemoryNotificationRepository : INotificationRepository
{
    private const string TableName = "notifications.notifications";

    private static IEnumerable<Notification> All(IUnitOfWork uow) => InMemoryUnitOfWork.From(uow).Table<Guid, Notification>(TableName).Values;

    public Task<bool> ExistsForSourceAsync(IUnitOfWork uow, AccountId accountId, Guid sourceMessageId, CancellationToken ct) =>
        Task.FromResult(All(uow).Any(n => n.AccountId == accountId && n.SourceMessageId == sourceMessageId));

    public Task AddAsync(IUnitOfWork uow, Notification n, CancellationToken ct)
    {
        if (All(uow).Any(x => x.AccountId == n.AccountId && x.SourceMessageId == n.SourceMessageId))
            throw new DuplicateKeyException("Notification for this source already exists.");
        var u = InMemoryUnitOfWork.From(uow);
        u.SetTable(TableName, u.Table<Guid, Notification>(TableName).Add(n.Id, n));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Notification>> ListAsync(IUnitOfWork uow, AccountId accountId, bool unreadOnly, Guid? before, int limit, CancellationToken ct)
    {
        IReadOnlyList<Notification> list = [.. All(uow)
            .Where(n => n.AccountId == accountId && (!unreadOnly || n.ReadAt is null) && (before is null || n.Id.CompareTo(before.Value) < 0))
            .OrderByDescending(n => n.Id).Take(limit)];
        return Task.FromResult(list);
    }

    public Task DeleteForAccountAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<Guid, Notification>(TableName);
        u.SetTable(TableName, t.RemoveRange(t.Values.Where(n => n.AccountId == accountId).Select(n => n.Id)));
        return Task.CompletedTask;
    }

    public Task<bool> MarkReadAsync(IUnitOfWork uow, AccountId accountId, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var u = InMemoryUnitOfWork.From(uow);
        var t = u.Table<Guid, Notification>(TableName);
        if (!t.TryGetValue(id, out var n) || n.AccountId != accountId) return Task.FromResult(false);
        if (n.ReadAt is null) u.SetTable(TableName, t.SetItem(id, n with { ReadAt = now }));
        return Task.FromResult(true);
    }
}

internal sealed class PostgresNotificationRepository : INotificationRepository
{
    private static Notification Map(NpgsqlDataReader r) => new(r.GetGuid(0), new AccountId(r.GetGuid(1)), r.GetString(2), r.GetString(3), r.GetGuid(4),
        r.GetFieldValue<DateTimeOffset>(5), r.IsDBNull(6) ? null : r.GetFieldValue<DateTimeOffset>(6));

    public async Task<bool> ExistsForSourceAsync(IUnitOfWork uow, AccountId accountId, Guid sourceMessageId, CancellationToken ct) =>
        (await PostgresUnitOfWork.From(uow).QueryAsync("SELECT 1 FROM notifications.notifications WHERE account_id = $1 AND source_message_id = $2",
            _ => 1, ct, accountId.Value, sourceMessageId)).Count > 0;

    public Task AddAsync(IUnitOfWork uow, Notification n, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync(
            "INSERT INTO notifications.notifications (id, account_id, type, payload, source_message_id, created_at, read_at) VALUES ($1, $2, $3, $4::jsonb, $5, $6, $7)",
            ct, n.Id, n.AccountId.Value, n.Type, n.PayloadJson, n.SourceMessageId, n.CreatedAt, n.ReadAt);

    public async Task<IReadOnlyList<Notification>> ListAsync(IUnitOfWork uow, AccountId accountId, bool unreadOnly, Guid? before, int limit, CancellationToken ct) =>
        await PostgresUnitOfWork.From(uow).QueryAsync(
            "SELECT id, account_id, type, payload::text, source_message_id, created_at, read_at FROM notifications.notifications " +
            "WHERE account_id = $1 AND ($2 = false OR read_at IS NULL) AND ($3::uuid IS NULL OR id < $3::uuid) ORDER BY id DESC LIMIT $4",
            Map, ct, accountId.Value, unreadOnly, before, limit);

    public Task DeleteForAccountAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct) =>
        PostgresUnitOfWork.From(uow).ExecuteAsync("DELETE FROM notifications.notifications WHERE account_id = $1", ct, accountId.Value);

    public async Task<bool> MarkReadAsync(IUnitOfWork uow, AccountId accountId, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var pg = PostgresUnitOfWork.From(uow);
        await pg.ExecuteAsync("UPDATE notifications.notifications SET read_at = $3 WHERE id = $1 AND account_id = $2 AND read_at IS NULL", ct, id, accountId.Value, now);
        return (await pg.QueryAsync("SELECT 1 FROM notifications.notifications WHERE id = $1 AND account_id = $2", _ => 1, ct, id, accountId.Value)).Count > 0;
    }
}

public sealed record NotificationView(Guid Id, string Type, JsonElement Payload, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public static class NotificationsModule
{
    public const int MaxPageSize = 50;

    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
        {
            services.AddSingleton<INotificationRepository, PostgresNotificationRepository>();
            services.AddSingleton<ISocialRepository, PostgresSocialRepository>();
            services.AddSingleton(new JianghuGo.Application.RetentionPolicy("social.messages", "DELETE FROM social.messages WHERE ctid IN (SELECT ctid FROM social.messages WHERE created_at < $1 - interval '90 days' LIMIT 1000)"));
            services.AddSingleton(new JianghuGo.Application.RetentionPolicy("social.news", "DELETE FROM social.news WHERE ctid IN (SELECT ctid FROM social.news WHERE created_at < $1 - interval '30 days' LIMIT 1000)"));
            services.AddSingleton(new JianghuGo.Application.RetentionPolicy("social.reports", "DELETE FROM social.reports WHERE ctid IN (SELECT ctid FROM social.reports WHERE created_at < $1 - interval '180 days' LIMIT 1000)"));
            services.AddSingleton(new JianghuGo.Application.RetentionPolicy("social.mail", "DELETE FROM social.mail WHERE ctid IN (SELECT ctid FROM social.mail WHERE created_at < $1 - interval '90 days' AND (claimed_at IS NOT NULL OR (stones = 0 AND item_id IS NULL)) LIMIT 1000)"));
            services.AddSingleton(new JianghuGo.Application.RetentionPolicy("notifications.notifications", "DELETE FROM notifications.notifications WHERE ctid IN (SELECT ctid FROM notifications.notifications WHERE created_at < $1 - interval '90 days' LIMIT 1000)"));
        }
        else
        {
            services.AddSingleton<INotificationRepository, InMemoryNotificationRepository>();
            services.AddSingleton<ISocialRepository, InMemorySocialRepository>();
        }

        services.AddSingleton<SocialService>();
        services.AddSingleton<IOpsMetric, OpenReportsMetric>();
        services.AddSingleton<IOutboxHandler, RealmNewsHandler>();
        services.AddSingleton<IOutboxHandler, AccountErasureHandler>();

        services.AddSingleton<IOutboxHandler, CharacterCreatedHandler>();
        services.AddSingleton<IOutboxHandler, ChallengeReceivedHandler>();
        services.AddSingleton<IOutboxHandler, BattleFinishedHandler>();
        services.AddSingleton<IOutboxHandler, ListingSoldHandler>();
        services.AddSingleton<IOutboxHandler, ApplicationDecidedHandler>();
        services.AddSingleton<IOutboxHandler, WarDeclaredHandler>();
        services.AddSingleton<IOutboxHandler, WarResolvedHandler>();
        services.AddSingleton<IOutboxHandler>(sp => new BreakthroughHandler(sp.GetRequiredService<INotificationRepository>(), CultivationEvents.BreakthroughSucceeded));
        services.AddSingleton<IOutboxHandler>(sp => new BreakthroughHandler(sp.GetRequiredService<INotificationRepository>(), CultivationEvents.BreakthroughFailed));
        return services;
    }

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization();

        g.MapGet("/", async (HttpContext http, IUnitOfWorkFactory db, INotificationRepository repo, bool? unreadOnly, int? limit, Guid? before, CancellationToken ct) =>
        {
            if (limit is < 1 or > MaxPageSize)
                return ApiResults.Problem(Error.Validation("limit_invalid", $"limit must be 1-{MaxPageSize}."));
            var take = limit ?? 20;
            var account = http.User.GetAccountId();
            var page = await db.RunAsync(uow => repo.ListAsync(uow, account, unreadOnly ?? false, before, take + 1, ct), ct);
            var items = page.Take(take).Select(n => new NotificationView(n.Id, n.Type, JsonDocument.Parse(n.PayloadJson).RootElement.Clone(), n.CreatedAt, n.ReadAt)).ToList();
            return Results.Ok(new { items, nextBefore = page.Count > take ? items[^1].Id : (Guid?)null });
        });

        g.MapPost("/{id:guid}/read", async (Guid id, HttpContext http, IUnitOfWorkFactory db, INotificationRepository repo, TimeProvider clock, CancellationToken ct) =>
        {
            var account = http.User.GetAccountId();
            var found = await db.RunAsync(async uow =>
            {
                var ok = await repo.MarkReadAsync(uow, account, id, clock.GetUtcNow(), ct);
                await uow.CommitAsync(ct);
                return ok;
            }, ct);
            return found ? Results.NoContent() : ApiResults.Problem(Error.NotFound("notification_not_found", "Notification not found."));
        });

        return app;
    }
}
