using System.Text.Json;
using JianghuGo.Infrastructure;
using JianghuGo.Modules.Sects.Application;
using JianghuGo.Modules.Sects.Contracts;
using JianghuGo.Modules.Sects.Domain;
using JianghuGo.Modules.Sects.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Sects;

public static class SectsModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IServiceCollection AddSectsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:SectsPath"] ?? "game-config/sects.json";
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        services.AddSingleton(LoadConfig(path));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
            services.AddSingleton<ISectRepository, PostgresSectRepository>();
        else
            services.AddSingleton<ISectRepository, InMemorySectRepository>();
        services.AddSingleton<ISectDirectory, SectDirectory>();
        services.AddScoped<SectService>();
        return services;
    }

    public static SectsConfig LoadConfig(string path)
    {
        var config = JsonSerializer.Deserialize<SectsConfig>(File.ReadAllText(path), StrictJson);
        var errors = SectsConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid sects config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        }

        return config!;
    }
}
