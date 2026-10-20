using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Cultivation.Contracts;
using JianghuGo.Modules.Identity.Api;
using JianghuGo.Modules.Inventory.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JianghuGo.Modules.Notifications;

// 2.3 — master and disciple: a cultivator from realm 22 may take up to 3 friends below realm 11 as disciples. The disciple
// fights with the master's teaching (+5 % damage and guard); every new realm the disciple reaches pays the master stones.
// The bond ends when either side leaves it, or by itself when the disciple reaches realm 22.

public sealed record Mentorship(CharacterId MasterId, CharacterId DiscipleId, DateTimeOffset Since);

public static class MentorRules
{
    public const int MasterMinRealm = 21;
    public const int DiscipleMaxRealm = 10;
    public const int GraduationRealm = 21;
    public const int MaxDisciples = 3;
    public const long StonesPerRealm = 300;
    public static readonly PerkBonus Teaching = new(DamagePermille: 50, GuardPermille: 50);
}

public interface IMentorRepository
{
    Task<Mentorship?> MasterOfAsync(IUnitOfWork uow, CharacterId disciple, CancellationToken ct);
    Task<IReadOnlyList<Mentorship>> DisciplesOfAsync(IUnitOfWork uow, CharacterId master, CancellationToken ct);
    Task AddAsync(IUnitOfWork uow, Mentorship m, CancellationToken ct);
    Task RemoveAsync(IUnitOfWork uow, CharacterId disciple, CancellationToken ct);
}

public sealed record MentorView(Guid CharacterId, string Name, int Realm, DateTimeOffset Since);

public sealed record MentorshipView(MentorView? Master, IReadOnlyList<MentorView> Disciples, bool CanTeach, bool CanLearn);

public sealed record TakeDiscipleCommand(Guid DiscipleCharacterId);

