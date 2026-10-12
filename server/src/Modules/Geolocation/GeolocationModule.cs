using System.Text.Json;
using JianghuGo.Infrastructure;
using JianghuGo.Modules.Geolocation.Application;
using JianghuGo.Modules.Geolocation.Contracts;
using JianghuGo.Modules.Geolocation.Domain;
using JianghuGo.Modules.Geolocation.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Geolocation;

public static class GeolocationModule
{
    public static IServiceCollection AddGeolocationModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:GeolocationPath"] ?? "game-config/geolocation.json";
        services.AddSingleton(LoadConfig(Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path)));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
            services.AddSingleton<IExplorationRepository, PostgresExplorationRepository>();
        else
            services.AddSingleton<IExplorationRepository, InMemoryExplorationRepository>();
        services.AddScoped<GeolocationService>();
        services.AddScoped<IPositionVerifier>(sp => sp.GetRequiredService<GeolocationService>());
        return services;
    }

    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static GeolocationConfig LoadConfig(string path)
    {
        var config = JsonSerializer.Deserialize<GeolocationConfig>(File.ReadAllText(path), StrictJson);
        var errors = GeolocationConfigValidator.Validate(config);
        return errors.Count == 0 ? config! : throw new InvalidOperationException($"Invalid geolocation config '{path}': " + string.Join("; ", errors));
    }
}
