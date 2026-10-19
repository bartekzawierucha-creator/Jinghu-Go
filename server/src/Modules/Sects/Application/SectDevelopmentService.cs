using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Domain;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Combat.Contracts;
using JianghuGo.Modules.Cultivation.Contracts;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.Modules.Sects.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Sects.Application;

public interface ISectDevelopmentRepository
{
    Task<SectDevelopment?> FindAsync(IUnitOfWork uow, Guid sectId, CancellationToken ct);
    Task<bool> TrySaveAsync(IUnitOfWork uow, SectDevelopment development, int expectedVersion, CancellationToken ct);

    Task<bool> TryMarkHandledAsync(IUnitOfWork uow, Guid guardId, DateTimeOffset at, CancellationToken ct);
}

public sealed record BuildingView(string Id, string NamePl, string DescriptionPl, int Level, int MaxLevel, long? NextCost, PerkBonus? PerLevel, int AlchemyPermillePerLevel,
    int ForgePermillePerLevel, int MissionRewardPermillePerLevel);

public sealed record MissionView(string Id, string NamePl, string DescriptionPl, string Metric, long Target, long Progress, long TreasuryReward, long ContributionReward,
    bool Completed, bool Claimed);

public sealed record SectBossView(string NpcId, string NamePl, int Seals, int SealsBroken, bool Defeated, bool Rewarded, int Fighters, int AttemptsLeft, long TreasuryReward,
    long MemberStones);

public sealed record SectArtView(int Level, string NamePl, string DescriptionPl, PerkBonus Bonus, bool Unlocked);

public sealed record DevelopmentView(Guid SectId, string Week, long Treasury, string MyRole, bool CanBuild, IReadOnlyList<BuildingView> Buildings,
    IReadOnlyList<MissionView> Missions, SectBossView Boss, int Level = 1, long Xp = 0, long? NextLevelXp = null, int MaxMembers = 0,
    IReadOnlyList<SectArtView>? Arts = null, int EraPerkPermille = 0);

public sealed record BuildCommand(Guid CharacterId, string BuildingId);

public sealed record MissionClaimCommand(Guid CharacterId, string MissionId);

public sealed record SectBossBattleView(Guid BattleId, DevelopmentView State);

