using System.Text.Json;
using JianghuGo.Application;
using JianghuGo.Infrastructure;
using JianghuGo.Infrastructure.Http;
using JianghuGo.Modules.Characters.Contracts;
using JianghuGo.Modules.Identity.Api;
using JianghuGo.Modules.Quests.Application;
using JianghuGo.Modules.Quests.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Quests;

public static class QuestsModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IServiceCollection AddQuestsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:QuestsPath"] ?? "game-config/quests.json";
        if (!Path.IsPathRooted(path)) path = Path.Combine(AppContext.BaseDirectory, path);
        services.AddSingleton(LoadConfig(path));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
            services.AddSingleton<IQuestRepository, PostgresQuestRepository>();
        else
            services.AddSingleton<IQuestRepository, InMemoryQuestRepository>();
        services.AddScoped<QuestService>();
        services.AddScoped<RoadService>();
        services.AddSingleton<QuestTitles>();
        services.AddSingleton<ICharacterDecorationProvider, QuestTitleDecoration>();
        services.AddSingleton<IOutboxHandler, RealmCounter>();
        services.AddSingleton<IOutboxHandler, ArtsCounter>();
        services.AddSingleton<IOutboxHandler, StepsCounter>();
        services.AddSingleton<IOutboxHandler, LeagueTitleHandler>();
        services.AddSingleton<IOutboxHandler, HarvestCounter>();
        services.AddSingleton<IOutboxHandler, ExpeditionCounter>();
        services.AddSingleton<JianghuGo.Modules.Combat.Contracts.ICombatStatBonuses, TitleCombatBonuses>();
        services.AddSingleton<JianghuGo.Modules.Combat.Contracts.ICombatStatBonuses, ZodiacCombatBonuses>();
        services.AddSingleton<JianghuGo.Modules.Combat.Contracts.ICombatPerkBonuses, PhysiqueCombatPerks>();
        services.AddSingleton<IOutboxHandler, BattleCounter>();
        services.AddSingleton<IOutboxHandler, MeditationCounter>();
        services.AddSingleton<IOutboxHandler, BreakthroughCounter>();
        services.AddSingleton<IOutboxHandler, CraftCounter>();
        services.AddSingleton<IOutboxHandler, GatherCounter>();
        services.AddSingleton<IOutboxHandler, WorldQiCounter>();
        services.AddSingleton<IOutboxHandler, PillCounter>();
        services.AddSingleton<IOutboxHandler, SaleCounter>();
        return services;
    }

    public static QuestsConfig LoadConfig(string path)
    {
        var config = JsonSerializer.Deserialize<QuestsConfig>(File.ReadAllText(path), StrictJson);
        var errors = QuestsConfigValidator.Validate(config);
        if (errors.Count > 0)
            throw new InvalidOperationException($"Invalid quests config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        return config!;
    }

    public static IEndpointRouteBuilder MapQuestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/characters/{characterId:guid}/road", async (Guid characterId, HttpContext http, RoadService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(http.User.GetAccountId(), characterId, ct), Results.Ok)).WithTags("Quests").RequireAuthorization();
        var g = app.MapGroup("/api/v1/characters/{characterId:guid}/quests").WithTags("Quests").RequireAuthorization();
        g.MapGet("/", async (Guid characterId, HttpContext http, QuestService svc, CancellationToken ct) =>
            ApiResults.From(await svc.GetAsync(http.User.GetAccountId(), characterId, ct), Results.Ok));
        g.MapPost("/{questId}/claim", async (Guid characterId, string questId, HttpContext http, QuestService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.ClaimAsync(http.User.GetAccountId(), characterId, questId, ct), Results.Ok)));
        g.MapPost("/checkin", async (Guid characterId, HttpContext http, QuestService svc, CancellationToken ct) =>
            await Guard(async () => ApiResults.From(await svc.CheckinAsync(http.User.GetAccountId(), characterId, ct), Results.Ok)));
        g.MapGet("/codex", async (Guid characterId, HttpContext http, QuestService svc, CancellationToken ct) =>
            ApiResults.From(await svc.CodexAsync(http.User.GetAccountId(), characterId, ct), list => Results.Ok(new { items = list })));
        g.MapGet("/titles", async (Guid characterId, HttpContext http, QuestService svc, CancellationToken ct) =>
            ApiResults.From(await svc.TitlesAsync(http.User.GetAccountId(), characterId, ct), list => Results.Ok(new { items = list })));
        g.MapPut("/titles/selected", async (Guid characterId, SelectTitleCommand cmd, HttpContext http, QuestService svc, CancellationToken ct) =>
            ApiResults.From(await svc.SelectTitleAsync(http.User.GetAccountId(), characterId, cmd, ct), list => Results.Ok(new { items = list })));
        g.MapGet("/leaders/{metric}", async (Guid characterId, string metric, HttpContext http, QuestService svc, CancellationToken ct) =>
            ApiResults.From(await svc.LeadersAsync(http.User.GetAccountId(), characterId, metric, ct), list => Results.Ok(new { items = list })));
        return app;
    }

    /// <summary>A concurrent duplicate claim/check-in loses on the unique key: report it as already done.</summary>
    private static async Task<IResult> Guard(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (DuplicateKeyException)
        {
            return ApiResults.IdempotencyRace();
        }
    }
}

