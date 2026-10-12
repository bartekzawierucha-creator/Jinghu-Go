using System.Text.Json;
using JianghuGo.Infrastructure;
using JianghuGo.Modules.Crafting.Application;
using JianghuGo.Modules.Crafting.Domain;
using JianghuGo.Modules.Crafting.Infrastructure;
using JianghuGo.Modules.Inventory.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Crafting;

public static class CraftingModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IServiceCollection AddCraftingModule(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["GameConfig:CraftingPath"] ?? "game-config/crafting.json";
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        services.AddSingleton(LoadConfig(path, Inventory.InventoryModule.LoadCatalog(configuration)));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
        {
            services.AddSingleton<ICraftingRepository, PostgresCraftingRepository>();
        }
        else
        {
            services.AddSingleton<ICraftingRepository, InMemoryCraftingRepository>();
        }

        services.AddScoped<CraftingService>();
        return services;
    }

    public static CraftingConfig LoadConfig(string path, IItemCatalog catalog)
    {
        var config = JsonSerializer.Deserialize<CraftingConfig>(File.ReadAllText(path), StrictJson);
        var errors = CraftingConfigValidator.Validate(config, id => catalog.Find(id)?.Stackable);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid crafting config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        }

        return config!;
    }
}
