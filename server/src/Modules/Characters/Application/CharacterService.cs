using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Domain;

namespace JianghuGo.Modules.Characters.Application;

/// <param name="Modifiers">What the trait adds to (or takes from) the stats, so players can see it.</param>
public sealed record TraitView(string Id, string Category, string Rarity, string NamePl, string NameEn, IReadOnlyDictionary<string, int> Modifiers);

public sealed record StatView(string Id, int Value);

public sealed record DraftView(
    Guid Id,
    int Version,
    string RulesVersion,
    IReadOnlyList<TraitView> Traits,
    IReadOnlyList<StatView> Stats,
    int RerollCount,
    int RerollsRemaining,
    int DestinyTokens,
    int RerollBaseCost,
    int RerollCostPerLockedTrait,
    int MaxLockedTraits);

public sealed record CharacterView(
    Guid Id,
    string Name,
    string Path,
    string RulesVersion,
    IReadOnlyList<TraitView> Traits,
    IReadOnlyList<StatView> Stats,
    DateTimeOffset CreatedAt);

public sealed record RerollCommand(int ExpectedVersion, IReadOnlyList<string>? LockedCategories);

public sealed record FinalizeCommand(int ExpectedVersion, string? Name, string? Path);

public sealed record DraftOutcome(DraftView Draft, bool Created);

