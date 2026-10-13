using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Sects.Domain;

namespace JianghuGo.Modules.Sects.Application;

/// <summary>
/// Every change to a sect's membership, roles or seat also bumps the sect's version (TryUpdateSectAsync), so concurrent
/// changes to the same sect serialise on one row and the loser retries or gets version_conflict.
/// </summary>
public interface ISectRepository
{
    Task AddSectAsync(IUnitOfWork uow, Sect sect, CancellationToken ct);
    Task<Sect?> FindSectAsync(IUnitOfWork uow, Guid id, CancellationToken ct);

    /// <summary>Any sect, disbanded or not, holding this normalised name (names are never reused).</summary>
    Task<bool> NameTakenAsync(IUnitOfWork uow, string normalizedName, CancellationToken ct);
    Task<bool> TryUpdateSectAsync(IUnitOfWork uow, Sect sect, int expectedVersion, CancellationToken ct);
    Task<IReadOnlyList<Sect>> ListSectsAsync(IUnitOfWork uow, Guid? before, int limit, CancellationToken ct);

    Task<Member?> FindMemberAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);
    Task<IReadOnlyList<Member>> ListMembersAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct);

    /// <summary>Throws <see cref="DuplicateKeyException"/> if the character already belongs to a sect.</summary>
    Task AddMemberAsync(IUnitOfWork uow, Member member, CancellationToken ct);
    Task UpdateMemberAsync(IUnitOfWork uow, Member member, CancellationToken ct);
    Task RemoveMemberAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    Task AddApplicationAsync(IUnitOfWork uow, SectApplication application, CancellationToken ct);
    Task<SectApplication?> FindApplicationAsync(IUnitOfWork uow, Guid id, CancellationToken ct);
    Task<IReadOnlyList<SectApplication>> ListPendingForSectAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct);
    Task<IReadOnlyList<SectApplication>> ListPendingForCharacterAsync(IUnitOfWork uow, CharacterId characterId, CancellationToken ct);

    /// <summary>Only a pending application can change; false if it was decided or withdrawn meanwhile.</summary>
    Task<bool> TryCloseApplicationAsync(IUnitOfWork uow, SectApplication closed, CancellationToken ct);

    Task<long> TreasuryAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct);

    /// <summary>Appends a treasury ledger line (error insufficient_treasury). Duplicate (sect, reason, reference) throws.</summary>
    Task<Result<long>> PostTreasuryAsync(IUnitOfWork uow, Guid sectId, long delta, string reason, string referenceId, DateTimeOffset at, CancellationToken ct);

    Task AddChatAsync(IUnitOfWork uow, ChatMessage message, CancellationToken ct);

    /// <summary>Newest first; only messages after <paramref name="after"/> when given.</summary>
    Task<IReadOnlyList<ChatMessage>> ListChatAsync(IUnitOfWork uow, Guid sectId, Guid? after, int limit, CancellationToken ct);
    Task<int> CountChatSinceAsync(IUnitOfWork uow, CharacterId characterId, DateTimeOffset since, CancellationToken ct);

    Task AddEventAsync(IUnitOfWork uow, SectEvent e, CancellationToken ct);
    Task<IReadOnlyList<SectEvent>> ListEventsAsync(IUnitOfWork uow, Guid sectId, Guid? before, int limit, CancellationToken ct);
}
