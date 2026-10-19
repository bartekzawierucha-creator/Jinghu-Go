using System.Globalization;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Crafting.Domain;

namespace JianghuGo.Modules.Crafting.Application;

/// <summary>A queued batch: materials and stones are taken when queued; the rolls (seed fixed then) are revealed when collected.</summary>
public sealed record QueueEntry(Guid Id, CharacterId CharacterId, AccountId AccountId, string RecipeId, string Kind, int Times, int ChancePermille, ulong Seed, long Stones,
    DateTimeOffset StartedAt, DateTimeOffset ReadyAt);

public sealed record QueueCommand(string? RecipeId, int Times);

public sealed record QueueEntryView(Guid Id, string RecipeId, string Kind, string OutputItem, string OutputNamePl, int Times, int ChancePermille, DateTimeOffset StartedAt,
    DateTimeOffset ReadyAt, bool Ready, int MinutesPerCraft);

public sealed record CollectedView(string RecipeId, string OutputItem, int Times, int Successes, int Quantity);

public sealed record QueueView(IReadOnlyList<QueueEntryView> Entries, IReadOnlyList<CollectedView> Collected, int MaxTimes, int AlchemyMinutesPerCraft, int ForgeMinutesPerCraft);

public sealed partial class CraftingService
{
    /// <summary>Queues a batch in the cauldron (alchemy) or the forge — one batch per profession at a time, up to <c>maxTimes</c> crafts.</summary>
    public Task<Result<QueueView>> QueueAsync(AccountId accountId, Guid characterId, string? key, QueueCommand cmd, CancellationToken ct) =>
        uowFactory.RunAsync<Result<QueueView>>(async uow =>
        {
            var result = await Idempotency.ExecuteAsync(idempotency, uow, clock, accountId, "crafting.queue", key, new { characterId, cmd }, async Task<Result<QueueView>> () =>
            {
                if (config.Find(cmd.RecipeId) is not { } recipe) return Error.Validation("recipe_unknown", "Unknown recipe.");
                if (catalog.Find(recipe.Output.Item)?.Stackable == false) return Error.Validation("queue_unique", "Unique items cannot be queued.");
                var rules = config.QueueRules;
                if (cmd.Times < 1 || cmd.Times > rules.MaxTimes) return Error.Validation("times_invalid", $"Times must be 1-{rules.MaxTimes}.");
                var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
                if (c is null) return CharacterNotFound;
                var queue = await repo.ListQueueAsync(uow, c.Id, ct);
                if (queue.Any(e => e.Kind == recipe.Kind)) return Error.Conflict("queue_busy", "This workshop is already working on a batch.");
                var xpNow = await XpAsync(uow, c.Id, ct);
                var level = config.ProfessionRules.Level(Professions.Of(recipe) == Professions.Alchemy ? xpNow.Alchemy : xpNow.Smithing);
                if (level < recipe.MinLevel)
                    return Error.Conflict("profession_level_too_low", $"This recipe needs {Professions.Of(recipe)} level {recipe.MinLevel} (you have {level}).");

                var now = clock.GetUtcNow();
                var id = Uuid7.New();
                var reference = id.ToString();
                foreach (var input in recipe.Inputs)
                {
                    var removed = await inventory.RemoveItemsAsync(uow, c.Id, input.Item, (long)input.Quantity * cmd.Times, "craft_queue", reference, ct);
                    if (!removed.IsSuccess) return removed.Error!;
                }

                var stones = checked(config.ProfessionRules.Stones(recipe.Stones, level) * cmd.Times);
                if (stones > 0)
                {
                    var debited = await inventory.DebitAsync(uow, c.Id, stones, "craft_queue", reference, ct);
                    if (!debited.IsSuccess) return debited.Error!;
                }

                var extra = await ExtraAsync(uow, c.Id.Value, ct);
                var chance = Math.Min(1000, CraftingRules.ChancePermille(config, recipe, c.Stats.GetValueOrDefault("insight")) + config.ProfessionRules.SuccessBonus(level)
                    + (recipe.Kind == RecipeKinds.Alchemy ? extra.Alchemy : extra.Forge));
                var entry = new QueueEntry(id, c.Id, accountId, recipe.Id, recipe.Kind, cmd.Times, chance, seeds.NextSeed(), stones, now,
                    now.AddMinutes((double)rules.MinutesFor(recipe) * cmd.Times));
                await repo.AddQueueAsync(uow, entry, ct);
                await AuditAsync(uow, accountId, "crafting.queued", c.Id, new { id, recipe = recipe.Id, cmd.Times, chance, stones, entry.ReadyAt }, now, ct);
                return await QueueViewAsync(uow, c.Id, [], now, ct);
            }, ct);
            if (result.IsSuccess) await uow.CommitAsync(ct);
            return result;
        }, ct);

