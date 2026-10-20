using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Application;

namespace JianghuGo.Modules.Characters.Contracts;

/// <summary>Read-only view of a character for other modules. The only Characters type they may depend on.</summary>
public sealed record CharacterSummary(CharacterId Id, AccountId AccountId, string Name, string Path, IReadOnlyDictionary<string, int> Stats);

public interface ICharacterDirectory
{
    /// <summary>The character if it exists AND belongs to <paramref name="accountId"/>; otherwise null.</summary>
    Task<CharacterSummary?> FindOwnedAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, CancellationToken ct);

    /// <summary>Any character (e.g. a PvP opponent). Character builds are public information in the game.</summary>
    Task<CharacterSummary?> FindAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct);

    /// <summary>All characters of an account (account erasure, data export).</summary>
    Task<IReadOnlyList<CharacterSummary>> ListByAccountAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CharacterSummary>>([]);

    /// <summary>Exact name lookup (case-insensitive), e.g. to add a friend (1.9).</summary>
    Task<CharacterSummary?> FindByNameAsync(IUnitOfWork uow, string name, CancellationToken ct) => Task.FromResult<CharacterSummary?>(null);
}

/// <summary>
/// How a character is shown to others next to its name: a chosen title (with rarity) and badges such as the Body
/// path's tempering "+27" or the Qi path's sword intent "⚔4". Provided by the modules that own them.
/// </summary>
public sealed record CharacterDecoration(string? Title, string? TitleRarity, IReadOnlyList<string> Badges)
{
    public static readonly CharacterDecoration None = new(null, null, []);

    public string DisplayName(string name) => Badges.Count == 0 ? name : name + " " + string.Join(" ", Badges);
}

public interface ICharacterDecorationProvider
{
    /// <summary>Title and/or badges this module contributes (nulls / empty when none).</summary>
    Task<CharacterDecoration> DecorateAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct);
}

public static class CharacterDecorations
{
    /// <summary>All providers merged: the first title found, every badge.</summary>
    public static async Task<CharacterDecoration> ForAsync(IEnumerable<ICharacterDecorationProvider> providers, IUnitOfWork uow, Guid characterId, CancellationToken ct)
    {
        string? title = null, rarity = null;
        var badges = new List<string>();
        foreach (var p in providers)
        {
            var d = await p.DecorateAsync(uow, characterId, ct);
            if (title is null && d.Title is not null)
            {
                title = d.Title;
                rarity = d.TitleRarity;
            }

            badges.AddRange(d.Badges);
        }

        return new CharacterDecoration(title, rarity, badges);
    }
}

/// <summary>A title a character has earned somewhere in the game (achievements, realms, flame codex, arena leagues...).</summary>
/// <param name="Rarity">common / rare / epic / legendary / mythic — the colour of the plate above the character.</param>
public sealed record TitleGrant(string Id, string NamePl, string Rarity, string Source);

/// <summary>Modules that grant titles list them here; the player picks one to show above the character (Quests).</summary>
public interface ITitleSource
{
    Task<IReadOnlyList<TitleGrant>> TitlesAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct);
}

/// <summary>Integration events published by the Characters module.</summary>
public static class CharacterEvents
{
    public const string CharacterCreated = "characters.character_created";

    public sealed record CharacterCreatedV1(Guid AccountId, Guid CharacterId, string Name, string Path);
}

internal sealed class CharacterDirectory(ICharacterRepository repo) : ICharacterDirectory
{
    public async Task<CharacterSummary?> FindOwnedAsync(IUnitOfWork uow, AccountId accountId, Guid characterId, CancellationToken ct)
    {
        var c = await FindAsync(uow, characterId, ct);
        return c is null || c.AccountId != accountId ? null : c;
    }

    public async Task<CharacterSummary?> FindAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct)
    {
        var c = await repo.FindCharacterAsync(uow, new CharacterId(characterId), ct);
        return c is null ? null : new CharacterSummary(c.Id, c.AccountId, c.Name, c.Path, c.Stats.ToDictionary(s => s.Key, s => s.Value));
    }

    public async Task<IReadOnlyList<CharacterSummary>> ListByAccountAsync(IUnitOfWork uow, AccountId accountId, CancellationToken ct) =>
        [.. (await repo.ListCharactersAsync(uow, accountId, ct)).Select(c => new CharacterSummary(c.Id, c.AccountId, c.Name, c.Path, c.Stats.ToDictionary(s => s.Key, s => s.Value)))];

    public async Task<CharacterSummary?> FindByNameAsync(IUnitOfWork uow, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64) return null;
        var id = await repo.FindIdByNameAsync(uow, name.Trim().Normalize(System.Text.NormalizationForm.FormC).ToUpperInvariant(), ct);
        return id is null ? null : await FindAsync(uow, id.Value.Value, ct);
    }
}

/// <summary>Account erasure (2.5): every character of the account becomes "Zapomniany …" (forgotten); names are freed.</summary>
internal sealed class AccountDeletedRenamer(ICharacterRepository repo) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Identity.Domain.IdentityEvents.AccountDeleted;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Identity.Domain.IdentityEvents.AccountDeletedV1>();
        foreach (var c in await repo.ListCharactersAsync(uow, new AccountId(e.AccountId), ct))
        {
            var suffix = c.Id.Value.ToString("N")[..8].ToUpperInvariant();
            var name = "Zapomniany " + string.Concat(suffix.Select(ch => char.IsDigit(ch) ? (char)('A' + (ch - '0')) : ch));
            if (c.Name == name) continue;
            await repo.RenameAsync(uow, c.Id, name, name.ToUpperInvariant(), ct);
        }
    }
}
