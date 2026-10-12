using System.Globalization;
using System.Text.Json;
using JianghuGo.Infrastructure;
using JianghuGo.Modules.World.Application;
using JianghuGo.Modules.World.Domain;
using JianghuGo.Modules.World.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.World;

public static class WorldModule
{
    public static IServiceCollection AddWorldModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:WorldPath"] ?? "game-config/world.json";
        services.AddSingleton(LoadConfig(Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path)));
        var seed = configuration["World:Seed"];
        if (string.IsNullOrWhiteSpace(seed) || !ulong.TryParse(seed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed == 0)
        {
            throw new InvalidOperationException("World:Seed (a secret non-zero 64-bit integer) must be configured, e.g. via the World__Seed environment variable.");
        }

        services.AddSingleton(new WorldSecret(parsed));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
        {
            services.AddSingleton<IWorldRepository, PostgresWorldRepository>();
            services.AddSingleton(new JianghuGo.Application.RetentionPolicy("world.node_claims", "DELETE FROM world.node_claims WHERE ctid IN (SELECT ctid FROM world.node_claims WHERE claimed_at < $1 - interval '7 days' LIMIT 1000)"));
        }
        else
            services.AddSingleton<IWorldRepository, InMemoryWorldRepository>();
        services.AddScoped<WorldService>();
        return services;
    }

    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static WorldConfig LoadConfig(string path)
    {
        var config = JsonSerializer.Deserialize<WorldConfig>(File.ReadAllText(path), StrictJson);
        var errors = WorldConfigValidator.Validate(config);
        return errors.Count == 0 ? config! : throw new InvalidOperationException($"Invalid world config '{path}': " + string.Join("; ", errors));
    }
}
