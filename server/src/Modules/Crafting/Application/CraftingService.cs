using System.Globalization;
using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Crafting.Domain;
using JianghuGo.Modules.Inventory.Contracts;

namespace JianghuGo.Modules.Crafting.Application;

/// <summary>Append-only record of every craft/upgrade request: the seed makes each roll replayable.</summary>
public sealed record CraftLogEntry(
    Guid Id,
    CharacterId CharacterId,
    AccountId AccountId,
    string Kind,
    string Subject,
    int Attempts,
    int Successes,
    int ChancePermille,
    ulong Seed,
    long StonesSpent,
    int? FromLevel,
    int? ToLevel,
    string RulesVersion,
    DateTimeOffset At);

public interface ICraftingRepository
{
    Task AddAsync(IUnitOfWork uow, CraftLogEntry entry, CancellationToken ct);
    Task<IReadOnlyList<CraftLogEntry>> ListAsync(IUnitOfWork uow, CharacterId characterId, Guid? before, int limit, CancellationToken ct);
}

public sealed record CraftCommand(string? RecipeId, int Times);

public sealed record CraftResult(Guid LogId, string RecipeId, int Attempts, int Successes, int ChancePermille, string OutputItem, int OutputQuantity,
    IReadOnlyList<Guid> CreatedItems, long StonesSpent, long Balance);

public sealed record UpgradeCommand(Guid InstanceId, int ExpectedVersion);

public sealed record UpgradeResult(Guid LogId, Guid InstanceId, bool Success, int ChancePermille, int FromLevel, int Level, int Version, long StonesSpent, long Balance);

