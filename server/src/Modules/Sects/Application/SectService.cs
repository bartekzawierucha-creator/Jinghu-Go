using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Geolocation.Contracts;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.Modules.Sects.Contracts;
using JianghuGo.Modules.Sects.Domain;

namespace JianghuGo.Modules.Sects.Application;

public sealed record MemberView(Guid CharacterId, string Name, string Role, long Contribution, DateTimeOffset JoinedAt);

public sealed record SectView(Guid Id, string Name, Guid LeaderCharacterId, int MemberCount, int MaxMembers, double? SeatLat, double? SeatLon,
    int Version, DateTimeOffset CreatedAt, bool Disbanded, IReadOnlyList<MemberView> Members);

public sealed record SectSummaryView(Guid Id, string Name, int MemberCount, bool HasSeat, DateTimeOffset CreatedAt);

public sealed record MySectView(SectView Sect, string MyRole, long Treasury, IReadOnlyList<ApplicationView> PendingApplications);

public sealed record ApplicationView(Guid Id, Guid SectId, Guid CharacterId, string CharacterName, string Message, string Status, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt);

public sealed record SectEventView(Guid Id, string Kind, Guid? Actor, Guid? Subject, JsonElement Data, DateTimeOffset At);

public sealed record FoundSectCommand(Guid CharacterId, string? Name);

public sealed record ApplyCommand(Guid CharacterId, string? Message);

public sealed record ActorCommand(Guid CharacterId);

public sealed record DecideCommand(Guid CharacterId, bool Accept);

public sealed record TargetCommand(Guid CharacterId, Guid TargetCharacterId);

public sealed record RoleCommand(Guid CharacterId, Guid TargetCharacterId, string? Role);

public sealed record DonateCommand(Guid CharacterId, long Amount);

