using System.Security.Cryptography;
using System.Text;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Arts.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Cultivation.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Arts.Application;

/// <summary>Applies an event to one character's arts profile exactly once (guard per event, purpose and character).</summary>
internal static class ArtsUpdate
{
    public static Guid Guard(Guid messageId, string purpose, Guid characterId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(messageId.ToString("N") + ":" + purpose + ":" + characterId.ToString("N")));
        return new Guid(bytes.AsSpan(0, 16));
    }

    public static async Task ApplyAsync(IServiceScopeFactory scopes, IUnitOfWork uow, OutboxMessage message, string purpose, Guid characterId,
        Func<CharacterSummary, CultivationStatus, ArtsProfile, ArtsProfile> change, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<IArtsRepository>();
        var character = await sp.GetRequiredService<ICharacterDirectory>().FindAsync(uow, characterId, ct);
        if (character is null) return; // deleted meanwhile
        if (!await repo.TryMarkHandledAsync(uow, Guard(message.Id, purpose, characterId), character.Id, message.OccurredAt, ct)) return;
        var config = sp.GetRequiredService<ArtsConfig>();
        var stored = await repo.FindAsync(uow, character.Id, ct);
        var status = await sp.GetRequiredService<ICultivationStatus>().StatusAsync(uow, characterId, ct);
        var current = ArtsRules.Settle(config, stored ?? ArtsProfile.New(character.Id), message.OccurredAt);
        var next = change(character, status, current);
        if (ReferenceEquals(next, current) && stored is not null) return;
        if (!await repo.TrySaveAsync(uow, next with { Version = (stored?.Version ?? 0) + 1 }, stored?.Version ?? 0, ct))
            throw new TransientConflictException("Arts profile changed while applying an event.");
        await sp.GetRequiredService<ArtsService>().PublishProgressAsync(uow, character, stored ?? ArtsProfile.New(character.Id), next, message.OccurredAt, ct);
    }
}

/// <summary>
/// Battles feed the arts: flame discovery (beasts, tower floors, the world boss), the result of a subdue battle,
/// coating charges, body tempering from damage taken (×2 in a tribulation) and sword intent from beaten swordsmen.
/// </summary>
internal sealed class ArtsBattleHandler(IServiceScopeFactory scopes, ArtsConfig config) : IOutboxHandler
{
    public string EventType => CombatEvents.BattleFinished;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<CombatEvents.BattleFinishedV1>();
        foreach (var part in e.Participants)
        {
            await ArtsUpdate.ApplyAsync(scopes, uow, message, "battle", part.CharacterId, (c, status, p) =>
            {
                var at = message.OccurredAt;
                var won = part.Result == "won";
                var rnd = new DeterministicRandom(ArtsRules.Seed(message.Id, part.CharacterId, "battle"));

                // subdue battle
                if (e.Kind == "spirit" && p.Spirit is { } spirit && e.Context == "spirit:" + spirit.Reference.ToString("D"))
                {
                    var flame = config.Flame(spirit.FlameId);
                    p = won && flame is not null
                        ? (p with { Spirit = null, SubduedFlame = flame.Id }).Notify("flame_subdued", $"Duch płomienia pokonany — {flame.NamePl} czeka na pochłonięcie.", at)
                        : (p with { Spirit = null }).Notify("spirit_lost", "Duch płomienia odparł cię. Odnaleziony płomień wciąż czeka.", at);
                }

                // discovery
                foreach (var flame in config.Flames)
                {
                    if (p.FlamesSeen.Contains(flame.Id) || p.Owned(flame.Id) is not null || p.SubduedFlame == flame.Id || p.Absorbing?.FlameId == flame.Id) continue;
                    var hit = flame.Discover.Any(s => s.Kind switch
                    {
                        FlameSourceKinds.Npc => won && e.Kind == "pve" && e.NpcId == s.Ref && rnd.NextInt(0, 1000) < s.ChancePermille,
                        FlameSourceKinds.Tower => won && e.Kind == "tower" && e.Context == "tower:" + s.Ref,
                        FlameSourceKinds.Boss => e.Kind == "boss" && rnd.NextInt(0, 1000) < 100,
                        _ => false,
                    });
                    if (hit) p = ArtsRules.Discover(p, flame, at);
                }

                // coating wears off
                if (p.CoatingCharges > 0)
                    p = p.CoatingCharges == 1
                        ? (p with { CoatingCharges = 0, Coating = null }).Notify("coating_gone", "Powłoka trucizny starła się z ostrza.", at)
                        : p with { CoatingCharges = p.CoatingCharges - 1 };

                // tempering from blows taken
                // 1.6: blows are measured against the fighter's health (as if they had TemperReferenceHp), so tempering does
                // not speed up just because numbers grow with the realm
                var taken = part.MaxHp > 0 ? (double)part.DamageTaken * ArtsRules.TemperReferenceHp / Math.Max(ArtsRules.TemperReferenceHp, part.MaxHp) : part.DamageTaken;
                var xp = (long)Math.Round(taken * config.Tempering.XpPerDamage * (c.Path == ArtsRules.BodyPath ? 1.5 : 1.0));
                if (e.Kind == "tribulation") xp *= config.Tempering.TribulationMultiplier;
                p = ArtsRules.AddTemperXp(config, p, xp);

                // sword intent from swordsmen
                if (won && e.NpcId is { } npc && config.SwordIntent.Swordsmen.Contains(npc))
                    p = p with { SwordXp = p.SwordXp + (config.SwordIntent.XpPerSwordsman * (c.Path == ArtsRules.QiPath ? 2 : 1)) };
                return p;
            }, ct);
        }
    }
}

/// <summary>Walking tempers the body: accepted steps become tempering XP.</summary>
internal sealed class ArtsStepsHandler(IServiceScopeFactory scopes, ArtsConfig config) : IOutboxHandler
{
    public string EventType => CultivationStepEvents.StepsAccepted;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<CultivationStepEvents.StepsAcceptedV1>();
        return e.Steps < config.Tempering.StepsPerXp
            ? Task.CompletedTask
            : ArtsUpdate.ApplyAsync(scopes, uow, message, "steps", e.CharacterId,
                (c, _, p) => ArtsRules.AddTemperXp(config, p, (long)e.Steps / config.Tempering.StepsPerXp * (c.Path == ArtsRules.BodyPath ? 2 : 1)), ct);
    }
}

/// <summary>The two highest flames reveal themselves to those who reach their realm.</summary>
internal sealed class ArtsRealmHandler(IServiceScopeFactory scopes, ArtsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Cultivation.Application.CultivationEvents.RealmReached;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Cultivation.Application.CultivationEvents.RealmReachedV1>();
        return Discover(uow, message, e.CharacterId, e.Realm, ct);
    }

    // takes plain values: the event type belongs to Cultivation's application layer and must not be captured
    private Task Discover(IUnitOfWork uow, OutboxMessage message, Guid characterId, int realm, CancellationToken ct)
    {
        var flames = config.Flames.Where(f => f.Discover.Any(s => s.Kind == FlameSourceKinds.Realm && int.TryParse(s.Ref, out var r) && realm >= r)).ToList();
        if (flames.Count == 0) return Task.CompletedTask;
        return ArtsUpdate.ApplyAsync(scopes, uow, message, "realm", characterId, (_, _, p) =>
        {
            foreach (var f in flames)
                if (p.Owned(f.Id) is null && p.SubduedFlame != f.Id && p.Absorbing?.FlameId != f.Id) p = ArtsRules.Discover(p, f, message.OccurredAt);
            return p;
        }, ct);
    }
}
