using System.Text.Json;
using JianghuGo.Modules.Combat.Domain;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Identity.Api;
using JianghuGo.Modules.Inventory.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Combat.Application;

// 2.3 — bounty boards: a cultivator puts stones on another's head (escrowed); whoever defeats the target in a duel or
// on the arena collects every open bounty on them. The poster may withdraw an unclaimed bounty (refund minus 10 %).

public sealed record Bounty(Guid Id, CharacterId TargetId, string TargetName, CharacterId PosterId, long Stones, DateTimeOffset CreatedAt,
    CharacterId? ClaimedBy, DateTimeOffset? ClosedAt, string Status);

public static class BountyRules
{
    public const long MinStones = 100;
    public const long MaxStones = 100_000;
    public const int MaxOpenPerPoster = 5;
    public const int WithdrawFeePermille = 100;
    public const string Open = "open";
    public const string Claimed = "claimed";
    public const string Withdrawn = "withdrawn";
    public static readonly string[] Kinds = [BattleKinds.Pvp, BattleKinds.Arena];
}

public interface IBountyRepository
{
    Task AddAsync(IUnitOfWork uow, Bounty b, CancellationToken ct);
    Task<Bounty?> FindAsync(IUnitOfWork uow, Guid id, CancellationToken ct);
    Task<IReadOnlyList<Bounty>> OpenOnAsync(IUnitOfWork uow, CharacterId target, CancellationToken ct);
    Task<IReadOnlyList<Bounty>> OpenAsync(IUnitOfWork uow, int limit, CancellationToken ct);
    Task<int> CountOpenByAsync(IUnitOfWork uow, CharacterId poster, CancellationToken ct);

    /// <summary>Closes an open bounty; false when it was already closed (no double payout).</summary>
    Task<bool> TryCloseAsync(IUnitOfWork uow, Guid id, string status, CharacterId? claimedBy, DateTimeOffset at, CancellationToken ct);
}

public sealed record BountyView(Guid Id, Guid TargetCharacterId, string TargetName, long Stones, DateTimeOffset CreatedAt, bool Mine);

public sealed record PostBountyCommand(Guid CharacterId, Guid? TargetCharacterId, long Stones, string? TargetName = null);

public sealed record BountyActorCommand(Guid CharacterId);