public sealed class MentorService(
    IUnitOfWorkFactory db,
    IMentorRepository repo,
    ISocialRepository social,
    ICharacterDirectory characters,
    ICultivationStatus cultivation,
    IAuditLog audit,
    ICorrelationContext correlation,
    TimeProvider clock)
{
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");

    public Task<Result<MentorshipView>> GetAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        db.RunAsync<Result<MentorshipView>>(async uow =>
        {
            var me = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (me is null) return CharacterNotFound;
            return await ViewAsync(uow, me.Id, ct);
        }, ct);

    /// <summary>The master takes a friend as a disciple (the disciple's consent is the friendship plus their own action to leave anytime).</summary>
    public Task<Result<MentorshipView>> TakeAsync(AccountId accountId, Guid characterId, TakeDiscipleCommand cmd, CancellationToken ct) =>
        db.RunAsync<Result<MentorshipView>>(async uow =>
        {
            var me = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (me is null) return CharacterNotFound;
            var disciple = await characters.FindAsync(uow, cmd.DiscipleCharacterId, ct);
            if (disciple is null) return CharacterNotFound;
            if (disciple.AccountId == accountId) return Error.Validation("disciple_own", "You cannot teach your own characters.");
            if (await social.LinkAsync(uow, me.Id, disciple.Id, ct) is not { Status: SocialRules.Accepted }) return Error.Forbidden("not_friends", "Only a friend can become your disciple.");
            if ((await cultivation.StatusAsync(uow, me.Id.Value, ct)).Realm < MentorRules.MasterMinRealm)
                return Error.Conflict("realm_too_low", $"A master needs realm {MentorRules.MasterMinRealm + 1}.");
            if ((await cultivation.StatusAsync(uow, disciple.Id.Value, ct)).Realm > MentorRules.DiscipleMaxRealm)
                return Error.Conflict("disciple_too_advanced", $"A disciple must be below realm {MentorRules.DiscipleMaxRealm + 2}.");
            if (await repo.MasterOfAsync(uow, disciple.Id, ct) is not null) return Error.Conflict("disciple_taken", "This cultivator already has a master.");
            if ((await repo.DisciplesOfAsync(uow, me.Id, ct)).Count >= MentorRules.MaxDisciples)
                return Error.LimitReached("disciples_limit", $"At most {MentorRules.MaxDisciples} disciples.");
            var now = clock.GetUtcNow();
            await repo.AddAsync(uow, new Mentorship(me.Id, disciple.Id, now), ct);
            await Audit(uow, accountId, "social.disciple_taken", me.Id.Value, new { disciple = disciple.Id.Value }, now, ct);
            var view = await ViewAsync(uow, me.Id, ct);
            await uow.CommitAsync(ct);
            return view;
        }, ct);

    /// <summary>Ends a bond: a master releases a disciple, or a disciple leaves the master.</summary>
    public Task<Result<MentorshipView>> EndAsync(AccountId accountId, Guid characterId, Guid otherId, CancellationToken ct) =>
        db.RunAsync<Result<MentorshipView>>(async uow =>
        {
            var me = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (me is null) return CharacterNotFound;
            var other = new CharacterId(otherId);
            if (await repo.MasterOfAsync(uow, other, ct) is { } m && m.MasterId == me.Id) await repo.RemoveAsync(uow, other, ct);
            else if (await repo.MasterOfAsync(uow, me.Id, ct) is { } mine && mine.MasterId == other) await repo.RemoveAsync(uow, me.Id, ct);
            else return Error.NotFound("bond_not_found", "No such bond.");
            await Audit(uow, accountId, "social.mentorship_ended", me.Id.Value, new { other = otherId }, clock.GetUtcNow(), ct);
            var view = await ViewAsync(uow, me.Id, ct);
            await uow.CommitAsync(ct);
            return view;
        }, ct);

    private async Task<MentorshipView> ViewAsync(IUnitOfWork uow, CharacterId me, CancellationToken ct)
    {
        async Task<MentorView?> V(CharacterId id, DateTimeOffset since) =>
            await characters.FindAsync(uow, id.Value, ct) is { } c ? new MentorView(c.Id.Value, c.Name, (await cultivation.StatusAsync(uow, c.Id.Value, ct)).Realm, since) : null;
        var master = await repo.MasterOfAsync(uow, me, ct) is { } m ? await V(m.MasterId, m.Since) : null;
        var disciples = new List<MentorView>();
        foreach (var d in await repo.DisciplesOfAsync(uow, me, ct))
            if (await V(d.DiscipleId, d.Since) is { } v) disciples.Add(v);
        var realm = (await cultivation.StatusAsync(uow, me.Value, ct)).Realm;
        return new MentorshipView(master, disciples, realm >= MentorRules.MasterMinRealm, realm <= MentorRules.DiscipleMaxRealm);
    }

    private Task Audit(IUnitOfWork uow, AccountId accountId, string action, Guid characterId, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "character", characterId.ToString(), JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}

/// <summary>The master's teaching in every battle of the disciple.</summary>
internal sealed class DiscipleCombatPerks(IMentorRepository repo) : ICombatPerkBonuses
{
    public string SourcePl => "Nauki mistrza";

    public async Task<PerkBonus> BonusAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct) =>
        await repo.MasterOfAsync(uow, new CharacterId(characterId), ct) is null ? PerkBonus.None : MentorRules.Teaching;
}

/// <summary>A disciple's new realm pays the master (once per breakthrough event); at the graduation realm the bond ends with honour.</summary>
internal sealed class DiscipleBreakthroughHandler(IMentorRepository repo, IInventory inventory, ICultivationStatus cultivation) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Cultivation.Application.CultivationEvents.BreakthroughSucceeded;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Cultivation.Application.CultivationEvents.BreakthroughV1>();
        return e.Success && e.FromRealm != e.ToRealm ? RewardAsync(uow, message, e.CharacterId, ct) : Task.CompletedTask;
    }

    private async Task RewardAsync(IUnitOfWork uow, OutboxMessage message, Guid discipleId, CancellationToken ct)
    {
        var disciple = new CharacterId(discipleId);
        if (await repo.MasterOfAsync(uow, disciple, ct) is not { } bond) return;
        var credited = await inventory.CreditAsync(uow, bond.MasterId, MentorRules.StonesPerRealm, "mentor_reward", message.Id.ToString(), ct);
        if (!credited.IsSuccess && credited.Error!.Code != "duplicate_reference") throw new InvalidOperationException("Mentor reward failed: " + credited.Error.Code);
        if ((await cultivation.StatusAsync(uow, discipleId, ct)).Realm >= MentorRules.GraduationRealm) await repo.RemoveAsync(uow, disciple, ct);
    }
}

