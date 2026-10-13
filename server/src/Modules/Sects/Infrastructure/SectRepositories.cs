using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Infrastructure.InMemory;
using JianghuGo.Infrastructure.Postgres;
using JianghuGo.Modules.Sects.Application;
using JianghuGo.Modules.Sects.Domain;
using Npgsql;

namespace JianghuGo.Modules.Sects.Infrastructure;

internal sealed class InMemorySectRepository : ISectRepository
{
    private const string Sects = "sects.sects";
    private const string Members = "sects.members";
    private const string Applications = "sects.applications";
    private const string Treasuries = "sects.treasuries";
    private const string TreasuryEntries = "sects.treasury_entries";
    private const string Events = "sects.events";

    private sealed record TreasuryEntry(Guid Id, Guid SectId, long Delta, long BalanceAfter, string Reason, string ReferenceId, DateTimeOffset At);

    private static InMemoryUnitOfWork U(IUnitOfWork uow) => InMemoryUnitOfWork.From(uow);

    public Task AddSectAsync(IUnitOfWork uow, Sect sect, CancellationToken ct)
    {
        var t = U(uow).Table<Guid, Sect>(Sects);
        if (t.Values.Any(s => s.NormalizedName == sect.NormalizedName)) throw new DuplicateKeyException("Sect name taken.");
        U(uow).SetTable(Sects, t.Add(sect.Id, sect));
        return Task.CompletedTask;
    }

    public Task<Sect?> FindSectAsync(IUnitOfWork uow, Guid id, CancellationToken ct) => Task.FromResult(U(uow).Table<Guid, Sect>(Sects).GetValueOrDefault(id));

    public Task<bool> NameTakenAsync(IUnitOfWork uow, string normalizedName, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, Sect>(Sects).Values.Any(s => s.NormalizedName == normalizedName));

    public Task<bool> TryUpdateSectAsync(IUnitOfWork uow, Sect sect, int expectedVersion, CancellationToken ct)
    {
        var t = U(uow).Table<Guid, Sect>(Sects);
        if (!t.TryGetValue(sect.Id, out var cur) || cur.Version != expectedVersion || sect.Version != expectedVersion + 1 || cur.DisbandedAt is not null) return Task.FromResult(false);
        U(uow).SetTable(Sects, t.SetItem(sect.Id, sect));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Sect>> ListSectsAsync(IUnitOfWork uow, Guid? before, int limit, CancellationToken ct)
    {
        IReadOnlyList<Sect> list = [.. U(uow).Table<Guid, Sect>(Sects).Values
            .Where(s => s.DisbandedAt is null && (before is null || s.Id.CompareTo(before.Value) < 0)).OrderByDescending(s => s.Id).Take(limit)];
        return Task.FromResult(list);
    }

    public Task<Member?> FindMemberAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<CharacterId, Member>(Members).GetValueOrDefault(characterId));