public sealed class BountyService(
    IUnitOfWorkFactory db,
    IBountyRepository repo,
    ICharacterDirectory characters,
    IInventory inventory,
    IIdempotencyStore idempotency,
    IAuditLog audit,
    ICorrelationContext correlation,
    TimeProvider clock)
{
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");

    public Task<Result<IReadOnlyList<BountyView>>> ListAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        db.RunAsync<Result<IReadOnlyList<BountyView>>>(async uow =>
        {
            var me = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (me is null) return CharacterNotFound;
            var open = await repo.OpenAsync(uow, 200, ct);
            return Result<IReadOnlyList<BountyView>>.Success([.. open.GroupBy(b => b.TargetId).Select(g => g.ToList())
                .OrderByDescending(g => g.Sum(b => b.Stones)).Take(50)
                .SelectMany(g => g).Select(b => new BountyView(b.Id, b.TargetId.Value, b.TargetName, b.Stones, b.CreatedAt, b.PosterId == me.Id))]);
        }, ct);

    public Task<Result<BountyView>> PostAsync(AccountId accountId, string? key, PostBountyCommand cmd, CancellationToken ct) =>
        db.RunAsync<Result<BountyView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "combat.bounty_post", key, cmd, async Task<Result<BountyView>> () =>
            {
                if (cmd.Stones is < BountyRules.MinStones or > BountyRules.MaxStones)
                    return Error.Validation("bounty_invalid", $"A bounty is {BountyRules.MinStones}-{BountyRules.MaxStones} stones.");
                var me = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
                if (me is null) return CharacterNotFound;
                var target = cmd.TargetCharacterId is { } tid ? await characters.FindAsync(uow, tid, ct)
                    : await characters.FindByNameAsync(uow, cmd.TargetName ?? string.Empty, ct);
                if (target is null) return CharacterNotFound;
                if (target.AccountId == accountId) return Error.Validation("bounty_self", "You cannot put a bounty on your own characters.");
                if (await repo.CountOpenByAsync(uow, me.Id, ct) >= BountyRules.MaxOpenPerPoster)
                    return Error.LimitReached("bounty_limit", $"At most {BountyRules.MaxOpenPerPoster} open bounties.");
                var now = clock.GetUtcNow();
                var b = new Bounty(Uuid7.New(), target.Id, target.Name, me.Id, cmd.Stones, now, null, null, BountyRules.Open);
                var paid = await inventory.DebitAsync(uow, me.Id, cmd.Stones, "bounty_escrow", b.Id.ToString(), ct);
                if (!paid.IsSuccess) return paid.Error!;
                await repo.AddAsync(uow, b, ct);
                await Audit(uow, accountId, "combat.bounty_posted", b.Id, new { target = target.Id.Value, cmd.Stones }, now, ct);
                return new BountyView(b.Id, target.Id.Value, target.Name, b.Stones, now, true);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    public Task<Result<long>> WithdrawAsync(AccountId accountId, Guid bountyId, BountyActorCommand cmd, CancellationToken ct) =>
        db.RunAsync<Result<long>>(async uow =>
        {
            var me = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
            if (me is null) return CharacterNotFound;
            var b = await repo.FindAsync(uow, bountyId, ct);
            if (b is null || b.PosterId != me.Id) return Error.NotFound("bounty_not_found", "Bounty not found.");
            var now = clock.GetUtcNow();
            if (!await repo.TryCloseAsync(uow, b.Id, BountyRules.Withdrawn, null, now, ct)) return Error.Conflict("bounty_closed", "The bounty is already closed.");
            var refund = b.Stones - (b.Stones * BountyRules.WithdrawFeePermille / 1000);
            var credited = await inventory.CreditAsync(uow, me.Id, refund, "bounty_refund", b.Id.ToString(), ct);
            if (!credited.IsSuccess) return credited.Error!;
            await Audit(uow, accountId, "combat.bounty_withdrawn", b.Id, new { refund }, now, ct);
            await uow.CommitAsync(ct);
            return refund;
        }, ct);

    private Task Audit(IUnitOfWork uow, AccountId accountId, string action, Guid id, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "bounty", id.ToString(), JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}

/// <summary>A duel or arena victory over a wanted cultivator pays every open bounty on them to the victor (exactly once).</summary>
internal sealed class BountyClaimHandler(IBountyRepository repo, IInventory inventory, IAuditLog audit) : IOutboxHandler
{
    public string EventType => CombatEvents.BattleFinished;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<CombatEvents.BattleFinishedV1>();
        if (!BountyRules.Kinds.Contains(e.Kind)) return;
        var winner = e.Participants.FirstOrDefault(p => p.Result == "won");
        var loser = e.Participants.FirstOrDefault(p => p.Result == "lost");
        if (winner is null || loser is null || winner.AccountId == loser.AccountId) return;
        foreach (var b in await repo.OpenOnAsync(uow, new CharacterId(loser.CharacterId), ct))
        {
            if (b.PosterId.Value == winner.CharacterId) continue; // the poster cannot collect their own bounty
            if (!await repo.TryCloseAsync(uow, b.Id, BountyRules.Claimed, new CharacterId(winner.CharacterId), message.OccurredAt, ct)) continue;
            var paid = await inventory.CreditAsync(uow, new CharacterId(winner.CharacterId), b.Stones, "bounty_claim", b.Id.ToString(), ct);
            if (!paid.IsSuccess) throw new InvalidOperationException("Bounty payout failed: " + paid.Error!.Code);
            await audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), new AccountId(winner.AccountId), "combat.bounty_claimed", "bounty", b.Id.ToString(),
                JsonSerializer.Serialize(new { battle = e.BattleId, b.Stones }, Idempotency.Json), message.CorrelationId, message.OccurredAt), ct);
        }
    }
}

internal sealed class InMemoryBountyRepository : IBountyRepository
{
    private const string Table = "combat.bounties";

    private static InMemoryUnitOfWork U(IUnitOfWork uow) => InMemoryUnitOfWork.From(uow);

    private static IEnumerable<Bounty> All(IUnitOfWork uow) => U(uow).Table<Guid, Bounty>(Table).Values;

    public Task AddAsync(IUnitOfWork uow, Bounty b, CancellationToken ct)
    {
        U(uow).SetTable(Table, U(uow).Table<Guid, Bounty>(Table).Add(b.Id, b));
        return Task.CompletedTask;
    }

    public Task<Bounty?> FindAsync(IUnitOfWork uow, Guid id, CancellationToken ct) => Task.FromResult(U(uow).Table<Guid, Bounty>(Table).GetValueOrDefault(id));

    public Task<IReadOnlyList<Bounty>> OpenOnAsync(IUnitOfWork uow, CharacterId target, CancellationToken ct)
    {
        IReadOnlyList<Bounty> l = [.. All(uow).Where(b => b.TargetId == target && b.Status == BountyRules.Open).OrderBy(b => b.Id)];
        return Task.FromResult(l);
    }

    public Task<IReadOnlyList<Bounty>> OpenAsync(IUnitOfWork uow, int limit, CancellationToken ct)
    {
        IReadOnlyList<Bounty> l = [.. All(uow).Where(b => b.Status == BountyRules.Open).OrderByDescending(b => b.Stones).Take(limit)];
        return Task.FromResult(l);
    }

