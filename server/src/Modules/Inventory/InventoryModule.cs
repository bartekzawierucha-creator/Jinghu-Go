using System.Text.Json;
using JianghuGo.Infrastructure;
using JianghuGo.Modules.Inventory.Application;
using JianghuGo.Modules.Inventory.Contracts;
using JianghuGo.Modules.Inventory.Domain;
using JianghuGo.Modules.Inventory.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Modules.Inventory;

public static class InventoryModule
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private static string ItemsPath(IConfiguration configuration)
    {
        var path = configuration["GameConfig:ItemsPath"] ?? "game-config/items.json";
        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }

    /// <summary>For other modules' registration-time config validation (fail fast at startup, not on first request).</summary>
    public static IItemCatalog LoadCatalog(IConfiguration configuration) => new ItemCatalog(LoadConfig(ItemsPath(configuration)));

    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(LoadConfig(ItemsPath(configuration)));
        if (StorageProvider.From(configuration) == StorageKind.Postgres)
        {
            services.AddSingleton<IInventoryRepository, PostgresInventoryRepository>();
        }
        else
        {
            services.AddSingleton<IInventoryRepository, InMemoryInventoryRepository>();
        }

        services.AddSingleton<InventoryLedger>();
        services.AddSingleton<IInventory>(sp => sp.GetRequiredService<InventoryLedger>());
        services.AddSingleton<IEquipmentStats>(sp => sp.GetRequiredService<InventoryLedger>());
        services.AddSingleton<IItemCatalog, ItemCatalog>();
        services.AddScoped<InventoryService>();
        services.AddSingleton<JianghuGo.Application.IOutboxHandler, CombatLootHandler>();
        services.AddSingleton<JianghuGo.Application.IOutboxHandler, WorldItemsHandler>();
        return services;
    }

    public static ItemsConfig LoadConfig(string path)
    {
        var config = JsonSerializer.Deserialize<ItemsConfig>(File.ReadAllText(path), StrictJson);
        var errors = ItemsConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid items config '{path}':{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors));
        }

        return config!;
    }
}
