using System.Security.Cryptography;
using System.Text;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Cultivation.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Cultivation.Application;

/// <summary>Helpers shared by the cultivation outbox handlers: exactly-once guards and profile updates through the service.</summary>
internal static class ProfileUpdate
{
    /// <summary>A stable id for one (event, purpose) pair, so different handlers of the same event never share a guard.</summary>
    public static Guid Derived(Guid messageId, string purpose)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(messageId.ToString("N") + ":" + purpose));
        return new Guid(bytes.AsSpan(0, 16));
    }

    /// <summary>Loads (settling the flow), applies <paramref name="change"/> and saves — once per (message, purpose, character).</summary>
    public static async Task ApplyAsync(IServiceScopeFactory scopes, IUnitOfWork uow, OutboxMessage message, string purpose, Guid accountId, Guid characterId,
        Func<CultivationService.Loaded, CultivationService.Loaded> change, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<CultivationService>();
        var repo = scope.ServiceProvider.GetRequiredService<ICultivationRepository>();
        var loaded = await svc.LoadAsync(uow, new AccountId(accountId), characterId, message.OccurredAt, ct);
        if (loaded is null) return;
        if (!await repo.TryMarkHandledAsync(uow, Derived(message.Id, purpose), loaded.Character.Id, message.OccurredAt, ct)) return;
        var next = change(loaded);
        if (!await svc.SaveAsync(uow, next, message.OccurredAt, ct)) throw new TransientConflictException("Cultivation changed while applying an event.");
    }
}

/// <summary>Tribulation battles end here: a won tribulation opens the next great realm.</summary>
internal sealed class TribulationHandler(IServiceScopeFactory scopes) : IOutboxHandler
{
    public string EventType => CombatEvents.BattleFinished;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<CombatEvents.BattleFinishedV1>();
        if (e.Kind != "tribulation") return;
        foreach (var p in e.Participants)
        {
            using var scope = scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ICultivationRepository>();
            if (!await repo.TryMarkHandledAsync(uow, ProfileUpdate.Derived(message.Id, "tribulation"), new CharacterId(p.CharacterId), message.OccurredAt, ct)) continue;
            await scope.ServiceProvider.GetRequiredService<CultivationService>()
                .ApplyTribulationAsync(uow, p.AccountId, p.CharacterId, e.BattleId, p.Result == "won", message.OccurredAt, ct);
        }
    }
}

/// <summary>Won battles: counted for the core colour; the active method may pay Qi per win.</summary>
internal sealed class BattleCountersHandler(IServiceScopeFactory scopes, CultivationConfig config) : IOutboxHandler
{
    public string EventType => CombatEvents.BattleFinished;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<CombatEvents.BattleFinishedV1>();
        if (e.Kind == "tribulation") return;
        foreach (var p in e.Participants.Where(p => p.Result == "won"))
        {
            await ProfileUpdate.ApplyAsync(scopes, uow, message, "battle_counters", p.AccountId, p.CharacterId, l =>
            {
                var win = config.Method(l.Profile.MethodId)?.WinQi ?? 0;
                var qi = (long)(win * Math.Pow((double)config.Flow.RealmGrowth, l.State.RealmIndex));
                return l with
                {
                    State = l.State with { Qi = l.State.Qi + qi },
                    Profile = l.Profile with { Counters = l.Profile.Counters with { BattlesWon = l.Profile.Counters.BattlesWon + 1 }, LifetimeQi = l.Profile.LifetimeQi + qi },
                };
            }, ct);
        }
    }
}

internal sealed class CraftCountersHandler(IServiceScopeFactory scopes) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Crafting.Contracts.CraftingEvents.Crafted;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Crafting.Contracts.CraftingEvents.CraftedV1>();
        return ProfileUpdate.ApplyAsync(scopes, uow, message, "craft_counters", e.AccountId, e.CharacterId,
            l => l with { Profile = l.Profile with { Counters = l.Profile.Counters with { Crafts = l.Profile.Counters.Crafts + Math.Max(1, e.Successes) } } }, ct);
    }
}

internal sealed class GatherCountersHandler(IServiceScopeFactory scopes) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.World.Contracts.WorldEvents.ItemsGathered;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.World.Contracts.WorldEvents.ItemsGatheredV1>();
        return ProfileUpdate.ApplyAsync(scopes, uow, message, "gather_counters", e.AccountId, e.CharacterId,
            l => l with { Profile = l.Profile with { Counters = l.Profile.Counters with { Gathered = l.Profile.Counters.Gathered + e.Items.Sum(i => (long)i.Quantity) } } }, ct);
    }
}

/// <summary>Pills shake the heart: every pill used raises instability (heart demons).</summary>
internal sealed class PillInstabilityHandler(IServiceScopeFactory scopes, CultivationConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Inventory.Contracts.InventoryEvents.ConsumableUsed;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Inventory.Contracts.InventoryEvents.ConsumableUsedV1>();
        return ProfileUpdate.ApplyAsync(scopes, uow, message, "pill_instability", e.AccountId, e.CharacterId, l =>
        {
            var add = config.HeartDemons.PerPillUse * e.Quantity * (1000 + (config.Method(l.Profile.MethodId)?.DemonPermille ?? 0)) / 1000;
            return l with { Profile = l.Profile with { Instability = Math.Clamp(l.Profile.Instability + add, 0, 100) } };
        }, ct);
    }
}

/// <summary>A new character starts cultivating at once: its Qi flow (×1) runs from the moment it is created.</summary>
internal sealed class CharacterStartHandler(ICultivationRepository repo, CultivationConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Characters.Contracts.CharacterEvents.CharacterCreated;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Characters.Contracts.CharacterEvents.CharacterCreatedV1>();
        var id = new CharacterId(e.CharacterId);
        if (await repo.FindProfileAsync(uow, id, ct) is not null) return;
        if (await repo.FindAsync(uow, id, ct) is null)
        {
            var initial = CultivationState.Initial(id, new AccountId(e.AccountId), e.Path, CultivationRules.WorldDate(config, message.OccurredAt), message.OccurredAt);
            if (!await repo.TrySaveAsync(uow, initial with { Version = 1 }, 0, ct)) return; // created meanwhile by another event
        }

        await repo.TrySaveProfileAsync(uow, CultivationProfile.New(id, e.Path, message.OccurredAt) with { Version = 1 }, 0, ct);
    }
}
