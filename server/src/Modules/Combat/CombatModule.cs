using System.Text.Json;
using JianghuGo.Infrastructure;
using JianghuGo.Modules.Combat.Application;
using JianghuGo.Modules.Combat.Domain;
using JianghuGo.Modules.Combat.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Combat;

public static class CombatModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IServiceCollection AddCombatModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:CombatPath"] ?? "game-config/combat.json";
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        services.AddSingleton(LoadConfig(path));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
            services.AddSingleton<ICombatRepository, PostgresCombatRepository>();
        else
            services.AddSingleton<ICombatRepository, InMemoryCombatRepository>();
        services.AddScoped<CombatService>();
        return services;
    }

    public static CombatConfig LoadConfig(string path)
    {
        var config = JsonSerializer.Deserialize<CombatConfig>(File.ReadAllText(path), StrictJson);
        var errors = CombatConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid combat config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        }

        return config!;
    }
}