public sealed class SectService(
    IUnitOfWorkFactory uowFactory,
    ISectRepository repo,
    ICharacterDirectory characters,
    IInventory inventory,
    IPositionVerifier positions,
    IIdempotencyStore idempotency,
    IAuditLog audit,
    IOutbox outbox,
    ICorrelationContext correlation,
    SectsConfig config,
    TimeProvider clock)
{
    public const int MaxPageSize = 100;

    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");
    private static readonly Error SectNotFound = Error.NotFound("sect_not_found", "Sect not found.");
    private static readonly Error NotMember = Error.Forbidden("not_a_member", "Your character is not a member of this sect.");
    private static readonly Error Denied = Error.Forbidden("sect_permission_denied", "Your rank in the sect does not allow this.");
    private static readonly Error Concurrent = Error.Conflict("version_conflict", "The sect changed concurrently; reload and retry.");
    private static readonly Error AlreadyInSect = Error.Conflict("already_in_sect", "The character already belongs to a sect.");

    private sealed record Actor(CharacterSummary Character, Member Member, Sect Sect);

    // ---------------------------------------------------------------- founding & reading

    public Task<Result<MySectView>> FoundAsync(AccountId accountId, string? key, FoundSectCommand cmd, CancellationToken ct) =>
        Write<MySectView, object>(accountId, "sects.found", key, cmd, async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
            if (c is null) return CharacterNotFound;
            var name = SectNames.Validate(cmd.Name);
            if (!name.IsSuccess) return name.Error!;
            if (await repo.FindMemberAsync(uow, c.Id, ct) is not null) return AlreadyInSect;
            if (await repo.NameTakenAsync(uow, name.Value.Normalized, ct)) return Error.Conflict("sect_name_taken", "This sect name is taken.");

            var now = clock.GetUtcNow();
            var sect = new Sect(Uuid7.New(), name.Value.Name, name.Value.Normalized, c.Id, accountId, 1, null, null, 1, now, null);
            if (config.FoundingFeeStones > 0)
            {
                var paid = await inventory.DebitAsync(uow, c.Id, config.FoundingFeeStones, "sect_founding", sect.Id.ToString(), ct);
                if (!paid.IsSuccess) return paid.Error!;
            }

            await repo.AddSectAsync(uow, sect, ct);
            var leader = new Member(c.Id, sect.Id, accountId, c.Name, SectRoles.Leader, 0, now);
            await repo.AddMemberAsync(uow, leader, ct);
            await CloseOtherApplicationsAsync(uow, c.Id, now, ct);
            await EventAsync(uow, accountId, sect.Id, "founded", c.Id, null, new { sect.Name }, now, ct);
            return await MyViewAsync(uow, sect, leader, ct);
        }, ct);

    public Task<Result<IReadOnlyList<SectSummaryView>>> ListAsync(Guid? before, int? limit, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<SectSummaryView>>>(async uow =>
        {
            var rows = await repo.ListSectsAsync(uow, before, Math.Clamp(limit ?? 50, 1, MaxPageSize), ct);
            return Result<IReadOnlyList<SectSummaryView>>.Success([.. rows.Select(s => new SectSummaryView(s.Id, s.Name, s.MemberCount, s.Seat is not null, s.CreatedAt))]);
        }, ct);

    /// <summary>Public profile: name, seat, members with roles and contributions. Treasury and applications are members-only.</summary>
    public Task<Result<SectView>> GetAsync(Guid sectId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<SectView>>(async uow =>
            await repo.FindSectAsync(uow, sectId, ct) is { } s ? await ViewAsync(uow, s, ct) : SectNotFound, ct);

    /// <summary>The character's own sect with member-only details; elders and the leader also see pending applications.</summary>
    public Task<Result<MySectView>> MineAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<MySectView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var m = await repo.FindMemberAsync(uow, c.Id, ct);
            if (m is null) return Error.NotFound("not_in_sect", "The character does not belong to a sect.");
            var s = (await repo.FindSectAsync(uow, m.SectId, ct))!;
            return await MyViewAsync(uow, s, m, ct);
        }, ct);

    public Task<Result<IReadOnlyList<SectEventView>>> HistoryAsync(AccountId accountId, Guid sectId, Guid characterId, Guid? before, int? limit, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<SectEventView>>>(async uow =>
        {
            var actor = await ActorAsync(uow, accountId, characterId, sectId, ct);
            if (!actor.IsSuccess) return actor.Error!;
            var rows = await repo.ListEventsAsync(uow, sectId, before, Math.Clamp(limit ?? 50, 1, MaxPageSize), ct);
            return Result<IReadOnlyList<SectEventView>>.Success([.. rows.Select(e => new SectEventView(e.Id, e.Kind, e.Actor?.Value, e.Subject?.Value,
                JsonDocument.Parse(e.DataJson).RootElement.Clone(), e.At))]);
        }, ct);

    // ---------------------------------------------------------------- applications

    public Task<Result<ApplicationView>> ApplyAsync(AccountId accountId, Guid sectId, string? key, ApplyCommand cmd, CancellationToken ct) =>
        Write<ApplicationView, object>(accountId, "sects.apply", key, new { sectId, cmd }, async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
            if (c is null) return CharacterNotFound;
            var message = (cmd.Message ?? string.Empty).Trim();
            if (message.Length > config.ApplicationMessageMaxLength || message.Any(char.IsControl))
                return Error.Validation("message_invalid", $"Message: up to {config.ApplicationMessageMaxLength} characters, no control characters.");
            var s = await repo.FindSectAsync(uow, sectId, ct);
            if (s is null || s.DisbandedAt is not null) return SectNotFound;
            if (await repo.FindMemberAsync(uow, c.Id, ct) is not null) return AlreadyInSect;
            if (s.MemberCount >= config.MaxMembers) return Error.LimitReached("sect_full", "The sect is full.");
            var pending = await repo.ListPendingForCharacterAsync(uow, c.Id, ct);
            if (pending.Any(a => a.SectId == sectId)) return Error.Conflict("application_pending", "You already applied to this sect.");
            if (pending.Count >= config.MaxPendingApplicationsPerCharacter)
                return Error.LimitReached("application_limit", $"At most {config.MaxPendingApplicationsPerCharacter} pending applications.");

            var now = clock.GetUtcNow();
            var application = new SectApplication(Uuid7.New(), sectId, c.Id, accountId, c.Name, message, ApplicationStatuses.Pending, now, null, null);
            await repo.AddApplicationAsync(uow, application, ct);
            await AuditAsync(uow, accountId, "sects.applied", sectId, new { characterId = c.Id.Value, applicationId = application.Id }, now, ct);
            return View(application);
        }, ct);

    public Task<Result<ApplicationView>> WithdrawAsync(AccountId accountId, Guid applicationId, ActorCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<ApplicationView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, cmd.CharacterId, ct);
            if (c is null) return CharacterNotFound;
            var a = await repo.FindApplicationAsync(uow, applicationId, ct);
            if (a is null || a.CharacterId != c.Id) return Error.NotFound("application_not_found", "Application not found.");
            var closed = a with { Status = ApplicationStatuses.Withdrawn, DecidedAt = clock.GetUtcNow(), DecidedBy = c.Id };
            if (!await repo.TryCloseApplicationAsync(uow, closed, ct)) return Error.Conflict("application_closed", "The application was already decided.");
            await uow.CommitAsync(ct);
            return View(closed);
        }, ct);

    /// <summary>Leader or elder accepts/rejects. Accepting checks capacity and that the applicant joined no other sect meanwhile.</summary>
    public Task<Result<ApplicationView>> DecideAsync(AccountId accountId, Guid applicationId, DecideCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<ApplicationView>>(async uow =>
        {
            var a = await repo.FindApplicationAsync(uow, applicationId, ct);
            if (a is null) return Error.NotFound("application_not_found", "Application not found.");
            var actor = await ActorAsync(uow, accountId, cmd.CharacterId, a.SectId, ct);
            if (!actor.IsSuccess) return actor.Error!;
            var (c, m, s) = (actor.Value.Character, actor.Value.Member, actor.Value.Sect);
            if (!SectPolicy.Can(m.Role, SectActions.DecideApplication)) return Denied;
            if (a.Status != ApplicationStatuses.Pending) return Error.Conflict("application_closed", "The application was already decided.");

            var now = clock.GetUtcNow();
            var closed = a with { Status = cmd.Accept ? ApplicationStatuses.Accepted : ApplicationStatuses.Rejected, DecidedAt = now, DecidedBy = c.Id };
            if (cmd.Accept)
            {
                if (s.MemberCount >= config.MaxMembers) return Error.LimitReached("sect_full", "The sect is full.");
                if (await repo.FindMemberAsync(uow, a.CharacterId, ct) is not null) return Error.Conflict("applicant_in_sect", "The applicant has joined another sect.");
                if (!await repo.TryUpdateSectAsync(uow, s with { MemberCount = s.MemberCount + 1, Version = s.Version + 1 }, s.Version, ct)) return Concurrent;
                await repo.AddMemberAsync(uow, new Member(a.CharacterId, s.Id, a.AccountId, a.CharacterName, SectRoles.Member, 0, now), ct);
            }

            if (!await repo.TryCloseApplicationAsync(uow, closed, ct)) return Error.Conflict("application_closed", "The application was already decided.");
            if (cmd.Accept) await CloseOtherApplicationsAsync(uow, a.CharacterId, now, ct);
            await EventAsync(uow, accountId, s.Id, cmd.Accept ? "joined" : "application_rejected", c.Id, a.CharacterId, new { applicationId }, now, ct);
            await outbox.EnqueueAsync(uow, OutboxMessage.Create(SectEvents.ApplicationDecided,
                new SectEvents.ApplicationDecidedV1(a.Id, s.Id, s.Name, a.AccountId.Value, a.CharacterId.Value, cmd.Accept), now, correlation.CorrelationId), ct);
            await uow.CommitAsync(ct);
            return View(closed);
        }, ct);

    // ---------------------------------------------------------------- membership & roles

    /// <summary>A member leaves. The leader must hand over leadership first, unless they are the last member (the sect is disbanded).</summary>
    public Task<Result<SectView>> LeaveAsync(AccountId accountId, Guid sectId, ActorCommand cmd, CancellationToken ct) =>
        Mutate<SectView>(accountId, cmd.CharacterId, sectId, async (uow, actor, now) =>
        {
            var (c, m, s) = (actor.Character, actor.Member, actor.Sect);
            if (m.Role == SectRoles.Leader && s.MemberCount > 1)
                return Error.Conflict("leader_must_transfer", "Transfer leadership before leaving (or be the last member).");
            await repo.RemoveMemberAsync(uow, c.Id, ct);
            var updated = s with { MemberCount = s.MemberCount - 1, Version = s.Version + 1, DisbandedAt = s.MemberCount == 1 ? now : null };
            if (!await repo.TryUpdateSectAsync(uow, updated, s.Version, ct)) return Concurrent;
            await EventAsync(uow, accountId, s.Id, updated.DisbandedAt is null ? "left" : "disbanded", c.Id, c.Id, new { }, now, ct);
            return await ViewAsync(uow, updated, ct);
        }, ct);

    public Task<Result<SectView>> KickAsync(AccountId accountId, Guid sectId, TargetCommand cmd, CancellationToken ct) =>
        Mutate<SectView>(accountId, cmd.CharacterId, sectId, async (uow, actor, now) =>
        {
            var target = await repo.FindMemberAsync(uow, new CharacterId(cmd.TargetCharacterId), ct);
            if (target is null || target.SectId != sectId) return Error.NotFound("member_not_found", "Member not found.");
            if (!SectPolicy.CanKick(actor.Member.Role, target.Role)) return Denied;
            await repo.RemoveMemberAsync(uow, target.CharacterId, ct);
            var updated = actor.Sect with { MemberCount = actor.Sect.MemberCount - 1, Version = actor.Sect.Version + 1 };
            if (!await repo.TryUpdateSectAsync(uow, updated, actor.Sect.Version, ct)) return Concurrent;
            await EventAsync(uow, accountId, sectId, "kicked", actor.Character.Id, target.CharacterId, new { target.Role }, now, ct);
            return await ViewAsync(uow, updated, ct);
        }, ct);

    /// <summary>Leader only. Role "leader" transfers leadership: the target becomes leader, the old leader an elder.</summary>
    public Task<Result<SectView>> SetRoleAsync(AccountId accountId, Guid sectId, RoleCommand cmd, CancellationToken ct) =>
        Mutate<SectView>(accountId, cmd.CharacterId, sectId, async (uow, actor, now) =>
        {
            if (!SectPolicy.Can(actor.Member.Role, SectActions.SetRole)) return Denied;
            if (cmd.Role is null || !SectRoles.All.Contains(cmd.Role)) return Error.Validation("role_invalid", "Role must be leader, elder or member.");
            var target = await repo.FindMemberAsync(uow, new CharacterId(cmd.TargetCharacterId), ct);
            if (target is null || target.SectId != sectId) return Error.NotFound("member_not_found", "Member not found.");
            if (target.CharacterId == actor.Character.Id) return Error.Validation("cannot_change_own_role", "Transfer leadership to someone else instead.");

            var sect = actor.Sect with { Version = actor.Sect.Version + 1 };
            if (cmd.Role == SectRoles.Leader)
            {
                await repo.UpdateMemberAsync(uow, actor.Member with { Role = SectRoles.Elder }, ct);
                sect = sect with { LeaderCharacterId = target.CharacterId, LeaderAccountId = target.AccountId };
            }

            await repo.UpdateMemberAsync(uow, target with { Role = cmd.Role }, ct);
            if (!await repo.TryUpdateSectAsync(uow, sect, actor.Sect.Version, ct)) return Concurrent;
            await EventAsync(uow, accountId, sectId, cmd.Role == SectRoles.Leader ? "leadership_transferred" : "role_changed", actor.Character.Id, target.CharacterId,
                new { from = target.Role, to = cmd.Role }, now, ct);
            return await ViewAsync(uow, sect, ct);
        }, ct);

    // ---------------------------------------------------------------- treasury & seat

    /// <summary>Member donates Spirit Stones: wallet → treasury in one transaction; contribution points grow by the amount.</summary>
    public Task<Result<MySectView>> DonateAsync(AccountId accountId, Guid sectId, string? key, DonateCommand cmd, CancellationToken ct) =>
        Write<MySectView, object>(accountId, "sects.donate", key, new { sectId, cmd }, async uow =>
        {
            if (cmd.Amount < config.MinDonation || cmd.Amount > config.MaxDonation)
                return Error.Validation("amount_invalid", $"Donation must be {config.MinDonation}-{config.MaxDonation} Spirit Stones.");
            var actor = await ActorAsync(uow, accountId, cmd.CharacterId, sectId, ct);
            if (!actor.IsSuccess) return actor.Error!;
            var (c, m, s) = (actor.Value.Character, actor.Value.Member, actor.Value.Sect);
            var now = clock.GetUtcNow();
            var reference = Uuid7.New().ToString();
            var paid = await inventory.DebitAsync(uow, c.Id, cmd.Amount, "sect_donation", reference, ct);
            if (!paid.IsSuccess) return paid.Error!;
            var treasury = await repo.PostTreasuryAsync(uow, sectId, cmd.Amount, "donation", reference, now, ct);
            if (!treasury.IsSuccess) throw new InvalidOperationException("Treasury credit failed: " + treasury.Error!.Code);
            var updated = m with { Contribution = checked(m.Contribution + cmd.Amount) };
            await repo.UpdateMemberAsync(uow, updated, ct);
            if (!await repo.TryUpdateSectAsync(uow, s with { Version = s.Version + 1 }, s.Version, ct)) return Concurrent;
            await EventAsync(uow, accountId, sectId, "donated", c.Id, c.Id, new { cmd.Amount }, now, ct);
            return await MyViewAsync(uow, s with { Version = s.Version + 1 }, updated, ct);
        }, ct);

    /// <summary>
    /// The leader founds (or moves) the seat where they stand — a VERIFIED position. The seat is public, so it is stored
    /// rounded (default 3 decimals ≈ 100 m), never the raw fix. Paid from the treasury; moving has a cooldown.
    /// </summary>
    public Task<Result<MySectView>> SetSeatAsync(AccountId accountId, Guid sectId, string? key, ActorCommand cmd, CancellationToken ct) =>
        Write<MySectView, object>(accountId, "sects.seat", key, new { sectId, cmd }, async uow =>
        {
            var actor = await ActorAsync(uow, accountId, cmd.CharacterId, sectId, ct);
            if (!actor.IsSuccess) return actor.Error!;
            var (c, m, s) = (actor.Value.Character, actor.Value.Member, actor.Value.Sect);
            if (!SectPolicy.Can(m.Role, SectActions.SetSeat)) return Denied;
            var now = clock.GetUtcNow();
            if (s.SeatSetAt is { } last && last.AddDays(config.Seat.MoveCooldownDays) > now)
                return Error.Conflict("seat_cooldown", $"The seat can move again after {last.AddDays(config.Seat.MoveCooldownDays):O}.");
            var check = await positions.CheckAsync(uow, accountId, c.Id, now, ct);
            if (check.Verified is not { } position) return Error.Conflict(check.Problem!, $"Your position cannot be used right now ({check.Problem}).");

            var reference = Uuid7.New().ToString();
            if (config.Seat.CostStones > 0)
            {
                var paid = await repo.PostTreasuryAsync(uow, sectId, -config.Seat.CostStones, "seat", reference, now, ct);
                if (!paid.IsSuccess) return paid.Error!;
            }

            var d = config.Seat.PrecisionDecimals;
            var seat = new GeoPoint(Math.Round(position.Point.Lat, d), Math.Round(position.Point.Lon, d));
            var updated = s with { Seat = seat, SeatSetAt = now, Version = s.Version + 1 };
            if (!await repo.TryUpdateSectAsync(uow, updated, s.Version, ct)) return Concurrent;
            await EventAsync(uow, accountId, sectId, "seat_set", c.Id, null, new { cost = config.Seat.CostStones }, now, ct); // no coordinates in history
            return await MyViewAsync(uow, updated, m, ct);
        }, ct);

    // ---------------------------------------------------------------- helpers

    private async Task<Result<Actor>> ActorAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, Guid sectId, CancellationToken ct)
    {
        var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
        if (c is null) return CharacterNotFound;
        var s = await repo.FindSectAsync(uow, sectId, ct);
        if (s is null || s.DisbandedAt is not null) return SectNotFound;
        var m = await repo.FindMemberAsync(uow, c.Id, ct);
        if (m is null || m.SectId != sectId) return NotMember;
        return new Actor(c, m, s);
    }

    private Task<Result<T>> Mutate<T>(AccountId accountId, Guid characterId, Guid sectId, Func<IUnitOfWork, Actor, DateTimeOffset, Task<Result<T>>> action, CancellationToken ct) =>
        uowFactory.RunAsync<Result<T>>(async uow =>
        {
            var actor = await ActorAsync(uow, accountId, characterId, sectId, ct);
            if (!actor.IsSuccess) return actor.Error!;
            var result = await action(uow, actor.Value, clock.GetUtcNow());
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    private Task<Result<T>> Write<T, TRequest>(AccountId accountId, string operation, string? key, TRequest request, Func<IUnitOfWork, Task<Result<T>>> action, CancellationToken ct) =>
        uowFactory.RunAsync<Result<T>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, operation, key, request, () => action(uow), ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    private async Task CloseOtherApplicationsAsync(IUnitOfWork uow, CharacterId characterId, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var other in await repo.ListPendingForCharacterAsync(uow, characterId, ct))
        {
            await repo.TryCloseApplicationAsync(uow, other with { Status = ApplicationStatuses.Withdrawn, DecidedAt = now, DecidedBy = characterId }, ct);
        }
    }

    private async Task<SectView> ViewAsync(IUnitOfWork uow, Sect s, CancellationToken ct)
    {
        var members = await repo.ListMembersAsync(uow, s.Id, ct);
        return new SectView(s.Id, s.Name, s.LeaderCharacterId.Value, s.MemberCount, config.MaxMembers, s.Seat?.Lat, s.Seat?.Lon, s.Version, s.CreatedAt,
            s.DisbandedAt is not null,
            [.. members.OrderByDescending(m => SectRoles.Rank(m.Role)).ThenByDescending(m => m.Contribution).ThenBy(m => m.JoinedAt)
                .Select(m => new MemberView(m.CharacterId.Value, m.Name, m.Role, m.Contribution, m.JoinedAt))]);
    }

    private async Task<MySectView> MyViewAsync(IUnitOfWork uow, Sect s, Member me, CancellationToken ct)
    {
        var pending = SectPolicy.Can(me.Role, SectActions.DecideApplication) ? await repo.ListPendingForSectAsync(uow, s.Id, ct) : [];
        return new MySectView(await ViewAsync(uow, s, ct), me.Role, await repo.TreasuryAsync(uow, s.Id, ct), [.. pending.Select(View)]);
    }

    private static ApplicationView View(SectApplication a) =>
        new(a.Id, a.SectId, a.CharacterId.Value, a.CharacterName, a.Message, a.Status, a.CreatedAt, a.DecidedAt);

    private async Task EventAsync(IUnitOfWork uow, AccountId accountId, Guid sectId, string kind, CharacterId? actor, CharacterId? subject, object data, DateTimeOffset now, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, Idempotency.Json);
        await repo.AddEventAsync(uow, new SectEvent(Uuid7.New(), sectId, kind, actor, subject, json, now), ct);
        await AuditAsync(uow, accountId, "sects." + kind, sectId, new { actor = actor?.Value, subject = subject?.Value, data }, now, ct);
    }

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, Guid sectId, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "sect", sectId.ToString(),
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}

