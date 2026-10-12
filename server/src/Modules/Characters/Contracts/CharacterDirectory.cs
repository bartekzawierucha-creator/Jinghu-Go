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
        var c = await repo.FindCharacterAsync(uow, new CharacterId(characterId), ct);
        return c is null || c.AccountId != accountId
            ? null
            : new CharacterSummary(c.Id, c.AccountId, c.Name, c.Path, c.Stats.ToDictionary(s => s.Key, s => s.Value));
    }
}