    public Task<IReadOnlyList<Member>> ListMembersAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct)
    {
        IReadOnlyList<Member> list = [.. U(uow).Table<CharacterId, Member>(Members).Values.Where(m => m.SectId == sectId)];
        return Task.FromResult(list);
    }

    public Task AddMemberAsync(IUnitOfWork uow, Member member, CancellationToken ct)
    {
        var t = U(uow).Table<CharacterId, Member>(Members);
        if (t.ContainsKey(member.CharacterId)) throw new DuplicateKeyException("Character already in a sect.");
        U(uow).SetTable(Members, t.Add(member.CharacterId, member));
        return Task.CompletedTask;
    }

    public Task UpdateMemberAsync(IUnitOfWork uow, Member member, CancellationToken ct)
    {
        U(uow).SetTable(Members, U(uow).Table<CharacterId, Member>(Members).SetItem(member.CharacterId, member));
        return Task.CompletedTask;
    }

    public Task RemoveMemberAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        U(uow).SetTable(Members, U(uow).Table<CharacterId, Member>(Members).Remove(characterId));
        return Task.CompletedTask;
    }

    public Task AddApplicationAsync(IUnitOfWork uow, SectApplication application, CancellationToken ct)
    {
        var t = U(uow).Table<Guid, SectApplication>(Applications);
        if (t.Values.Any(a => a.Status == ApplicationStatuses.Pending && a.SectId == application.SectId && a.CharacterId == application.CharacterId))
            throw new DuplicateKeyException("Application already pending.");
        U(uow).SetTable(Applications, t.Add(application.Id, application));
        return Task.CompletedTask;
    }

    public Task<SectApplication?> FindApplicationAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, SectApplication>(Applications).GetValueOrDefault(id));

    public Task<IReadOnlyList<SectApplication>> ListPendingForSectAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct)
    {
        IReadOnlyList<SectApplication> list = [.. U(uow).Table<Guid, SectApplication>(Applications).Values
            .Where(a => a.SectId == sectId && a.Status == ApplicationStatuses.Pending).OrderBy(a => a.Id)];
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<SectApplication>> ListPendingForCharacterAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct)
    {
        IReadOnlyList<SectApplication> list = [.. U(uow).Table<Guid, SectApplication>(Applications).Values
            .Where(a => a.CharacterId == characterId && a.Status == ApplicationStatuses.Pending).OrderBy(a => a.Id)];
        return Task.FromResult(list);
    }

    public Task<bool> TryCloseApplicationAsync(IUnitOfWork uow, SectApplication closed, CancellationToken ct)
    {
        var t = U(uow).Table<Guid, SectApplication>(Applications);
        if (!t.TryGetValue(closed.Id, out var cur) || cur.Status != ApplicationStatuses.Pending) return Task.FromResult(false);
        U(uow).SetTable(Applications, t.SetItem(closed.Id, closed));
        return Task.FromResult(true);
    }

    public Task<long> TreasuryAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, long>(Treasuries).GetValueOrDefault(sectId));

    public Task<Result<long>> PostTreasuryAsync(IUnitOfWork uow, Guid sectId, long delta, string reason, string referenceId, DateTimeOffset at, CancellationToken ct)
    {
        var u = U(uow);
        var treasuries = u.Table<Guid, long>(Treasuries);
        var current = treasuries.GetValueOrDefault(sectId);
        var after = checked(current + delta);
        if (after < 0) return Task.FromResult<Result<long>>(Error.InsufficientFunds("insufficient_treasury", $"The sect treasury holds {current} Spirit Stones (needs {-delta})."));
        var entries = u.Table<Guid, TreasuryEntry>(TreasuryEntries);
        if (entries.Values.Any(e => e.SectId == sectId && e.Reason == reason && e.ReferenceId == referenceId)) throw new DuplicateKeyException("Treasury entry already applied.");
        var entry = new TreasuryEntry(Uuid7.New(), sectId, delta, after, reason, referenceId, at);
        u.SetTable(TreasuryEntries, entries.Add(entry.Id, entry));
        u.SetTable(Treasuries, treasuries.SetItem(sectId, after));
        return Task.FromResult<Result<long>>(after);
    }

    private const string Chat = "sects.chat_messages";

    public Task AddChatAsync(IUnitOfWork uow, ChatMessage m, CancellationToken ct)
    {
        U(uow).SetTable(Chat, U(uow).Table<Guid, ChatMessage>(Chat).Add(m.Id, m));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ChatMessage>> ListChatAsync(IUnitOfWork uow, Guid sectId, Guid? after, int limit, CancellationToken ct)
    {
        IReadOnlyList<ChatMessage> list = [.. U(uow).Table<Guid, ChatMessage>(Chat).Values
            .Where(m => m.SectId == sectId && (after is null || m.Id.CompareTo(after.Value) > 0)).OrderByDescending(m => m.Id).Take(limit)];
        return Task.FromResult(list);
    }

    public Task<int> CountChatSinceAsync(IUnitOfWork uow, CharacterId characterId, DateTimeOffset since, CancellationToken ct) =>
        Task.FromResult(U(uow).Table<Guid, ChatMessage>(Chat).Values.Count(m => m.CharacterId == characterId && m.At >= since));

    public Task AddEventAsync(IUnitOfWork uow, SectEvent e, CancellationToken ct)
    {
        U(uow).SetTable(Events, U(uow).Table<Guid, SectEvent>(Events).Add(e.Id, e));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SectEvent>> ListEventsAsync(IUnitOfWork uow, Guid sectId, Guid? before, int limit, CancellationToken ct)
    {
        IReadOnlyList<SectEvent> list = [.. U(uow).Table<Guid, SectEvent>(Events).Values
            .Where(e => e.SectId == sectId && (before is null || e.Id.CompareTo(before.Value) < 0)).OrderByDescending(e => e.Id).Take(limit)];
        return Task.FromResult(list);
    }
}

internal sealed class PostgresSectRepository : ISectRepository
{
    private const string SectColumns = "id, name, normalized_name, leader_character_id, leader_account_id, member_count, seat_lat, seat_lon, seat_set_at, version, created_at, disbanded_at";
    private const string MemberColumns = "character_id, sect_id, account_id, name, role, contribution, joined_at";
    private const string ApplicationColumns = "id, sect_id, character_id, account_id, character_name, message, status, created_at, decided_at, decided_by";

    private static PostgresUnitOfWork Pg(IUnitOfWork uow) => PostgresUnitOfWork.From(uow);

    private static Sect MapSect(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetString(1), r.GetString(2), new CharacterId(r.GetGuid(3)), new AccountId(r.GetGuid(4)), r.GetInt32(5),
        r.IsDBNull(6) ? null : new GeoPoint(r.GetDouble(6), r.GetDouble(7)), r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8),
        r.GetInt32(9), r.GetFieldValue<DateTimeOffset>(10), r.IsDBNull(11) ? null : r.GetFieldValue<DateTimeOffset>(11));

    private static Member MapMember(NpgsqlDataReader r) => new(
        new CharacterId(r.GetGuid(0)), r.GetGuid(1), new AccountId(r.GetGuid(2)), r.GetString(3), r.GetString(4), r.GetInt64(5), r.GetFieldValue<DateTimeOffset>(6));

    private static SectApplication MapApplication(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetGuid(1), new CharacterId(r.GetGuid(2)), new AccountId(r.GetGuid(3)), r.GetString(4), r.GetString(5), r.GetString(6),
        r.GetFieldValue<DateTimeOffset>(7), r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8), r.IsDBNull(9) ? null : new CharacterId(r.GetGuid(9)));

    public Task AddSectAsync(IUnitOfWork uow, Sect s, CancellationToken ct) =>
        Pg(uow).ExecuteAsync($"INSERT INTO sects.sects ({SectColumns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)",
            ct, s.Id, s.Name, s.NormalizedName, s.LeaderCharacterId.Value, s.LeaderAccountId.Value, s.MemberCount, s.Seat?.Lat, s.Seat?.Lon, s.SeatSetAt,
            s.Version, s.CreatedAt, s.DisbandedAt);

    public Task<Sect?> FindSectAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        Pg(uow).QuerySingleOrDefaultAsync($"SELECT {SectColumns} FROM sects.sects WHERE id = $1", MapSect, ct, id);

    public async Task<bool> NameTakenAsync(IUnitOfWork uow, string normalizedName, CancellationToken ct) =>
        (await Pg(uow).QueryAsync("SELECT 1 FROM sects.sects WHERE normalized_name = $1", _ => 1, ct, normalizedName)).Count > 0;

    public async Task<bool> TryUpdateSectAsync(IUnitOfWork uow, Sect s, int expectedVersion, CancellationToken ct) =>
        s.Version == expectedVersion + 1 && await Pg(uow).ExecuteAsync(
            "UPDATE sects.sects SET leader_character_id = $2, leader_account_id = $3, member_count = $4, seat_lat = $5, seat_lon = $6, seat_set_at = $7, " +
            "version = $8, disbanded_at = $9 WHERE id = $1 AND version = $10 AND disbanded_at IS NULL",
            ct, s.Id, s.LeaderCharacterId.Value, s.LeaderAccountId.Value, s.MemberCount, s.Seat?.Lat, s.Seat?.Lon, s.SeatSetAt, s.Version, s.DisbandedAt, expectedVersion) == 1;

    public async Task<IReadOnlyList<Sect>> ListSectsAsync(IUnitOfWork uow, Guid? before, int limit, CancellationToken ct) =>
        await Pg(uow).QueryAsync($"SELECT {SectColumns} FROM sects.sects WHERE disbanded_at IS NULL AND ($1::uuid IS NULL OR id < $1::uuid) ORDER BY id DESC LIMIT $2",
            MapSect, ct, before, limit);

    public Task<Member?> FindMemberAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        Pg(uow).QuerySingleOrDefaultAsync($"SELECT {MemberColumns} FROM sects.members WHERE character_id = $1", MapMember, ct, characterId.Value);

    public async Task<IReadOnlyList<Member>> ListMembersAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct) =>
        await Pg(uow).QueryAsync($"SELECT {MemberColumns} FROM sects.members WHERE sect_id = $1", MapMember, ct, sectId);

    public Task AddMemberAsync(IUnitOfWork uow, Member m, CancellationToken ct) =>
        Pg(uow).ExecuteAsync($"INSERT INTO sects.members ({MemberColumns}) VALUES ($1, $2, $3, $4, $5, $6, $7)",
            ct, m.CharacterId.Value, m.SectId, m.AccountId.Value, m.Name, m.Role, m.Contribution, m.JoinedAt);

    public Task UpdateMemberAsync(IUnitOfWork uow, Member m, CancellationToken ct) =>
        Pg(uow).ExecuteAsync("UPDATE sects.members SET role = $3, contribution = $4 WHERE character_id = $1 AND sect_id = $2",
            ct, m.CharacterId.Value, m.SectId, m.Role, m.Contribution);

    public Task RemoveMemberAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        Pg(uow).ExecuteAsync("DELETE FROM sects.members WHERE character_id = $1", ct, characterId.Value);

    public Task AddApplicationAsync(IUnitOfWork uow, SectApplication a, CancellationToken ct) =>
        Pg(uow).ExecuteAsync($"INSERT INTO sects.applications ({ApplicationColumns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)",
            ct, a.Id, a.SectId, a.CharacterId.Value, a.AccountId.Value, a.CharacterName, a.Message, a.Status, a.CreatedAt, a.DecidedAt, a.DecidedBy?.Value);

    public Task<SectApplication?> FindApplicationAsync(IUnitOfWork uow, Guid id, CancellationToken ct) =>
        Pg(uow).QuerySingleOrDefaultAsync($"SELECT {ApplicationColumns} FROM sects.applications WHERE id = $1", MapApplication, ct, id);

    public async Task<IReadOnlyList<SectApplication>> ListPendingForSectAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct) =>
        await Pg(uow).QueryAsync($"SELECT {ApplicationColumns} FROM sects.applications WHERE sect_id = $1 AND status = 'pending' ORDER BY id", MapApplication, ct, sectId);

    public async Task<IReadOnlyList<SectApplication>> ListPendingForCharacterAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct) =>
        await Pg(uow).QueryAsync($"SELECT {ApplicationColumns} FROM sects.applications WHERE character_id = $1 AND status = 'pending' ORDER BY id",
            MapApplication, ct, characterId.Value);

    public async Task<bool> TryCloseApplicationAsync(IUnitOfWork uow, SectApplication a, CancellationToken ct) =>
        await Pg(uow).ExecuteAsync("UPDATE sects.applications SET status = $2, decided_at = $3, decided_by = $4 WHERE id = $1 AND status = 'pending'",
            ct, a.Id, a.Status, a.DecidedAt, a.DecidedBy?.Value) == 1;

    public async Task<long> TreasuryAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct) =>
        (await Pg(uow).QueryAsync("SELECT balance FROM sects.treasuries WHERE sect_id = $1", r => r.GetInt64(0), ct, sectId)).DefaultIfEmpty(0).Single();

    public async Task<Result<long>> PostTreasuryAsync(IUnitOfWork uow, Guid sectId, long delta, string reason, string referenceId, DateTimeOffset at, CancellationToken ct)
    {
        var current = (await Pg(uow).QueryAsync("SELECT balance FROM sects.treasuries WHERE sect_id = $1 FOR UPDATE", r => r.GetInt64(0), ct, sectId)).DefaultIfEmpty(0).Single();
        var after = checked(current + delta);
        if (after < 0) return Error.InsufficientFunds("insufficient_treasury", $"The sect treasury holds {current} Spirit Stones (needs {-delta}).");
        await Pg(uow).ExecuteAsync(
            "INSERT INTO sects.treasury_entries (id, sect_id, delta, balance_after, reason, reference_id, created_at) VALUES ($1, $2, $3, $4, $5, $6, $7)",
            ct, Uuid7.New(), sectId, delta, after, reason, referenceId, at);
        return after;
    }

    public Task AddChatAsync(IUnitOfWork uow, ChatMessage m, CancellationToken ct) =>
        Pg(uow).ExecuteAsync("INSERT INTO sects.chat_messages (id, sect_id, character_id, name, text, created_at) VALUES ($1, $2, $3, $4, $5, $6)",
            ct, m.Id, m.SectId, m.CharacterId.Value, m.Name, m.Text, m.At);

    public async Task<IReadOnlyList<ChatMessage>> ListChatAsync(IUnitOfWork uow, Guid sectId, Guid? after, int limit, CancellationToken ct) =>
        await Pg(uow).QueryAsync(
            "SELECT id, character_id, name, text, created_at FROM sects.chat_messages WHERE sect_id = $1 AND ($2::uuid IS NULL OR id > $2::uuid) ORDER BY id DESC LIMIT $3",
            r => new ChatMessage(r.GetGuid(0), sectId, new CharacterId(r.GetGuid(1)), r.GetString(2), r.GetString(3), r.GetFieldValue<DateTimeOffset>(4)),
            ct, sectId, after, limit);

    public async Task<int> CountChatSinceAsync(IUnitOfWork uow, CharacterId characterId, DateTimeOffset since, CancellationToken ct) =>
        (int)(await Pg(uow).QueryAsync("SELECT count(*) FROM sects.chat_messages WHERE character_id = $1 AND created_at >= $2",
            r => r.GetInt64(0), ct, characterId.Value, since)).Single();

    public Task AddEventAsync(IUnitOfWork uow, SectEvent e, CancellationToken ct) =>
        Pg(uow).ExecuteAsync("INSERT INTO sects.events (id, sect_id, kind, actor_character_id, subject_character_id, data, created_at) VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7)",
            ct, e.Id, e.SectId, e.Kind, e.Actor?.Value, e.Subject?.Value, e.DataJson, e.At);

    public async Task<IReadOnlyList<SectEvent>> ListEventsAsync(IUnitOfWork uow, Guid sectId, Guid? before, int limit, CancellationToken ct) =>
        await Pg(uow).QueryAsync(
            "SELECT id, kind, actor_character_id, subject_character_id, data::text, created_at FROM sects.events " +
            "WHERE sect_id = $1 AND ($2::uuid IS NULL OR id < $2::uuid) ORDER BY id DESC LIMIT $3",
            r => new SectEvent(r.GetGuid(0), sectId, r.GetString(1), r.IsDBNull(2) ? null : new CharacterId(r.GetGuid(2)),
                r.IsDBNull(3) ? null : new CharacterId(r.GetGuid(3)), r.GetString(4), r.GetFieldValue<DateTimeOffset>(5)),
            ct, sectId, before, limit);
}