/// <summary>ISectDirectory for Territory: membership lookups and treasury movements inside the caller's transaction.</summary>
internal sealed class SectDirectory(ISectRepository repo, TimeProvider clock) : ISectDirectory
{
    public async Task<SectMembership?> MembershipAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        var m = await repo.FindMemberAsync(uow, characterId, ct);
        if (m is null) return null;
        var s = await repo.FindSectAsync(uow, m.SectId, ct);
        return s is null || s.DisbandedAt is not null ? null : new SectMembership(s.Id, s.Name, m.Role);
    }

    public async Task<SectInfo?> FindAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct) =>
        await repo.FindSectAsync(uow, sectId, ct) is { } s
            ? new SectInfo(s.Id, s.Name, s.LeaderCharacterId.Value, s.LeaderAccountId.Value, s.MemberCount, s.DisbandedAt is not null)
            : null;

    public bool Can(string role, string action) => SectPolicy.Can(role, action);

    public Task<Result<long>> DebitTreasuryAsync(IUnitOfWork uow, Guid sectId, long amount, string reason, string referenceId, CancellationToken ct) =>
        amount <= 0 ? Task.FromResult<Result<long>>(Error.Validation("amount_invalid", "Amount must be positive."))
            : repo.PostTreasuryAsync(uow, sectId, -amount, reason, referenceId, clock.GetUtcNow(), ct);

    public Task<Result<long>> CreditTreasuryAsync(IUnitOfWork uow, Guid sectId, long amount, string reason, string referenceId, CancellationToken ct) =>
        amount <= 0 ? Task.FromResult<Result<long>>(Error.Validation("amount_invalid", "Amount must be positive."))
            : repo.PostTreasuryAsync(uow, sectId, amount, reason, referenceId, clock.GetUtcNow(), ct);
}