internal sealed class BattleCounter(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Combat.Contracts.CombatEvents.BattleFinished;

    public async Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Combat.Contracts.CombatEvents.BattleFinishedV1>();
        var night = QuestRules.IsNight(config, message.OccurredAt);
        var moon = QuestRules.IsFullMoon(message.OccurredAt);
        foreach (var p in e.Participants)
        {
            if (p.Result != "won")
            {
                await QuestCounter.CountAsync(repo, characters, config, uow, message, p.CharacterId, ct,
                    (Metrics.BattlesLost, p.Result == "lost" ? 1 : 0), (Metrics.WorldBossFights, e.Kind == "boss" ? 1 : 0));
                continue;
            }

            var pvp = e.Kind == "pvp";
            // Codex (2.3): every beast defeated is remembered (lifetime count per species), counted with the rest exactly once.
            await QuestCounter.CountAsync(repo, characters, config, uow, message, p.CharacterId, pvp ? null : e.NpcId, ct,
                (Metrics.BattlesWon, 1), (Metrics.PvpWon, pvp ? 1 : 0), (Metrics.BossesWon, !pvp && e.Boss ? 1 : 0),
                (Metrics.ArenaWon, e.Kind == "arena" ? 1 : 0), (Metrics.TowerWon, e.Kind == "tower" ? 1 : 0), (Metrics.Tribulations, e.Kind == "tribulation" ? 1 : 0),
                (Metrics.SpiritsWon, e.Kind == "spirit" ? 1 : 0), (Metrics.NightWins, night ? 1 : 0), (Metrics.FullMoonWins, moon ? 1 : 0),
                (Metrics.WorldBossFights, e.Kind == "boss" ? 1 : 0));
        }
    }
}

internal sealed class MeditationCounter(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Cultivation.Application.CultivationEvents.MeditationFinished;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Cultivation.Application.CultivationEvents.MeditationFinishedV1>();
        return QuestCounter.CountAsync(repo, characters, config, uow, message, e.CharacterId, ct, (Metrics.MeditationMinutes, e.Minutes));
    }
}

internal sealed class BreakthroughCounter(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Cultivation.Application.CultivationEvents.BreakthroughSucceeded;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Cultivation.Application.CultivationEvents.BreakthroughV1>();
        return QuestCounter.CountAsync(repo, characters, config, uow, message, e.CharacterId, ct, (Metrics.Breakthroughs, 1));
    }
}

internal sealed class CraftCounter(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Crafting.Contracts.CraftingEvents.Crafted;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Crafting.Contracts.CraftingEvents.CraftedV1>();
        return QuestCounter.CountAsync(repo, characters, config, uow, message, e.CharacterId, ct,
            e.Operation == "upgrade" ? (Metrics.Upgrades, e.Attempts) : (Metrics.Crafts, e.Successes));
    }
}