/// <summary>
/// Sect development: buildings bought from the treasury (bonuses for every member), weekly shared missions and the
/// weekly sect boss whose seals the members break together. Leaders and elders build and claim; everyone contributes.
/// </summary>
public sealed class SectDevelopmentService(
    IUnitOfWorkFactory uowFactory,
    ISectRepository sects,
    ISectDevelopmentRepository repo,
    ICharacterDirectory characters,
    ICultivationStatus cultivation,
    ICombatEncounters encounters,
    IAuditLog audit,
    ICorrelationContext correlation,
    SectsConfig config,
    TimeProvider clock)
{
    public const string Build = "build";

    private static readonly Error Off = Error.NotFound("sect_development_disabled", "Sect development is not available.");
    private static readonly Error NotMember = Error.Forbidden("not_a_member", "Your character is not a member of this sect.");
    private static readonly Error Denied = Error.Forbidden("sect_permission_denied", "Your rank in the sect does not allow this.");

    private DevelopmentRules Rules => config.Development!;

    private sealed record Actor(CharacterSummary Character, Member Member, Sect Sect, SectDevelopment Development, int StoredVersion);

    public Task<Result<DevelopmentView>> GetAsync(AccountId accountId, Guid sectId, Guid characterId, CancellationToken ct) =>
        Run(accountId, sectId, characterId, (_, a, _) => Task.FromResult<Result<SectDevelopment>>(a.Development), null, ct);

    public Task<Result<DevelopmentView>> BuildAsync(AccountId accountId, Guid sectId, BuildCommand cmd, CancellationToken ct) =>
        Run(accountId, sectId, cmd.CharacterId, async (uow, a, now) =>
        {
            if (!CanBuild(a.Member.Role)) return Denied;
            if (Rules.Buildings.FirstOrDefault(b => b.Id == cmd.BuildingId) is not { } building) return Error.NotFound("building_unknown", "Unknown building.");
            var level = a.Development.Level(building.Id);
            if (level >= building.Costs.Count) return Error.LimitReached("building_max", "This building is fully built.");
            var paid = await sects.PostTreasuryAsync(uow, a.Sect.Id, -building.Costs[level], "building", $"{building.Id}:{level + 1}", now, ct);
            if (!paid.IsSuccess) return paid.Error!;
            var xp = Rules.Levels is { } lv ? building.Costs[level] * lv.BuildingXpPermille / 1000 : 0;
            return a.Development with { Buildings = new Dictionary<string, int>(a.Development.Buildings) { [building.Id] = level + 1 }, Xp = a.Development.Xp + xp };
        }, "sects.building_upgraded", ct);

    /// <summary>A completed weekly mission pays the treasury (more with the Hall of Tasks) and contribution to the claimer.</summary>
    public Task<Result<DevelopmentView>> ClaimMissionAsync(AccountId accountId, Guid sectId, MissionClaimCommand cmd, CancellationToken ct) =>
        Run(accountId, sectId, cmd.CharacterId, async (uow, a, now) =>
        {
            if (!CanBuild(a.Member.Role)) return Denied;
            var d = a.Development;
            var mission = SectDevelopmentRules.MissionsFor(Rules, a.Sect.Id, d.Week).FirstOrDefault(m => m.Id == cmd.MissionId);
            if (mission is null) return Error.NotFound("mission_not_found", "This mission is not active this week.");
            if (d.MissionsClaimed.Contains(mission.Id)) return Error.Conflict("mission_claimed", "Already claimed.");
            var members = a.Sect.MemberCount;
            if (d.Missions.GetValueOrDefault(mission.Metric) < SectDevelopmentRules.Scaled(mission.Target, members)) return Error.Conflict("mission_not_completed", "The mission is not completed yet.");
            var bonus = Rules.Buildings.Sum(b => b.MissionRewardPermillePerLevel * d.Level(b.Id));
            var reward = SectDevelopmentRules.Scaled(mission.TreasuryReward, members) * (1000 + bonus) / 1000;
            if (reward > 0)
            {
                var credited = await sects.PostTreasuryAsync(uow, a.Sect.Id, reward, "mission", d.Week + ":" + mission.Id, now, ct);
                if (!credited.IsSuccess) return credited.Error!;
            }

            await sects.UpdateMemberAsync(uow, a.Member with { Contribution = a.Member.Contribution + mission.ContributionReward }, ct);
            return d with { MissionsClaimed = [.. d.MissionsClaimed, mission.Id], Xp = d.Xp + (Rules.Levels is { } lv ? reward * lv.MissionXpPermille / 1000 : 0) };
        }, "sects.mission_claimed", ct);

    /// <summary>Fights the sect boss's avatar; each victory breaks one seal. Limited attempts per member per day.</summary>
    public async Task<Result<SectBossBattleView>> FightBossAsync(AccountId accountId, Guid sectId, ActorCommand cmd, CancellationToken ct)
    {
        var battle = Guid.Empty;
        var r = await Run(accountId, sectId, cmd.CharacterId, async (uow, a, now) =>
        {
            var d = a.Development;
            if (d.SealsBroken >= Rules.Boss.Seals) return Error.Conflict("boss_defeated", "This week's boss is already sealed away.");
            var key = SectDevelopment.AttemptKey(a.Character.Id.Value, now);
            var attempts = d.BossAttempts.GetValueOrDefault(key);
            if (attempts >= Rules.Boss.AttemptsPerDay) return Error.LimitReached("boss_attempts", "No more attempts today.");
            var realm = (await cultivation.StatusAsync(uow, a.Character.Id.Value, ct)).Realm;
            var started = await encounters.StartSpiritAsync(uow, accountId, a.Character.Id.Value, Rules.Boss.NpcId, realm + Rules.Boss.RealmBonus, Rules.Boss.PowerPermille,
                d.BossReference, Rules.Boss.NamePl, ct);
            if (!started.IsSuccess) return started.Error!;
            battle = started.Value;
            // only today's counters are kept
            var counters = d.BossAttempts.Where(kv => kv.Key.EndsWith(key[^9..], StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value);
            counters[key] = attempts + 1;
            return d with
            {
                BossFighters = d.BossFighters.Contains(a.Character.Id.Value) ? d.BossFighters : [.. d.BossFighters, a.Character.Id.Value],
                BossAttempts = counters,
            };
        }, "sects.boss_fought", ct);
        return r.IsSuccess ? new SectBossBattleView(battle, r.Value) : r.Error!;
    }

    private static bool CanBuild(string role) => role is SectRoles.Leader or SectRoles.Elder;

    private async Task<Result<DevelopmentView>> Run(AccountId accountId, Guid sectId, Guid characterId, Func<IUnitOfWork, Actor, DateTimeOffset, Task<Result<SectDevelopment>>> change,
        string? action, CancellationToken ct) =>
        await uowFactory.RunAsync<Result<DevelopmentView>>(async uow =>
        {
            if (config.Development is null) return Off;
            var c = await characters.FindOwnedAsync(uow, accountId, characterId, ct);
            if (c is null) return Error.NotFound("character_not_found", "Character not found.");
            var member = await sects.FindMemberAsync(uow, c.Id, ct);
            if (member is null || member.SectId != sectId) return NotMember;
            var sect = await sects.FindSectAsync(uow, sectId, ct);
            if (sect is null || sect.DisbandedAt is not null) return Error.NotFound("sect_not_found", "Sect not found.");
            var now = clock.GetUtcNow();
            var week = SectDevelopmentRules.WeekOf(now);
            var stored = await repo.FindAsync(uow, sectId, ct);
            var dev = (stored ?? SectDevelopment.New(sectId, week)).ForWeek(week);
            var actor = new Actor(c, member, sect, dev, stored?.Version ?? 0);
            var changed = await change(uow, actor, now);
            if (!changed.IsSuccess) return changed.Error!;
            var next = changed.Value;
            if (action is not null || stored is null || !ReferenceEquals(next, stored))
            {
                next = next with { Version = actor.StoredVersion + 1 };
                if (!await repo.TrySaveAsync(uow, next, actor.StoredVersion, ct)) return Error.Conflict("version_conflict", "The sect changed concurrently; reload and retry.");
            }

            if (action is not null)
                await audit.AppendAsync(uow, new AuditEntry(Uuid7.New(), accountId, action, "sect", sectId.ToString(), "{}", correlation.CorrelationId, now), ct);
            var member2 = await sects.FindMemberAsync(uow, c.Id, ct) ?? member;
            var view = await ViewAsync(uow, sect, member2, next, now, ct);
            await uow.CommitAsync(ct);
            return view;
        }, ct);

    private async Task<DevelopmentView> ViewAsync(IUnitOfWork uow, Sect sect, Member me, SectDevelopment d, DateTimeOffset now, CancellationToken ct)
    {
        var buildings = Rules.Buildings.Select(b =>
        {
            var level = d.Level(b.Id);
            return new BuildingView(b.Id, b.NamePl, b.DescriptionPl, level, b.Costs.Count, level < b.Costs.Count ? b.Costs[level] : null, b.PerLevel, b.AlchemyPermillePerLevel,
                b.ForgePermillePerLevel, b.MissionRewardPermillePerLevel);
        }).ToList();
        var missions = SectDevelopmentRules.MissionsFor(Rules, sect.Id, d.Week).Select(m =>
        {
            var p = d.Missions.GetValueOrDefault(m.Metric);
            var target = SectDevelopmentRules.Scaled(m.Target, sect.MemberCount);
            return new MissionView(m.Id, m.NamePl, m.DescriptionPl, m.Metric, target, Math.Min(p, target), SectDevelopmentRules.Scaled(m.TreasuryReward, sect.MemberCount),
                m.ContributionReward, p >= target, d.MissionsClaimed.Contains(m.Id));
        }).ToList();
        var used = d.BossAttempts.GetValueOrDefault(SectDevelopment.AttemptKey(me.CharacterId.Value, now));
        var b2 = Rules.Boss;
        var boss = new SectBossView(b2.NpcId, b2.NamePl, b2.Seals, Math.Min(d.SealsBroken, b2.Seals), d.SealsBroken >= b2.Seals, d.BossRewarded,
            d.BossFighters.Count, Math.Max(0, b2.AttemptsPerDay - used), b2.TreasuryReward, b2.MemberStones);
        var lv = Rules.Levels;
        var level = lv?.LevelOf(d.Xp) ?? 1;
        return new DevelopmentView(sect.Id, d.Week, await sects.TreasuryAsync(uow, sect.Id, ct), me.Role, CanBuild(me.Role), buildings, missions, boss,
            level, d.Xp, lv?.NextXp(d.Xp), SectDevelopmentRules.MaxMembers(config, d.Xp),
            lv is null ? [] : [.. lv.Arts.Select(x => new SectArtView(x.Level, x.NamePl, x.DescriptionPl, x.Bonus, level >= x.Level))], lv?.EraPerkPermille ?? 0);
    }
}

/// <summary>Members' battles, gathering and crafting advance the weekly missions; sect boss victories break its seals.</summary>
internal sealed class SectProgressHandler(IServiceScopeFactory scopes, SectsConfig config, string eventType) : IOutboxHandler
{
    public string EventType => eventType;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        if (config.Development is null) return;
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var sects = sp.GetRequiredService<ISectRepository>();
        var repo = sp.GetRequiredService<ISectDevelopmentRepository>();
        var increments = new List<(Guid CharacterId, string Metric, long Amount, Guid? BossWin)>();
        switch (message.Type)
        {
            case CombatEvents.BattleFinished:
            {
                var e = message.Payload<CombatEvents.BattleFinishedV1>();
                foreach (var p in e.Participants.Where(p => p.Result == "won"))
                {
                    Guid? boss = e.Kind == "spirit" && e.Context is { } ctx && Guid.TryParse(ctx.AsSpan(7), out var reference) ? reference : null;
                    increments.Add((p.CharacterId, MissionMetrics.BattlesWon, 1, boss));
                    if (e.Boss || e.Kind == "boss") increments.Add((p.CharacterId, MissionMetrics.BossesWon, 1, null));
                }

                break;
            }

            case JianghuGo.Modules.World.Contracts.WorldEvents.ItemsGathered:
            {
                var e = message.Payload<JianghuGo.Modules.World.Contracts.WorldEvents.ItemsGatheredV1>();
                increments.Add((e.CharacterId, MissionMetrics.ItemsGathered, e.Items.Sum(i => (long)i.Quantity), null));
                break;
            }

            case JianghuGo.Modules.Crafting.Contracts.CraftingEvents.Crafted:
            {
                var e = message.Payload<JianghuGo.Modules.Crafting.Contracts.CraftingEvents.CraftedV1>();
                if (e.Successes > 0) increments.Add((e.CharacterId, MissionMetrics.Crafts, e.Successes, null));
                break;
            }
        }

        foreach (var group in increments.GroupBy(i => i.CharacterId))
        {
            var member = await sects.FindMemberAsync(uow, new CharacterId(group.Key), ct);
            if (member is null) continue;
            if (!await repo.TryMarkHandledAsync(uow, Guard(message.Id, group.Key), message.OccurredAt, ct)) continue;
            var week = SectDevelopmentRules.WeekOf(message.OccurredAt);
            var stored = await repo.FindAsync(uow, member.SectId, ct);
            var dev = (stored ?? SectDevelopment.New(member.SectId, week)).ForWeek(week);
            var missions = new Dictionary<string, long>(dev.Missions);
            foreach (var i in group) missions[i.Metric] = missions.GetValueOrDefault(i.Metric) + i.Amount;
            var next = dev with { Missions = missions };
            var bossWins = group.Count(i => i.BossWin is { } r && r == dev.BossReference);
            if (bossWins > 0 && dev.SealsBroken < config.Development.Boss.Seals)
            {
                next = next with { SealsBroken = Math.Min(config.Development.Boss.Seals, dev.SealsBroken + bossWins) };
                if (next.SealsBroken >= config.Development.Boss.Seals && !next.BossRewarded)
                {
                    next = next with { BossRewarded = true, Xp = next.Xp + (config.Development.Levels?.BossXp ?? 0) };
                    await RewardBossAsync(sp, uow, member.SectId, next, message.OccurredAt, ct);
                }
            }

            if (!await repo.TrySaveAsync(uow, next with { Version = (stored?.Version ?? 0) + 1 }, stored?.Version ?? 0, ct))
                throw new TransientConflictException("Sect development changed while counting.");
        }
    }

    /// <summary>The boss is sealed: the treasury and every member who fought it are paid once.</summary>
    private async Task RewardBossAsync(IServiceProvider sp, IUnitOfWork uow, Guid sectId, SectDevelopment d, DateTimeOffset at, CancellationToken ct)
    {
        var rules = config.Development!.Boss;
        var sects = sp.GetRequiredService<ISectRepository>();
        if (rules.TreasuryReward > 0) await sects.PostTreasuryAsync(uow, sectId, rules.TreasuryReward, "sect_boss", d.Week, at, ct);
        if (rules.MemberStones <= 0) return;
        var inventory = sp.GetRequiredService<IInventory>();
        var members = (await sects.ListMembersAsync(uow, sectId, ct)).ToDictionary(m => m.CharacterId.Value);
        foreach (var fighter in d.BossFighters.Distinct().Where(members.ContainsKey))
            await inventory.CreditAsync(uow, new CharacterId(fighter), rules.MemberStones, "sect_boss", d.Week, ct);
    }

    private static Guid Guard(Guid messageId, Guid characterId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("sect:" + messageId.ToString("N") + characterId.ToString("N")));
        return new Guid(bytes.AsSpan(0, 16));
    }
}