internal sealed class InMemoryMentorRepository : IMentorRepository
{
    private const string Table = "social.mentorships";

    private static InMemoryUnitOfWork U(IUnitOfWork uow) => InMemoryUnitOfWork.From(uow);

    public Task<Mentorship?> MasterOfAsync(IUnitOfWork uow, CharacterId disciple, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<CharacterId, Mentorship>(Table).GetValueOrDefault(disciple));

    public Task<IReadOnlyList<Mentorship>> DisciplesOfAsync(IUnitOfWork uow, CharacterId master, CancellationToken ct)
    {
        IReadOnlyList<Mentorship> l = [.. U(uow).Table<CharacterId, Mentorship>(Table).Values.Where(m => m.MasterId == master).OrderBy(m => m.Since)];
        return Task.FromResult(l);
    }

    public Task AddAsync(IUnitOfWork uow, Mentorship m, CancellationToken ct)
    {
        var t = U(uow).Table<CharacterId, Mentorship>(Table);
        if (t.ContainsKey(m.DiscipleId)) throw new DuplicateKeyException("Disciple already has a master.");
        U(uow).SetTable(Table, t.Add(m.DiscipleId, m));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(IUnitOfWork uow, CharacterId disciple, CancellationToken ct)
    {
        U(uow).SetTable(Table, U(uow).Table<CharacterId, Mentorship>(Table).Remove(disciple));
        return Task.CompletedTask;
    }
}

internal sealed class PostgresMentorRepository : IMentorRepository
{
    private static PostgresUnitOfWork P(IUnitOfWork uow) => PostgresUnitOfWork.From(uow);

    private static Mentorship Map(Npgsql.NpgsqlDataReader r) => new(new CharacterId(r.GetGuid(0)), new CharacterId(r.GetGuid(1)), r.GetFieldValue<DateTimeOffset>(2));

    public async Task<Mentorship?> MasterOfAsync(IUnitOfWork uow, CharacterId disciple, CancellationToken ct) =>
        (await P(uow).QueryAsync("SELECT master_id, disciple_id, since FROM social.mentorships WHERE disciple_id = $1", Map, ct, disciple.Value)).FirstOrDefault();

    public async Task<IReadOnlyList<Mentorship>> DisciplesOfAsync(IUnitOfWork uow, CharacterId master, CancellationToken ct) =>
        await P(uow).QueryAsync("SELECT master_id, disciple_id, since FROM social.mentorships WHERE master_id = $1 ORDER BY since", Map, ct, master.Value);

    public Task AddAsync(IUnitOfWork uow, Mentorship m, CancellationToken ct) =>
        P(uow).ExecuteAsync("INSERT INTO social.mentorships (master_id, disciple_id, since) VALUES ($1, $2, $3)", ct, m.MasterId.Value, m.DiscipleId.Value, m.Since);

    public Task RemoveAsync(IUnitOfWork uow, CharacterId disciple, CancellationToken ct) =>
        P(uow).ExecuteAsync("DELETE FROM social.mentorships WHERE disciple_id = $1", ct, disciple.Value);
}

public static class MentorEndpoints
{
    public static IEndpointRouteBuilder MapMentorEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/characters/{characterId:guid}/social/mentorship").WithTags("Social").RequireAuthorization();
        g.MapGet("/", async (Guid characterId, HttpContext http, MentorService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/disciples", async (Guid characterId, TakeDiscipleCommand cmd, HttpContext http, MentorService svc, CancellationToken ct) =>
            ApiResults.From(await svc.TakeAsync(http.User.GetAccountId(), characterId, cmd, ct), Results.Ok));
        g.MapDelete("/{otherId:guid}", async (Guid characterId, Guid otherId, HttpContext http, MentorService svc, CancellationToken ct) =>
            ApiResults.From(await svc.EndAsync(http.User.GetAccountId(), characterId, otherId, ct), Results.Ok));
        return app;
    }
}