internal sealed class GatherCounter(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.World.Contracts.WorldEvents.ItemsGathered;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.World.Contracts.WorldEvents.ItemsGatheredV1>();
        return QuestCounter.CountAsync(repo, characters, config, uow, message, e.CharacterId, ct, (Metrics.ItemsGathered, e.Items.Sum(i => (long)i.Quantity)));
    }
}

internal sealed class WorldQiCounter(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.World.Contracts.WorldEvents.QiReward;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.World.Contracts.WorldEvents.QiRewardV1>();
        return QuestCounter.CountAsync(repo, characters, config, uow, message, e.CharacterId, ct, (Metrics.WorldQi, 1));
    }
}

internal sealed class PillCounter(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Inventory.Contracts.InventoryEvents.ConsumableUsed;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Inventory.Contracts.InventoryEvents.ConsumableUsedV1>();
        return QuestCounter.CountAsync(repo, characters, config, uow, message, e.CharacterId, ct, (Metrics.PillsUsed, e.Quantity));
    }
}

internal sealed class SaleCounter(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Market.Contracts.MarketEvents.ListingSold;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Market.Contracts.MarketEvents.ListingSoldV1>();
        return QuestCounter.CountAsync(repo, characters, config, uow, message, e.SellerCharacterId, ct, (Metrics.MarketSales, 1));
    }
}

internal sealed class RealmCounter(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Cultivation.Application.CultivationEvents.RealmReached;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Cultivation.Application.CultivationEvents.RealmReachedV1>();
        return QuestCounter.CountAsync(repo, characters, config, uow, message, e.CharacterId, ct, (Metrics.Realm, e.Realm));
    }
}

internal sealed class ArtsCounter(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Arts.Contracts.ArtsEvents.Progress;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Arts.Contracts.ArtsEvents.ProgressV1>();
        return QuestCounter.CountAsync(repo, characters, config, uow, message, e.CharacterId, ct,
            (Metrics.FlamesOwned, e.FlamesOwned), (Metrics.ToxinLevel, e.ToxinLevel), (Metrics.Temper, e.Temper), (Metrics.Swords, e.Swords));
    }
}

internal sealed class StepsCounter(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Cultivation.Contracts.CultivationStepEvents.StepsAccepted;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Cultivation.Contracts.CultivationStepEvents.StepsAcceptedV1>();
        return QuestCounter.CountAsync(repo, characters, config, uow, message, e.CharacterId, ct, (Metrics.Steps, e.Steps));
    }
}

/// <summary>A weekly reward collected in an arena league grants that league's title (if it has one).</summary>
internal sealed class LeagueTitleHandler(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Combat.Contracts.CombatEvents.ArenaLeague;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Combat.Contracts.CombatEvents.ArenaLeagueV1>();
        return config.ArenaTitleList.Any(t => t.League == e.LeagueId)
            ? QuestCounter.FlagAsync(repo, characters, uow, message, e.CharacterId, "l_" + e.LeagueId, ct)
            : Task.CompletedTask;
    }
}

internal sealed class HarvestCounter(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Estate.Contracts.EstateEvents.Harvested;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Estate.Contracts.EstateEvents.HarvestedV1>();
        return QuestCounter.CountAsync(repo, characters, config, uow, message, e.CharacterId, ct, (Metrics.Harvests, e.Plots));
    }
}

internal sealed class ExpeditionCounter(IQuestRepository repo, ICharacterDirectory characters, QuestsConfig config) : IOutboxHandler
{
    public string EventType => JianghuGo.Modules.Estate.Contracts.EstateEvents.ExpeditionFinished;

    public Task HandleAsync(IUnitOfWork uow, OutboxMessage message, CancellationToken ct)
    {
        var e = message.Payload<JianghuGo.Modules.Estate.Contracts.EstateEvents.ExpeditionFinishedV1>();
        return QuestCounter.CountAsync(repo, characters, config, uow, message, e.CharacterId, ct, (Metrics.Expeditions, e.Count));
    }
}