/// <summary>Buildings give every member combat perks…</summary>
internal sealed class SectCombatPerks(ISectRepository sects, ISectDevelopmentRepository repo, SectsConfig config, ICultivationStatus cultivation) : ICombatPerkBonuses
{
    public string SourcePl => "Sekta";

    public async Task<PerkBonus> BonusAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct)
    {
        if (config.Development is null || await sects.FindMemberAsync(uow, new CharacterId(characterId), ct) is not { } m) return PerkBonus.None;
        if (await repo.FindAsync(uow, m.SectId, ct) is not { } d) return PerkBonus.None;
        var sum = PerkBonus.None;
        foreach (var b in config.Development.Buildings.Where(b => b.PerLevel is not null))
            for (var i = 0; i < d.Level(b.Id); i++) sum += b.PerLevel!;
        if (config.Development.Levels is not { } lv) return sum;
        // Buildings grow with the member's era (1.9): +EraPerkPermille per era.
        var era = (await cultivation.StatusAsync(uow, characterId, ct)).Realm / 11;
        sum = Scale(sum, 1000 + (lv.EraPerkPermille * era));
        var level = lv.LevelOf(d.Xp);
        foreach (var art in lv.Arts.Where(a => level >= a.Level)) sum += art.Bonus;
        return sum;
    }

    private static PerkBonus Scale(PerkBonus p, int permille) => new(
        p.DamagePermille * permille / 1000, p.GuardPermille * permille / 1000, p.EffectChancePermille * permille / 1000, p.BleedPermille * permille / 1000,
        p.HealPermille * permille / 1000, p.QiRegen, p.QiPermille * permille / 1000, p.RegenPermille * permille / 1000, p.ReflectPermille * permille / 1000,
        p.LifestealPermille * permille / 1000, p.FirstStrikePermille * permille / 1000, p.HpPermille * permille / 1000, p.SurviveOnce, p.PetPower, p.PetEvery);
}

/// <summary>…and workshop bonuses.</summary>
internal sealed class SectCraftingBonuses(ISectRepository sects, ISectDevelopmentRepository repo, SectsConfig config) : JianghuGo.Modules.Crafting.Contracts.ICraftingBonuses
{
    public async Task<(int AlchemyPermille, int ForgePermille)> BonusAsync(IUnitOfWork uow, Guid characterId, CancellationToken ct)
    {
        if (config.Development is null || await sects.FindMemberAsync(uow, new CharacterId(characterId), ct) is not { } m) return (0, 0);
        if (await repo.FindAsync(uow, m.SectId, ct) is not { } d) return (0, 0);
        return (config.Development.Buildings.Sum(b => b.AlchemyPermillePerLevel * d.Level(b.Id)), config.Development.Buildings.Sum(b => b.ForgePermillePerLevel * d.Level(b.Id)));
    }
}
