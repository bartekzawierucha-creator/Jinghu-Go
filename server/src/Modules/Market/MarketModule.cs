using System.Text.Json;
using JianghuGo.Infrastructure;
using JianghuGo.Modules.Market.Application;
using JianghuGo.Modules.Market.Domain;
using JianghuGo.Modules.Market.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Market;

public static class MarketModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IServiceCollection AddMarketModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:MarketPath"] ?? "game-config/market.json";
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        services.AddSingleton(LoadConfig(path));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
        {
            services.AddSingleton<IMarketRepository, PostgresMarketRepository>();
            services.AddSingleton<ICraftOrderRepository, PostgresCraftOrderRepository>();
        }
        else
        {
            services.AddSingleton<IMarketRepository, InMemoryMarketRepository>();
            services.AddSingleton<ICraftOrderRepository, InMemoryCraftOrderRepository>();
        }

        services.AddScoped<MarketService>();
        services.AddScoped<CraftOrderService>();
        return services;
    }

    public static MarketConfig LoadConfig(string path)
    {
        var config = JsonSerializer.Deserialize<MarketConfig>(File.ReadAllText(path), StrictJson);
        var errors = MarketConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid market config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        }

        return config!;
    }
}
