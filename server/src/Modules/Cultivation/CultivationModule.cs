using System.Text.Json;
using JianghuGo.Infrastructure;
using JianghuGo.Modules.Cultivation.Application;
using JianghuGo.Modules.Cultivation.Domain;
using JianghuGo.Modules.Cultivation.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Cultivation;

public static class CultivationModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IServiceCollection AddCultivationModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:CultivationPath"] ?? "game-config/cultivation.json";
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        services.AddSingleton(LoadConfig(path));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
        {
            services.AddSingleton<ICultivationRepository, PostgresCultivationRepository>();
        }
        else
        {
            services.AddSingleton<ICultivationRepository, InMemoryCultivationRepository>();
        }

        services.AddScoped<CultivationService>();
        services.AddScoped<MeridianService>();
        services.AddScoped<RankingService>();
        services.AddSingleton<JianghuGo.Modules.Combat.Contracts.ICombatStatBonuses, CultivationCombatBonuses>();
        services.AddSingleton<JianghuGo.Application.IOutboxHandler, CombatRewardHandler>();
        services.AddSingleton<JianghuGo.Application.IOutboxHandler, WorldRewardHandler>();
        services.AddSingleton<JianghuGo.Application.IOutboxHandler, ConsumableQiHandler>();
        return services;
    }

    public static CultivationConfig LoadConfig(string path)
    {
        var config = JsonSerializer.Deserialize<CultivationConfig>(File.ReadAllText(path), StrictJson);
        var errors = CultivationConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid cultivation config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        }

        return config!;
    }
}
