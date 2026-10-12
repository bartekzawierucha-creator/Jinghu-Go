using System.Text.Json;
using JianghuGo.Infrastructure;
using JianghuGo.Modules.Territory.Application;
using JianghuGo.Modules.Territory.Domain;
using JianghuGo.Modules.Territory.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Territory;

public static class TerritoryModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IServiceCollection AddTerritoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:TerritoryPath"] ?? "game-config/territory.json";
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        services.AddSingleton(LoadConfig(path));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
            services.AddSingleton<ITerritoryRepository, PostgresTerritoryRepository>();
        else
            services.AddSingleton<ITerritoryRepository, InMemoryTerritoryRepository>();
        services.AddScoped<TerritoryService>();
        services.AddSingleton<JianghuGo.Application.IOutboxHandler, WarScoringHandler>();
        return services;
    }

    public static TerritoryConfig LoadConfig(string path)
    {
        var config = JsonSerializer.Deserialize<TerritoryConfig>(File.ReadAllText(path), StrictJson);
        var errors = TerritoryConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid territory config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        }

        return config!;
    }
}