    public Task<int> CountOpenByAsync(IUnitOfWork uow, CharacterId poster, CancellationToken ct) =>
        Task.FromResult(All(uow).Count(b => b.PosterId == poster && b.Status == BountyRules.Open));

    public Task<bool> TryCloseAsync(IUnitOfWork uow, Guid id, string status, CharacterId? claimedBy, DateTimeOffset at, CancellationToken ct)
    {
        var t = U(uow).Table<Guid, Bounty>(Table);
        if (!t.TryGetValue(id, out var b) || b.Status != BountyRules.Open) return Task.FromResult(false);
        U(uow).SetTable(Table, t.SetItem(id, b with { Status = status, ClaimedBy = claimedBy, ClosedAt = at }));
        return Task.FromResult(true);
    }
}

internal sealed class PostgresBountyRepository : IBountyRepository
{
    private const string Columns = "id, target_id, target_name, poster_id, stones, created_at, claimed_by, closed_at, status";

    private static PostgresUnitOfWork P(IUnitOfWork uow) => PostgresUnitOfWork.From(uow);

    private static Bounty Map(Npgsql.NpgsqlDataReader r) => new(r.GetGuid(0), new CharacterId(r.GetGuid(1)), r.GetString(2), new CharacterId(r.GetGuid(3)), r.GetInt64(4),
        r.GetFieldValue<DateTimeOffset>(5), r.IsDBNull(6) ? null : new CharacterId(r.GetGuid(6)), r.IsDBNull(7) ? null : r.GetFieldValue<DateTimeOffset>(7), r.GetString(8));

    public Task AddAsync(IUnitOfWork uow, Bounty b, CancellationToken ct) =>
        P(uow).ExecuteAsync($"INSERT INTO combat.bounties ({Columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)",
            ct, b.Id, b.TargetId.Value, b.TargetName, b.PosterId.Value, b.Stones, b.CreatedAt, b.ClaimedBy?.Value, b.ClosedAt, b.Status);

    public async Task<Bounty?> FindAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        (await P(uow).QueryAsync($"SELECT {Columns} FROM combat.bounties WHERE id = $1", Map, ct, id)).FirstOrDefault();

    public async Task<IReadOnlyList<Bounty>> OpenOnAsync(IUnitOfWork uow, CharacterId target, CancellationToken ct) =>
        await P(uow).QueryAsync($"SELECT {Columns} FROM combat.bounties WHERE target_id = $1 AND status = 'open' ORDER BY id", Map, ct, target.Value);

    public async Task<IReadOnlyList<Bounty>> OpenAsync(IUnitOfWork uow, int limit, CancellationToken ct) =>
        await P(uow).QueryAsync($"SELECT {Columns} FROM combat.bounties WHERE status = 'open' ORDER BY stones DESC LIMIT $1", Map, ct, limit);

    public async Task<int> CountOpenByAsync(IUnitOfWork uow, CharacterId poster, CancellationToken ct) =>
        (await P(uow).QueryAsync("SELECT count(*)::int FROM combat.bounties WHERE poster_id = $1 AND status = 'open'", r => r.GetInt32(0), ct, poster.Value))[0];

    public async Task<bool> TryCloseAsync(IUnitOfWork uow, Guid id, string status, CharacterId? claimedBy, DateTimeOffset at, CancellationToken ct) =>
        await P(uow).ExecuteAsync("UPDATE combat.bounties SET status = $2, claimed_by = $3, closed_at = $4 WHERE id = $1 AND status = 'open'",
            ct, id, status, claimedBy?.Value, at) == 1;
}

public static class BountyEndpoints
{
    public static IEndpointRouteBuilder MapBountyEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/bounties").WithTags("Combat").RequireAuthorization();
        g.MapGet("/", async (Guid characterId, HttpContext http, BountyService svc, CancellationToken ct) =>
            ApiResults.From(await svc.ListAsync(http.User.GetAccountId(), characterId, ct), list => Results.Ok(new { items = list })));
        g.MapPost("/", async (PostBountyCommand body, [Microsoft.AspNetCore.Mvc.FromHeader(Name = "Idempotency-Key")] string? key, HttpContext http, BountyService svc, CancellationToken ct) =>
            ApiResults.From(await svc.PostAsync(http.User.GetAccountId(), key, body, ct), Results.Ok));
        g.MapPost("/{bountyId:guid}/withdraw", async (Guid bountyId, BountyActorCommand body, HttpContext http, BountyService svc, CancellationToken ct) =>
            ApiResults.From(await svc.WithdrawAsync(http.User.GetAccountId(), bountyId, body, ct), refund => Results.Ok(new { refund })));
        return app;
    }
}