public sealed class CharacterService(
    IUnitOfWorkFactory uowFactory,
    ICharacterRepository repo,
    IDestinyTokenLedger ledger,
    IIdempotencyStore idempotency,
    IAuditLog audit,
    IOutbox outbox,
    ICorrelationContext correlation,
    IRandomSeedSource seeds,
    CharacterCreationConfig config,
    TimeProvider clock)
{
    public const string RerollOperation = "characters.draft.reroll";
    public const string FinalizeOperation = "characters.draft.finalize";

    private static readonly Error DraftNotFound = Error.NotFound("draft_not_found", "Character draft not found.");
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");
    private static readonly Error VersionConflict = Error.Conflict("version_conflict", "The draft was modified; reload it and retry with the current version.");

    /// <summary>Returns the active draft, creating it (free initial roll) if none exists. Safe to repeat.</summary>
    public async Task<Result<DraftOutcome>> GetOrCreateDraftAsync(AccountId accountId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return await uowFactory.RunAsync<Result<DraftOutcome>>(async uow =>
        {
            var existing = await repo.FindActiveDraftAsync(uow, accountId, ct);
            if (existing is not null)
            {
                return new DraftOutcome(await ViewAsync(uow, existing, ct), false);
            }

            if ((await repo.ListCharactersAsync(uow, accountId, ct)).Count >= config.MaxCharactersPerAccount)
            {
                return Error.LimitReached("character_limit_reached", $"An account may own at most {config.MaxCharactersPerAccount} characters.");
            }

            if (await ledger.GetBalanceAsync(uow, accountId, ct) is null)
            {
                var grant = await ledger.PostAsync(uow, accountId, config.InitialDestinyTokens, DestinyTokenReasons.InitialGrant, accountId.ToString(), now, ct);
                if (!grant.IsSuccess)
                {
                    return grant.Error!;
                }
            }

            var seed = seeds.NextSeed();
            var roll = CharacterRoller.Roll(config, seed, null, new HashSet<string>());
            var draft = new CharacterDraft(DraftId.New(), accountId, 1, DraftStatus.Active, config.RulesVersion, seed, roll.Traits, roll.Stats, 0, now, now);
            await repo.AddDraftAsync(uow, draft, ct);
            await AuditAsync(uow, accountId, "character.draft_created", "character_draft", draft.Id.ToString(),
                new { seed = draft.LastSeed.ToString(System.Globalization.CultureInfo.InvariantCulture), draft.RulesVersion, traits = draft.Traits }, now, ct);
            var view = await ViewAsync(uow, draft, ct);
            await uow.CommitAsync(ct);
            return new DraftOutcome(view, true);
        }, ct);
    }

    public async Task<Result<DraftView>> RerollAsync(AccountId accountId, Guid draftId, string? idempotencyKey, RerollCommand command, CancellationToken ct)
    {
        return await uowFactory.RunAsync<Result<DraftView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, RerollOperation, idempotencyKey, new { draftId, command },
                () => RerollCoreAsync(uow, accountId, new DraftId(draftId), command, ct), ct);
            if (result.IsSuccess)
            {
                await uow.CommitAsync(ct);
            }

            return result;
        }, ct);
    }

    private async Task<Result<DraftView>> RerollCoreAsync(IUnitOfWork uow, AccountId accountId, DraftId draftId, RerollCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var draft = await repo.FindDraftAsync(uow, draftId, ct);
        if (draft is null || draft.AccountId != accountId)
        {
            return DraftNotFound; // never reveal that another account's draft exists
        }

        if (draft.Status != DraftStatus.Active)
        {
            return Error.Conflict("draft_finalized", "This draft has already been finalized.");
        }

        if (draft.Version != command.ExpectedVersion)
        {
            return VersionConflict;
        }

        if (draft.RulesVersion != config.RulesVersion)
        {
            return Error.Conflict("rules_version_changed", "Character creation rules changed; start a new draft.");
        }

        if (draft.RerollCount >= config.MaxRerollsPerDraft)
        {
            return Error.LimitReached("reroll_limit_reached", "Maximum number of rerolls for this draft reached.");
        }

        var locked = command.LockedCategories ?? [];
        if (locked.Any(c => !TraitCategories.All.Contains(c)) || locked.Distinct().Count() != locked.Count)
        {
            return Error.Validation("locked_categories_invalid", $"Locked categories must be distinct values of: {string.Join(", ", TraitCategories.All)}.");
        }

        if (locked.Count > config.Reroll.MaxLockedTraits)
        {
            return Error.Validation("too_many_locked", $"At most {config.Reroll.MaxLockedTraits} traits can be locked.");
        }

        var cost = config.RerollCost(locked.Count);
        var debit = await ledger.PostAsync(uow, accountId, -cost, DestinyTokenReasons.Reroll, $"{draft.Id}:{draft.Version}", now, ct);
        if (!debit.IsSuccess)
        {
            return debit.Error!;
        }

        var seed = seeds.NextSeed();
        var roll = CharacterRoller.Roll(config, seed, draft.Traits, locked.ToHashSet());
        var updated = draft with
        {
            Version = draft.Version + 1,
            LastSeed = seed,
            Traits = roll.Traits,
            Stats = roll.Stats,
            RerollCount = draft.RerollCount + 1,
            UpdatedAt = now,
        };
        if (!await repo.TryUpdateDraftAsync(uow, updated, command.ExpectedVersion, ct))
        {
            return VersionConflict;
        }

        await AuditAsync(uow, accountId, "character.draft_rerolled", "character_draft", draft.Id.ToString(),
            new { cost, locked, seed = seed.ToString(System.Globalization.CultureInfo.InvariantCulture), fromVersion = draft.Version, traits = roll.Traits }, now, ct);
        return await ViewAsync(uow, updated, ct);
    }

    public async Task<Result<CharacterView>> FinalizeAsync(AccountId accountId, Guid draftId, string? idempotencyKey, FinalizeCommand command, CancellationToken ct)
    {
        return await uowFactory.RunAsync<Result<CharacterView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, FinalizeOperation, idempotencyKey, new { draftId, command },
                () => FinalizeCoreAsync(uow, accountId, new DraftId(draftId), command, ct), ct);
            if (result.IsSuccess)
            {
                await uow.CommitAsync(ct);
            }

            return result;
        }, ct);
    }

    private async Task<Result<CharacterView>> FinalizeCoreAsync(IUnitOfWork uow, AccountId accountId, DraftId draftId, FinalizeCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var draft = await repo.FindDraftAsync(uow, draftId, ct);
        if (draft is null || draft.AccountId != accountId)
        {
            return DraftNotFound;
        }

        if (draft.Status != DraftStatus.Active)
        {
            return Error.Conflict("draft_finalized", "This draft has already been finalized.");
        }

        if (draft.Version != command.ExpectedVersion)
        {
            return VersionConflict;
        }

        if (command.Path is null || !config.Paths.Contains(command.Path))
        {
            return Error.Validation("path_invalid", $"Path must be one of: {string.Join(", ", config.Paths)}.");
        }

        var name = CharacterNames.Validate(command.Name, config.Name);
        if (!name.IsSuccess)
        {
            return name.Error!;
        }

        if ((await repo.ListCharactersAsync(uow, accountId, ct)).Count >= config.MaxCharactersPerAccount)
        {
            return Error.LimitReached("character_limit_reached", $"An account may own at most {config.MaxCharactersPerAccount} characters.");
        }

        if (await repo.NameExistsAsync(uow, name.Value.Normalized, ct))
        {
            return Error.Conflict("name_taken", "This name is already taken.");
        }

        var character = new Character(CharacterId.New(), accountId, name.Value.Name, name.Value.Normalized, command.Path,
            draft.RulesVersion, draft.Traits, draft.Stats, draft.Id, now);
        if (!await repo.TryUpdateDraftAsync(uow, draft with { Status = DraftStatus.Finalized, Version = draft.Version + 1, UpdatedAt = now }, command.ExpectedVersion, ct))
        {
            return VersionConflict;
        }

        try
        {
            await repo.AddCharacterAsync(uow, character, ct);
        }
        catch (DuplicateKeyException)
        {
            return Error.Conflict("name_taken", "This name is already taken.");
        }

        await AuditAsync(uow, accountId, "character.created", "character", character.Id.ToString(),
            new { draftId = draft.Id.Value, character.Name, character.Path }, now, ct);
        await outbox.EnqueueAsync(uow, OutboxMessage.Create(Contracts.CharacterEvents.CharacterCreated,
            new Contracts.CharacterEvents.CharacterCreatedV1(accountId.Value, character.Id.Value, character.Name, character.Path), now, correlation.CorrelationId), ct);
        return View(character);
    }

    public async Task<Result<DraftView>> GetActiveDraftAsync(AccountId accountId, CancellationToken ct)
    {
        return await uowFactory.RunAsync<Result<DraftView>>(async uow =>
        {
            var draft = await repo.FindActiveDraftAsync(uow, accountId, ct);
            return draft is null ? DraftNotFound : await ViewAsync(uow, draft, ct);
        }, ct);
    }

    public async Task<IReadOnlyList<CharacterView>> ListAsync(AccountId accountId, CancellationToken ct)
    {
        return await uowFactory.RunAsync<IReadOnlyList<CharacterView>>(async uow =>
        {
            return [.. (await repo.ListCharactersAsync(uow, accountId, ct)).Select(View)];
        }, ct);
    }

    public async Task<Result<CharacterView>> GetAsync(AccountId accountId, Guid characterId, CancellationToken ct)
    {
        return await uowFactory.RunAsync<Result<CharacterView>>(async uow =>
        {
            var character = await repo.FindCharacterAsync(uow, new CharacterId(characterId), ct);
            return character is null || character.AccountId != accountId ? CharacterNotFound : View(character);
        }, ct);
    }

    private async Task<DraftView> ViewAsync(IUnitOfWork uow, CharacterDraft d, CancellationToken ct) =>
        new(d.Id.Value, d.Version, d.RulesVersion, TraitViews(d.Traits), StatViews(d.Stats), d.RerollCount,
            Math.Max(0, config.MaxRerollsPerDraft - d.RerollCount),
            await ledger.GetBalanceAsync(uow, d.AccountId, ct) ?? 0,
            config.Reroll.BaseCost, config.Reroll.CostPerLockedTrait, config.Reroll.MaxLockedTraits);

    private CharacterView View(Character c) =>
        new(c.Id.Value, c.Name, c.Path, c.RulesVersion, TraitViews(c.Traits), StatViews(c.Stats), c.CreatedAt);

    private List<TraitView> TraitViews(RolledTraits traits) =>
        [.. traits.All.Select(config.Trait).Select(t => new TraitView(t.Id, t.Category, t.Rarity, t.NamePl, t.NameEn,
            // keys in PostgreSQL jsonb order (length, then bytes) so a replayed idempotent response is byte-identical
            (t.Modifiers ?? new Dictionary<string, int>()).OrderBy(kv => System.Text.Encoding.UTF8.GetByteCount(kv.Key)).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(kv => kv.Key, kv => kv.Value)))];

    private static List<StatView> StatViews(IReadOnlyList<KeyValuePair<string, int>> stats) =>
        [.. stats.Select(s => new StatView(s.Key, s.Value))];

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, string entityType, string entityId, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, entityType, entityId,
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}