public sealed class CraftingService(
    IUnitOfWorkFactory uowFactory,
    ICraftingRepository repo,
    ICharacterDirectory characters,
    IInventory inventory,
    IItemCatalog catalog,
    IIdempotencyStore idempotency,
    IAuditLog audit,
    ICorrelationContext correlation,
    IRandomSeedSource seeds,
    CraftingConfig config,
    TimeProvider clock)
{
    private static readonly Error CharacterNotFound = Error.NotFound("character_not_found", "Character not found.");

    /// <summary>
    /// One atomic transaction: take inputs, charge stones, roll, give outputs, log. Any failure (missing materials,
    /// stones, bag space) rolls back everything — nothing is consumed (acceptance #5).
    /// </summary>
    public Task<Result<CraftResult>> CraftAsync(AccountId accountId, Guid characterId, string? key, CraftCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<CraftResult>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "crafting.craft", key, new { characterId, cmd }, async Task<Result<CraftResult>> () =>
            {
                if (config.Find(cmd.RecipeId) is not { } recipe) return Error.Validation("recipe_unknown", "Unknown recipe.");
                var unique = catalog.Find(recipe.Output.Item)?.Stackable == false;
                if (cmd.Times < 1 || cmd.Times > (unique ? 1 : CraftingRules.MaxTimesPerRequest))
                    return Error.Validation("times_invalid", unique ? "Unique items are crafted one at a time." : $"Times must be 1-{CraftingRules.MaxTimesPerRequest}.");
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;

                var now = clock.GetUtcNow();
                var logId = Uuid7.New();
                var reference = logId.ToString();
                foreach (var input in recipe.Inputs)
                {
                    var removed = await inventory.RemoveItemsAsync(uow, c.Id, input.Item, (long)input.Quantity * cmd.Times, "craft", reference, ct);
                    if (!removed.IsSuccess) return removed.Error!;
                }

                var stones = checked(recipe.Stones * cmd.Times);
                long balance = await inventory.BalanceAsync(uow, c.Id, ct);
                if (stones > 0)
                {
                    var debited = await inventory.DebitAsync(uow, c.Id, stones, "craft", reference, ct);
                    if (!debited.IsSuccess) return debited.Error!;
                    balance = debited.Value;
                }

                var chance = CraftingRules.ChancePermille(config, recipe, c.Stats.GetValueOrDefault("insight"));
                var seed = seeds.NextSeed();
                var successes = CraftingRules.Rolls(seed, cmd.Times).Count(r => r < chance);
                var outputQty = successes * recipe.Output.Quantity;
                var created = new List<Guid>();
                if (outputQty > 0)
                {
                    if (unique)
                    {
                        var item = await inventory.CreateInstanceAsync(uow, c.Id, accountId, recipe.Output.Item, "craft", reference, ct);
                        if (!item.IsSuccess) return item.Error!;
                        created.Add(item.Value.Id);
                    }
                    else
                    {
                        var added = await inventory.AddItemsAsync(uow, c.Id, recipe.Output.Item, outputQty, "craft_output", reference, ct);
                        if (!added.IsSuccess) return added.Error!;
                    }
                }

                await repo.AddAsync(uow, new CraftLogEntry(logId, c.Id, accountId, "craft", recipe.Id, cmd.Times, successes, chance, seed, stones,
                    null, null, config.RulesVersion, now), ct);
                await AuditAsync(uow, accountId, "crafting.crafted", c.Id,
                    new { logId, recipe = recipe.Id, cmd.Times, successes, chance, seed = seed.ToString(CultureInfo.InvariantCulture), stones }, now, ct);
                return new CraftResult(logId, recipe.Id, cmd.Times, successes, chance, recipe.Output.Item, outputQty, created, stones, balance);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>+1 per attempt. Failure keeps the current level; the material and stones are spent either way.</summary>
    public Task<Result<UpgradeResult>> UpgradeAsync(AccountId accountId, Guid characterId, string? key, UpgradeCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<UpgradeResult>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "crafting.upgrade", key, new { characterId, cmd }, async Task<Result<UpgradeResult>> () =>
            {
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                var item = await inventory.FindInstanceAsync(uow, cmd.InstanceId, ct);
                if (item is null || item.CharacterId != c.Id.Value) return Error.NotFound("item_not_found", "Item not found.");
                if (item.Version != cmd.ExpectedVersion) return Error.Conflict("version_conflict", "The item changed concurrently; reload and retry.");
                if (item.State == "listed") return Error.Conflict("item_listed", "The item is listed on the market.");
                if (item.UpgradeLevel >= config.Upgrade.MaxLevel) return Error.LimitReached("max_upgrade_level", $"The item is already +{config.Upgrade.MaxLevel}.");

                var now = clock.GetUtcNow();
                var logId = Uuid7.New();
                var reference = logId.ToString();
                var target = item.UpgradeLevel + 1;
                var removed = await inventory.RemoveItemsAsync(uow, c.Id, config.Upgrade.Material, 1, "upgrade", reference, ct);
                if (!removed.IsSuccess) return removed.Error!;
                var stones = CraftingRules.UpgradeStones(config, target);
                var debited = await inventory.DebitAsync(uow, c.Id, stones, "upgrade", reference, ct);
                if (!debited.IsSuccess) return debited.Error!;

                var chance = CraftingRules.UpgradeChancePermille(config, target);
                var seed = seeds.NextSeed();
                var success = CraftingRules.Rolls(seed, 1)[0] < chance;
                var level = item.UpgradeLevel;
                var version = item.Version;
                if (success)
                {
                    var updated = await inventory.SetUpgradeLevelAsync(uow, item.Id, item.Version, target, "upgrade:" + reference, ct);
                    if (!updated.IsSuccess) return updated.Error!;
                    (level, version) = (updated.Value.UpgradeLevel, updated.Value.Version);
                }

                await repo.AddAsync(uow, new CraftLogEntry(logId, c.Id, accountId, "upgrade", item.Id.ToString(), 1, success ? 1 : 0, chance, seed, stones,
                    item.UpgradeLevel, level, config.RulesVersion, now), ct);
                await AuditAsync(uow, accountId, "crafting.upgraded", c.Id,
                    new { logId, instanceId = item.Id, item.ItemId, from = item.UpgradeLevel, to = level, success, chance, seed = seed.ToString(CultureInfo.InvariantCulture), stones }, now, ct);
                return new UpgradeResult(logId, item.Id, success, chance, item.UpgradeLevel, level, version, stones, debited.Value);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    public Task<Result<IReadOnlyList<CraftLogEntry>>> LogAsync(AccountId accountId, Guid characterId, Guid? before, int? limit, CancellationToken ct) =>
        uowFactory.RunAsync<Result<IReadOnlyList<CraftLogEntry>>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            return Result<IReadOnlyList<CraftLogEntry>>.Success(await repo.ListAsync(uow, c.Id, before, Math.Clamp(limit ?? 50, 1, 200), ct));
        }, ct);

    private Task AuditAsync(IUnitOfWork uow, AccountId accountId, string action, CharacterId characterId, object data, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "character", characterId.ToString(),
            JsonSerializer.Serialize(data, Idempotency.Json), correlation.CorrelationId, now), ct);
}