    public Task<Result<QueueView>> GetQueueAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<QueueView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            return await QueueViewAsync(uow, c.Id, [], clock.GetUtcNow(), ct);
        }, ct);

    /// <summary>Collects every finished batch: rolls revealed, outputs into the bag, logged like a normal craft.</summary>
    public Task<Result<QueueView>> CollectQueueAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<QueueView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var now = clock.GetUtcNow();
            var collected = new List<CollectedView>();
            foreach (var e in (await repo.ListQueueAsync(uow, c.Id, ct)).Where(e => e.ReadyAt <= now))
            {
                if (config.Find(e.RecipeId) is not { } recipe || !await repo.RemoveQueueAsync(uow, e.Id, ct)) continue;
                var successes = CraftingRules.Rolls(e.Seed, e.Times).Count(r => r < e.ChancePermille);
                var qty = successes * recipe.Output.Quantity;
                if (qty > 0)
                {
                    var added = await inventory.AddItemsAsync(uow, c.Id, recipe.Output.Item, qty, "craft_output", e.Id.ToString(), ct);
                    if (!added.IsSuccess) return added.Error!;
                }

                await repo.AddAsync(uow, new CraftLogEntry(e.Id, c.Id, accountId, "craft", recipe.Id, e.Times, successes, e.ChancePermille, e.Seed, e.Stones,
                    null, null, config.RulesVersion, now), ct);
                await AuditAsync(uow, accountId, "crafting.queue_collected", c.Id,
                    new { e.Id, recipe = recipe.Id, e.Times, successes, seed = e.Seed.ToString(CultureInfo.InvariantCulture) }, now, ct);
                await outbox.EnqueueAsync(uow, OutboxMessage.Create(JianghuGo.Modules.Crafting.Contracts.CraftingEvents.Crafted,
                    new JianghuGo.Modules.Crafting.Contracts.CraftingEvents.CraftedV1(accountId.Value, c.Id.Value, "craft", recipe.Id, e.Times, successes), now, correlation.CorrelationId), ct);
                collected.Add(new CollectedView(recipe.Id, recipe.Output.Item, e.Times, successes, qty));
            }

            if (collected.Count == 0) return Error.Conflict("nothing_ready", "No batch is ready yet.");
            var view = await QueueViewAsync(uow, c.Id, collected, now, ct);
            await uow.CommitAsync(ct);
            return view;
        }, ct);

    private async Task<QueueView> QueueViewAsync(IUnitOfWork uow, CharacterId id, IReadOnlyList<CollectedView> collected, DateTimeOffset now, CancellationToken ct)
    {
        var rules = config.QueueRules;
        var entries = (await repo.ListQueueAsync(uow, id, ct)).Select(e =>
        {
            var recipe = config.Find(e.RecipeId);
            var output = recipe?.Output.Item ?? e.RecipeId;
            return new QueueEntryView(e.Id, e.RecipeId, e.Kind, output, catalog.Find(output)?.NamePl ?? output, e.Times, e.ChancePermille, e.StartedAt, e.ReadyAt,
                e.ReadyAt <= now, recipe is null ? 0 : rules.MinutesFor(recipe));
        }).ToList();
        return new QueueView(entries, collected, rules.MaxTimes, rules.AlchemyMinutesPerCraft, rules.ForgeMinutesPerCraft);
    }
}
