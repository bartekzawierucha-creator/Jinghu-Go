using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Combat.Domain;

namespace JianghuGo.Modules.Combat.Application;

/// <summary>A foe as this cultivator would meet it now: its power at the cultivator's realm and what a win pays.</summary>
public sealed record FoeScoutView(string NpcId, long Power, long RewardQi, long RewardStones);

/// <summary>The cultivator's own combat power and every training/hunt foe scaled to their realm (2.2).</summary>
public sealed record CombatScoutView(int Realm, long MyPower, IReadOnlyList<FoeScoutView> Foes);

public sealed partial class CombatService
{
    /// <summary>
    /// One number for "how strong": stats, HP, Qi and the techniques' levels. Shown on the hero and on every foe card,
    /// so the scaling of both sides with the realm is visible. Display only: battles use the full rules.
    /// </summary>
    public static long CombatPower(CombatConfig c, Combatant x)
    {
        var hp = (long)(x.HpOverride ?? c.Vitals.BaseHp + (x.Stat("constitution") * c.Vitals.HpPerConstitution)) * (1000 + x.HpBonusPermille) / 1000;
        var qi = (long)c.Vitals.BaseQi + (x.Stat("spirit") * c.Vitals.QiPerSpirit);
        var stats = x.Stats.Values.Sum(v => (long)Math.Max(0, v));
        var techniques = x.Slots.Where(s => s is not null).Sum(s => (long)s!.Level);
        return (stats * 10) + hp + (qi / 2) + (techniques * 15);
    }

    public Task<Result<CombatScoutView>> ScoutAsync(AccountId accountId, Guid characterId, CancellationToken ct) =>
        uowFactory.RunAsync<Result<CombatScoutView>>(async uow =>
        {
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return CharacterNotFound;
            var realm = await RealmOfAsync(uow, c.Id, ct);
            long stageQi = 0;
            foreach (var r in realms) { stageQi = await r.StageQiAsync(uow, c.Id.Value, ct); break; }
            var me = CombatPower(config, await SnapshotAsync(uow, c, ct));
            var foes = new List<FoeScoutView>();
            foreach (var n in config.Npcs)
            {
                var scaled = ForFighter(n, realm);
                long qi = n.RewardQi, stones = 0;
                if (config.Scaling is { } sc)
                {
                    qi = sc.TierQi(n, stageQi) ?? sc.Qi(n.RewardQi, sc.RealmsAbove(n, realm));
                    stones = sc.TierStones(n, realm) ?? 0;
                }

                foes.Add(new FoeScoutView(n.Id, CombatPower(config, NpcCombatant(scaled)), qi, stones));
            }

            return new CombatScoutView(realm, me, foes);
        }, ct);
}
